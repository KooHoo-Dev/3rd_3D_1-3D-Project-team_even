namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 상태(Idle/Chase/Attack...)가 이동을 멈추고 재개하는 통로.
    /// 실제 이동 방식(직선 추적, NavMesh, 군중 제어)은 구현체가 정한다.
    /// </summary>
    public interface IMonsterMover
    {
        bool IsStopped { get; }
        void Stop();
        void Resume();
    }

    /// <summary>군중 이동의 공격 허가와 공격 중 예약 유지를 위한 추가 계약.</summary>
    public interface IMonsterCombatMover : IMonsterMover
    {
        bool CanAttack { get; }
        void StopForAttack();
    }
}
