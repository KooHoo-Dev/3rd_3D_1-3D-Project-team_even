using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HybridShooter.Monster.Movement
{
    /// <summary>씬 단위 군중 제어 파라미터 (초기값은 팀 ConfigTable 제안값 6m / 8마리 참고)</summary>
    [Serializable]
    public sealed class CrowdSettings
    {
        [Min(0)] public int EngageCapacity = 8;
        [Min(0f)] public float NearRadius = 6f;
        [Min(0f)] public float WaitRadius = 9f;
        [Min(0f)] public float ReleaseRadius = 10.5f;
        [Tooltip("토큰 배분 주기 (s)")]
        [Min(0.02f)] public float AllocationInterval = 0.2f;
        [Tooltip("개체 판단을 이 시간 동안 프레임에 나눠 한 바퀴 돈다 (s)")]
        [Min(0.02f)] public float ThinkCycle = 0.2f;
        public bool EnableCrowdControl = true;
        public bool UseSlots = true;
        public SlotSettings Slots = new SlotSettings();
    }

    /// <summary>디버그·HUD 용 집계 (읽기 전용)</summary>
    public readonly struct CrowdSnapshot
    {
        public readonly int Registered;
        public readonly AllocationSummary Summary;
        /// <summary>토큰 보유 + NearRadius 안 개체 수 (불변식: ≤ Capacity)</summary>
        public readonly int EngagedInsideNear;
        public readonly int Capacity;
        /// <summary>마지막 배분 + 판단에 걸린 시간 (ms)</summary>
        public readonly float LastTickMs;

        public CrowdSnapshot(int registered, AllocationSummary summary, int engagedInsideNear, int capacity, float lastTickMs)
        {
            Registered = registered;
            Summary = summary;
            EngagedInsideNear = engagedInsideNear;
            Capacity = capacity;
            LastTickMs = lastTickMs;
        }
    }

    /// <summary>
    /// 씬에 1개. 등록 관리 + 주기적 토큰 배분 + 개체 판단을 프레임에 분산 호출.
    /// 이동 "명령"은 내리지 않는다. CrowdDirective(허가 + 정보)만 전달한다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CrowdCoordinator : MonoBehaviour
    {
        [Tooltip("IMovementTarget 을 구현한 컴포넌트")]
        [SerializeField] private MonoBehaviour targetSource;
        [SerializeField] private CrowdSettings settings = new CrowdSettings();

        /// <summary>씬에서 활성화된 Coordinator. MonsterMovement 에 직접 지정하지 않았을 때 사용</summary>
        public static CrowdCoordinator Active { get; private set; }

        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("CrowdCoordinator.Tick");

        private readonly List<MonsterMovement> agents = new List<MonsterMovement>();
        private readonly List<SlotObstacle> slotObstacles = new List<SlotObstacle>();
        private IReadOnlyList<MonsterMovement> agentsView;
        private SlotRegistry slots;
        private bool slotsRefreshed;
        private AllocationAgent[] agentBuffer = new AllocationAgent[16];
        private AllocationResult[] resultBuffer = new AllocationResult[16];
        private IMovementTarget target;
        private AllocationSummary lastSummary;
        private int lastEngagedInsideNear;
        private float lastTickMs;
        private float nextAllocationTime;
        private float thinkBudget;
        private int thinkCursor;
        private bool loggedMissingTarget;

        public IMovementTarget Target => target ?? targetSource as IMovementTarget;
        public CrowdSettings Settings => settings;
        public int EngageCapacity => settings.EngageCapacity;
        public IReadOnlyList<MonsterMovement> Agents => agentsView ??= agents.AsReadOnly();
        public int SlotCount => slotsRefreshed ? slots.Count : 0;

        public SlotSnapshot GetSlotSnapshot(int id)
        {
            if (SlotCount == 0)
                throw new InvalidOperationException("슬롯 후보가 아직 갱신되지 않았다. RefreshSlots를 먼저 호출해야 한다");
            return slots.GetSnapshot(id);
        }

        private void Awake()
        {
            if (targetSource != null)
            {
                target = targetSource as IMovementTarget;
                if (target == null)
                    Debug.LogError($"targetSource({targetSource.GetType().Name}) 가 IMovementTarget 을 구현하지 않음", this);
            }
            ValidateSettings();
        }

        private void OnValidate() => ValidateSettings();

        private void ValidateSettings()
        {
            if (!(settings.NearRadius >= 0f && settings.NearRadius < settings.WaitRadius && settings.WaitRadius <= settings.ReleaseRadius))
                Debug.LogError($"CrowdSettings 반경 오류: 0 <= Near({settings.NearRadius}) < Wait({settings.WaitRadius}) <= Release({settings.ReleaseRadius}) 이어야 함", this);
            if (settings.Slots == null || settings.Slots.ApproachCount < 1 ||
                settings.Slots.ApproachRadius <= 0f || settings.Slots.MinimumRingGap <= 0f ||
                settings.Slots.MaxRingCount < 2)
                Debug.LogError("SlotSettings 첫 링 후보 수·반경·간격은 양수이고 최대 링 수는 2 이상이어야 함", this);
        }

        private void OnEnable()
        {
            if (Active != null && Active != this)
                Debug.LogError("CrowdCoordinator 가 2개 이상 활성화됨", this);
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            slots?.ClearReservations();
        }

        /// <summary>목표 교체 (대체 객체 ↔ 실제 플레이어). null 이면 ArgumentNullException</summary>
        public void SetTarget(IMovementTarget newTarget)
        {
            target = newTarget ?? throw new ArgumentNullException(nameof(newTarget));
            targetSource = newTarget as MonoBehaviour;
            loggedMissingTarget = false;
            slots?.ClearReservations();
            slotsRefreshed = false;
            nextAllocationTime = 0f;
        }

        /// <summary>근접 동시 허용 수 변경. 다음 배분부터 반영. 음수면 ArgumentOutOfRangeException</summary>
        public void SetEngageCapacity(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "0 이상이어야 함");
            settings.EngageCapacity = capacity;
            nextAllocationTime = 0f;
        }

        public CrowdSnapshot GetSnapshot() =>
            new CrowdSnapshot(agents.Count, lastSummary, lastEngagedInsideNear, settings.EngageCapacity, lastTickMs);

        /// <summary>MonsterMovement.OnEnable 에서 호출. 중복 등록은 LogError</summary>
        public void Register(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (agents.Contains(agent))
            {
                Debug.LogError($"이미 등록된 MonsterMovement: {agent.name}", agent);
                return;
            }
            agents.Add(agent);
            if (settings.EnableCrowdControl && settings.UseSlots &&
                agent.GetComponent<UnityEngine.AI.NavMeshAgent>().agentTypeID != settings.Slots.AgentTypeId)
                Debug.LogError($"{agent.name}: NavMeshAgent 타입과 SlotSettings.AgentTypeId가 다름", agent);
            nextAllocationTime = 0f;
        }

        /// <summary>등록 개체와 현재 타겟 기준으로 후보와 실제 점유를 갱신한다.</summary>
        public void RefreshSlots()
        {
            EnsureSlots();
            IMovementTarget currentTarget = Target;
            if (!MovementTargetUtility.IsValid(currentTarget))
            {
                slots.ClearReservations();
                slotsRefreshed = false;
                lastSummary = default;
                lastEngagedInsideNear = 0;
                return;
            }
            slotObstacles.Clear();
            for (int i = 0; i < agents.Count; i++)
            {
                MonsterMovement agent = agents[i];
                slotObstacles.Add(new SlotObstacle(agent.GetInstanceID(), agent.transform.position, agent.Radius));
            }
            slots.Refresh(currentTarget.Position, Time.time, slotObstacles);
            slotsRefreshed = true;
            int maxApproach = Mathf.Min(settings.EngageCapacity, slots.ApproachCount);
            for (int id = slots.ApproachCount - 1; slots.ApproachReservations > maxApproach && id >= 0; id--)
            {
                SlotSnapshot slot = slots.GetSnapshot(id);
                if (slot.Use != SlotUse.Free) slots.Release(slot.OwnerId);
            }
            int tokenAgents = 0, unrestricted = 0, uncontrolledInside = 0, engagedNear = 0;
            float nearSqr = settings.NearRadius * settings.NearRadius;
            for (int i = 0; i < agents.Count; i++)
            {
                MonsterMovement agent = agents[i];
                Vector3 delta = agent.transform.position - currentTarget.Position;
                bool inside = delta.x * delta.x + delta.z * delta.z <= nearSqr;
                if (!agent.Profile.UsesEngageToken)
                {
                    unrestricted++;
                    if (inside) uncontrolledInside++;
                }
                else
                {
                    tokenAgents++;
                    if (inside && slots.HasApproachReservation(agent.GetInstanceID())) engagedNear++;
                }
            }
            lastSummary = new AllocationSummary
            {
                Engaged = slots.ApproachReservations,
                Waiting = tokenAgents - slots.ApproachReservations,
                Unrestricted = unrestricted,
                UncontrolledInside = uncontrolledInside,
                FreeCapacity = Mathf.Max(0, maxApproach - slots.ApproachReservations)
            };
            lastEngagedInsideNear = engagedNear;
        }

        /// <summary>현재 개체가 사용할 후보를 동기적으로 예약한다.</summary>
        public CrowdDirective RequestDirective(MonsterMovement agent, float now)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (!settings.EnableCrowdControl || !agent.Profile.UsesEngageToken)
            {
                ReleaseSlot(agent);
                return new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f);
            }
            if (!settings.UseSlots) return agent.Directive;
            EnsureSlots();
            if (!slotsRefreshed) RefreshSlots();
            IMovementTarget currentTarget = Target;
            if (!MovementTargetUtility.IsValid(currentTarget))
            {
                slots.ClearReservations();
                return CrowdDirective.WaitingForSlot(agent.transform.position);
            }
            int capacity = Mathf.Min(settings.EngageCapacity, slots.ApproachCount);
            bool alreadyApproaching = slots.HasApproachReservation(agent.GetInstanceID());
            bool allowApproach = capacity > 0 && (alreadyApproaching || slots.ApproachReservations < capacity);
            if (!slots.TryRequest(agent.GetInstanceID(), agent.transform.position, now, allowApproach, out SlotSnapshot slot))
                return CrowdDirective.WaitingForSlot(agent.transform.position);
            DirectiveKind kind = slot.Kind == SlotKind.Approach ? DirectiveKind.Engage : DirectiveKind.Wait;
            return new CrowdDirective(kind, in slot, settings.Slots.ArrivalDistance);
        }

        public void ReleaseSlot(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            slots?.Release(agent.GetInstanceID());
        }

        public void RejectSlot(MonsterMovement agent, int slotId, float now)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            slots?.Reject(agent.GetInstanceID(), slotId, now);
        }

        public void PauseSlotProgress(MonsterMovement agent, float duration)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            slots?.PauseProgress(agent.GetInstanceID(), duration);
        }

        private void EnsureSlots()
        {
            if (slots != null && slots.ApproachCount == settings.Slots.ApproachCount &&
                slots.ApproachRadius == settings.Slots.ApproachRadius &&
                slots.MinimumRingGap == settings.Slots.MinimumRingGap &&
                slots.MaxRingCount == settings.Slots.MaxRingCount) return;
            slots = new SlotRegistry(settings.Slots, new NavMeshSlotNavigation(settings.Slots));
            slotsRefreshed = false;
        }

        /// <summary>MonsterMovement.OnDisable 에서 호출. 토큰 회수</summary>
        public void Unregister(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            int index = agents.IndexOf(agent);
            if (index < 0)
            {
                Debug.LogError($"등록되지 않은 MonsterMovement 해제: {agent.name}", agent);
                return;
            }
            agents.RemoveAt(index);
            ReleaseSlot(agent);
            if (thinkCursor > index) thinkCursor--;
            if (thinkCursor >= agents.Count) thinkCursor = 0;
            agent.ResetCrowdState();
            nextAllocationTime = 0f; // 빈 자리를 다음 프레임에 재배분
        }

        private void Update()
        {
            if (!MovementTargetUtility.IsValid(target))
            {
                slots?.ClearReservations();
                slotsRefreshed = false;
                lastSummary = default;
                lastEngagedInsideNear = 0;
                if (target == null && agents.Count > 0 && !loggedMissingTarget)
                {
                    Debug.LogError("목표(IMovementTarget) 미지정: targetSource 를 지정하거나 SetTarget 을 호출해야 함", this);
                    loggedMissingTarget = true;
                }
                for (int i = 0; i < agents.Count; i++)
                    agents[i].Think(target, settings.WaitRadius, Time.time);
                return;
            }

            long startTs = Stopwatch.GetTimestamp();
            using (TickMarker.Auto())
            {
                float now = Time.time;
                bool allocated = false;
                if (now >= nextAllocationTime)
                {
                    if (settings.EnableCrowdControl && settings.UseSlots)
                    {
                        RefreshSlots();
                        nextAllocationTime = now + settings.AllocationInterval;
                    }
                    else if (settings.EnableCrowdControl)
                    {
                        slots?.ClearReservations();
                        Allocate(now);
                    }
                    else
                    {
                        slots?.ClearReservations();
                        for (int i = 0; i < agents.Count; i++)
                            agents[i].ApplyAllocation(new AllocationResult
                            {
                                Directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f),
                                WaitingSince = -1f,
                                SlotIndex = -1
                            });
                        lastSummary = new AllocationSummary
                        {
                            Unrestricted = agents.Count,
                            FreeCapacity = settings.EngageCapacity
                        };
                        lastEngagedInsideNear = 0;
                        nextAllocationTime = now + settings.AllocationInterval;
                    }
                    allocated = true;
                }

                int n = agents.Count;
                if (n > 0)
                {
                    // 배분 직후에는 전원이 한 번씩 판단 (5Hz 전체 갱신: 지시 종류 변화를 빨리 반영하는 대신 해당 프레임 비용 증가)
                    if (allocated) thinkBudget = n;
                    else thinkBudget = Mathf.Min(thinkBudget + n * Time.deltaTime / settings.ThinkCycle, n);

                    int k = Mathf.Min(n, (int)thinkBudget);
                    thinkBudget -= k;
                    for (int i = 0; i < k; i++)
                    {
                        if (agents.Count != n) break; // 판단 중 등록/해제 발생
                        agents[thinkCursor % n].Think(target, settings.WaitRadius, now);
                        thinkCursor = (thinkCursor + 1) % n;
                    }
                }
            }
            lastTickMs = (Stopwatch.GetTimestamp() - startTs) * 1000f / Stopwatch.Frequency;
        }

        private void Allocate(float now)
        {
            int n = agents.Count;
            if (agentBuffer.Length < n)
            {
                int size = agentBuffer.Length;
                while (size < n) size *= 2;
                agentBuffer = new AllocationAgent[size];
                resultBuffer = new AllocationResult[size];
            }

            for (int i = 0; i < n; i++)
                agentBuffer[i] = agents[i].BuildAllocationAgent();

            Vector3 targetPos = target.Position;
            var p = new AllocationParams
            {
                TargetPosition = targetPos,
                Capacity = settings.EngageCapacity,
                NearRadius = settings.NearRadius,
                WaitRadius = settings.WaitRadius,
                ReleaseRadius = settings.ReleaseRadius,
                Now = now,
                UseSlots = settings.UseSlots,
            };
            lastSummary = EngageAllocator.Allocate(p, agentBuffer, n, resultBuffer);

            float nearSqr = settings.NearRadius * settings.NearRadius;
            int engagedNear = 0;
            for (int i = 0; i < n; i++)
            {
                agents[i].ApplyAllocation(resultBuffer[i]);
                if (!resultBuffer[i].HasToken) continue;
                float dx = agentBuffer[i].Position.x - targetPos.x;
                float dz = agentBuffer[i].Position.z - targetPos.z;
                if (dx * dx + dz * dz <= nearSqr) engagedNear++;
            }
            lastEngagedInsideNear = engagedNear;
            nextAllocationTime = now + settings.AllocationInterval;
        }
    }
}
