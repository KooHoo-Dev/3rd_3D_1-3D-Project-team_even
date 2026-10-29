namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 사거리 안에서 공격. 공격이 진행 중(예고·공격·회복)이면 사거리를 벗어나도 끝까지 진행한다.
    /// 예고 중 피하기와 회복 중 반격이 명세의 대응 수단이기 때문이다.
    /// 공격이 끝나면 FinishAttackCycle 이 행선지를 정한다. 기본(After = StayInAttack)은 Attack 에 머물고,
    /// 다음 틱에 아래 분기가 판단한다 (대상 없음 → Idle, 사거리 밖·공격 불허 → Chase, 그 외 → 다시 Begin).
    /// 그래서 기본 공격의 연속 공격 중에는 상태 변경(StateChanged)이 일어나지 않는다.
    /// </summary>
    public sealed class AttackState : MonsterState
    {
        /// <summary>MonsterStateMachine 생성자가 만든다</summary>
        /// <param name="owner">이 상태를 가진 상태 머신</param>
        public AttackState(MonsterStateMachine owner) : base(owner) { }

        /// <summary>공격하려고 멈춘다. 군중 이동의 공격 슬롯 예약은 유지한다</summary>
        public override void Enter() => Owner.StopForAttack();

        /// <summary>
        /// 진행 중인 공격을 진행하고(벽 충돌이면 기절), 공격 중이 아니면 상태 유지·전이를 정한다.
        /// 공격을 시작할 수 있는 분기에서는 다음 공격을 고른 뒤(ChooseNextAttack) 시작한다.
        /// </summary>
        public override void Tick()
        {
            var currentAttack = Owner.CurrentAttack;
            if (currentAttack.IsAttacking)
            {
                currentAttack.Tick(Owner.Now, Owner.DeltaTime);
                if (currentAttack.IsAttacking == false) FinishAttackCycle(currentAttack);
                return;
            }

            if (Owner.HasTarget == false)
                Owner.ChangeState(Owner.Idle);
            else if (Owner.IsTargetInAttackRange == false || Owner.CanAttack == false)
                Owner.ChangeState(Owner.Chase);
            else
            {
                var nextAttack = Owner.ChooseNextAttack();
                if (nextAttack.CanStart(Owner.Now))
                    nextAttack.Begin(Owner.Target, Owner.Now);
            }
        }

        /// <summary>
        /// 공격 한 주기가 막 끝났을 때 행선지를 정한다. Tick 이 공격 진행 직후 부른다.
        /// 벽 충돌이면 기절, 정상 완료면 공격의 After 값에 따라 Idle 휴식 / Chase 재정비 / Attack 유지 / 자폭 사망.
        /// Attack 유지면 다음 틱에 Tick 의 분기가 판단한다.
        /// </summary>
        /// <param name="attack">방금 끝난 공격. LastResult·After 를 읽는다</param>
        private void FinishAttackCycle(MonsterAttack attack)
        {
            if (attack.LastResult == AttackResult.WallHit)
            {
                Owner.Stun(attack.WallHitStunTime);
                return;
            }
            if (attack.LastResult != AttackResult.Completed) return;

            switch (attack.After)
            {
                case AfterAttack.Die:
                    Owner.Die(false);
                    return;
                case AfterAttack.RestInIdle:
                    Owner.RestInIdle();
                    return;
                case AfterAttack.RepositionInChase:
                    Owner.RepositionInChase();
                    return;
                case AfterAttack.StayInAttack:
                    return;
            }
        }

        /// <summary>진행 중 공격을 취소하고, 멈추면서 군중 이동의 예약을 반환한다</summary>
        public override void Exit()
        {
            if (Owner.CurrentAttack.IsAttacking) Owner.CurrentAttack.Cancel();
            Owner.StopMove(); // Attack 을 떠날 때 예약을 반환한 뒤 다시 경쟁한다.
        }
    }
}
