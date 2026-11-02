using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 토큰 모드 배분 입력: 개체 1마리의 현재 상태.
    /// CrowdController.Allocate 가 MonsterMovement.BuildAllocationAgent 로 모아 EngageAllocator.Allocate 에 넘긴다.
    /// </summary>
    public struct AllocationAgent
    {
        public Vector3 Position;
        // 근접 허용 토큰 대상인가 (MovementProfile.UsesEngageToken)
        public bool UsesToken;
        // 직전 판단에서 토큰을 갖고 있었나
        public bool HasToken;
        // Wait 를 처음 받은 시각 (s). 음수 = 대기 중 아님
        public float WaitingSince;
        // 토큰 재획득 전 최소 대기 시간 (s)
        public float MinWaitSeconds;
        // 기존 선회 방향 (+1/-1, 0 = 미지정)
        public int OrbitSign;
        // 기존 포위 슬롯 번호 (-1 = 없음)
        public int SlotIndex;
    }

    /// <summary>
    /// 토큰 모드 배분 입력: 모든 개체에 공통인 전역 파라미터.
    /// CrowdController.Allocate 가 CrowdSettings 와 목표 위치로 채워 EngageAllocator.Allocate 에 넘긴다.
    /// </summary>
    public struct AllocationParams
    {
        public Vector3 TargetPosition;
        // 근접 동시 허용 수 K (≥ 0)
        public int Capacity;
        // 과밀 판정 반경 (m)
        public float NearRadius;
        // 슬롯/토큰 모드가 공유하는 링 설정
        public SlotSettings Slots;
        // 토큰 보유자가 이 반경 밖으로 나가면 토큰 회수 (m), Slots.WaitingRadius 이상
        public float ReleaseRadius;
        // 현재 시각 (s)
        public float Now;
        // true 면 토큰 보유자에게 360/K 간격 포위 슬롯을 배정
        public bool UseSlots;
    }

    /// <summary>
    /// 배분 출력 중 개체별 결과 외의 집계.
    /// CrowdController 가 lastSummary 로 보관해 디버그·HUD 용 CrowdSnapshot 에 담는다.
    /// </summary>
    public struct AllocationSummary
    {
        public int Engaged;          // 근접 허용(토큰 보유·접근 링 예약) 개체 수
        public int Waiting;          // 대기 중인 토큰 대상 개체 수
        public int Unrestricted;     // 군중 제어 비대상 개체 수
        // 토큰 대상이 아닌데 NearRadius 안에 있는 개체 수 (자리를 차지함)
        public int UncontrolledInside;
        // 이번 판단 후 남은 자리
        public int FreeCapacity;
    }

    /// <summary>
    /// 배분 출력: 개체별 결과 (CrowdDirective + 다음 판단에 넘길 상태).
    /// CrowdController 가 MonsterMovement.ApplyAllocation 으로 각 개체에 반영한다.
    /// </summary>
    public struct AllocationResult
    {
        public CrowdDirective Directive;
        public bool HasToken;
        // Wait 시작 시각 갱신값 (s). 음수 = 대기 아님
        public float WaitingSince;
        public int OrbitSign;        // 선회 방향 (+1/-1, 0 = 미지정)
        public int SlotIndex;        // 포위 슬롯 번호 (-1 = 없음)
    }
}
