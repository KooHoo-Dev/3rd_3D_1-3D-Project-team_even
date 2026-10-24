namespace HybridShooter.Monster.Behaviour
{
    /// <summary>처치됨. 종료 상태 (더 이상 전이하지 않음)</summary>
    public sealed class DeadState : MonsterState
    {
        public DeadState(MonsterStateMachine owner) : base(owner) { }

        public override void Enter()
        {
            Owner.StopMove();
            Owner.NotifyDied();
        }

        public override void Update() { }
    }
}
