using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Movement
{
    /// <summary>NavMeshAgent 로 MoveIntent 를 실행. 재경로를 거리·시간 임계값으로 제한한다</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NavMeshMoveExecutor : MonoBehaviour, IMoveExecutor
    {
        private const float SampleWarnInterval = 2f;

        [Tooltip("목적지가 이 거리 이상 바뀌어야 재경로 (m)")]
        [SerializeField, Min(0f)] private float repathDistance = 0.5f;
        [Tooltip("재경로 최소 간격 (s)")]
        [SerializeField, Min(0f)] private float minRepathInterval = 0.25f;
        [Tooltip("목적지가 NavMesh 밖이면 이 반경 안에서 가장 가까운 점으로 보정 (m)")]
        [SerializeField, Min(0f)] private float sampleRadius = 4f;
        [Tooltip("Engaged 상태 회피 우선순위 (낮을수록 우선)")]
        [SerializeField, Range(0, 99)] private int engagedPriority = 30;
        [SerializeField, Range(0, 99)] private int defaultPriority = 60;

        private NavMeshAgent agent;
        private bool hasDestination;
        private Vector3 lastRequestedDest;
        private float lastRepathTime;
        private int repathCount;
        private bool warnedOffMesh;
        private float lastSampleWarnTime = float.NegativeInfinity;
        private MoveExecutionState requestedState;
        private NavMeshPath path;
        private int priorityOffset;

        public Vector3 Velocity => agent.isActiveAndEnabled ? agent.velocity : Vector3.zero;

        /// <summary>디버그: 누적 SetPath 호출 수</summary>
        public int RepathCount => repathCount;
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

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            path = new NavMeshPath();
            // 같은 우선순위의 정면 대치가 반복되지 않게 개체별 우선순위를 분산한다.
            priorityOffset = GetInstanceID() & 15;
        }

        private void OnDisable()
        {
            hasDestination = false;
            warnedOffMesh = false;
        }

        public void Apply(in MoveIntent intent)
        {
            if (!intent.HasDestination)
            {
                Stop();
                return;
            }

            if (!agent.isOnNavMesh)
            {
                if (!warnedOffMesh)
                {
                    warnedOffMesh = true;
                    Debug.LogWarning($"[NavMeshMoveExecutor] NavMesh 위에 있지 않음: {name} @ {transform.position}", this);
                }
                return;
            }
            warnedOffMesh = false;

            agent.isStopped = false;
            agent.speed = intent.Speed;
            agent.stoppingDistance = intent.StopDistance;
            agent.avoidancePriority = intent.State == MovementState.Engaged
                ? engagedPriority : Mathf.Min(99, defaultPriority + priorityOffset);
            agent.autoBraking = intent.State != MovementState.Orbiting;

            if (hasDestination)
            {
                float moved = (intent.Destination - lastRequestedDest).magnitude;
                if ((moved < repathDistance && agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete)
                    || Time.time - lastRepathTime < minRepathInterval)
                    return;
            }

            if (!NavMesh.SamplePosition(intent.Destination, out NavMeshHit hit, sampleRadius, agent.areaMask))
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                if (Time.time - lastSampleWarnTime >= SampleWarnInterval)
                {
                    lastSampleWarnTime = Time.time;
                    Debug.LogWarning($"[NavMeshMoveExecutor] 목적지 근처({sampleRadius}m)에 NavMesh 없음: {name} dest={intent.Destination}", this);
                }
                return;
            }

            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                return;
            }
            if (!agent.SetPath(path))
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                return;
            }
            requestedState = MoveExecutionState.Moving;
            repathCount++;
            lastRequestedDest = intent.Destination;
            lastRepathTime = Time.time;
            hasDestination = true;
        }

        public void Stop()
        {
            hasDestination = false;
            requestedState = MoveExecutionState.Idle;
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
        }
    }
}
