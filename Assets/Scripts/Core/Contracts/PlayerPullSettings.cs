using System;
using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    /// <summary>
    /// 플레이어 끌어당김 한 건 (보스 빨아들이기 등). IPlayerPullable.StartPull 에 넘긴다.
    /// 수평으로만 당긴다. 플레이어 이동 입력은 그대로 살아 있어 반대로 걸으면 덜 끌려간다 (저항).
    /// 방향은 플레이어가 매 물리 프레임 중심(Anchor 또는 Center) 쪽으로 다시 잡는다
    /// </summary>
    [Serializable]
    public struct PlayerPullSettings
    {
        [Tooltip("있으면 매 물리 프레임 이 위치로 당긴다 (보스 입 등). 이 오브젝트가 파괴되면 끌어당김도 끝난다")]
        public Transform Anchor;
        [Tooltip("Anchor 가 없을 때 중심 (월드)")]
        public Vector3 Center;
        [Tooltip("끌려가는 최대 속도(m/s)")]
        public float Speed;
        [Tooltip("중심에서 이 거리(m) 안으로는 당기지 않는다 (중심에서 떨리거나 지나치지 않게)")]
        public float StopRadius;
        [Tooltip("0 = 거리 무관하게 일정. 그 외에는 이 거리(m) 밖은 0, 안쪽으로 갈수록 세진다")]
        public float MaxRadius;
        [Tooltip("지속 시간(초). 0 이하 = StopPull 할 때까지 계속")]
        public float Duration;
        [Tooltip("켜지는 시간(초)")]
        public float FadeIn;
        [Tooltip("꺼지는 시간(초). 끝나기 이 시간 전부터 약해진다")]
        public float FadeOut;

        public PlayerPullSettings(float speed, float duration, float stopRadius, float maxRadius, float fadeIn = 0.3f, float fadeOut = 0.3f)
        {
            Anchor = null;
            Center = Vector3.zero;
            Speed = speed;
            StopRadius = stopRadius;
            MaxRadius = maxRadius;
            Duration = duration;
            FadeIn = fadeIn;
            FadeOut = fadeOut;
        }

        /// <summary>이 Transform 쪽으로 당기는 사본 (보스 입·손 등 움직이는 곳)</summary>
        public PlayerPullSettings Toward(Transform anchor)
        {
            var copy = this;
            copy.Anchor = anchor;
            return copy;
        }

        /// <summary>이 위치로 당기는 사본 (고정된 곳)</summary>
        public PlayerPullSettings Toward(Vector3 center)
        {
            var copy = this;
            copy.Anchor = null;
            copy.Center = center;
            return copy;
        }

        /// <summary>지속 시간만 바꾼 사본</summary>
        public PlayerPullSettings WithDuration(float duration)
        {
            var copy = this;
            copy.Duration = duration;
            return copy;
        }
    }

    /// <summary>자주 쓰는 끌어당김 값. Toward 로 중심을 넣어 쓴다</summary>
    public static class PlayerPullPresets
    {
        /// <summary>보스 빨아들이기. StopPull 할 때까지, 걸어서 버틸 수 있는 세기</summary>
        public static readonly PlayerPullSettings BossInhale = new PlayerPullSettings(4f, 0f, 2.5f, 30f, 0.4f, 0.4f);
        /// <summary>짧고 강한 당김 (갈고리·흡입 한 번)</summary>
        public static readonly PlayerPullSettings Yank = new PlayerPullSettings(9f, 0.6f, 2f, 0f, 0.05f, 0.2f);
    }
}
