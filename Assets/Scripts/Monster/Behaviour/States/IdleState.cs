namespace HybridShooter.Monster.Behaviour
{
    /// <summary>추적 대상 없음 → 제자리</summary>
    public sealed class IdleState : MonsterState
    {
        public IdleState(MonsterBrain owner) : base(owner) { }

        public override void Enter() => Owner.StopMove();

        public override void Update()
        {
            if (Owner.HasTarget) Owner.ChangeState(Owner.Chase);
        }
    }
}
