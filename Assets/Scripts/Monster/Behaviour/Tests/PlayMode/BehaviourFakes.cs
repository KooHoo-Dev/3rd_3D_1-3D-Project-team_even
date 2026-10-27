using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>플레이어 대역. 피해도 직접 받는다</summary>
    internal sealed class FakeTarget : IPlayerTarget, IDamageable
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
        public void TakeDamage(in DamageInfo info) => Received.Add(info);

        private static Camera SharedViewCamera()
        {
            if (sharedViewCamera == null)
                sharedViewCamera = new GameObject("FakeTargetViewCamera").AddComponent<Camera>();
            return sharedViewCamera;
        }
    }
}
