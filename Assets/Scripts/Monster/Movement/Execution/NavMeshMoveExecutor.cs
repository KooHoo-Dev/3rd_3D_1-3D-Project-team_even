using System;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 지상 몬스터용 실행기. MonsterMovement 가 넘긴 MoveIntent 를 NavMeshAgent 로 실행한다.
    /// 재경로를 거리·시간 임계값으로 제한한다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NavMeshMoveExecutor : MonoBehaviour, IMoveExecutor
    {
        private const float SampleWarnInterval = 2f; // s, "목적지 근처 NavMesh 없음" 경고 최소 간격

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
        [SerializeField, Range(0, 99)] private int defaultPriority = 60; // Engaged 외 상태 기본 우선순위. 개체별 분산값(0~15)을 더한다

        // ════════ 컴포넌트·재사용 객체 ════════
        private NavMeshAgent agent;
        private NavMeshPath path;         // CalculatePath 결과를 담는 재사용 버퍼 (매번 할당 방지)
        private int priorityOffset;       // 개체별 회피 우선순위 분산값 (0~15)

        // ════════ 재경로 추적 ════════
        private bool hasDestination;      // 현재 유효한 경로를 따라가는 중인가
        private Vector3 lastRequestedDest;// 마지막으로 경로를 설정한 목적지
        private float lastRepathTime;     // 마지막 재경로 시각 (s, Time.time 기준)
        private int repathCount;          // 디버그용 누적 SetPath 횟수

        // ════════ 상태·경고 억제 ════════
        private MoveExecutionState requestedState; // Apply 가 기록한 상태 (Unreachable 보고용)
        private bool warnedOffMesh;                // NavMesh 밖 경고는 1회만
        private float lastSampleWarnTime = float.NegativeInfinity; // 목적지 근처 NavMesh 없음 경고 간격 제한

        // ════════ 외부 공개 ════════
        private bool attackMotion;
        private bool savedUpdatePosition;
        private Vector3 attackRootOffset;
        private Vector3 attackReturnGround;
        private bool needsAttackWarp;
        private Func<Vector3> resolveAttackInterruption;

        public bool HasAttackMotion => attackMotion;
        public float AttackRadius => agent.radius;
        public float AttackHeight => agent.height;
        public Vector3 AttackGroundOrigin => transform.position - Vector3.up * agent.baseOffset;

        public NavMeshQueryFilter AttackNavigationFilter => new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

        /// <summary>정지된 Agent 의 자동 위치 갱신을 잠시 인계한다. 일반 이동의 Stop 과 별개다.</summary>
        public void BeginAttackMotion(Vector3 startGround, Func<Vector3> resolveInterruption)
        {
            if (attackMotion) throw new InvalidOperationException("이미 공격이 위치를 제어하고 있다");
            if (agent.isActiveAndEnabled == false || agent.isOnNavMesh == false)
                throw new InvalidOperationException("도약 시작에는 활성 NavMeshAgent 가 필요하다");
            resolveAttackInterruption = resolveInterruption;
            savedUpdatePosition = agent.updatePosition;
            attackRootOffset = Vector3.up * (transform.position.y - startGround.y);
            attackReturnGround = startGround;
            Stop();
            agent.updatePosition = false;
            attackMotion = true;
        }

        /// <summary>공격이 검증한 루트 위치로 이동한다. Agent 내부 위치는 이륙 지면에 남겨 둔다.</summary>
        public void MoveForAttack(Vector3 position)
        {
            if (attackMotion == false) throw new InvalidOperationException("공격 위치 제어를 시작하지 않았다");
            transform.position = position;
        }

        /// <summary>정상 착지 또는 취소 지면으로 Agent 와 루트를 동기화하고 원래 갱신 설정을 복원한다.</summary>
        public void EndAttackMotion(Vector3 ground)
        {
            if (attackMotion == false) return; // 실행기 OnDisable 이 먼저 복원한 경우
            attackReturnGround = ground;
            transform.position = ground + attackRootOffset;
            agent.updatePosition = savedUpdatePosition;
            attackMotion = false;
            resolveAttackInterruption = null;
            needsAttackWarp = true;
            if (agent.isActiveAndEnabled) RestoreAttackAgent();
        }

        /// <summary>오브젝트 비활성 중에는 Warp 할 수 없으므로 다시 활성화될 때 동기화를 마친다.</summary>
        private void OnEnable()
        {
            if (needsAttackWarp) RestoreAttackAgent();
        }

        private void RestoreAttackAgent()
        {
            Vector3 root = attackReturnGround + attackRootOffset;
            if (agent.Warp(root) == false)
                throw new InvalidOperationException($"도약 후 NavMesh 복귀 실패: {name} @ {root}");
            transform.position = root;
            agent.nextPosition = root;
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            needsAttackWarp = false;
        }

        public Vector3 Velocity => agent.isActiveAndEnabled ? agent.velocity : Vector3.zero;

        // 디버그: 누적 SetPath 호출 수
        public int RepathCount => repathCount;
        // Idle / OffNavMesh / Unreachable / Pending / Moving / Arrived 중 현재 상태를 NavMeshAgent 에서 계산.
        // 남은 거리가 정지 거리 + 0.15m 이하면 Arrived
        public MoveExecutionState ExecutionState
        {
            get
            {
                if (agent == null || agent.isActiveAndEnabled == false) return MoveExecutionState.Idle;
                if (agent.isOnNavMesh == false) return MoveExecutionState.OffNavMesh;
                if (requestedState == MoveExecutionState.Unreachable) return requestedState;
                if (hasDestination == false) return MoveExecutionState.Idle;
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
            if (attackMotion) EndAttackMotion(resolveAttackInterruption());
            hasDestination = false;
            warnedOffMesh = false;
        }

        /// <summary>
        /// 이동 의도를 NavMeshAgent 에 적용한다. 정지 의도면 멈추고,
        /// 목적지 변화가 작거나 재경로 간격이 짧으면 기존 경로를 유지/보류해 경로 계산 비용을 줄인다.
        /// </summary>
        /// <param name="intent">정책이 낸 이동 의도. HasDestination 이 false 면 정지</param>
        /// <returns>
        /// PathSet/PathKept/Deferred/Unreachable/OffNavMesh/Stopped. MonsterMovement 가 슬롯 추적에 사용한다.
        /// 목적지 보정·경로 계산·경로 설정 중 하나라도 실패하면 정지 후 Unreachable
        /// </returns>
        public MoveApplyResult Apply(in MoveIntent intent)
        {
            if (intent.HasDestination == false)
            {
                Stop();
                return MoveApplyResult.Stopped;
            }

            // 1) NavMesh 위에 있는지 확인 (아니면 경고 1회 후 실패)
            if (agent.isOnNavMesh == false)
            {
                if (warnedOffMesh == false)
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
                if (CanKeepCurrentPath(intent.Destination))
                    return MoveApplyResult.PathKept;
                if (Time.time - lastRepathTime < minRepathInterval)
                    return MoveApplyResult.Deferred;
            }

            // 4) 목적지를 NavMesh 위로 보정 → 경로 계산 → 설정. 어느 단계든 실패하면 정지 + Unreachable
            if (NavMesh.SamplePosition(intent.Destination, out NavMeshHit hit, sampleRadius, agent.areaMask) == false)
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

            if (agent.CalculatePath(hit.position, path) == false || path.status != NavMeshPathStatus.PathComplete)
            {
                Stop();
                requestedState = MoveExecutionState.Unreachable;
                return MoveApplyResult.Unreachable;
            }
            if (agent.SetPath(path) == false)
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

        /// <summary>
        /// 새 목적지가 마지막 경로 목적지와 거의 같고 지금 경로가 완전하면 재경로 없이 기존 경로를 유지해도 되는가.
        /// Apply 의 재경로 제한 단계가 호출한다. 앞 조건이 거짓이면 뒤의 NavMeshAgent 경로 상태는 읽지 않는다.
        /// </summary>
        /// <param name="destination">이번 의도의 목적지 (월드)</param>
        /// <returns>목적지 이동량 &lt; repathDistance 이고 경로가 있으며 PathComplete 면 true</returns>
        private bool CanKeepCurrentPath(Vector3 destination)
        {
            float moved = (destination - lastRequestedDest).magnitude;
            bool destinationAlmostSame = moved < repathDistance;
            if (destinationAlmostSame == false) return false;
            if (agent.hasPath == false) return false;
            return agent.pathStatus == NavMeshPathStatus.PathComplete;
        }

        /// <summary>이동을 멈추고 경로 추적 상태를 초기화한다 (Agent 가 꺼져 있거나 NavMesh 위가 아니면 상태만 초기화). MonsterMovement 와 Apply 의 실패 경로가 호출한다</summary>
        public void Stop()
        {
            hasDestination = false;
            requestedState = MoveExecutionState.Idle;
            if (agent == null) return;
            if (agent.isActiveAndEnabled == false) return;
            if (agent.isOnNavMesh == false) return;
            agent.ResetPath();
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            if (attackMotion == false) agent.nextPosition = transform.position;
        }

        /// <summary>
        /// 경로 없이 몸을 정해진 양만큼 옮긴다. 이동 구간에 NavMesh 경계(벽)가 있으면 경계까지만 옮긴다.
        /// MonsterMovement.MoveDuringAttack 이 돌진·준비 동작 중에 호출한다.
        /// </summary>
        /// <param name="displacement">이번 틱 이동량 (월드, m). 높이는 NavMesh 표면을 따른다</param>
        /// <returns>true = NavMesh 경계에 막혔다</returns>
        public bool MoveBy(Vector3 displacement)
        {
            Vector3 from = agent.nextPosition;
            Vector3 to = from + displacement;
            bool hitWall = NavMesh.Raycast(from, to, out NavMeshHit hit, agent.areaMask);
            Vector3 allowedDisplacement = displacement;
            if (hitWall) allowedDisplacement = hit.position - from;

            agent.Move(allowedDisplacement);
            return hitWall;
        }
    }
}
