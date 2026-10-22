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
        [SerializeField] private MovementProfile profile;
        [Tooltip("비우면 CrowdCoordinator.Active 사용")]
        [SerializeField] private CrowdCoordinator coordinator;
        [Tooltip("군중 관리자 없는 직접 추적 모드의 IMovementTarget")]
        [SerializeField] private MonoBehaviour directTargetSource;

        private IMoveExecutor executor;
        private IMovementPolicy policy;
        private MovementTuning tuning;
        private MovementState state = MovementState.Idle;
        private CrowdDirective directive;
        private bool hasToken;
        private float waitingSince = -1f;
        private int orbitSign;
        private int slotIndex = -1;
        private bool suspended;
        private bool isInAttackRange;
        private bool hasThought;
        private float lastThinkTime;
        private CrowdCoordinator crowdCoordinator;
        private IMovementTarget targetInfo;
        private float nextDirectThink;
        private MoveIntent lastIntent;
        private MoveIntent pendingIntent;
        private bool hasPendingIntent;
        private float pendingApplyAt;
        private Vector3 reactionReferenceDestination;
        private bool hasReactionReference;
        private int reactionSlotId = -1;
        private int executingSlotId = -1;
        private IMovementTarget reactionTargetInfo;

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

        private void Awake()
        {
            executor = GetComponent<IMoveExecutor>();
            if (executor == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] IMoveExecutor 구현 컴포넌트 없음", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (profile == null)
            {
                Debug.LogError($"[{nameof(MonsterMovement)}] MovementProfile 없음", this);
                enabled = false;
                return;
            }

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

            tuning = profile.ToTuning();
            policy ??= MovementPolicyFactory.Create(profile.PolicyKind, GetInstanceID());
            hasThought = false;
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            if (coord != null)
            {
                coord.Register(this);
                crowdCoordinator = coord;
            }
            else
                directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f);
        }

        private void Update()
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
            if (crowdCoordinator != null || targetInfo == null || Time.time < nextDirectThink) return;
            Think(targetInfo, 0f, Time.time);
            nextDirectThink = Time.time + 0.1f;
        }

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

        /// <summary>판단 1회: 정책 Evaluate → 실행기 Apply → 상태 갱신</summary>
        internal void Think(IMovementTarget target, float waitRadius, float now)
        {
            if (suspended) return;

            if (crowdCoordinator != null && crowdCoordinator.Settings.UseSlots && crowdCoordinator.Settings.EnableCrowdControl)
            {
                if (executingSlotId >= 0 && executor.ExecutionState == MoveExecutionState.Unreachable)
                {
                    crowdCoordinator.RejectSlot(this, executingSlotId, now);
                    executingSlotId = -1;
                }
                directive = crowdCoordinator.RequestDirective(this, now);
            }

            float dt = hasThought ? now - lastThinkTime : 0f;
            hasThought = true;
            lastThinkTime = now;

            Vector3 self = transform.position;
            bool targetValid = MovementTargetUtility.IsValid(target);
            Vector3 targetPosition = targetValid ? target.Position : self;
            Vector3 targetForward = targetValid ? target.Forward : Vector3.forward;
            var ctx = new MovementContext(self, targetPosition, targetForward, targetValid,
                                          directive, waitRadius, tuning, now, dt);
            var intent = policy.Evaluate(in ctx);
            SubmitIntent(in intent, target, targetValid, now);

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

        private void SubmitIntent(in MoveIntent intent, IMovementTarget target, bool targetValid, float now)
        {
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

            if (changed) reactionReferenceDestination = intent.Destination;
            CommitIntent(in intent);
        }

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

        private bool HasCurrentReservation(int slotId)
        {
            if (crowdCoordinator == null || slotId < 0 || slotId >= crowdCoordinator.SlotCount) return false;
            SlotSnapshot slot = crowdCoordinator.GetSlotSnapshot(slotId);
            return slot.OwnerId == GetInstanceID() && slot.Validity == SlotValidity.Valid;
        }

        private void StopForInvalidReservation()
        {
            CancelPendingIntent();
            hasReactionReference = false;
            executingSlotId = -1;
            executor.Stop();
            lastIntent = MoveIntent.Stop(MovementState.Idle);
            SetState(MovementState.Idle);
        }

        private void CancelPendingIntent() => hasPendingIntent = false;

        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private void SetState(MovementState next)
        {
            if (state == next) return;
            var prev = state;
            state = next;
            StateChanged?.Invoke(this, prev, next);
        }
    }
}
