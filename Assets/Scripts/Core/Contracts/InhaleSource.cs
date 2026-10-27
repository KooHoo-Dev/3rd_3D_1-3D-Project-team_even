using System;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    /// <summary>흡입이 끝난 이유. None = 아직 진행 중</summary>
    public enum InhaleEndReason
    {
        None,
        /// <summary>최대 지속 시간(MaxDuration) 경과</summary>
        Timeout,
        /// <summary>근처 범위(NearRadius) 안에 연속 NearDwellTime 체류</summary>
        NearDwell,
        /// <summary>보스가 Stop 으로 끝냄</summary>
        Stopped,
        /// <summary>중심으로 쓰던 Transform(보스 입 등)이 파괴됨</summary>
        AnchorLost,
        /// <summary>플레이어가 뗌 (처음부터 재시작·플레이어 비활성화)</summary>
        PlayerReset,
        /// <summary>시작할 때 플레이어가 없었다</summary>
        NoPlayer,
    }

    /// <summary>흡입 수치. 속도는 "플레이어 달리기 속도보다 얼마나 더 빠른가"로 정한다</summary>
    [Serializable]
    public struct InhaleSettings
    {
        [Tooltip("보스 근처 범위(m). 공격 범위보다 넓게. 이 안에서는 약하게 당기고 체류 시간을 센다")]
        public float NearRadius;
        [Tooltip("이 거리(m) 안으로는 당기지 않는다 (보스 몸통에 파묻히지 않게). 공격 범위 이하로")]
        public float StopRadius;
        [Tooltip("근처 범위 밖: 플레이어 달리기보다 이만큼(m/s) 빠르게 당긴다")]
        public float FarExtraSpeed;
        [Tooltip("근처 범위 안: 플레이어 달리기보다 이만큼(m/s) 빠르게 당긴다")]
        public float NearExtraSpeed;
        [Tooltip("최대 지속 시간(초). 0 이하 = 시간 제한 없음")]
        public float MaxDuration;
        [Tooltip("근처 범위 안에 연속 이만큼(초) 있으면 끝. 범위를 벗어나면 다시 0부터. 0 이하 = 끔")]
        public float NearDwellTime;
        [Tooltip("시작할 때 세기가 0에서 최대가 되기까지 걸리는 시간(초)")]
        public float FadeIn;

        /// <summary>기획서 "공기 흡입" 기준값 (거리 값은 보스 크기에 맞춰 조정)</summary>
        public static InhaleSettings Default => new InhaleSettings
        {
            NearRadius = 6f,
            StopRadius = 2.5f,
            FarExtraSpeed = 4f,
            NearExtraSpeed = 0.5f,
            MaxDuration = 10f,
            NearDwellTime = 2f,
            FadeIn = 0.3f,
        };
    }

    /// <summary>
    /// 보스 "공기 흡입" 기본 구현 (IPlayerVelocitySource).
    ///  - 근처 범위 밖은 달리기 + FarExtraSpeed, 안은 달리기 + NearExtraSpeed 로 중심 쪽으로 당긴다
    ///    → 반대로 달리면 ExtraSpeed 만큼씩만 다가가고, 가만히 있으면 달리기 + ExtraSpeed 로 끌려간다
    ///  - MaxDuration 경과 또는 근처 범위 안 연속 NearDwellTime 체류 시 저절로 끝나고, 이유를 EndReason 에 남긴다
    ///  - 체류 시간은 실제로 당기는 동안만 센다 (플레이어 쓰러짐 중에는 멈춤). 지속 시간은 실제 시간
    /// 사용: var inhale = InhaleSource.Start(settings, 입Transform); … if (inhale.IsRunning == false) { inhale.EndReason 으로 분기 }
    /// </summary>
    public sealed class InhaleSource : IPlayerVelocitySource
    {
        private readonly InhaleSettings settings;
        private readonly Transform anchor;
        private readonly bool hasAnchor;
        private readonly Vector3 fixedCenter;
        private readonly float startTime;
        private float endTime = -1f;
        private InhaleEndReason endReason;

        /// <summary>흡입 수치 (시작할 때 값)</summary>
        public InhaleSettings Settings => settings;
        /// <summary>끝난 이유. 진행 중이면 None</summary>
        public InhaleEndReason EndReason { get { CheckEnd(); return endReason; } }
        public bool IsRunning => EndReason == InhaleEndReason.None;
        /// <summary>시작 후 지난 시간(초). 끝났으면 끝난 시점까지</summary>
        public float Elapsed => (endTime >= 0f ? endTime : Time.time) - startTime;
        /// <summary>근처 범위 안에 연속으로 있었던 시간(초)</summary>
        public float NearStayTime { get; private set; }
        /// <summary>지난 물리 프레임 플레이어와 중심 사이 수평 거리(m). 아직 모르면 무한대</summary>
        public float Distance { get; private set; } = float.PositiveInfinity;
        /// <summary>지난 물리 프레임 플레이어가 근처 범위 안에 있었다</summary>
        public bool IsPlayerNear => Distance <= settings.NearRadius;

        bool IPlayerVelocitySource.IsFinished => IsRunning == false;

        private InhaleSource(in InhaleSettings settings, Transform anchor, Vector3 center)
        {
            this.settings = settings;
            this.anchor = anchor;
            hasAnchor = anchor != null;
            fixedCenter = center;
            startTime = Time.time;
        }

        /// <summary>이 Transform(보스 입 등, 움직여도 따라감) 쪽으로 흡입 시작. 플레이어가 없으면 바로 NoPlayer 로 끝난 상태로 돌려준다 (null 아님)</summary>
        public static InhaleSource Start(in InhaleSettings settings, Transform anchor) => Attach(new InhaleSource(settings, anchor, Vector3.zero));

        /// <summary>고정된 위치 쪽으로 흡입 시작</summary>
        public static InhaleSource Start(in InhaleSettings settings, Vector3 center) => Attach(new InhaleSource(settings, null, center));

        /// <summary>보스가 직접 끝낸다 (보스 사망·패턴 취소·OnDisable). 이미 끝났으면 아무 일 없음</summary>
        public void Stop() => End(InhaleEndReason.Stopped);

        private static InhaleSource Attach(InhaleSource source)
        {
            if (ServiceLocator.TryGet(out IPlayerExternalVelocity player) == false || player.AddVelocitySource(source) == false)
                source.End(InhaleEndReason.NoPlayer);
            return source;
        }

        Vector3 IPlayerVelocitySource.GetVelocity(in PlayerMotionContext context)
        {
            if (IsRunning == false) return Vector3.zero;

            Vector3 toCenter = (hasAnchor ? anchor.position : fixedCenter) - context.Position;
            toCenter.y = 0f;
            float distance = toCenter.magnitude;
            Distance = distance;

            bool near = distance <= settings.NearRadius;
            if (near)
            {
                NearStayTime += context.DeltaTime;
                if (settings.NearDwellTime > 0f && NearStayTime >= settings.NearDwellTime)
                {
                    End(InhaleEndReason.NearDwell);
                    return Vector3.zero;
                }
            }
            else NearStayTime = 0f;

            float reach = distance - settings.StopRadius;
            if (reach <= 0f || distance < 0.0001f) return Vector3.zero;

            float extra = near ? settings.NearExtraSpeed : settings.FarExtraSpeed;
            float fadeIn = settings.FadeIn > 0f ? Mathf.Clamp01(Elapsed / settings.FadeIn) : 1f;
            float speed = Mathf.Max(0f, context.RunSpeed + extra) * fadeIn;
            // 이번 프레임에 StopRadius 를 넘어가지 않을 만큼만 (중심에서 떨리지 않게)
            speed = Mathf.Min(speed, reach / Mathf.Max(context.DeltaTime, 0.0001f));
            return toCenter / distance * speed;
        }

        void IPlayerVelocitySource.OnRemovedByPlayer() => End(InhaleEndReason.PlayerReset);

        private void CheckEnd()
        {
            if (endReason != InhaleEndReason.None) return;
            if (hasAnchor && anchor == null) End(InhaleEndReason.AnchorLost);
            else if (settings.MaxDuration > 0f && Time.time - startTime >= settings.MaxDuration)
            {
                endReason = InhaleEndReason.Timeout;
                endTime = startTime + settings.MaxDuration;
            }
        }

        private void End(InhaleEndReason reason)
        {
            if (endReason != InhaleEndReason.None) return;
            endReason = reason;
            endTime = Time.time;
        }
    }
}
