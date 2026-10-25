using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    internal sealed class FakeMover : IMonsterMover
    {
        public bool IsStopped { get; private set; }
        public void Stop() => IsStopped = true;
        public void Resume() => IsStopped = false;
    }

    internal sealed class FakeBody : IMonsterBody
    {
        public Vector3 Position { get; set; }
        public Vector3 Forward { get; set; } = Vector3.forward;
        public Vector3? LastFaced { get; private set; }

        public void Face(Vector3 worldPoint)
        {
            LastFaced = worldPoint;
            Vector3 flat = worldPoint - Position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-8f) Forward = flat.normalized;
        }
    }

    /// <summary>플레이어 대역. 피해도 직접 받는다</summary>
    internal sealed class FakeTarget : IPlayerTarget, IDamageable
    {
        public Transform Root => null;
        // 이동 브랜치의 기존 계약 충족용 대역이며 실제 플레이어 연결이 아니다.
        public Transform ViewPoint => null;
        public PlayerTrackingInfo PlayerTrackingInfo => new PlayerTrackingInfo{Forward = Vector3.forward, Position = Position, Rotation = Quaternion.identity};
        public Vector3 Position { get; set; }

        public Faction Faction => Faction.Player;
        public bool IsAlive { get; set; } = true;
        public List<DamageInfo> Received { get; } = new List<DamageInfo>();
        public void TakeDamage(in DamageInfo info) => Received.Add(info);
    }

    /// <summary>테스트가 진행·종료를 직접 조종하는 공격</summary>
    internal sealed class FakeAttack : IMonsterAttack
    {
        public float Range { get; set; } = 1.5f;
        public bool IsAttacking { get; set; }
        public AttackResult LastResult { get; set; }
        public float WallHitStunTime { get; set; } = 2f;
        public bool Ready { get; set; } = true;

        public int BeginCount { get; private set; }
        public int TickCount { get; private set; }
        public int CancelCount { get; private set; }

        public bool CanStart(float now) => Ready;

        public void Begin(IPlayerTarget target, float now)
        {
            BeginCount++;
            IsAttacking = true;
            LastResult = AttackResult.None;
        }

        public void Tick(float now, float deltaTime) => TickCount++;

        public void Cancel()
        {
            CancelCount++;
            IsAttacking = false;
            LastResult = AttackResult.Cancelled;
        }

        /// <summary>다음 Tick 이후 끝난 것으로 보이게 한다</summary>
        public void Finish(AttackResult result)
        {
            IsAttacking = false;
            LastResult = result;
        }
    }
}
