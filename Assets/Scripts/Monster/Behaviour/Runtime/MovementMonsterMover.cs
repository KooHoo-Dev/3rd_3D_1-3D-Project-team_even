using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

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

        public void SetTarget(IPlayerTarget target)
        {
            // 군중 전체가 같은 플레이어 어댑터를 공유한다. 개체마다 목표를 다시 설정해 예약을 지우지 않는다.
            if (movement.Target is PlayerMovementTarget existing && ReferenceEquals(existing.Player, target)) return;
            movement.SetTarget(new PlayerMovementTarget(target));
        }
    }

    /// <summary>실제 플레이어 등록·피해 연결은 후속 작업. 두 모듈에 동일 위치·생존 의미를 전달한다.</summary>
    public sealed class PlayerMovementTarget : IPlayerTarget
    {
        public IPlayerTarget Player { get; }
        public PlayerMovementTarget(IPlayerTarget player) => Player = player;
        public bool IsValid => true; // 대상 상실(사망 등)은 MonsterController가 Brain.SetTarget(null)로 처리한다
        public Transform Root { get; }
        public Transform ViewPoint { get; }
        public PlayerTrackingInfo PlayerTrackingInfo { get; }
        public Vector3 Position => Player.PlayerTrackingInfo.Position;
        public Vector3 Forward => Player.Root != null ? Player.Root.forward : Vector3.forward;

        // (IsAlive 삭제: 생존 판정은 MonsterController 소유. 아래는 삭제된 메서드 안에 있던 설계 메모)
            // 플레이어 살아있는 여부가 왜 필요해?
            // -> 콜라이더를 안써서
            // 그럼 범위 내 적들 누가 있는지 어떻게 알아?
            // IHitArea.Contains는 단순히 hitarea 내 position 벡터가 위치 여부만 판단하는데?
                // 싱글 플레이어니까 플레이어 한명 고정이니 플레이어에게만 피해를 주면 된다는 거네
                // 이 부분은 ㅈㄴ 유연하게 짜야됨. 피해 주는걸 왜 플레이어 하나만 주려고 해. 어떻게 바뀔지 모르는데
                // 콜라이더로 하든,
    }
}
