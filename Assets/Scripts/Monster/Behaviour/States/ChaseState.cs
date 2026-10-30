namespace HybridShooter.Monster.Behaviour
{
    /// <summary>대상을 향해 이동. 실제 이동은 MonsterMovement 가 한다</summary>
    public sealed class ChaseState : MonsterState
    {
        /// <summary>MonsterStateMachine 생성자가 만든다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신</param>
        public ChaseState(MonsterStateMachine owner) : base(owner) { }

        /// <summary>멈춰 있던 이동을 재개한다</summary>
        public override void Enter() => Owner.ResumeMove();

        /// <summary>
        /// 대상이 없으면 Idle 로 간다.
        /// 재정비 중(RepositionInChase 공격 뒤)이면 공격의 IsRepositioned 가 true 가 될 때까지 Attack 으로 가지 않는다.
        /// 쿨다운 대기 중(ChaseUntilReady 공격 뒤)이면 쿨다운이 끝날 때까지 Attack 으로 가지 않고 슬롯으로 이동한다.
        /// 그 외에는 사거리 안이고 공격 허가가 있으면 Attack 으로 간다.
        /// </summary>
        public override void Tick()
        {
            if (Owner.HasTarget == false)
            {
                Owner.ChangeState(Owner.Idle);
                return;
            }
            if (Owner.IsRepositioning)
            {
                if (Owner.CurrentAttack.IsRepositioned(Owner.Target, Owner.Now) == false) return;
                Owner.ClearAfterAttackFlags();
            }
            if (IsWaitingCooldown()) return;

            if (Owner.IsTargetInAttackRange && Owner.CanAttack) Owner.ChangeState(Owner.Attack);
        }

        /// <summary>
        /// ChaseUntilReady 공격의 쿨다운이 아직 안 끝났는지. Tick 이 Attack 전이 전에 묻는다.
        /// 다른 After 공격(돌진·공중)은 기존 흐름대로 쿨다운을 Attack 안에서 기다리므로 false.
        /// </summary>
        /// <returns>true = 쿨다운 대기 중, Attack 으로 가지 않는다</returns>
        private bool IsWaitingCooldown()
        {
            MonsterAttack currentAttack = Owner.CurrentAttack;
            if (currentAttack.After != AfterAttack.ChaseUntilReady) return false;
            return currentAttack.CanStart(Owner.Now) == false;
        }
    }
}
