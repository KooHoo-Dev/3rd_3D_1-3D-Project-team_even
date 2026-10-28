using HybridShooter.Core.Data;
using HybridShooter.Core.Events;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 보스 전역 연출: 경고(경고음·덕킹)·등장(보스 BGM)·처치(BGM 정지·클리어·아나운서).
    /// 이벤트에 담긴 보스 정보는 읽지 않는다 (보스 정보 타입이 바뀌어도 영향 없음).
    /// 보스 몸 소리(패턴 예고·흡입 루프·타격·사망)는 보스 프리팹의 BossSoundEmitter 담당
    /// </summary>
    public sealed partial class GameSoundRouter
    {
        private void OnBossWarning(BossWarningEvent e)
        {
            service.PlaySfx(bossWarningKey);
            service.Duck(SoundCategory.Monster, bossDuckLevel, bossDuckTime);
        }

        private void OnBossAppeared(BossAppearedEvent e) => service.PlayBgm(bossBgmKey);

        /// <summary>BGM 정지 + 스테이지 클리어 → 잠시 뒤 아나운서 승리</summary>
        private void OnBossDefeated(BossDefeatedEvent e)
        {
            service.StopBgm();
            service.PlaySfx(stageClearKey);
            Announce(announceVictoryKey, announcerDelay);
        }
    }
}
