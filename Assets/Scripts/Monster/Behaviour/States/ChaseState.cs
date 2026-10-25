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

            if (Owner.IsTargetInAttackRange && Owner.CanAttack) Owner.ChangeState(Owner.Attack);
        }
    }
}
