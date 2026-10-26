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
        // Wait 일 때 대기 링 위의 지점 (월드, m)
        public readonly Vector3 WaitPoint;
        // 선회 방향 +1 / -1 (0 = 미지정)
        public readonly int OrbitSign;
        // Engage 이고 포위 슬롯을 쓸 때만 true
        public readonly bool HasSlot;
        // 목표 기준 슬롯 방위각 (도, atan2(x, z) 기준)
        public readonly float SlotAngleDeg;
        // SlotRegistry 가 배정한 슬롯 번호 (-1 = 없음)
        public readonly int SlotId;
        // NavMesh 에 투영된 슬롯의 실제 목적지 (월드, m)
        public readonly Vector3 SlotPosition;
        // SlotPosition 이 유효한가 (슬롯 모드에서 슬롯을 배정받았을 때 true)
        public readonly bool HasSlotPosition;
        // true 면 슬롯을 받기 전에는 움직이지 말고 대기해야 한다 (슬롯 모드)
        public readonly bool RequiresSlot;
        // 이 거리(m) 안이면 슬롯에 도착한 것으로 본다
        public readonly float SlotArrivalDistance;

        /// <summary>토큰 모드·제한 없음용 생성자 (슬롯 위치 없음, SlotId = -1, 도착 거리 0.45m)</summary>
        /// <param name="kind">지시 종류</param>
        /// <param name="waitPoint">Wait 일 때 대기 링 위의 지점 (월드, m). 그 외는 무시</param>
        /// <param name="orbitSign">선회 방향 +1 / -1 (0 = 미지정)</param>
        /// <param name="hasSlot">Engage 이고 포위 슬롯 방위를 쓰는가</param>
        /// <param name="slotAngleDeg">목표 기준 슬롯 방위각 (도). hasSlot 이 false 면 무시</param>
        /// <param name="requiresSlot">true = 슬롯을 받기 전에는 대기 (슬롯 모드)</param>
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

        /// <summary>슬롯 모드용 생성자: 배정받은 슬롯의 위치·번호를 담는다</summary>
        /// <param name="kind">지시 종류 (Engage = 접근 슬롯, Wait = 대기 슬롯)</param>
        /// <param name="slot">배정받은 슬롯. Position 은 대기 지점과 목적지로 함께 쓴다</param>
        /// <param name="arrivalDistance">이 거리(m) 안이면 슬롯에 도착한 것으로 본다</param>
        public CrowdDirective(DirectiveKind kind, in SlotSnapshot slot, float arrivalDistance = 0.45f)
        {
            Kind = kind; WaitPoint = slot.Position; OrbitSign = 0;
            HasSlot = true; SlotAngleDeg = 0f;
            SlotId = slot.Id; SlotPosition = slot.Position; HasSlotPosition = true;
            RequiresSlot = true;
            SlotArrivalDistance = arrivalDistance;
        }

        /// <summary>슬롯을 아직 못 받음: 제자리(position)에서 대기하라는 지시</summary>
        /// <param name="position">대기할 현재 위치 (월드, m)</param>
        /// <returns>RequiresSlot = true 이고 슬롯 위치가 없는 Wait 지시</returns>
        public static CrowdDirective WaitingForSlot(Vector3 position) =>
            new CrowdDirective(DirectiveKind.Wait, position, 0, false, 0f, true);

        // 아직 판단 전 (Kind = None)
        public static CrowdDirective None => default;
    }

    /// <summary>정책이 실행기에 넘기는 이동 의도. MonsterMovement 가 반응 지연을 거쳐 IMoveExecutor.Apply 로 전달한다</summary>
    public readonly struct MoveIntent
    {
        // 정책이 판단한 현재 상태 (표시·외부 출력용)
        public readonly MovementState State;
        // false 면 정지
        public readonly bool HasDestination;
        public readonly Vector3 Destination;
        public readonly float Speed;         // m/s
        public readonly float StopDistance;  // m

        /// <summary>목적지로 이동하라는 의도</summary>
        /// <param name="state">정책이 판단한 현재 상태</param>
        /// <param name="destination">목적지 (월드)</param>
        /// <param name="speed">이동 속도 (m/s)</param>
        /// <param name="stopDistance">목적지 앞에서 멈출 거리 (m)</param>
        public MoveIntent(MovementState state, Vector3 destination, float speed, float stopDistance)
        {
            State = state;
            HasDestination = true;
            Destination = destination;
            Speed = speed;
            StopDistance = stopDistance;
        }

        /// <summary>정지 의도 생성자. Stop 만 사용한다</summary>
        /// <param name="state">정지 이유를 나타내는 상태</param>
        private MoveIntent(MovementState state)
        {
            State = state;
            HasDestination = false;
            Destination = default;
            Speed = 0f;
            StopDistance = 0f;
        }

        /// <summary>제자리 정지 의도 (HasDestination = false)</summary>
        /// <param name="state">정지 이유를 표시하는 상태</param>
        /// <returns>목적지 없는 정지 의도</returns>
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
        public readonly float FlankRadius;          // m, 측면 경유점 반경
        public readonly float FlankSpeedMultiplier; // 시야 밖 돌입 속도 배율
        public readonly float FlankSwitchDistance;  // m, 이 거리 이하면 근거리 돌입 모드로 전환
        public readonly float FlankSwitchBand;      // m, 근거리 → 원거리 복귀는 전환 거리 + 이 폭을 넘을 때

        /// <summary>수치 복사본 생성. MovementProfile.ToTuning 과 MonsterMovement(개체별 속도·사거리 반영)가 호출한다</summary>
        /// <param name="moveSpeed">이동 속도 (m/s)</param>
        /// <param name="attackRange">공격 사거리 (m)</param>
        /// <param name="orbitChance">대기 진입 시 선회를 고를 확률 (0~1)</param>
        /// <param name="orbitAngularSpeedDeg">선회 각속도 (deg/s)</param>
        /// <param name="keepDistanceMin">거리 유지 밴드 최소 (m)</param>
        /// <param name="keepDistanceMax">거리 유지 밴드 최대 (m)</param>
        /// <param name="flankRadius">측면 경유점 반경 (m)</param>
        /// <param name="flankSpeedMultiplier">시야 밖 돌입 속도 배율</param>
        /// <param name="flankSwitchDistance">근거리 돌입 모드로 바꾸는 거리 (m). flankRadius 보다 커야 한다</param>
        /// <param name="flankSwitchBand">근거리 → 원거리 복귀 여유 폭 (m)</param>
        public MovementTuning(float moveSpeed, float attackRange, float orbitChance, float orbitAngularSpeedDeg,
                              float keepDistanceMin, float keepDistanceMax,
                              float flankRadius, float flankSpeedMultiplier,
                              float flankSwitchDistance, float flankSwitchBand)
        {
            MoveSpeed = moveSpeed;
            AttackRange = attackRange;
            OrbitChance = orbitChance;
            OrbitAngularSpeedDeg = orbitAngularSpeedDeg;
            KeepDistanceMin = keepDistanceMin;
            KeepDistanceMax = keepDistanceMax;
            FlankRadius = flankRadius;
            FlankSpeedMultiplier = flankSpeedMultiplier;
            FlankSwitchDistance = flankSwitchDistance;
            FlankSwitchBand = flankSwitchBand;
        }
    }

    /// <summary>정책 한 번의 판단 입력. MonsterMovement.Think 가 만들어 IMovementPolicy.Evaluate 에 넘긴다</summary>
    public readonly struct MovementContext
    {
        public readonly Vector3 SelfPosition;
        public readonly Vector3 TargetPosition;
        public readonly Vector3 TargetForward;  // 플레이어 카메라(ViewPoint)가 바라보는 방향
        public readonly Vector3 ViewPosition;   // 플레이어 카메라(ViewPoint) 위치 (월드). 시야 원뿔의 꼭짓점
        public readonly float ViewHalfAngleDeg; // 도, 플레이어 카메라 FOV 의 수평 반각
        public readonly bool TargetValid;   // false 면 TargetPosition 은 자기 위치 (정책은 정지해야 한다)
        // 군중 제어가 내린 지시 (허가 + 슬롯 정보)
        public readonly CrowdDirective Directive;
        // 몬스터 유형별 수치
        public readonly MovementTuning Tuning;
        public readonly float Now;          // 현재 시각 (s)
        public readonly float DeltaTime;    // 직전 판단 이후 경과 시간 (s), 첫 판단은 0

        /// <summary>판단 입력 생성. MonsterMovement.Think 가 매 판단마다 만들어 정책에 넘긴다</summary>
        /// <param name="selfPosition">자기 위치 (월드)</param>
        /// <param name="targetPosition">목표 위치 (월드). 목표가 무효면 자기 위치</param>
        /// <param name="targetForward">플레이어 카메라가 바라보는 방향</param>
        /// <param name="viewPosition">플레이어 카메라 위치 (월드)</param>
        /// <param name="viewHalfAngleDeg">플레이어 카메라 FOV 의 수평 반각 (도)</param>
        /// <param name="targetValid">목표가 유효한가</param>
        /// <param name="directive">군중 제어가 내린 지시</param>
        /// <param name="tuning">몬스터 유형별 수치</param>
        /// <param name="now">현재 시각 (s)</param>
        /// <param name="deltaTime">직전 판단 이후 경과 시간 (s), 첫 판단은 0</param>
        public MovementContext(Vector3 selfPosition, Vector3 targetPosition, Vector3 targetForward,
                               Vector3 viewPosition, float viewHalfAngleDeg, bool targetValid,
                               CrowdDirective directive, MovementTuning tuning, float now, float deltaTime)
        {
            SelfPosition = selfPosition;
            TargetPosition = targetPosition;
            TargetForward = targetForward;
            ViewPosition = viewPosition;
            ViewHalfAngleDeg = viewHalfAngleDeg;
            TargetValid = targetValid;
            Directive = directive;
            Tuning = tuning;
            Now = now;
            DeltaTime = deltaTime;
        }
    }
}
