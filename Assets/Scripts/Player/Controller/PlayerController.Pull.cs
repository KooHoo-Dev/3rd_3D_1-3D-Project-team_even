using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 끌어당김 (IPlayerPullable, 보스 빨아들이기 등).
    ///  - 모터(걷기·구르기·미끄럼)가 정한 속도 위에 수평 속도를 "더한다". 이동 입력은 살아 있어 반대로 걸으면 버틴다
    ///  - 누적 방지: 모터는 지난 프레임 rb 속도에서 출발하므로, 모터 전에 지난 프레임에 더한 몫(appliedPull)을 뺀다
    ///    (빼지 않으면 가감속이 매 프레임 일부만 지우고 또 더해져 속도가 끝없이 커진다)
    ///  - 속도 배율(SpeedModifier)·maxWalkSpeed 와 무관. 대신 합계 상한 maxPullSpeed
    ///  - 벽을 파고들지 않게 SlideAlongWalls, 급경사 미끄럼에서는 오르막 성분을 지운다 (끌려서 급경사를 오르지 않게)
    ///  - 쓰러짐·부활 연출·외부 제어(벽넘기) 중에는 적용하지 않는다 (목록은 유지). 처음부터 재시작·비활성화 때는 모두 지운다
    /// </summary>
    public partial class PlayerController
    {
        #region 설정(인스펙터)
        [Header("끌어당김")]
        [Tooltip("모든 끌어당김을 합친 수평 속도 상한(m/s)")]
        [SerializeField] private float maxPullSpeed = 10f;
        #endregion

        private struct ActivePull
        {
            public int Handle;
            public PlayerPullSettings Settings;
            public float StartTime;
            public bool HasAnchor; // Anchor 로 시작했는지 (파괴되어 null 이 되면 끝낸다)
        }

        private readonly List<ActivePull> pulls = new List<ActivePull>();
        private int nextPullHandle = 1;
        private Vector3 appliedPull; // 지난 물리 프레임에 rb 속도에 더한 몫

        /// <summary>끌어당김이 하나라도 걸려 있다 (쓰러짐 등으로 지금 적용되지 않는 중이어도 true)</summary>
        public bool IsBeingPulled => pulls.Count > 0;
        /// <summary>지난 물리 프레임에 실제로 더한 끌어당김 속도 (벽·경사 반영 뒤). Velocity 에 이미 포함되어 있다</summary>
        public Vector3 PullVelocity => appliedPull;

        public int StartPull(in PlayerPullSettings settings)
        {
            if (settings.Speed <= 0f) return 0;
            int handle = nextPullHandle++;
            pulls.Add(new ActivePull
            {
                Handle = handle,
                Settings = settings,
                StartTime = Time.time,
                HasAnchor = settings.Anchor != null,
            });
            return handle;
        }

        public void SetPullCenter(int handle, Vector3 worldCenter)
        {
            for (int i = 0; i < pulls.Count; i++)
            {
                if (pulls[i].Handle != handle) continue;
                var pull = pulls[i];
                pull.Settings.Anchor = null;
                pull.Settings.Center = worldCenter;
                pull.HasAnchor = false;
                pulls[i] = pull;
                return;
            }
        }

        public void StopPull(int handle, float fadeOut = 0.2f)
        {
            for (int i = 0; i < pulls.Count; i++)
            {
                if (pulls[i].Handle != handle) continue;
                var pull = pulls[i];
                float elapsed = Time.time - pull.StartTime;
                fadeOut = Mathf.Max(0f, fadeOut);
                if (pull.Settings.Duration > 0f && elapsed + fadeOut >= pull.Settings.Duration) return; // 이미 그보다 빨리 끝난다
                pull.Settings.Duration = elapsed + fadeOut;
                pull.Settings.FadeOut = fadeOut;
                if (fadeOut <= 0f) pulls.RemoveAt(i);
                else pulls[i] = pull;
                return;
            }
        }

        public void StopAllPulls() => pulls.Clear();

        private void SubscribePullEvents() => ServiceLocator.Register<IPlayerPullable>(this);

        private void UnsubscribePullEvents()
        {
            ServiceLocator.Unregister<IPlayerPullable>(this);
            StopAllPulls();
        }

        /// <summary>FixedUpdate, 모터 전: 지난 프레임에 더한 몫을 뺀다. 벽 등으로 이미 줄었으면 남은 만큼만 (반대로 튕기지 않게)</summary>
        private void RemoveAppliedPull()
        {
            float magnitude = appliedPull.magnitude;
            Vector3 direction = magnitude < 0.0001f ? Vector3.zero : appliedPull / magnitude;
            appliedPull = Vector3.zero;
            if (magnitude < 0.0001f) return;

            Vector3 velocity = rb.linearVelocity;
            float along = Vector3.Dot(velocity, direction);
            rb.linearVelocity = velocity - direction * Mathf.Clamp(along, 0f, magnitude);
        }

        /// <summary>FixedUpdate, 모터 뒤: 이번 프레임 끌어당김을 더한다</summary>
        private void ApplyPull(float dt)
        {
            Vector3 pull = SumPulls(dt);
            if (pull.sqrMagnitude < 0.000001f || IsDead || isReviving) return;

            if (isSliding)
            {
                // UpdateSlide 의 이동 입력처럼 오르막 성분은 지운다
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
                Vector3 downhillFlat = new Vector3(downhill.x, 0f, downhill.z).normalized;
                float uphill = Vector3.Dot(pull, -downhillFlat);
                if (uphill > 0f) pull += downhillFlat * uphill;
            }

            pull = SlideAlongWalls(pull, dt);
            if (isGrounded) pull = AlongGround(pull);
            else if (isSliding) pull = Vector3.ProjectOnPlane(pull, groundNormal);

            rb.linearVelocity += pull;
            appliedPull = pull;
        }

        /// <summary>살아 있는 끌어당김의 수평 속도 합 (상한 maxPullSpeed). 끝났거나 Anchor 가 파괴된 것은 지운다</summary>
        private Vector3 SumPulls(float dt)
        {
            if (pulls.Count == 0) return Vector3.zero;

            float now = Time.time;
            Vector3 position = rb.position;
            Vector3 sum = Vector3.zero;

            for (int i = pulls.Count - 1; i >= 0; i--)
            {
                var pull = pulls[i];
                var s = pull.Settings;
                float elapsed = now - pull.StartTime;
                if ((s.Duration > 0f && elapsed >= s.Duration) || (pull.HasAnchor && s.Anchor == null))
                {
                    pulls.RemoveAt(i);
                    continue;
                }

                Vector3 center = pull.HasAnchor ? s.Anchor.position : s.Center;
                Vector3 toCenter = center - position;
                toCenter.y = 0f;
                float distance = toCenter.magnitude;
                float reach = distance - s.StopRadius;
                if (reach <= 0f || distance < 0.0001f) continue;
                if (s.MaxRadius > 0f && distance >= s.MaxRadius) continue;

                float falloff = s.MaxRadius > s.StopRadius ? 1f - Mathf.InverseLerp(s.StopRadius, s.MaxRadius, distance) : 1f;
                // 이번 프레임에 StopRadius 를 넘어가지 않을 만큼만
                float speed = Mathf.Min(s.Speed * PullEnvelope(s, elapsed) * falloff, reach / Mathf.Max(dt, 0.0001f));
                sum += toCenter / distance * speed;
            }

            return Vector3.ClampMagnitude(sum, maxPullSpeed);
        }

        /// <summary>켜짐(FadeIn)·꺼짐(FadeOut) 중 작은 쪽. Duration 0 이하면 꺼짐 없음</summary>
        private static float PullEnvelope(in PlayerPullSettings s, float elapsed)
        {
            float fadeIn = s.FadeIn > 0f ? Mathf.Clamp01(elapsed / s.FadeIn) : 1f;
            float fadeOut = s.Duration > 0f && s.FadeOut > 0f ? Mathf.Clamp01((s.Duration - elapsed) / s.FadeOut) : 1f;
            return Mathf.Min(fadeIn, fadeOut);
        }
    }
}
