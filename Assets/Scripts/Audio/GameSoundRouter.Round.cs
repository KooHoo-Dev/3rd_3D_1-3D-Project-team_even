using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;

namespace HybridShooter.Audio
{
    /// <summary>게임 흐름: 강화 화면 스냅샷, 라운드 시작 BGM·아나운서, 게임오버</summary>
    public sealed partial class GameSoundRouter
    {
        /// <summary>강화 화면 동안만 Reward 스냅샷</summary>
        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current == GameState.Reward) service.TransitionToSnapshot(SoundService.RewardSnapshot);
            else if (e.Previous == GameState.Reward) service.TransitionToSnapshot(SoundService.DefaultSnapshot);
        }

        private void OnRoundStarted(RoundStartedEvent e)
        {
            StopHealthWarning();
            service.PlayBgm(crowdBgmKey);
            Announce(announceFightKey, 0f);
        }

        /// <summary>정리(다음 라운드·재시작) 때 저체력 경고·예약된 아나운서를 멈춘다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            StopHealthWarning();
            CancelAnnouncer();
        }

        private void OnGameOver(GameOverEvent e)
        {
            StopHealthWarning();
            service.StopBgm();
            service.PlaySfx(gameOverKey);
            Announce(announceGameOverKey, announcerDelay);
        }
    }
}
