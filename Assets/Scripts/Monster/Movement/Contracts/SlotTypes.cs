using System;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    public enum SlotKind { Approach, Waiting }
    public enum SlotValidity { Valid, OffNavMesh, TooClose }
    public enum SlotUse { Free, Reserved, Occupied }

    [Serializable]
    public sealed class SlotSettings
    {
        [Min(1)] public int ApproachCount = 8;
        [Min(1)] public int WaitingCount = 16;
        [Min(0.1f)] public float ApproachRadius = 2.8f;
        [Min(0.1f)] public float WaitingRadius = 8f;
        [Min(0.1f)] public float MinimumSpacing = 1.1f;
        [Min(0.01f)] public float SampleRadius = 0.6f;
        [Min(0.01f)] public float ArrivalDistance = 0.45f;
        [Min(0.1f)] public float ProgressTimeout = 6f;
        [Min(0.01f)] public float ProgressDistance = 0.15f;
        [Min(0.1f)] public float RejectionSeconds = 2f;
        public int AgentTypeId;
        public int AreaMask = -1;
    }

    /// <summary>값 복사본. 후보 위치와 NavMesh 투영 결과, 예약과 실제 도착을 구분한다.</summary>
    public readonly struct SlotSnapshot
    {
        public readonly int Id;
        public readonly SlotKind Kind;
        public readonly Vector3 CandidatePosition;
        public readonly Vector3 Position;
        public readonly SlotValidity Validity;
        public readonly int OwnerId;
        public readonly SlotUse Use;
        public readonly bool Congested;

        public SlotSnapshot(int id, SlotKind kind, Vector3 candidatePosition, Vector3 position,
                            SlotValidity validity, int ownerId, SlotUse use, bool congested)
        {
            Id = id; Kind = kind; CandidatePosition = candidatePosition; Position = position;
            Validity = validity; OwnerId = ownerId; Use = use; Congested = congested;
        }
    }

    public readonly struct SlotObstacle
    {
        public readonly int OwnerId;
        public readonly Vector3 Position;
        public readonly float Radius;
        public SlotObstacle(int ownerId, Vector3 position, float radius)
        { OwnerId = ownerId; Position = position; Radius = radius; }
    }

    /// <summary>기하 후보의 실제 이동 가능성. 런타임은 NavMesh, 논리 테스트는 결정적 구현을 사용한다.</summary>
    public interface ISlotNavigation
    {
        bool TryProject(Vector3 candidate, out Vector3 position);
        bool CanReach(Vector3 origin, Vector3 destination);
    }
}
