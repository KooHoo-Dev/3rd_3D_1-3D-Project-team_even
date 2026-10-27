using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>바닥 판정, 걷기·구르기·점프(물리), 속도 배율</summary>
    public partial class PlayerController
    {
        #region 이동(물리)
        /// <summary>
        /// 발밑 판정. 한 번의 SphereCast 로 세 가지를 본다
        ///  - 걸을 수 있는 땅(maxSlopeAngle 이하)이 발에 닿음 → 지상
        ///  - 지상이었는데 땅이 살짝 떨어짐(능선 꼭대기, 내리막 턱) → 아래로 끌어당기는 속도를 붙여 지상 유지 (지면 스냅)
        ///  - 너무 가파른 면이 발에 닿음 → 미끄러지는 중 (공중이 아니다. 속도 상한·좌우 조작 있음)
        /// 발밑 땅까지 거리(GroundDistance)는 GroundProbeDepth 까지 잰다 (애니메이션의 낙하 판정용)
        /// </summary>
        private void UpdateGround(float dt)
        {
            bool wasGrounded = isGrounded;
            float radius = capsule.radius;
            float skin = radius * 0.1f; // 평지에 서 있을 때 구 아래 끝과 발 사이 간격
            Vector3 origin = rb.position + Vector3.up * radius;
            float castDistance = skin + Mathf.Max(groundCheckDistance + groundSnapDistance, GroundProbeDepth);
            bool hitSomething = Physics.SphereCast(origin, radius * 0.9f, Vector3.down, out var hit,
                castDistance, groundMask, QueryTriggerInteraction.Ignore);

            groundDistance = hitSomething ? Mathf.Max(0f, hit.distance - skin) : float.PositiveInfinity;
            float slope = hitSomething ? Vector3.Angle(hit.normal, Vector3.up) : 0f;
            // 점프 직후엔 아직 바닥이 가까워도 공중으로 취급
            bool justJumped = Time.time - lastJumpTime < 0.1f;
            bool touching = groundDistance <= groundCheckDistance && justJumped == false;

            bool walkable = touching && slope <= maxSlopeAngle;
            snapDownSpeed = 0f;
            if (walkable == false && CanSnapToGround(wasGrounded, justJumped, slope))
            {
                // 위치를 직접 옮기면(rb.position) 리지드바디 보간이 끊겨 몸이 한 프레임씩 툭 튄다 → 다음 물리 프레임에 닿을 만큼의 속도로 끌어내린다
                snapDownSpeed = Mathf.Min(groundDistance / Mathf.Max(dt, 0.0001f), MaxSnapDownSpeed);
                walkable = true;
            }

            isGrounded = walkable;
            // 85° 이상은 벽: 벽을 스치며 떨어질 때 미끄럼으로 잡히지 않게
            isSliding = walkable == false && touching && slope < 85f;
            groundNormal = walkable || isSliding ? hit.normal : Vector3.up;

            if (isGrounded)
            {
                lastGroundedTime = Time.time;
                jumpedSinceGrounded = false;
                if (wasGrounded == false) Landed?.Invoke();
            }

            // 지상·미끄럼에서는 속도를 직접 면에 붙여 주므로 중력을 끈다
            rb.useGravity = isGrounded == false && isSliding == false;
        }

        /// <summary>바로 전까지 지상이었고, 점프하지 않았고, 걸을 수 있는 땅이 스냅 거리 안에 있으면 붙인다</summary>
        private bool CanSnapToGround(bool wasGrounded, bool justJumped, float slope)
        {
            if (wasGrounded == false || justJumped || jumpedSinceGrounded) return false;
            if (groundSnapDistance <= 0f || float.IsInfinity(groundDistance)) return false;
            return groundDistance <= groundCheckDistance + groundSnapDistance && slope <= maxSlopeAngle;
        }

        /// <summary>
        /// 급경사 미끄럼: 면을 따라 내려가는 속도는 steepSlideMaxSpeed 까지만 붙고,
        /// 이동 입력은 좌우·내리막 쪽만 받는다 (오르막 성분은 지움 → 급경사를 기어오를 수 없음)
        /// </summary>
        private void UpdateSlide(float dt)
        {
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
            Vector3 downhillFlat = new Vector3(downhill.x, 0f, downhill.z).normalized;

            Vector3 steer = moveDirection * (walkSpeed * steepSlideSteer * ModifierProduct());
            float uphill = Vector3.Dot(steer, -downhillFlat);
            if (uphill > 0f) steer += downhillFlat * uphill;
            steer = Vector3.ProjectOnPlane(steer, groundNormal);

            Vector3 target = downhill * steepSlideMaxSpeed + steer;
            Vector3 velocity = Vector3.ProjectOnPlane(rb.linearVelocity, groundNormal);
            rb.linearVelocity = Vector3.MoveTowards(velocity, target, steepSlideAcceleration * dt);
        }

        private void UpdateWalk(float dt)
        {
            float speed = walkSpeed * (IsSprintingInput ? sprintMultiplier : 1f) * ModifierProduct() * FacingSpeedFactor();
            speed = Mathf.Clamp(speed, 0, maxWalkSpeed);
            Vector3 target = moveDirection * speed;

            Vector3 velocity = rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            float accel = isGrounded ? (target.sqrMagnitude > 0.0001f ? groundAcceleration : groundDeceleration) : airAcceleration;
            horizontal = Vector3.MoveTowards(horizontal, target, accel * dt);
            horizontal = SlideAlongWalls(horizontal, dt);

            if (isGrounded)
                rb.linearVelocity = AlongGround(horizontal) + Vector3.down * snapDownSpeed;
            else
                rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        /// <summary>
        /// 벽 미끄러짐 (collide and slide). 가려는 방향으로 캡슐을 미리 쏴서 가파른 면(벽)이 있으면
        /// 벽을 뚫는 성분을 지우고 벽과 평행한 성분만 남긴다. 모서리(벽 두 개)를 위해 두 번 반복.
        /// 매 물리 프레임 벽에 박았다 튕겨 나오며 속도가 0 ↔ 최대를 오가던 떨림(애니메이션 깜빡임)을 없앤다.
        /// 막혀서 못 간 몫은 버린다 (다음 프레임에 더하면 대각선에서 빨라진다)
        /// </summary>
        private Vector3 SlideAlongWalls(Vector3 horizontal, float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                float speed = horizontal.magnitude;
                if (speed < 0.0001f) return Vector3.zero;
                if (FindWall(horizontal / speed, speed * dt + wallProbeDistance, out Vector3 normal) == false) return horizontal;

                float into = Vector3.Dot(horizontal, normal);
                if (into >= 0f) return horizontal;
                horizontal -= normal * into;
            }
            // 모서리 안쪽처럼 두 벽에 다 막히면 정지
            float rest = horizontal.magnitude;
            if (rest > 0.0001f && FindWall(horizontal / rest, rest * dt + wallProbeDistance, out Vector3 last)
                && Vector3.Dot(horizontal, last) < 0f) return Vector3.zero;
            return horizontal;
        }

        /// <summary>direction 으로 distance 안에 있는 가장 가까운 벽(걸을 수 없는 가파른 면)의 수평 법선</summary>
        private bool FindWall(Vector3 direction, float distance, out Vector3 normal)
        {
            normal = Vector3.zero;
            float radius = capsule.radius * 0.95f;
            Vector3 center = rb.position + rb.rotation * capsule.center;
            float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            // 아래 구는 발밑 턱·경사에 걸리지 않게 조금 올린다
            Vector3 top = center + Vector3.up * half;
            Vector3 bottom = center - Vector3.up * Mathf.Max(0f, half - groundCheckDistance);

            int count = Physics.CapsuleCastNonAlloc(top, bottom, radius, direction, wallHits, distance, groundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = wallHits[i];
                if (hit.distance <= 0f || hit.distance >= nearest) continue;          // 시작부터 겹친 것은 물리가 밀어낸다
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle) continue;  // 걸을 수 있는 면은 벽이 아님
                Vector3 flat = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (flat.sqrMagnitude < 0.0001f) continue;
                nearest = hit.distance;
                normal = flat.normalized;
            }
            return float.IsPositiveInfinity(nearest) == false;
        }

        // IsSprinting 은 "지금 속도" 기준이라 출발 순간이 false → 입력 기준으로 따로 본다
        private bool IsSprintingInput => sprintRequested && CanSprint && isGrounded && moveDirection.sqrMagnitude > 0.0001f;

        private void UpdateRoll(float dt)
        {
            float t = (Time.time - rollStartTime) / rollDuration;
            if (t >= 1f)
            {
                isRolling = false;
                RollEnded?.Invoke();
                return;
            }

            Vector3 horizontal = rollDirection * (rollSpeed * Mathf.Max(0f, rollSpeedCurve.Evaluate(t)));
            horizontal = SlideAlongWalls(horizontal, dt);
            if (isGrounded)
            {
                rb.linearVelocity = AlongGround(horizontal) + Vector3.down * snapDownSpeed;
            }
            else
            {
                float y = rb.linearVelocity.y;
                rb.linearVelocity = new Vector3(horizontal.x, y, horizontal.z);
            }
        }

        /// <summary>최대 하강 속도. 공기 저항처럼 일정 속도 이상으로는 빨라지지 않게</summary>
        private void ClampFallSpeed()
        {
            if (isGrounded || maxFallSpeed <= 0f) return;
            Vector3 v = rb.linearVelocity;
            if (v.y >= -maxFallSpeed) return;
            v.y = -maxFallSpeed;
            rb.linearVelocity = v;
        }

        private void UpdateJump()
        {
            if (isRolling) return;
            if (Time.time - jumpRequestedTime > jumpBufferTime) return;

            bool canJump = isGrounded || (jumpedSinceGrounded == false && Time.time - lastGroundedTime <= coyoteTime);
            if (canJump == false) return;

            float gravity = Mathf.Abs(Physics.gravity.y);
            Vector3 v = rb.linearVelocity;
            v.y = Mathf.Sqrt(2f * gravity * jumpHeight);
            rb.linearVelocity = v;

            jumpRequestedTime = float.NegativeInfinity;
            jumpedSinceGrounded = true;
            lastJumpTime = Time.time;
            isGrounded = false;
            rb.useGravity = true;
            Jumped?.Invoke();
        }

        private void UpdateFacing() => rb.MoveRotation(Quaternion.Euler(0f, lockedYaw, 0f));
        #endregion

        #region 보조
        /// <summary>수평 속도를 크기는 유지한 채 경사면 접선 방향으로 바꾼다 (오르막에서 느려지지 않고, 내리막에서 바닥에 붙음)</summary>
        private Vector3 AlongGround(Vector3 horizontal)
        {
            float magnitude = horizontal.magnitude;
            if (magnitude < 0.0001f) return Vector3.zero;
            return Vector3.ProjectOnPlane(horizontal, groundNormal).normalized * magnitude;
        }

        /// <summary>몸 정면과 이동 방향이 어긋날수록 느려짐: 정면/옆 1.0 → 정반대 backwardMultiplier</summary>
        private float FacingSpeedFactor()
        {
            if (moveDirection.sqrMagnitude < 0.0001f) return 1f;
            float dot = Vector3.Dot(rb.rotation * Vector3.forward, moveDirection.normalized);
            return Mathf.Lerp(backwardMultiplier, 1f, Mathf.InverseLerp(-0.6f, 0.2f, dot));
        }

        private float ModifierProduct()
        {
            float product = 1f;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                var m = modifiers[i];
                if (m.EndTime >= 0f && Time.time >= m.EndTime) { modifiers.RemoveAt(i); continue; }
                product *= m.Multiplier;
            }
            return product;
        }

        private void RemoveModifier(string key)
        {
            for (int i = modifiers.Count - 1; i >= 0; i--)
                if (modifiers[i].Key == key) modifiers.RemoveAt(i);
        }
        #endregion
    }
}
