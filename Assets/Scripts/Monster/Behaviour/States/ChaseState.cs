namespace HybridShooter.Monster.Behaviour
{
    /// <summary>대상을 향해 이동. 실제 이동은 MonsterMovement 가 한다</summary>
    public sealed class ChaseState : MonsterState
    {
        public ChaseState(MonsterStateMachine owner) : base(owner) { }

        public override void Enter() => Owner.ResumeMove();

        public override void Tick()
        {
            if (Owner.HasTarget == false) Owner.ChangeState(Owner.Idle);
            else if (Owner.IsTargetInAttackRange && Owner.CanAttack) Owner.ChangeState(Owner.Attack);
        }
    }
}
