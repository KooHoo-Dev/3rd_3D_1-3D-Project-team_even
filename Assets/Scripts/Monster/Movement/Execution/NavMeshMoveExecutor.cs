using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Movement
{
    /// <summary>NavMeshAgent 로 MoveIntent 를 실행. 재경로를 거리·시간 임계값으로 제한한다</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NavMeshMoveExecutor : MonoBehaviour, IMoveExecutor
    {
        private const float SampleWarnInterval = 2f;

        // ════════ 설정 (Inspector) ════════
        [Header("재경로 제한")]
        [Tooltip("목적지가 이 거리 이상 바뀌어야 재경로 (m)")]
        [SerializeField, Min(0f)] private float repathDistance = 0.5f;
        [Tooltip("재경로 최소 간격 (s)")]
        [SerializeField, Min(0f)] private float minRepathInterval = 0.25f;
        [Tooltip("목적지가 NavMesh 밖이면 이 반경 안에서 가장 가까운 점으로 보정 (m)")]
        [SerializeField, Min(0f)] private float sampleRadius = 4f;
        [Header("회피 우선순위")]
        [Tooltip("Engaged 상태 회피 우선순위 (낮을수록 우선)")]
        [SerializeField, Range(0, 99)] private int engagedPriority = 30;
        [SerializeField, Range(0, 99)] private int defaultPriority = 60;

        // ════════ 컴포넌트·재사용 객체 ════════
        private NavMeshAgent agent;
        private NavMeshPath path;         // CalculatePath 결과를 담는 재사용 버퍼 (매번 할당 방지)
        private int priorityOffset;       // 개체별 회피 우선순위 분산값 (0~15)

        // ════════ 재경로 추적 ════════
        private bool hasDestination;      // 현재 유효한 경로를 따라가는 중인가
        private Vector3 lastRequestedDest;// 마지막으로 경로를 설정한 목적지
        private float lastRepathTime;
        private int repathCount;          // 디버그용 누적 SetPath 횟수

        // ════════ 상태·경고 억제 ════════
        private MoveExecutionState requestedState; // Apply 가 기록한 상태 (Unreachable 보고용)
        private bool warnedOffMesh;                // NavMesh 밖 경고는 1회만
        private float lastSampleWarnTime = float.NegativeInfinity; // 목적지 근처 NavMesh 없음 경고 간격 제한

        // ════════ 외부 공개 ════════
        public Vector3 Velocity => agent.isActiveAndEnabled ? agent.velocity : Vector3.zero;

        /// <summary>디버그: 누적 SetPath 호출 수</summary>
        public int RepathCount => repathCount;
        /// <summary>Idle / OffNavMesh / Unreachable / Pending / Moving / Arrived 중 현재 상태를 NavMeshAgent 에서 계산</summary>
        public MoveExecutionState ExecutionState
        {
            get
            {
                if (agent == null || !agent.isActiveAndEnabled) return MoveExecutionState.Idle;
                if (!agent.isOnNavMesh) return MoveExecutionState.OffNavMesh;
                if (requestedState == MoveExecutionState.Unreachable) return requestedState;
                if (!hasDestination) return MoveExecutionState.Idle;
                if (agent.pathPending) return MoveExecutionState.Pending;
                if (agent.pathStatus != NavMeshPathStatus.PathComplete) return MoveExecutionState.Unreachable;
                return agent.remainingDistance <= agent.stoppingDistance + 0.15f
                    ? MoveExecutionState.Arrived : MoveExecutionState.Moving;
            }
        }

        /// <summary>NavMeshAgent 참조와 개체별 회피 우선순위 오프셋 준비</summary>
        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false; // 이동 방향 자동 회전 끔. 시선은 MonsterMovement.ApplyLookRotation 이 담당
            path = new NavMeshPath();
            // 같은 우선순위의 정면 대치가 반복되지 않게 개체별 우선순위를 분산한다.
            priorityOffset = GetInstanceID() & 15;
        }

        /// <summary>비활성화 시 경로 추적 상태 초기화</summary>
        private void OnDisable()
        {
            hasDestination = false;
            warnedOffMesh = false;
        }

        /// <summary>
        /// 이동 의도를 NavMeshAgent 에 적용한다. 정지 의도면 멈추고,
        /// 목적지 변화가 작거나 재경로 간격이 짧으면 기존 경로를 유지/보류해 경로 계산 비용을 줄인다.
        /// 결과(PathSet/PathKept/Deferred/Unreachable/OffNavMesh/Stopped)는 MonsterMovement 가 슬롯 추적에 사용한다.
        /// </summary>
        public MoveApplyResult Apply(in MoveIntent intent)
        {
            if (!intent.HasDestination)
            {
                Stop();
                return MoveApplyResult.Stopped;
            }

            // 1) NavMesh 위에 있는지 확인 (아니면 경고 1회 후 실패)
            if (!agent.isOnNavMesh)
            {
                if (!warnedOffMesh)
                {
                    warnedOffMesh = true;
                    Debug.LogWarning($"[NavMeshMoveExecutor] NavMesh 위에 있지 않음: {name} @ {transform.position}", this);
                }
                return MoveApplyResult.OffNavMesh;
            }
            warnedOffMesh = false;

            // 2) 이번 의도의 속도·정지거리·회피 우선순위 반영 (Engaged 는 우선, 선회 중엔 자동 감속 끔)
            agent.isStopped = false;
            agent.speed = intent.Speed;
            agent.stoppingDistance = intent.StopDistance;
            agent.avoidancePriority = intent.State == MovementState.Engaged
                ? engagedPriority : Mathf.Min(99, defaultPriority + priorityOffset);
            agent.autoBraking = intent.State != MovementState.Orbiting;

            // 3) 재경로 제한: 목적지가 거의 안 변했으면 유지, 직전 재경로가 너무 최근이면 보류
            if (hasDestination)
            {
                float moved = (intent.Destination - lastRequestedDest).magnitude;
                if (moved < repathDistance && agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete)
                    return MoveApplyResult.PathKept;
                if (Time.time - lastRepathTime < minRepathInterval)
                    return MoveApplyResult.Deferred;
            }

            // 4) 목적지를 NavMesh 위로 보정 → 경로 계산 → 설정. 어느 단계든 실패하면 정지 + Unreachable
            if (!NavMesh.SamplePosition(intent.Destination, out NavMeshHit hit, sampleRadius, agent.areaMask))
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                if (Time.time - lastSampleWarnTime >= SampleWarnInterval)
                {
                    lastSampleWarnTime = Time.time;
                    Debug.LogWarning($"[NavMeshMoveExecutor] 목적지 근처({sampleRadius}m)에 NavMesh 없음: {name} dest={intent.Destination}", this);
                }
                return MoveApplyResult.Unreachable;
            }

            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                return MoveApplyResult.Unreachable;
            }
            if (!agent.SetPath(path))
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                return MoveApplyResult.Unreachable;
            }
            requestedState = MoveExecutionState.Moving;
            repathCount++;
            lastRequestedDest = intent.Destination;
            lastRepathTime = Time.time;
            hasDestination = true;
            return MoveApplyResult.PathSet;
        }

        /// <summary>이동을 멈추고 경로 추적 상태를 초기화한다 (NavMesh 위가 아니면 상태만 초기화)</summary>
        public void Stop()
        {
            hasDestination = false;
            requestedState = MoveExecutionState.Idle;
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            agent.ResetPath();
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.nextPosition = transform.position;
        }
    }
}
