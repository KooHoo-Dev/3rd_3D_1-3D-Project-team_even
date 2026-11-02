using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 땅울림 (IPlayerGroundTremor, 보스 Tyranopode).
    ///  - 땅에 서 있을 때만 받는다. 점프로 공중에 있거나 구르기 무적 중이면 피한 것
    ///  - 받으면 화면을 흔들고, 총 퍼짐(GunSlot.TremorSpread → Gun.MovementSpread)에 더할 값을 정한다. 값은 duration 동안 선형으로 0 이 된다
    /// </summary>
    public partial class PlayerController : IPlayerGroundTremor
    {
        private float tremorSpread;   // 받은 순간의 추가 퍼짐(도)
        private float tremorStartTime;
        private float tremorDuration;

        /// <summary>지금 총 퍼짐에 더할 값(도). 땅울림이 없으면 0</summary>
        public float TremorSpread
        {
            get
            {
                if (tremorSpread <= 0f) return 0f;
                float t = tremorDuration > 0f ? (Time.time - tremorStartTime) / tremorDuration : 1f;
                if (t >= 1f)
                {
                    tremorSpread = 0f;
                    return 0f;
                }
                return tremorSpread * (1f - t);
            }
        }

        public bool ApplyGroundTremor(float extraSpread, float duration, in CameraShakeSettings shake)
        {
            if (IsAlive == false || IsInvulnerable || IsOnGround == false) return false;

            if (shake.Duration > 0f) Shake(shake);
            if (extraSpread > 0f && extraSpread >= TremorSpread)
            {
                tremorSpread = extraSpread;
                tremorStartTime = Time.time;
                tremorDuration = Mathf.Max(0f, duration);
            }
            return true;
        }

        private void SubscribeGroundTremor() => ServiceLocator.Register<IPlayerGroundTremor>(this);

        private void UnsubscribeGroundTremor()
        {
            ServiceLocator.Unregister<IPlayerGroundTremor>(this);
            tremorSpread = 0f;
        }
    }
}
