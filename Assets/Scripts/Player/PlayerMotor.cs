using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>몸체 회전 방식. 3인칭 = 이동 방향을 바라봄, 1인칭 = 카메라 방향에 고정</summary>
    public enum FacingMode { FollowMove, LockYaw }

    /// <summary>
    /// Rigidbody + CapsuleCollider 기반 이동. 입력·카메라·스태미나를 전혀 모른다.
    /// 바깥에서 "월드 기준 이동 방향", "달리기 여부", "바라볼 방식"만 넣어주면 된다. (PlayerController 가 중개)
    ///
    /// 구현 요약
    ///  - 터레인 위: SphereCast 로 바닥·경사를 판정, 걷기는 경사면을 따라 속도를 투영한다. 지상에서는 중력을 꺼서 미끄러짐/들썩임 방지.
    ///  - 마찰 0 콜라이더 → 벽에 비비며 걸려도 붙지 않음.
    ///  - 감속/가속: 이름(key)이 있는 속도 배율 목록. 곱해서 적용한다. (피격 둔화, 스태미나 바닥, 상태이상 등)
    ///    + 자동: 몸이 이동 방향과 어긋나 있을 때(뒤로 걷기, 뒤돌아 출발) 느려짐.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("이동")]
        [Tooltip("걷기 속도(m/s). PlayerController 가 PlayerTable.MoveSpeed 로 덮어쓴다")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float groundAcceleration = 45f;
        [SerializeField] private float groundDeceleration = 55f;
        [SerializeField] private float airAcceleration = 10f;
        [Tooltip("3인칭에서 몸이 이동 방향으로 도는 속도(도/초)")]
        [SerializeField] private float turnSpeed = 720f;
        [Tooltip("몸 정면 기준 정반대로 움직일 때의 속도 배율. 옆걸음은 거의 감속 없음")]
        [SerializeField, Range(0.1f, 1f)] private float backwardMultiplier = 0.65f;

        [Header("점프")]
        [SerializeField] private float jumpHeight = 1.2f;
        [Tooltip("발이 떨어진 직후에도 점프를 허용하는 시간")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("착지 직전에 누른 점프를 기억해 주는 시간")]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("구르기")]
        [SerializeField] private float rollSpeed = 9f;
        [SerializeField] private float rollDuration = 0.45f;
        [SerializeField] private float rollCooldown = 0.35f;
        [Tooltip("구르기 시작 후 이 시간 동안 무적")]
        [SerializeField] private float rollInvulnerableTime = 0.3f;
        [Tooltip("구르기 진행도(0~1) → 속도 배율. 처음 빠르고 끝에서 느려짐")]
        [SerializeField] private AnimationCurve rollSpeedCurve = new AnimationCurve(new Keyframe(0f, 1.4f), new Keyframe(1f, 0.4f));

        [Header("바닥 판정")]
        [Tooltip("플레이어 자신 레이어는 빼 두는 것을 권장")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundCheckDistance = 0.15f;
        [SerializeField] private float maxSlopeAngle = 50f;

        private Rigidbody rb;
        private CapsuleCollider capsule;

        // 외부 입력
        private Vector3 moveDirection;
        private bool sprintRequested;
        private FacingMode facingMode = FacingMode.FollowMove;
        private float lockedYaw;
        private float jumpRequestedTime = float.NegativeInfinity;

        // 상태
        private bool isGrounded;
        private Vector3 groundNormal = Vector3.up;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpTime = float.NegativeInfinity;
        private bool jumpedSinceGrounded;

        private bool isRolling;
        private Vector3 rollDirection;
        private float rollStartTime;
        private float rollReadyTime;

        private struct SpeedModifier { public string Key; public float Multiplier; public float EndTime; }
        private readonly List<SpeedModifier> modifiers = new List<SpeedModifier>();

        public event Action Jumped;
        public event Action Landed;
        /// <summary>인자 = 구르는 월드 방향</summary>
        public event Action<Vector3> RollStarted;
        public event Action RollEnded;

        public bool IsGrounded => isGrounded;
        public bool IsRolling => isRolling;
        public bool IsInvulnerable => isRolling && Time.time - rollStartTime < rollInvulnerableTime;
        public bool IsSprinting => sprintRequested && isGrounded && isRolling == false && HorizontalSpeed > 0.1f;
        public bool CanRoll => isRolling == false && isGrounded && Time.time >= rollReadyTime;
        public Vector3 Velocity => rb.linearVelocity;
        public float HorizontalSpeed { get { var v = rb.linearVelocity; return new Vector2(v.x, v.z).magnitude; } }
        public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }
        public Vector3 Forward => rb.rotation * Vector3.forward;
        private Animator animator;
        private bool hasAnimator => animator != null;
        private float animationBlend;
        
        
        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();

            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            animator = GetComponentInChildren<Animator>();
            capsule.sharedMaterial = new PhysicsMaterial("PlayerNoFriction")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            AssignAnimationIDs();
        }

        // ───────────────────────── 외부에서 넣는 값 ─────────────────────────

        /// <summary>worldDirection: 월드 기준, 수평, 크기 0~1 (스틱 기울기 반영)</summary>
        public void SetMoveInput(Vector3 worldDirection, bool sprint)
        {
            worldDirection.y = 0f;
            moveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
            sprintRequested = sprint;
        }

        /// <summary>1인칭: 몸을 이 방향(yaw)에 고정</summary>
        public void LockFacing(float yaw) { facingMode = FacingMode.LockYaw; lockedYaw = yaw; }

        /// <summary>3인칭: 이동(구르기) 방향으로 몸을 돌림</summary>
        public void FollowMoveFacing() => facingMode = FacingMode.FollowMove;

        public void RequestJump() => jumpRequestedTime = Time.time;

        /// <summary>방향은 호출 쪽이 정한다 (4방향 스냅 등). 지상이 아니거나 쿨다운이면 false</summary>
        public bool TryRoll(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (CanRoll == false || worldDirection.sqrMagnitude < 0.0001f) return false;

            isRolling = true;
            rollDirection = worldDirection.normalized;
            rollStartTime = Time.time;
            rollReadyTime = Time.time + rollDuration + rollCooldown;
            RollStarted?.Invoke(rollDirection);
            return true;
        }

        /// <summary>
        /// 지속되는 속도 배율 (스태미나 바닥 등). 같은 key 는 덮어쓰고, 1 이면 해제.
        /// </summary>
        public void SetSpeedModifier(string key, float multiplier)
        {
            RemoveModifier(key);
            if (Mathf.Approximately(multiplier, 1f) == false)
                modifiers.Add(new SpeedModifier { Key = key, Multiplier = multiplier, EndTime = -1f });
        }

        /// <summary>
        /// 시간제한 속도 배율. multiplier &lt; 1 감속, &gt; 1 가속. 같은 key 를 다시 걸면 시간이 갱신된다 (피격 연타 시 중첩되지 않음)
        /// </summary>
        public void AddTimedSpeedModifier(string key, float multiplier, float duration)
        {
            RemoveModifier(key);
            modifiers.Add(new SpeedModifier { Key = key, Multiplier = multiplier, EndTime = Time.time + duration });
        }

        public void RemoveSpeedModifier(string key) => RemoveModifier(key);

        public void Teleport(Vector3 position)
        {
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            isRolling = false;
        }

        // ───────────────────────── 물리 ─────────────────────────

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            UpdateGround();

            if (isRolling) UpdateRoll(dt);
            else UpdateWalk(dt);

            UpdateJump();
            UpdateFacing(dt);
        }

        private void UpdateGround()
        {
            bool wasGrounded = isGrounded;
            float radius = capsule.radius;
            Vector3 origin = rb.position + Vector3.up * radius;
            bool hitSomething = Physics.SphereCast(origin, radius * 0.9f, Vector3.down, out var hit,
                groundCheckDistance + radius * 0.1f, groundMask, QueryTriggerInteraction.Ignore);

            bool walkable = hitSomething && Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle;
            // 점프 직후엔 아직 바닥이 가까워도 공중으로 취급
            if (Time.time - lastJumpTime < 0.1f) walkable = false;

            isGrounded = walkable;
            groundNormal = walkable ? hit.normal : Vector3.up;

            if (isGrounded)
            {
                lastGroundedTime = Time.time;
                jumpedSinceGrounded = false;
                if (wasGrounded == false) Landed?.Invoke();
            }

            rb.useGravity = isGrounded == false;
        }

        private void UpdateWalk(float dt)
        {
            float speed = walkSpeed * (IsSprintingInput ? sprintMultiplier : 1f) * ModifierProduct() * FacingSpeedFactor();
            Vector3 target = moveDirection * speed;

            Vector3 velocity = rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            float accel = isGrounded ? (target.sqrMagnitude > 0.0001f ? groundAcceleration : groundDeceleration) : airAcceleration;
            horizontal = Vector3.MoveTowards(horizontal, target, accel * dt);

            if (isGrounded)
                rb.linearVelocity = AlongGround(horizontal);
            else
                rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        // IsSprinting 은 "지금 속도" 기준이라 출발 순간이 false → 입력 기준으로 따로 본다
        private bool IsSprintingInput => sprintRequested && isGrounded && moveDirection.sqrMagnitude > 0.0001f;

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
            if (isGrounded)
            {
                rb.linearVelocity = AlongGround(horizontal);
            }
            else
            {
                float y = rb.linearVelocity.y;
                rb.linearVelocity = new Vector3(horizontal.x, y, horizontal.z);
            }
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

        private void UpdateFacing(float dt)
        {
            if (facingMode == FacingMode.LockYaw)
            {
                rb.MoveRotation(Quaternion.Euler(0f, lockedYaw, 0f));
                return;
            }

            Vector3 dir = isRolling ? rollDirection : moveDirection;
            if (dir.sqrMagnitude < 0.01f) return;

            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(rb.rotation.eulerAngles.y, targetYaw, turnSpeed * dt);
            rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
        }

        // ───────────────────────── 보조 ─────────────────────────

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

        public void UpdateAnimation()
        {
            // update animator if using character
            
            // animationBlend = Mathf.Lerp(animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            // if (animationBlend < 0.01f) animationBlend = 0f;
            // if (hasAnimator)
            // {
            //     animator.SetFloat(_animIDSpeed, animationBlend);
            //     animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            // }
        }
        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

    }
}
