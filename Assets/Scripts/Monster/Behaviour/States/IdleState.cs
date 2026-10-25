namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 추적 대상 없음 → 제자리. 대상이 생기면 Chase.
    /// 휴식 중(RestInIdle 공격 뒤)이면 공격의 준비 동작을 돌리고, 준비가 끝나면 바로 Attack 또는 Chase 로 간다.
    /// </summary>
    public sealed class IdleState : MonsterState
    {
        /// <summary>MonsterStateMachine 생성자가 만든다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신</param>
        public IdleState(MonsterStateMachine owner) : base(owner) { }

        /// <summary>제자리에 멈춘다</summary>
        public override void Enter() => Owner.StopMove();

        /// <summary>
        /// 휴식 중이 아니면 대상이 생길 때 Chase 로 간다.
        /// 휴식 중이면 준비 동작 1틱 → 준비가 끝나면 휴식 해제 → 사거리 안·공격 허가면 Attack, 아니면 Chase.
        /// </summary>
        public override void Tick()
        {
            if (Owner.HasTarget == false) return;
            if (Owner.IsResting == false)
            {
                Owner.ChangeState(Owner.Chase);
                return;
            }

            Owner.CurrentAttack.TickPreparation(Owner.Target, Owner.DeltaTime);
            if (Owner.CurrentAttack.IsPrepared == false) return;

            Owner.ClearAfterAttackFlags();
            bool canAttackNow = Owner.IsTargetInAttackRange && Owner.CanAttack;
            if (canAttackNow) Owner.ChangeState(Owner.Attack);
            else Owner.ChangeState(Owner.Chase);
        }
    }
}
