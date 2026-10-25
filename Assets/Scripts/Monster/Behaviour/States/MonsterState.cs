namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 모든 몬스터 상태의 공통 약속. Enter / Update / Exit.
    /// 전이는 각 상태의 Update 안에서 Owner.ChangeState(...) 로 직접 한다.
    /// </summary>
    public abstract class MonsterState
    {
        protected MonsterStateMachine Owner { get; }
        protected MonsterState(MonsterStateMachine owner) { Owner = owner; }

        public virtual void Enter() { }     // 이 상태로 들어온 순간 1번
        public abstract void Tick();      // 머무는 동안 매 판단
        public virtual void Exit() { }      // 이 상태를 떠나는 순간 1번
    }
}
