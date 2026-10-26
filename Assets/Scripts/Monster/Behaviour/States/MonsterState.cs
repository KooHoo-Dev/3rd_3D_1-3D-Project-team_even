namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 모든 몬스터 상태의 공통 약속. Enter / Tick / Exit.
    /// 전이는 각 상태의 Tick 안에서 Owner.ChangeState(...) 로 직접 한다.
    /// </summary>
    public abstract class MonsterState
    {
        protected MonsterStateMachine Owner { get; }
        /// <summary>MonsterStateMachine 생성자가 상태 5개를 만들 때 자신을 넘긴다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신. 이동·공격·전이를 이 머신을 통해 한다</param>
        protected MonsterState(MonsterStateMachine owner) { Owner = owner; }

        /// <summary>이 상태로 들어온 순간 1번 (ChangeState 가 호출)</summary>
        public virtual void Enter() { }
        /// <summary>머무는 동안 매 판단 (MonsterStateMachine.Update 가 호출)</summary>
        public abstract void Tick();
        /// <summary>이 상태를 떠나는 순간 1번 (ChangeState 가 호출)</summary>
        public virtual void Exit() { }
    }
}
