namespace HybridShooter.Monster.Behaviour
{
    /// <summary>기절 (돌진 벽 충돌 등). 시간이 끝나면 다시 추적</summary>
    public sealed class StunnedState : MonsterState
    {
        private float endTime;   // 기절이 끝나는 시각 (s, Owner.Now 기준)

        /// <summary>MonsterStateMachine 생성자가 만든다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신</param>
        public StunnedState(MonsterStateMachine owner) : base(owner) { }

        /// <summary>멈추고, 상태 머신이 받은 StunDuration 으로 종료 시각을 정한다</summary>
        public override void Enter()
        {
            Owner.StopMove();
            endTime = Owner.Now + Owner.StunDuration;
        }

        /// <summary>종료 시각이 지나면 대상이 있으면 Chase, 없으면 Idle 로 간다</summary>
        public override void Tick()
        {
            if (Owner.Now < endTime) return;
            Owner.ChangeState(Owner.HasTarget ? Owner.Chase : Owner.Idle);
        }
    }
}
