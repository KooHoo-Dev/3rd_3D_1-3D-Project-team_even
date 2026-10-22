namespace HybridShooter.Monster.Behaviour
{
    /// <summary>대상을 향해 이동. 실제 이동은 IMonsterMover 가 한다</summary>
    public sealed class ChaseState : MonsterState
    {
        public ChaseState(MonsterBrain owner) : base(owner) { }

        public override void Enter() => Owner.ResumeMove();

        public override void Update()
        {
            if (Owner.HasTarget == false) Owner.ChangeState(Owner.Idle);
            else if (Owner.IsTargetInAttackRange) Owner.ChangeState(Owner.Attack);
        }
    }
}
