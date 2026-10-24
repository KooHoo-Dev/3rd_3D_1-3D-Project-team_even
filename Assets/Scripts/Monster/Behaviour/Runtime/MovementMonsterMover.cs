using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>Behaviour가 Movement의 정지·예약·공격 허가를 사용하는 연결 경계.</summary>
    public sealed class MovementMonsterMover : IMonsterCombatMover
    {
        private readonly MonsterMovement movement;

        public MovementMonsterMover(MonsterMovement movement) => this.movement = movement;
        public bool IsStopped => movement.IsSuspended;
        public bool CanAttack => movement.CanAttack;
        public void Stop() => movement.SetSuspended(true);
        public void StopForAttack() => movement.SetSuspended(true, retainReservation: true);
        public void Resume() => movement.SetSuspended(false);

        /// <summary>null = 대상 상실(사망 등). 군중 전체가 같은 목표를 공유하므로 같은 값이면 예약을 지우지 않도록 다시 설정하지 않는다.</summary>
        public void SetTarget(IPlayerTarget target)
        {
            if (ReferenceEquals(movement.Target, target)) return;
            movement.SetTarget(target);
        }
    }
}
