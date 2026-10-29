using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>UI 피드백: 저체력 경고 반복(BL-007), 아나운서 (최소 사용 — 라운드 시작·보스 처치·게임오버)</summary>
    public sealed partial class GameSoundRouter
    {
        private bool lowHealth;
        private float nextHealthWarning;

        private string pendingAnnounceKey;
        private float pendingAnnounceTime;

        /// <summary>체력 비율이 LowHpRatio 이하로 내려가면 바로 한 번 울리고, 회복하거나 죽을 때까지 반복</summary>
        private void OnPlayerHpChanged(PlayerHpChangedEvent e)
        {
            bool low = e.Current > 0 && e.Ratio <= DataManager.Config.GetFloat(GameConfig.LowHpRatio, 0.3f);
            if (low && lowHealth == false) nextHealthWarning = 0f;
            lowHealth = low;
        }

        private void OnPlayerDied(PlayerDiedEvent e) => StopHealthWarning();

        private void UpdateHealthWarning()
        {
            if (lowHealth == false || Time.unscaledTime < nextHealthWarning) return;
            nextHealthWarning = Time.unscaledTime + healthWarningInterval;
            service.PlaySfx(healthWarningKey);
        }

        private void StopHealthWarning() => lowHealth = false;

        /// <summary>아나운서 재생 예약. 새 예약이 오면 이전 예약은 버린다 (한 번에 한 마디)</summary>
        private void Announce(string key, float delay)
        {
            if (useAnnouncer == false || string.IsNullOrEmpty(key)) return;
            pendingAnnounceKey = key;
            pendingAnnounceTime = Time.unscaledTime + delay;
        }

        private void UpdateAnnouncer()
        {
            if (pendingAnnounceKey == null || Time.unscaledTime < pendingAnnounceTime) return;
            service.PlaySfx(pendingAnnounceKey);
            pendingAnnounceKey = null;
        }

        private void CancelAnnouncer() => pendingAnnounceKey = null;
    }
}
