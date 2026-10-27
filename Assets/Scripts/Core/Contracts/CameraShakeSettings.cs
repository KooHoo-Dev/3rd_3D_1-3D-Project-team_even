using System;
using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    /// <summary>
    /// 카메라 흔들림 한 건. CameraShakeRequestedEvent 로 보내거나 PlayerController.Shake 에 넘긴다.
    /// 화면(렌더)만 흔든다 → 조준선·IK·벽 충돌은 흔들리지 않은 카메라 기준. 멀미 방지 상한은 플레이어 쪽이 건다
    /// </summary>
    [Serializable]
    public struct CameraShakeSettings
    {
        [Tooltip("세기 0~1. 내부에서 제곱해서 쓴다 (약한 흔들림은 더 약하게)")]
        [Range(0f, 1f)] public float Intensity;
        [Tooltip("지속 시간(초). 0 이하 = StopShake 할 때까지 계속 (이벤트로는 받지 않는다)")]
        public float Duration;
        [Tooltip("흔들리는 빠르기(Hz). 낮을수록 묵직하다 (등장 ~4, 걸음 ~9)")]
        public float Frequency;
        [Tooltip("켜지는 시간(초)")]
        public float FadeIn;
        [Tooltip("꺼지는 시간(초). 끝나기 이 시간 전부터 줄어든다")]
        public float FadeOut;
        [Tooltip("진원 위치 (Radius > 0 일 때만 사용)")]
        public Vector3 Source;
        [Tooltip("0 = 거리 감쇠 없음. 그 외에는 진원에서 멀수록 약해지고 Radius 밖에서는 0")]
        public float Radius;

        public CameraShakeSettings(float intensity, float duration, float frequency, float fadeIn = 0.02f, float fadeOut = 0.2f)
        {
            Intensity = intensity;
            Duration = duration;
            Frequency = frequency;
            FadeIn = fadeIn;
            FadeOut = fadeOut;
            Source = Vector3.zero;
            Radius = 0f;
        }

        /// <summary>진원·감쇠 반경을 넣은 사본 (보스 발 위치 등)</summary>
        public CameraShakeSettings At(Vector3 source, float radius)
        {
            var copy = this;
            copy.Source = source;
            copy.Radius = radius;
            return copy;
        }

        /// <summary>세기만 바꾼 사본</summary>
        public CameraShakeSettings WithIntensity(float intensity)
        {
            var copy = this;
            copy.Intensity = intensity;
            return copy;
        }
    }

    /// <summary>자주 쓰는 흔들림 값. 그대로 쓰거나 At / WithIntensity 로 바꿔 쓴다</summary>
    public static class CameraShakePresets
    {
        /// <summary>거대 보스 등장 (포효·착지). 길고 묵직하게</summary>
        public static readonly CameraShakeSettings BossAppear = new CameraShakeSettings(0.8f, 1.6f, 4f, 0.15f, 0.8f);
        /// <summary>거대 보스 한 걸음. 짧게 쿵 (진원·반경은 At 으로)</summary>
        public static readonly CameraShakeSettings BossFootstep = new CameraShakeSettings(0.45f, 0.35f, 9f, 0.02f, 0.25f);
        /// <summary>짓밟기·착탄 등 강한 한 방</summary>
        public static readonly CameraShakeSettings Impact = new CameraShakeSettings(0.7f, 0.5f, 12f, 0.02f, 0.35f);
        /// <summary>가벼운 진동</summary>
        public static readonly CameraShakeSettings Light = new CameraShakeSettings(0.25f, 0.3f, 10f, 0.02f, 0.2f);
    }
}
