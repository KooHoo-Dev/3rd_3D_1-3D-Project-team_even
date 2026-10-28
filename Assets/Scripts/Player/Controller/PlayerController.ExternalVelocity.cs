using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 외부 속도 (IPlayerExternalVelocity, 보스 흡입 등).
    ///  - 붙어 있는 소스(IPlayerVelocitySource)를 매 물리 프레임 불러, 모터(걷기·구르기·미끄럼)가 정한 속도 위에 결과를 "더한다".
    ///    이동 입력은 살아 있어 반대로 달리면 버틴다. 계산은 소스(보스)가 하고, 벽·경사·상한·적용 여부는 여기서 지킨다
    ///  - 누적 방지: 모터는 지난 프레임 rb 속도에서 출발하므로, 모터 전에 지난 프레임에 더한 몫(appliedExternal)을 뺀다
    ///    (빼지 않으면 가감속이 매 프레임 일부만 지우고 또 더해져 속도가 끝없이 커진다)
    ///  - 합계 상한 maxExternalSpeed. 벽을 파고들지 않게 SlideAlongWalls, 급경사 미끄럼에서는 오르막 성분을 지운다
    ///  - 쓰러짐·부활 연출·외부 제어(벽넘기) 중에는 소스를 부르지 않는다 (붙은 상태는 유지). 처음부터 재시작·비활성화 때는 모두 뗀다
    ///  - 소스가 예외를 던지면 로그를 남기고 그 소스만 뗀다 (플레이어 물리는 계속)
    /// </summary>
    public partial class PlayerController
    {
        #region 설정(인스펙터)
        [Header("외부 속도 (보스 흡입 등)")]
        [Tooltip("모든 외부 속도를 합친 수평 속도 상한(m/s). 달리기 속도 + 흡입 추가 속도보다 크게")]
        [SerializeField] private float maxExternalSpeed = 20f;
        #endregion

        private readonly List<IPlayerVelocitySource> velocitySources = new List<IPlayerVelocitySource>();
        private Vector3 appliedExternal; // 지난 물리 프레임에 rb 속도에 더한 몫

        /// <summary>외부 속도 소스가 하나라도 붙어 있다 (쓰러짐 등으로 지금 적용되지 않는 중이어도 true)</summary>
        public bool HasExternalVelocity => velocitySources.Count > 0;
        /// <summary>지난 물리 프레임에 실제로 더한 외부 속도 (벽·경사 반영 뒤). Velocity 에 이미 포함되어 있다</summary>
        public Vector3 ExternalVelocity => appliedExternal;
        /// <summary>기준 달리기 속도(m/s): 걷기 × 달리기 배율 × 속도 배율, maxWalkSpeed 상한. 스태미나·뒷걸음 감속 제외</summary>
        public float RunSpeed => Mathf.Min(walkSpeed * sprintMultiplier * ModifierProduct(), maxWalkSpeed);

        public bool AddVelocitySource(IPlayerVelocitySource source)
        {
            if (source == null || velocitySources.Contains(source)) return false;
            velocitySources.Add(source);
            return true;
        }

        public void RemoveVelocitySource(IPlayerVelocitySource source) => velocitySources.Remove(source);

        /// <summary>모두 떼고 각 소스에 알린다 (처음부터 재시작·비활성화)</summary>
        private void ClearVelocitySources()
        {
            if (velocitySources.Count == 0) return;
            // 알림 중에 목록이 바뀌어도 안전하게 먼저 비운다
            var removed = velocitySources.ToArray();
            velocitySources.Clear();
            foreach (var source in removed) NotifyRemoved(source);
        }

        private void SubscribeExternalVelocityEvents() => ServiceLocator.Register<IPlayerExternalVelocity>(this);

        private void UnsubscribeExternalVelocityEvents()
        {
            ServiceLocator.Unregister<IPlayerExternalVelocity>(this);
            ClearVelocitySources();
        }

        /// <summary>FixedUpdate, 모터 전: 지난 프레임에 더한 몫을 뺀다. 벽 등으로 이미 줄었으면 남은 만큼만 (반대로 튕기지 않게)</summary>
        private void RemoveAppliedExternal()
        {
            float magnitude = appliedExternal.magnitude;
            Vector3 direction = magnitude < 0.0001f ? Vector3.zero : appliedExternal / magnitude;
            appliedExternal = Vector3.zero;
            if (magnitude < 0.0001f) return;

            Vector3 velocity = rb.linearVelocity;
            float along = Vector3.Dot(velocity, direction);
            rb.linearVelocity = velocity - direction * Mathf.Clamp(along, 0f, magnitude);
        }

        /// <summary>FixedUpdate, 모터 뒤: 이번 프레임 외부 속도를 더한다</summary>
        private void ApplyExternal(float dt)
        {
            Vector3 external = SumExternal(dt, IsDead == false && isReviving == false);
            if (external.sqrMagnitude < 0.000001f) return;

            if (isSliding)
            {
                // UpdateSlide 의 이동 입력처럼 오르막 성분은 지운다
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                Vector3 downhillFlat = new Vector3(downhill.x, 0f, downhill.z).normalized;
                float uphill = Vector3.Dot(external, -downhillFlat);
                if (uphill > 0f) external += downhillFlat * uphill;
            }

            external = SlideAlongWalls(external, dt);
            if (isGrounded) external = AlongGround(external);
            else if (isSliding) external = Vector3.ProjectOnPlane(external, groundNormal);

            rb.linearVelocity += external;
            appliedExternal = external;
        }

        /// <summary>
        /// 붙은 소스의 수평 속도 합 (상한 maxExternalSpeed). 끝났거나 파괴된 소스는 뗀다.
        /// apply = false(쓰러짐·부활 연출)면 떼기만 하고 GetVelocity 는 부르지 않는다
        /// </summary>
        private Vector3 SumExternal(float dt, bool apply)
        {
            if (velocitySources.Count == 0) return Vector3.zero;

            var context = new PlayerMotionContext(rb.position, rb.linearVelocity, moveDirection, RunSpeed, isGrounded, dt);
            Vector3 sum = Vector3.zero;

            for (int i = velocitySources.Count - 1; i >= 0; i--)
            {
                if (i >= velocitySources.Count) continue; // 소스가 호출 중에 다른 소스를 뗐을 때
                var source = velocitySources[i];
                if (source is UnityEngine.Object unityObject && unityObject == null)
                {
                    velocitySources.RemoveAt(i);
                    continue;
                }

                Vector3 velocity;
                try
                {
                    if (source.IsFinished)
                    {
                        velocitySources.Remove(source);
                        continue;
                    }
                    if (apply == false) continue;
                    velocity = source.GetVelocity(context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    velocitySources.Remove(source);
                    NotifyRemoved(source);
                    continue;
                }

                velocity.y = 0f;
                if (float.IsFinite(velocity.x) && float.IsFinite(velocity.z)) sum += velocity;
            }

            return Vector3.ClampMagnitude(sum, maxExternalSpeed);
        }

        private static void NotifyRemoved(IPlayerVelocitySource source)
        {
            if (source is UnityEngine.Object unityObject && unityObject == null) return;
            try { source.OnRemovedByPlayer(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
