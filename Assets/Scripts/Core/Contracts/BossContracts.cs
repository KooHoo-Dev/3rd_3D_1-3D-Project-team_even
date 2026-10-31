using HybridShooter.Core.Data.Defs;

namespace HybridShooter.Core.Contracts
{
    /// <summary>
    /// 라운드(RoundBossEncounter)가 보스 프리팹을 만든 직후 부르는 초기화. 보스 프리팹 루트에 붙인다.
    /// 구현한 보스는 이 안에서 BossController.Initialize 와 자기 패턴 초기화를 모두 끝낸다.
    /// 구현이 없는 보스는 RoundBossEncounter 가 BossController.Initialize 만 부른다
    /// </summary>
    public interface IBossEncounterInitializer
    {
        void Initialize(BossDef def, IPlayerTarget target);

        /// <summary>
        /// 보스가 직접 등장 연출을 하는 중. true 인 동안 라운드는 보스전으로 넘어가지 않고,
        /// 남은 웨이브 몬스터도 정리하지 않는다 (보스가 연출 안에서 직접 정리한다)
        /// </summary>
        bool IsEntranceRunning { get; }
    }
}
