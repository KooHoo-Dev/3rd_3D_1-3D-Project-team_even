using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>EngageAllocator 입력: 개체 1마리의 현재 상태</summary>
    public struct AllocationAgent
    {
        public Vector3 Position;
        /// <summary>근접 허용 토큰 대상인가 (MovementProfile.UsesEngageToken)</summary>
        public bool UsesToken;
        /// <summary>직전 판단에서 토큰을 갖고 있었나</summary>
        public bool HasToken;
        /// <summary>Wait 를 처음 받은 시각 (s). 음수 = 대기 중 아님</summary>
        public float WaitingSince;
        /// <summary>토큰 재획득 전 최소 대기 시간 (s)</summary>
        public float MinWaitSeconds;
        /// <summary>기존 선회 방향 (+1/-1, 0 = 미지정)</summary>
        public int OrbitSign;
        /// <summary>기존 포위 슬롯 번호 (-1 = 없음)</summary>
        public int SlotIndex;
    }

    /// <summary>EngageAllocator 입력: 전역 파라미터</summary>
    public struct AllocationParams
    {
        public Vector3 TargetPosition;
        /// <summary>근접 동시 허용 수 K (≥ 0)</summary>
        public int Capacity;
        /// <summary>과밀 판정 반경 (m)</summary>
        public float NearRadius;
        /// <summary>슬롯/토큰 모드가 공유하는 링 설정</summary>
        public SlotSettings Slots;
        /// <summary>토큰 보유자가 이 반경 밖으로 나가면 토큰 회수 (m), Slots.WaitingRadius 이상</summary>
        public float ReleaseRadius;
        /// <summary>현재 시각 (s)</summary>
        public float Now;
        /// <summary>true 면 토큰 보유자에게 360/K 간격 포위 슬롯을 배정</summary>
        public bool UseSlots;
    }

    /// <summary>EngageAllocator 출력 (개체별 결과 외의 집계)</summary>
    public struct AllocationSummary
    {
        public int Engaged;
        public int Waiting;
        public int Unrestricted;
        /// <summary>토큰 대상이 아닌데 NearRadius 안에 있는 개체 수 (자리를 차지함)</summary>
        public int UncontrolledInside;
        /// <summary>이번 판단 후 남은 자리</summary>
        public int FreeCapacity;
    }

    /// <summary>EngageAllocator 출력: 개체별 결과 (CrowdDirective + 다음 판단에 넘길 상태)</summary>
    public struct AllocationResult
    {
        public CrowdDirective Directive;
        public bool HasToken;
        /// <summary>Wait 시작 시각 갱신값 (음수 = 대기 아님)</summary>
        public float WaitingSince;
        public int OrbitSign;
        public int SlotIndex;
    }
}
