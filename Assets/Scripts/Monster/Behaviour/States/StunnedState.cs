namespace HybridShooter.Monster.Behaviour
{
    /// <summary>기절 (돌진 벽 충돌 등). 시간이 끝나면 다시 추적</summary>
    public sealed class StunnedState : MonsterState
    {
        private float endTime;

        public StunnedState(MonsterStateMachine owner) : base(owner) { }

        public override void Enter()
        {
            Owner.StopMove();
            endTime = Owner.Now + Owner.StunDuration;
        }

        public override void Tick()
        {
            if (Owner.Now < endTime) return;
            Owner.ChangeState(Owner.HasTarget ? Owner.Chase : Owner.Idle);
        }
    }
}
