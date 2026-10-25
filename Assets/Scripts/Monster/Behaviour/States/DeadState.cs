namespace HybridShooter.Monster.Behaviour
{
    /// <summary>처치됨. 종료 상태 (더 이상 전이하지 않음)</summary>
    public sealed class DeadState : MonsterState
    {
        /// <summary>MonsterStateMachine 생성자가 만든다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신</param>
        public DeadState(MonsterStateMachine owner) : base(owner) { }

        /// <summary>이동을 멈추고 Died 이벤트를 발행한다 (MonsterController 가 받아 처치 처리)</summary>
        public override void Enter()
        {
            Owner.StopMove();
            Owner.NotifyDied();
        }

        /// <summary>종료 상태라 아무것도 하지 않는다</summary>
        public override void Tick() { }
    }
}
