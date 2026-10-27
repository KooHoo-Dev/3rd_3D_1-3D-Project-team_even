using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 보스: 경고·등장(BGM·덕킹)·공격 예고·페이즈 전환·처치.
    /// ※ 보스 이벤트는 아직 옛 MonsterDef 타입이다. R11 에서 BossDef 기반 + BossSoundEmitter 로 바뀐다
    /// </summary>
    public sealed partial class GameSoundRouter
    {
        private MonsterDef boss;
        private GameObject bossInstance;

        private void OnBossWarning(BossWarningEvent e)
        {
            service.PlaySfx(bossWarningKey);
            service.Duck(SoundCategory.Monster, bossDuckLevel, bossDuckTime);
        }

        private void OnBossAppeared(BossAppearedEvent e)
        {
            boss = e.Boss;
            bossInstance = e.Instance;
            service.PlayBgm(bossBgmKey);
        }

        /// <summary>예고(Windup) 시작에 공격 소리 → 플레이어가 소리로 피할 타이밍을 안다</summary>
        private void OnBossSkillPhaseChanged(BossSkillPhaseChangedEvent e)
        {
            if (e.Phase != SkillPhase.Windup || e.Attack == null) return;
            if (e.Attack.IsEntrance) service.Duck(SoundCategory.Monster, bossDuckLevel, bossDuckTime);
            service.PlaySfx(e.Attack.Sfx, BossPosition());
        }

        /// <summary>Stage 0 = 멀쩡 (처음 상태 통지)이므로 1 이상에서만</summary>
        private void OnBossDamageStageChanged(BossDamageStageChangedEvent e)
        {
            if (e.Stage <= 0 || boss == null) return;
            service.PlaySfx(boss.HurtSfx, BossPosition());
        }

        /// <summary>사망음 + BGM 정지 + 스테이지 클리어 → 잠시 뒤 아나운서 승리</summary>
        private void OnBossDefeated(BossDefeatedEvent e)
        {
            service.PlaySfx(e.Boss?.DeathSfx, e.Position);
            service.StopBgm();
            service.PlaySfx(stageClearKey);
            Announce(announceVictoryKey, announcerDelay);
            ClearBoss();
        }

        private void ClearBoss()
        {
            boss = null;
            bossInstance = null;
        }

        /// <summary>보스 위치. 인스턴스를 모르면 null → 2D 재생 (보스 소리는 어디서든 들려야 하므로)</summary>
        private Vector3? BossPosition() => bossInstance != null ? bossInstance.transform.position : (Vector3?)null;
    }
}
