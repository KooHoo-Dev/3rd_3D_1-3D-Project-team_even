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
        [Min(1)] public int ApproachCount = 8;           // 접근 링(0번)의 슬롯 수
        [Min(0.1f)] public float ApproachRadius = 2.8f;  // m, 접근 링 반경
        [Min(0.1f)] public float MinimumRingGap = 1.2f;  // m, 링 사이 최소 간격
        [Min(2)] public int MaxRingCount = 6;            // 접근 링 포함 최대 링 수
        [Tooltip("토큰 모드(UseSlots=false) 전용 대기 반경 (m)")]
        [Min(0.1f)] public float WaitingRadius = 8f;
        [Min(0.1f)] public float MinimumSpacing = 1.1f;  // m, 슬롯 간·슬롯과 다른 몬스터 사이 최소 간격
        [Min(0.01f)] public float SampleRadius = 0.6f;   // m, 후보 지점을 NavMesh 에 투영할 때 찾는 반경
        [Min(0.01f)] public float ArrivalDistance = 0.45f; // m, 이 거리 안이면 슬롯에 도착(Occupied)
        [Min(0.1f)] public float ProgressTimeout = 6f;   // s, 이 시간 동안 진행이 없으면 슬롯 거절
        [Min(0.01f)] public float ProgressDistance = 0.15f; // m, 슬롯까지 거리가 이만큼 줄어야 진행으로 본다
        [Min(0.1f)] public float RejectionSeconds = 2f;  // s, 거절한 슬롯을 같은 몬스터에게 다시 주지 않는 시간
        public int AgentTypeId;                          // NavMesh 투영에 쓰는 에이전트 타입
        public int AreaMask = -1;                        // NavMesh 영역 마스크 (-1 = 모든 영역)
    }

    /// <summary>슬롯 1개의 값 복사본. 후보 위치와 NavMesh 투영 결과, 예약과 실제 도착을 구분한다. SlotRegistry 가 만들어 밖으로 내준다</summary>
    public readonly struct SlotSnapshot
    {
        public readonly int Id;
        public readonly int RingIndex;
        public readonly Vector3 CandidatePosition;  // NavMesh 투영 전 후보 위치 (월드)
        public readonly Vector3 Position;           // NavMesh 에 투영된 실제 목적지 (월드)
        public readonly SlotValidity Validity;
        public readonly int OwnerId;                // 예약한 몬스터 id (0 = 없음)
        public readonly SlotUse Use;
        public readonly bool Congested;             // 소유자 외 다른 몬스터가 슬롯 근처에 서 있어 쓰기 곤란함

        /// <summary>슬롯 값 복사본 생성. 의미는 각 필드와 같다</summary>
        /// <param name="id">슬롯 번호</param>
        /// <param name="ringIndex">링 번호 (0 = 접근 링)</param>
        /// <param name="candidatePosition">NavMesh 투영 전 후보 위치 (월드)</param>
        /// <param name="position">NavMesh 에 투영된 실제 목적지 (월드)</param>
        /// <param name="validity">배정 가능 여부</param>
        /// <param name="ownerId">예약한 몬스터 id (0 = 없음)</param>
        /// <param name="use">점유 단계</param>
        /// <param name="congested">다른 몬스터가 근처에 있어 쓰기 곤란한가</param>
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
        public readonly float Radius;   // m, 몬스터 NavMeshAgent 반경
        /// <summary>장애물 생성. CrowdController 가 등록 몬스터마다 만든다</summary>
        /// <param name="ownerId">몬스터 id (GetInstanceID)</param>
        /// <param name="position">몬스터 위치 (월드)</param>
        /// <param name="radius">몬스터 반경 (m)</param>
        public SlotObstacle(int ownerId, Vector3 position, float radius)
        { OwnerId = ownerId; Position = position; Radius = radius; }
    }
}
