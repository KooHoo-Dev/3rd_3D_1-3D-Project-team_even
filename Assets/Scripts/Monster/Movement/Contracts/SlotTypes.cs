using System;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>슬롯 후보의 사용 가능 여부. Valid 만 배정 가능 (NavMesh 밖이거나 다른 슬롯과 너무 가까우면 제외)</summary>
    public enum SlotValidity { Valid, OffNavMesh, TooClose }
    /// <summary>슬롯 점유 단계. Free 비어 있음 → Reserved 예약됨(이동 중) → Occupied 소유자가 도착함</summary>
    public enum SlotUse { Free, Reserved, Occupied }

    /// <summary>
    /// 포위 슬롯 설정. 목표 둘레에 동심원(링)을 만들고 각 링 위에 후보 지점을 놓는다.
    /// 0번 링 = 접근 링(공격 위치, 동시 인원 제한 대상), 1번 이후 = 바깥 대기 링.
    /// </summary>
    [Serializable]
    public sealed class SlotSettings
    {
        [Min(1)] public int ApproachCount = 8;
        [Min(0.1f)] public float ApproachRadius = 2.8f;
        [Min(0.1f)] public float MinimumRingGap = 1.2f;
        [Min(2)] public int MaxRingCount = 6;
        [Tooltip("토큰 모드(UseSlots=false) 전용 대기 반경 (m)")]
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
        public readonly int RingIndex;
        public readonly Vector3 CandidatePosition;
        public readonly Vector3 Position;
        public readonly SlotValidity Validity;
        public readonly int OwnerId;
        public readonly SlotUse Use;
        public readonly bool Congested;

        public SlotSnapshot(int id, int ringIndex, Vector3 candidatePosition, Vector3 position,
                            SlotValidity validity, int ownerId, SlotUse use, bool congested)
        {
            Id = id; RingIndex = ringIndex; CandidatePosition = candidatePosition; Position = position;
            Validity = validity; OwnerId = ownerId; Use = use; Congested = congested;
        }
    }

    /// <summary>슬롯 혼잡 판정용 장애물(몬스터 1마리의 위치·반경). 소유자 본인의 슬롯 판정에서는 제외된다</summary>
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
        /// <summary>후보를 이동 가능한 지점으로 투영. 투영할 곳이 없으면 false</summary>
        bool TryProject(Vector3 candidate, out Vector3 position);
        /// <summary>origin 에서 destination 까지 끊기지 않는 경로가 있는가</summary>
        bool CanReach(Vector3 origin, Vector3 destination);
    }
}
