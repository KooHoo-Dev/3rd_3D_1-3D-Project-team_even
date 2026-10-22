using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>몬스터 이동 상태. 공격·애니메이션 모듈이 읽는다</summary>
    public enum MovementState
    {
        Idle,       // 목표 없음 / 무효
        Approach,   // 목표 또는 대기 링으로 이동 중
        Engaged,    // 공격 위치에 있음 (근접: 토큰 보유 + 사거리 안, 원거리: 유지 거리 안)
        Waiting,    // 대기 링에서 정지
        Orbiting,   // 대기 링에서 선회
        Suspended,  // 외부 요청(공격 동작·경직)으로 이동 정지
    }

    /// <summary>군중 제어가 개체에 주는 지시 종류</summary>
    public enum DirectiveKind
    {
        None,          // 아직 판단 전
        Engage,        // 근접 구역 진입 허용 (토큰 보유)
        Wait,          // 진입 불허 → 대기 링에서 대기·선회
        Unrestricted,  // 토큰 대상 아님 (거리 유지형 등) → 정책이 자유롭게 이동
    }

    /// <summary>이동 정책 종류. MovementProfile 에서 선택하고 MovementPolicyFactory 가 생성한다</summary>
    public enum MovementPolicyKind
    {
        Melee,         // 기본 근접형 (BL-010, BL-016)
        Flank,         // 측면 침투형 (BL-012)
        KeepDistance,  // 위치 압박형 (BL-011)
    }

    /// <summary>
    /// 군중 제어 결과. EngageAllocator 가 만들고 정책이 해석한다.
    /// 명령이 아니라 "허가 + 정보"다. 실제로 어떻게 움직일지는 정책이 정한다.
    /// </summary>
    public readonly struct CrowdDirective
    {
        public readonly DirectiveKind Kind;
        /// <summary>Wait 일 때 대기 링 위의 지점 (월드, m)</summary>
        public readonly Vector3 WaitPoint;
        /// <summary>선회 방향 +1 / -1 (0 = 미지정)</summary>
        public readonly int OrbitSign;
        /// <summary>Engage 이고 포위 슬롯을 쓸 때만 true</summary>
        public readonly bool HasSlot;
        /// <summary>목표 기준 슬롯 방위각 (도, atan2(x, z) 기준)</summary>
        public readonly float SlotAngleDeg;
        public readonly int SlotId;
        public readonly Vector3 SlotPosition;
        public readonly bool HasSlotPosition;
        public readonly bool RequiresSlot;
        public readonly float SlotArrivalDistance;

        public CrowdDirective(DirectiveKind kind, Vector3 waitPoint, int orbitSign, bool hasSlot, float slotAngleDeg, bool requiresSlot = false)
        {
            Kind = kind;
            WaitPoint = waitPoint;
            OrbitSign = orbitSign;
            HasSlot = hasSlot;
            SlotAngleDeg = slotAngleDeg;
            SlotId = -1;
            SlotPosition = default;
            HasSlotPosition = false;
            RequiresSlot = requiresSlot;
            SlotArrivalDistance = 0.45f;
        }

        public CrowdDirective(DirectiveKind kind, in SlotSnapshot slot, float arrivalDistance = 0.45f)
        {
            Kind = kind; WaitPoint = slot.Position; OrbitSign = 0;
            HasSlot = true; SlotAngleDeg = 0f;
            SlotId = slot.Id; SlotPosition = slot.Position; HasSlotPosition = true;
            RequiresSlot = true;
            SlotArrivalDistance = arrivalDistance;
        }

        public static CrowdDirective WaitingForSlot(Vector3 position) =>
            new CrowdDirective(DirectiveKind.Wait, position, 0, false, 0f, true);

        public static CrowdDirective None => default;
    }

    /// <summary>정책이 실행기에 넘기는 이동 의도</summary>
    public readonly struct MoveIntent
    {
        /// <summary>정책이 판단한 현재 상태 (표시·외부 출력용)</summary>
        public readonly MovementState State;
        /// <summary>false 면 정지</summary>
        public readonly bool HasDestination;
        public readonly Vector3 Destination;
        /// <summary>m/s</summary>
        public readonly float Speed;
        /// <summary>m</summary>
        public readonly float StopDistance;

        public MoveIntent(MovementState state, Vector3 destination, float speed, float stopDistance)
        {
            State = state;
            HasDestination = true;
            Destination = destination;
            Speed = speed;
            StopDistance = stopDistance;
        }

        private MoveIntent(MovementState state)
        {
            State = state;
            HasDestination = false;
            Destination = default;
            Speed = 0f;
            StopDistance = 0f;
        }

        public static MoveIntent Stop(MovementState state) => new MoveIntent(state);
    }

    /// <summary>
    /// MovementProfile 의 값 복사본. 정책이 ScriptableObject 에 의존하지 않게 해서
    /// Unity 밖(EditMode/순수 NUnit)에서도 정책을 테스트할 수 있게 한다.
    /// </summary>
    public readonly struct MovementTuning
    {
        public readonly float MoveSpeed;            // m/s
        public readonly float AttackRange;          // m
        public readonly float OrbitChance;          // 0~1, 대기 진입 시 선회를 고를 확률
        public readonly float OrbitAngularSpeedDeg; // deg/s
        public readonly float KeepDistanceMin;      // m
        public readonly float KeepDistanceMax;      // m
        public readonly float FlankViewHalfAngleDeg;// 도, 목표 시야 반각
        public readonly float FlankRadius;          // m, 측면 경유점 반경
        public readonly float FlankSpeedMultiplier; // 시야 밖 돌입 속도 배율

        public MovementTuning(float moveSpeed, float attackRange, float orbitChance, float orbitAngularSpeedDeg,
                              float keepDistanceMin, float keepDistanceMax,
                              float flankViewHalfAngleDeg, float flankRadius, float flankSpeedMultiplier)
        {
            MoveSpeed = moveSpeed;
            AttackRange = attackRange;
            OrbitChance = orbitChance;
            OrbitAngularSpeedDeg = orbitAngularSpeedDeg;
            KeepDistanceMin = keepDistanceMin;
            KeepDistanceMax = keepDistanceMax;
            FlankViewHalfAngleDeg = flankViewHalfAngleDeg;
            FlankRadius = flankRadius;
            FlankSpeedMultiplier = flankSpeedMultiplier;
        }
    }

    /// <summary>정책 한 번의 판단 입력</summary>
    public readonly struct MovementContext
    {
        public readonly Vector3 SelfPosition;
        public readonly Vector3 TargetPosition;
        public readonly Vector3 TargetForward;
        public readonly bool TargetValid;
        public readonly CrowdDirective Directive;
        public readonly MovementTuning Tuning;
        /// <summary>현재 시각 (s)</summary>
        public readonly float Now;
        /// <summary>직전 판단 이후 경과 시간 (s)</summary>
        public readonly float DeltaTime;

        public MovementContext(Vector3 selfPosition, Vector3 targetPosition, Vector3 targetForward, bool targetValid,
                               CrowdDirective directive, MovementTuning tuning, float now, float deltaTime)
        {
            SelfPosition = selfPosition;
            TargetPosition = targetPosition;
            TargetForward = targetForward;
            TargetValid = targetValid;
            Directive = directive;
            Tuning = tuning;
            Now = now;
            DeltaTime = deltaTime;
        }
    }
}
