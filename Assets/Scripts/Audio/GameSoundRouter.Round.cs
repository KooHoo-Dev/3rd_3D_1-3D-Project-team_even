using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;

namespace HybridShooter.Audio
{
    /// <summary>게임 흐름: 강화 화면 스냅샷·곡, 라운드 시작 곡·아나운서, 게임오버</summary>
    public sealed partial class GameSoundRouter
    {
        /// <summary>강화 화면 동안만 Reward 스냅샷 + 업그레이드 곡</summary>
        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current == GameState.Reward) service.TransitionToSnapshot(SoundService.RewardSnapshot);
            else if (e.Previous == GameState.Reward) service.TransitionToSnapshot(SoundService.DefaultSnapshot);
            SetRewardBgm(e.Current == GameState.Reward);
        }

        private void OnRoundStarted(RoundStartedEvent e)
        {
            StopHealthWarning();
            SetRoundBgm(e.Def);
            Announce(announceFightKey, 0f);
        }

        /// <summary>정리(다음 라운드·재시작) 때 저체력 경고·예약된 아나운서를 멈춘다. 재시작이면 곡도 씬 기본으로</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            StopHealthWarning();
            CancelAnnouncer();
            if (e.FullReset) ResetBgmForRestart();
        }

        private void OnGameOver(GameOverEvent e)
        {
            StopHealthWarning();
            ClearBgmForGameOver();
            service.PlaySfx(gameOverKey);
            Announce(announceGameOverKey, announcerDelay);
        }
    }
}
