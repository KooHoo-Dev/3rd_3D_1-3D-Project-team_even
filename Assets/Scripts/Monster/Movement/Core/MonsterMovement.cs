using System;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 몬스터 1마리의 이동 진입점. 상태 보유, 정책 호출, 실행기 전달, 외부 정지 요청 처리.
    /// 같은 GameObject 에 IMoveExecutor 구현 컴포넌트가 있어야 한다.
    /// 런타임 생성 시: 비활성 GameObject 에 붙이고 Configure() → SetActive(true).
    /// </summary>
    public sealed class MonsterMovement : MonoBehaviour
    {
        // ════════ 설정 (Inspector / Configure 로 주입) ════════
        [Header("설정")]
        [SerializeField] private MovementProfile profile;
        [Tooltip("비우면 CrowdCoordinator.Active 사용")]
        [SerializeField] private CrowdCoordinator coordinator;
        [Tooltip("군중 관리자 없는 직접 추적 모드의 IMovementTarget")]
        [SerializeField] private MonoBehaviour directTargetSource;

        // ════════ 협력 객체 (Awake/OnEnable 에서 연결) ════════
        // 실행기: "어떻게 움직일지"를 NavMeshAgent 로 실제 수행 (정책이 낸 MoveIntent 를 받는다)
        // 나중에 NavMesh 만으로
        private IMoveExecutor executor;
        // 정책: "어디로 갈지"를 판단 (profile.PolicyKind 로 생성, 새 이동 방식은 여기만 추가)
        private IMovementPolicy policy;
        // profile 의 수치 복사본 (정책이 ScriptableObject 에 의존하지 않게)
        private MovementTuning tuning;
        // 슬롯·토큰을 나눠주는 군중 관리자 (직접 추적 모드면 null)
        private CrowdCoordinator crowdCoordinator;
        // 추적 대상. 군중 모드에서는 Think(target) 인자로 받고, 직접 모드에서는 이 값을 쓴다
        private IMovementTarget targetInfo;

        // ════════ 현재 이동 상태 (외부 공개 프로퍼티의 원본) ════════
        private MovementState state = MovementState.Idle;
        private bool suspended;         // 공격·경직 등으로 이동 정지 중
        private bool isInAttackRange;   // 목표 유효 + 수평 거리 ≤ AttackRange
        private MoveIntent lastIntent;  // 마지막으로 실행기에 전달한 의도 (디버그용)

        // ════════ 군중 배분 결과 (CrowdCoordinator 가 채워 넣고 정책이 읽는다) ════════
        private CrowdDirective directive;
        private bool hasToken;                // 근접 토큰 보유 여부 (토큰 모드)
        private float waitingSince = -1f;     // Wait 시작 시각 (음수 = 대기 아님)
        private int orbitSign;                // 대기 중 선회 방향 +1/-1
        private int slotIndex = -1;           // 포위 슬롯 번호 (토큰 모드)

        // ════════ 초기화 시점 ════════
        private bool started;                 // Start 를 거쳤는가 (첫 활성화는 Start, 이후 재활성화는 OnEnable 에서 초기화)

        // ════════ 판단 주기 ════════
        private bool hasThought;              // 한 번이라도 Think 했는가 (첫 dt 를 0 으로 만들기 위함)
        private float lastThinkTime;
        private float nextDirectThink;        // 직접 추적 모드의 다음 판단 시각

        // ════════ 반응 지연 (슬롯 목적지가 크게 바뀌면 잠깐 뜸 들인 뒤 반응) ════════
        private MoveIntent pendingIntent;     // 지연 중인 의도
        private bool hasPendingIntent;
        private float pendingApplyAt;         // pendingIntent 를 적용할 시각
        private Vector3 reactionReferenceDestination; // "목적지가 바뀌었나" 비교 기준점
        private bool hasReactionReference;
        private int reactionSlotId = -1;      // 기준점을 잡은 슬롯
        private IMovementTarget reactionTargetInfo; // 기준점을 잡은 목표 (목표 교체 감지용)

        // ════════ 슬롯 실행 추적 ════════
        // 실행기가 지금 향하고 있는 슬롯 번호. 경로 없음(Unreachable)이면 그 슬롯을 거절하기 위해 기억한다
        private int executingSlotId = -1;

        // ════════ 외부 공개 (읽기 전용) ════════
        public MovementProfile Profile => profile;
        public MovementState State => state;
        /// <summary>목표 유효 + 수평 거리 ≤ AttackRange</summary>
        public bool IsInAttackRange => isInAttackRange;
        public bool IsSuspended => suspended;
        /// <summary>현재 속도 (m/s)</summary>
        public Vector3 Velocity => executor != null ? executor.Velocity : Vector3.zero;
        /// <summary>마지막으로 받은 군중 지시 (디버그용)</summary>
        public CrowdDirective Directive => directive;
        public MoveIntent LastIntent => lastIntent;
        public MoveExecutionState ExecutionState => executor != null ? executor.ExecutionState : MoveExecutionState.Idle;
        public float Radius => GetComponent<UnityEngine.AI.NavMeshAgent>().radius;

        /// <summary>(sender, previous, current)</summary>
        public event Action<MonsterMovement, MovementState, MovementState> StateChanged;

        /// <summary>같은 GameObject 의 IMoveExecutor 를 찾는다. 없으면 에러 로그 후 컴포넌트 비활성화</summary>
        private void Awake()
        {
            executor = GetComponent<IMoveExecutor>();
            if (executor == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] IMoveExecutor 구현 컴포넌트 없음", this);
                enabled = false;
            }
        }

        /// <summary>
        /// 재활성화 때만 여기서 바로 초기화한다. 첫 활성화는 Start 로 미룬다.
        /// 씬에 미리 놓인 몬스터가 CrowdCoordinator.OnEnable(Active 지정)보다 먼저 활성화돼도,
        /// 모든 OnEnable 이 끝난 Start 시점에는 Active 가 정해져 있다 (DefaultExecutionOrder 없이 순서 보장).
        /// </summary>
        private void OnEnable()
        {
            if (started) Activate();
        }

        /// <summary>첫 활성화 초기화 (모든 OnEnable 이 끝난 뒤). 이후 재활성화는 OnEnable 이 Activate 를 호출한다</summary>
        private void Start()
        {
            started = true;
            if (enabled) Activate();
        }

        /// <summary>
        /// 동작 모드를 결정한다.
        /// 군중 모드(CrowdCoordinator 에 등록) 또는 직접 추적 모드(목표를 직접 쥠) 중 하나가 성립해야 하며, 둘 다 없으면 비활성화.
        /// </summary>
        private void Activate()
        {
            if (profile == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] MovementProfile 없음", this);
                enabled = false;
                return;
            }

            // 1) 목표·군중 관리자 확인: 직접 목표가 있으면 군중 관리자 없이 동작, 없으면 Coordinator 필수
            if (targetInfo == null && directTargetSource != null)
                targetInfo = directTargetSource as IMovementTarget
                    ?? throw new InvalidOperationException("directTargetSource 는 IMovementTarget 이어야 한다");
            var coord = coordinator != null ? coordinator : (targetInfo == null ? CrowdCoordinator.Active : null);
            if (coord == null && targetInfo == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] CrowdCoordinator 없음 (직접 지정하거나 씬에 배치)", this);
                enabled = false;
                return;
            }

            // 2) 정책·수치 준비 (정책은 개체 하나당 인스턴스 하나, 재활성화 시 재사용)
            tuning = profile.ToTuning();
            policy ??= MovementPolicyFactory.Create(profile.PolicyKind, GetInstanceID());

            // 3) 이전 활성화 때 남은 상태 초기화
            hasThought = false;
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;

            // 4) 군중 모드면 등록, 직접 모드면 제한 없음(Unrestricted) 지시를 고정
            if (coord != null)
            {
                coord.Register(this);
                crowdCoordinator = coord;
            }
            else
                directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f);
        }

        /// <summary>
        /// 직접 추적 모드만 스스로 갱신한다 (TickMovement 후 0.1초마다 Think).
        /// 군중 모드는 CrowdCoordinator.Update 가 Think 와 TickMovement 를 순서대로 호출하므로 여기서는 아무것도 하지 않는다
        /// </summary>
        private void Update()
        {
            if (crowdCoordinator != null || targetInfo == null) return;
            TickMovement();
            if (Time.time < nextDirectThink) return;
            Think(targetInfo, Time.time);
            nextDirectThink = Time.time + 0.1f;
        }

        /// <summary>
        /// 프레임 갱신: 슬롯 예약이 사라졌으면 정지, 반응 지연이 끝난 의도 적용.
        /// 군중 모드는 CrowdCoordinator 가 Think 직후 호출, 직접 모드는 Update 가 호출
        /// </summary>
        internal void TickMovement()
        {
            if (hasReactionReference && (!MovementTargetUtility.IsValid(reactionTargetInfo) ||
                                         !HasCurrentReservation(reactionSlotId)))
                StopForInvalidReservation();
            if (hasPendingIntent)
            {
                if (Time.time >= pendingApplyAt)
                {
                    hasPendingIntent = false;
                    reactionReferenceDestination = pendingIntent.Destination;
                    CommitIntent(in pendingIntent);
                }
            }
        }

        /// <summary>
        /// 비활성화/파괴(몬스터 사망 포함) 시 정리: 군중 관리자 등록 해제(점유 중인 슬롯·토큰 반환) 후 이동 정지.
        /// 이 덕분에 몬스터가 중간에 제거돼도 슬롯이 다른 몬스터에게 돌아간다
        /// </summary>
        private void OnDisable()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            if (crowdCoordinator != null)
            {
                crowdCoordinator.Unregister(this); // 내부에서 ResetCrowdState 호출
                crowdCoordinator = null;
            }
            executor?.Stop();
            suspended = false;
            SetState(MovementState.Idle);
            isInAttackRange = false;
            lastIntent = MoveIntent.Stop(MovementState.Idle);
        }

        /// <summary>OnEnable 전에 호출. coordinator 가 null 이면 Active 사용</summary>
        public void Configure(MovementProfile profile, CrowdCoordinator coordinator)
        {
            if (isActiveAndEnabled)
                throw new InvalidOperationException("Configure 는 비활성 상태에서만 호출할 수 있다");
            this.profile = profile;
            this.coordinator = coordinator;
            targetInfo = null;
            directTargetSource = null;
            policy = null; // 활성화 시 새 profile 로 재생성
        }

        /// <summary>군중 관리자 없이 목표를 직접 추적하는 모드로 설정. OnEnable 전에만 호출 가능</summary>
        public void ConfigureDirect(MovementProfile profile, IMovementTarget target)
        {
            if (isActiveAndEnabled) throw new InvalidOperationException("ConfigureDirect 는 비활성 상태에서만 호출할 수 있다");
            this.profile = profile;
            targetInfo = target ?? throw new ArgumentNullException(nameof(target));
            directTargetSource = target as MonoBehaviour;
            coordinator = null;
            policy = null;
        }

        /// <summary>공격 동작·경직 중 이동 정지 요청. 토큰은 유지된다</summary>
        public void SetSuspended(bool suspended)
        {
            this.suspended = suspended;
            if (!suspended) return; // 상태는 다음 Think 에서 재계산
            isInAttackRange = false;
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            if (crowdCoordinator != null) crowdCoordinator.ReleaseSlot(this);
            executor.Stop();
            lastIntent = MoveIntent.Stop(MovementState.Suspended);
            SetState(MovementState.Suspended);
        }

        // ── CrowdCoordinator 전용 (internal) ──

        /// <summary>배분 입력용 현재 상태</summary>
        internal AllocationAgent BuildAllocationAgent() => new AllocationAgent
        {
            Position = transform.position,
            UsesToken = profile.UsesEngageToken,
            HasToken = hasToken,
            WaitingSince = waitingSince,
            MinWaitSeconds = profile.MinWaitSeconds,
            OrbitSign = orbitSign,
            SlotIndex = slotIndex,
        };

        /// <summary>배분 결과 반영 (토큰·대기 시각·선회 방향·슬롯)</summary>
        internal void ApplyAllocation(in AllocationResult result)
        {
            directive = result.Directive;
            hasToken = result.HasToken;
            waitingSince = result.WaitingSince;
            orbitSign = result.OrbitSign;
            slotIndex = result.SlotIndex;
        }

        /// <summary>판단 1회: (슬롯 모드면 지시 갱신) → 정책 Evaluate → 실행기 Apply → 공격 사거리 갱신</summary>
        internal void Think(IMovementTarget target, float now)
        {
            if (suspended) return;

            // 1) 슬롯 모드: 도달 불가였던 슬롯은 거절하고, 군중 관리자에게 최신 지시(슬롯)를 받는다
            if (crowdCoordinator != null && crowdCoordinator.Settings.UseSlots && crowdCoordinator.Settings.EnableCrowdControl)
            {
                if (executingSlotId >= 0 && executor.ExecutionState == MoveExecutionState.Unreachable)
                {
                    crowdCoordinator.RejectSlot(this, executingSlotId, now);
                    executingSlotId = -1;
                }
                directive = crowdCoordinator.RequestDirective(this, now);
            }

            // 2) 정책 입력(Context) 구성. 목표가 무효면 자기 위치를 목표로 둔다 (정책이 TargetValid 로 걸러냄)
            float dt = hasThought ? now - lastThinkTime : 0f;
            hasThought = true;
            lastThinkTime = now;

            Vector3 self = transform.position;
            bool targetValid = MovementTargetUtility.IsValid(target);
            Vector3 targetPosition = targetValid ? target.Position : self;
            Vector3 targetForward = targetValid ? target.Forward : Vector3.forward;
            var ctx = new MovementContext(self, targetPosition, targetForward, targetValid,
                                           directive, tuning, now, dt);
            // 3) 정책이 "어디로 갈지" 결정 → 반응 지연 규칙을 거쳐 실행기에 전달
            var intent = policy.Evaluate(in ctx);
            SubmitIntent(in intent, target, targetValid, now);

            // 4) 공격 사거리 안인지 갱신 (공격 모듈이 읽음)
            float dx = targetPosition.x - self.x;
            float dz = targetPosition.z - self.z;
            isInAttackRange = targetValid && dx * dx + dz * dz <= tuning.AttackRange * tuning.AttackRange;
        }

        /// <summary>등록 해제 시 토큰·대기 상태 초기화 (선회 방향은 유지)</summary>
        internal void ResetCrowdState()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            hasToken = false;
            waitingSince = -1f;
            slotIndex = -1;
            directive = CrowdDirective.None;
        }

        /// <summary>
        /// 정책의 의도를 실행기에 보내기 전 "반응 지연"을 적용한다.
        /// 슬롯 목적지가 기준점에서 ReactionChangeDistance 이상 바뀌면 랜덤 시간 뒤에 반응해 몬스터들이 동시에 꺾이는 부자연스러움을 줄인다.
        /// 슬롯 모드가 아니거나 슬롯이 없으면 지연 없이 즉시 실행.
        /// </summary>
        private void SubmitIntent(in MoveIntent intent, IMovementTarget target, bool targetValid, float now)
        {
            // 1) 지연 대상이 아닌 경우: 슬롯 모드가 아니거나, 목표/목적지/슬롯이 없거나, 예약이 이미 무효 → 즉시 실행
            bool slotMode = crowdCoordinator != null && crowdCoordinator.Settings.UseSlots &&
                            crowdCoordinator.Settings.EnableCrowdControl;
            if (!slotMode || !targetValid || !intent.HasDestination || !directive.HasSlotPosition ||
                !HasCurrentReservation(directive.SlotId))
            {
                CancelPendingIntent();
                hasReactionReference = false;
                if (!targetValid || !intent.HasDestination || (slotMode && !directive.HasSlotPosition))
                    executingSlotId = -1;
                CommitIntent(in intent);
                return;
            }

            // 2) 기준점이 없거나 슬롯/목표가 바뀌었으면: 새 기준점을 잡고 즉시 실행 (슬롯이 바뀌면 이전 이동은 멈춤)
            int slotId = directive.SlotId;
            if (!hasReactionReference || !ReferenceEquals(reactionTargetInfo, target) || reactionSlotId != slotId)
            {
                CancelPendingIntent();
                if (hasReactionReference && reactionSlotId != slotId)
                {
                    executor.Stop();
                    executingSlotId = -1;
                }
                reactionTargetInfo = target;
                reactionSlotId = slotId;
                reactionReferenceDestination = intent.Destination;
                hasReactionReference = true;
                CommitIntent(in intent);
                return;
            }

            // 3) 같은 슬롯을 계속 추적 중: 목적지 변화량이 임계값 이상인지 판단.
            //    이미 지연 중이면 최신 의도로 갱신(변화 유지) 또는 취소(변화 사라짐), 아니면 지연을 새로 시작
            float thresholdSqr = profile.ReactionChangeDistance * profile.ReactionChangeDistance;
            float changeSqr = HorizontalSqr(intent.Destination, reactionReferenceDestination);
            bool changed = changeSqr > 0f && changeSqr >= thresholdSqr;
            if (hasPendingIntent)
            {
                if (changed)
                {
                    pendingIntent = intent;
                    return;
                }
                CancelPendingIntent();
            }
            else if (changed && profile.ReactionDelayMax > 0f)
            {
                float delay = UnityEngine.Random.Range(profile.ReactionDelayMin, profile.ReactionDelayMax);
                if (delay > 0f)
                {
                    pendingIntent = intent;
                    hasPendingIntent = true;
                    pendingApplyAt = now + delay;
                    crowdCoordinator.PauseSlotProgress(this, delay);
                    return;
                }
            }

            // 4) 지연할 필요 없음: 기준점 갱신 후 즉시 실행
            if (changed) reactionReferenceDestination = intent.Destination;
            CommitIntent(in intent);
        }

        /// <summary>의도를 실행기에 적용하고, 결과에 따라 "실행 중인 슬롯"과 상태를 갱신한다</summary>
        private void CommitIntent(in MoveIntent intent)
        {
            lastIntent = intent;
            MoveApplyResult result = executor.Apply(in intent);
            if (result == MoveApplyResult.PathSet || result == MoveApplyResult.Unreachable)
                executingSlotId = directive.HasSlotPosition ? directive.SlotId : -1;
            else if (result == MoveApplyResult.Stopped)
                executingSlotId = -1;
            SetState(intent.State);
        }

        /// <summary>
        /// slotId 가 아직 내 예약이며 유효한 슬롯인가 (슬롯이 무효화/회수되면 false).
        /// 스냅샷(혼잡 판정 포함) 대신 코디네이터의 O(1) 조회를 쓴다
        /// </summary>
        private bool HasCurrentReservation(int slotId) =>
            crowdCoordinator != null && crowdCoordinator.IsSlotReservedBy(this, slotId);

        /// <summary>예약 슬롯이 사라졌을 때(타겟 소실·NavMesh 변경 등) 이동을 멈추고 Idle 로 돌아간다. 다음 Think 에서 새 슬롯을 받는다</summary>
        private void StopForInvalidReservation()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            executor.Stop();
            lastIntent = MoveIntent.Stop(MovementState.Idle);
            SetState(MovementState.Idle);
        }

        /// <summary>반응 지연 중인 의도를 버린다</summary>
        private void CancelPendingIntent() => hasPendingIntent = false;

        /// <summary>수평(XZ) 거리의 제곱</summary>
        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>상태 변경. 실제로 바뀐 경우에만 StateChanged 이벤트 발생</summary>
        private void SetState(MovementState next)
        {
            if (state == next) return;
            var prev = state;
            state = next;
            StateChanged?.Invoke(this, prev, next);
        }
    }
}
