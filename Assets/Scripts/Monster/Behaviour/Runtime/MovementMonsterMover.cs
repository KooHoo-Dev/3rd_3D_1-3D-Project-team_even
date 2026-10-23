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
    public sealed class PlayerMovementTarget : IMovementTarget
    {
        public IPlayerTarget Player { get; }
        public PlayerMovementTarget(IPlayerTarget player) => Player = player;
        public bool IsValid => IsAlive(Player);
        public Vector3 Position => Player.Position;
        public Vector3 Forward => Player.Root != null ? Player.Root.forward : Vector3.forward;

        public static bool IsAlive(IPlayerTarget player)
        {
            if (player == null || (player is Object obj && obj == null)) return false;
            // 순수 C# 대역의 미지정 Root와 파괴되어 인터페이스에 남은 Transform을 구분한다.
            var root = player.Root;
            return (ReferenceEquals(root, null) || root != null) && player.IsAlive;
        }
    }
}
