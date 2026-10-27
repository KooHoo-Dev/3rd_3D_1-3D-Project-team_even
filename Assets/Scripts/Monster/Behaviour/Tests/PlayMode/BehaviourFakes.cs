using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>플레이어 대역. 피해·넉백도 직접 받는다</summary>
    internal sealed class FakeTarget : IPlayerTarget, IDamageable, IKnockbackable
    {
        public Transform Root => null;
        // 이동 브랜치의 기존 계약 충족용 대역이며 실제 플레이어 연결이 아니다.
        // MonsterMovement.Think 가 ViewPoint 의 위치·정면·Camera FOV 를 읽으므로 Camera 가 붙은 Transform 을 준다.
        // 테스트 전체가 카메라 하나를 공유한다 (원점, +Z 를 바라봄).
        public Transform ViewPoint => SharedViewCamera().transform;
        private static Camera sharedViewCamera;
        public PlayerTrackingInfo PlayerTrackingInfo => new PlayerTrackingInfo{Forward = Vector3.forward, Position = Position, Rotation = Quaternion.identity};
        public Vector3 Position { get; set; }

        public Faction Faction => Faction.Player;
        public bool IsAlive { get; set; } = true;
        public List<DamageInfo> Received { get; } = new List<DamageInfo>();
        /// <summary>true 면 피해를 받는 순간 죽는다 (IsAlive = false)</summary>
        public bool DiesOnHit { get; set; }
        public void TakeDamage(in DamageInfo info)
        {
            Received.Add(info);
            if (DiesOnHit) IsAlive = false;
        }

        /// <summary>받은 넉백 (방향, 거리)</summary>
        public List<(Vector3 Direction, float Distance)> Knockbacks { get; } = new List<(Vector3, float)>();
        public void ApplyKnockback(Vector3 direction, float distance) => Knockbacks.Add((direction, distance));

        private static Camera SharedViewCamera()
        {
            if (sharedViewCamera == null)
                sharedViewCamera = new GameObject("FakeTargetViewCamera").AddComponent<Camera>();
            return sharedViewCamera;
        }
    }
}
