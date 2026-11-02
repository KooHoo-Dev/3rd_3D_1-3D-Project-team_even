using HybridShooter.Boss.Tyranopode;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 보스 전역 연출: 경고(경고음·덕킹)·등장(보스 BGM·몸 소리 이미터 부착)·처치(BGM 정지·클리어·아나운서).
    /// 이벤트에 담긴 보스 정보(Def)는 읽지 않는다 (보스 정보 타입이 바뀌어도 영향 없음).
    /// 보스 몸 소리는 이미터 담당: 클리페오사우루스 = 프리팹의 BossSoundEmitter, 티라노포드 = 여기서 붙이는 TyranopodeSoundEmitter
    /// </summary>
    public sealed partial class GameSoundRouter
    {
        private void OnBossWarning(BossWarningEvent e)
        {
            service.PlaySfx(bossWarningKey);
            service.Duck(SoundCategory.Monster, bossDuckLevel, bossDuckTime);
        }

        private void OnBossAppeared(BossAppearedEvent e)
        {
            if (e.Instance != null) EnsureTyranopodeEmitter(e.Instance);
            service.PlayBgm(bossBgmKey);
        }

        /// <summary>씬에 직접 둔 티라노포드(BossAppearedEvent 없음)에도 붙인다. 시작 시 1번</summary>
        private void AttachBossEmittersInScene()
        {
            foreach (var tyranopode in FindObjectsByType<TyranopodeController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                EnsureTyranopodeEmitter(tyranopode.gameObject);
        }

        /// <summary>티라노포드면 몸 소리 이미터를 붙인다 (이미 있으면 그대로 둔다). 다른 보스는 무시</summary>
        private static void EnsureTyranopodeEmitter(GameObject boss)
        {
            if (boss.TryGetComponent<TyranopodeController>(out _) && boss.GetComponent<TyranopodeSoundEmitter>() == null)
                boss.AddComponent<TyranopodeSoundEmitter>();
        }

        /// <summary>BGM 정지 + 스테이지 클리어 → 잠시 뒤 아나운서 승리</summary>
        private void OnBossDefeated(BossDefeatedEvent e)
        {
            service.StopBgm();
            service.PlaySfx(stageClearKey);
            Announce(announceVictoryKey, announcerDelay);
        }
    }
}
