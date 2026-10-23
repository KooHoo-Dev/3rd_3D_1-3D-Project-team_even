namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 사거리 안에서 공격. 공격이 진행 중(예고·공격·회복)이면 사거리를 벗어나도 끝까지 진행한다.
    /// 예고 중 피하기와 회복 중 반격이 명세의 대응 수단이기 때문이다.
    /// </summary>
    public sealed class AttackState : MonsterState
    {
        public AttackState(MonsterBrain owner) : base(owner) { }

        public override void Enter() => Owner.StopForAttack();

        public override void Update()
        {
            var attack = Owner.CurrentAttack;
            if (attack.IsRunning)
            {
                attack.Tick(Owner.Now, Owner.DeltaTime);
                if (attack.IsRunning == false && attack.LastResult == AttackResult.WallHit)
                    Owner.Stun(attack.WallHitStunTime);
                else if (!attack.IsRunning)
                    Owner.ChangeState(Owner.HasTarget ? Owner.Chase : Owner.Idle);
                return;
            }

            if (Owner.HasTarget == false) Owner.ChangeState(Owner.Idle);
            else if (Owner.IsTargetInAttackRange == false || !Owner.CanAttack) Owner.ChangeState(Owner.Chase);
            else
            {
                Owner.FaceTarget();
                if (attack.CanStart(Owner.Now)) attack.Begin(Owner.Target, Owner.Now);
            }
        }

        public override void Exit()
        {
            if (Owner.CurrentAttack.IsRunning) Owner.CurrentAttack.Cancel();
            Owner.StopMove(); // 공격 한 주기가 끝나면 예약을 반환한 뒤 다시 경쟁한다.
        }
    }
}
