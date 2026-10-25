namespace HybridShooter.Monster.Behaviour
{
    /// <summary>추적 대상 없음 → 제자리</summary>
    public sealed class IdleState : MonsterState
    {
        /// <summary>MonsterStateMachine 생성자가 만든다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신</param>
        public IdleState(MonsterStateMachine owner) : base(owner) { }

        /// <summary>제자리에 멈춘다</summary>
        public override void Enter() => Owner.StopMove();

        /// <summary>대상이 생기면 Chase 로 간다</summary>
        public override void Tick()
        {
            if (Owner.HasTarget) Owner.ChangeState(Owner.Chase);
        }
    }
}
