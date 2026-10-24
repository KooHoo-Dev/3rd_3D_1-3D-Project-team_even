using System;
using System.Collections.Generic;
using System.Diagnostics;
using HybridShooter.Core.Contracts;
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
        [Min(0f)] public float ReleaseRadius = 10.5f;
        [Tooltip("토큰 배분 주기 (s)")]
        [Min(0.02f)] public float AllocationInterval = 0.2f;
        [Tooltip("개체 판단을 이 시간 동안 프레임에 나눠 한 바퀴 돈다 = 개체당 Think 간격 (s)")]
        [Min(0.02f)] public float ThinkCycle = 0.5f;
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
    public sealed class CrowdCoordinator : MonoBehaviour
    {
        // ════════ 설정 (Inspector) ════════
        [Header("설정")]
        [Tooltip("IPlayerTarget 을 구현한 컴포넌트")]
        [SerializeField] private MonoBehaviour targetSource;
        [SerializeField] private CrowdSettings settings = new CrowdSettings();

        // ════════ 정적 (씬 전역) ════════
        /// <summary>씬에서 활성화된 Coordinator. MonsterMovement 에 직접 지정하지 않았을 때 사용</summary>
        public static CrowdCoordinator Active { get; private set; }

        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("CrowdCoordinator.Tick");

        // ════════ 등록된 몬스터·추적 대상 ════════
        private readonly List<MonsterMovement> agents = new List<MonsterMovement>(); // Register/Unregister 로 관리
        private IReadOnlyList<MonsterMovement> agentsView;                            // agents 의 읽기 전용 뷰 (캐시)
        private IPlayerTarget target;                                               // 추적 대상 (플레이어 등). null = 목표 없음(사망 등) → 전원 Idle

        // ════════ 슬롯 모드 (UseSlots = true) ════════
        private SlotRegistry slots;                                                   // 목표 둘레 슬롯 배정 관리
        private bool slotsRefreshed;                                                  // 현재 목표 기준으로 슬롯을 갱신했나
        private readonly List<SlotObstacle> slotObstacles = new List<SlotObstacle>(); // 혼잡 판정용 몬스터 위치 목록 (재사용)

        // ════════ 토큰 모드 (UseSlots = false) 배분 버퍼 ════════
        private AllocationAgent[] agentBuffer = new AllocationAgent[16];              // EngageAllocator 입력 (매번 할당하지 않도록 재사용)
        private AllocationResult[] resultBuffer = new AllocationResult[16];           // EngageAllocator 출력

        // ════════ 주기·판단 분산 ════════
        private float nextAllocationTime;   // 다음 배분(슬롯 갱신) 시각
        private float thinkBudget;          // 이번 프레임에 판단시킬 수 (소수 누적)
        private int thinkCursor;            // 다음에 판단할 개체 순번 (라운드 로빈)

        // ════════ 디버그·HUD 집계 ════════
        private AllocationSummary lastSummary;
        private int lastEngagedInsideNear;
        private float lastTickMs;

        // ════════ 외부 공개 ════════
        public IPlayerTarget Target => target ?? targetSource as IPlayerTarget;
        public CrowdSettings Settings => settings;
        public int EngageCapacity => settings.EngageCapacity;
        public IReadOnlyList<MonsterMovement> Agents => agentsView ??= agents.AsReadOnly();
        public int SlotCount => slotsRefreshed ? slots.Count : 0;

        /// <summary>슬롯 id 의 현재 상태 복사본. 슬롯이 아직 갱신 전이면 InvalidOperationException</summary>
        public SlotSnapshot GetSlotSnapshot(int id)
        {
            if (SlotCount == 0)
                throw new InvalidOperationException("슬롯 후보가 아직 갱신되지 않았다. RefreshSlots를 먼저 호출해야 한다");
            return slots.GetSnapshot(id);
        }

        /// <summary>
        /// agent 가 slotId 를 아직 유효하게 예약 중인가 (MonsterMovement.HasCurrentReservation 용 O(1) 조회).
        /// 슬롯이 아직 갱신 전이면 false 이며, 스냅샷을 만들지 않아 혼잡 판정 비용이 없다
        /// </summary>
        public bool IsSlotReservedBy(MonsterMovement agent, int slotId) =>
            slotsRefreshed && slots.IsValidlyReservedBy(slotId, agent.GetInstanceID());

        /// <summary>targetSource 를 IPlayerTarget 으로 변환해 저장하고 설정값 검증</summary>
        private void Awake()
        {
            if (targetSource != null)
            {
                target = targetSource as IPlayerTarget;
                if (target == null)
                    Debug.LogError($"targetSource({targetSource.GetType().Name}) 가 IPlayerTarget 을 구현하지 않음", this);
            }
            ValidateSettings();
        }

        private void OnValidate() => ValidateSettings();

        /// <summary>반경·슬롯 설정이 잘못되면 에러 로그 (Inspector 수정 시에도 호출)</summary>
        private void ValidateSettings()
        {
            if (!settings.UseSlots && settings.Slots != null && !(settings.NearRadius >= 0f && settings.NearRadius < settings.Slots.WaitingRadius && settings.Slots.WaitingRadius <= settings.ReleaseRadius))
                Debug.LogError($"CrowdSettings 반경 오류: 0 <= Near({settings.NearRadius}) < Waiting({settings.Slots.WaitingRadius}) <= Release({settings.ReleaseRadius}) 이어야 함", this);
            if (settings.Slots == null || settings.Slots.ApproachCount < 1 ||
                settings.Slots.ApproachRadius <= 0f || settings.Slots.MinimumRingGap <= 0f ||
                settings.Slots.MaxRingCount < 2)
                Debug.LogError("SlotSettings 첫 링 후보 수·반경·간격은 양수이고 최대 링 수는 2 이상이어야 함", this);
        }

        /// <summary>씬 전역 Active 로 등록 (2개 이상이면 에러 로그)</summary>
        private void OnEnable()
        {
            if (Active != null && Active != this)
                Debug.LogError("CrowdCoordinator 가 2개 이상 활성화됨", this);
            Active = this;
        }

        /// <summary>Active 해제, 모든 슬롯 예약 비우기</summary>
        private void OnDisable()
        {
            if (Active == this) Active = null;
            slots?.ClearReservations();
        }

        /// <summary>목표 교체 (대체 객체 ↔ 실제 플레이어). null 이면 목표 상실: 예약을 비우고 전원 Idle 로 정지한다</summary>
        public void SetTarget(IPlayerTarget newTarget)
        {
            target = newTarget;
            targetSource = newTarget as MonoBehaviour;
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

        /// <summary>디버그·HUD 용 현재 집계</summary>
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

        /// <summary>
        /// 등록 개체와 현재 타겟 기준으로 후보와 실제 점유를 갱신한다.
        /// 목표가 무효면 모든 예약을 비운다. 접근 링 예약이 허용 인원을 넘으면 먼 번호부터 회수하고, 집계(lastSummary)를 새로 계산한다.
        /// </summary>
        public void RefreshSlots()
        {
            EnsureSlots();
            IPlayerTarget currentTarget = Target;

            // 1) 목표가 없으면 예약 전부 해제하고 종료
            if (currentTarget == null)
            {
                slots.ClearReservations();
                slotsRefreshed = false;
                lastSummary = default;
                lastEngagedInsideNear = 0;
                return;
            }
            // 2) 몬스터 위치를 장애물로 넘겨 슬롯 후보·점유 상태 갱신
            slotObstacles.Clear();
            for (int i = 0; i < agents.Count; i++)
            {
                MonsterMovement agent = agents[i];
                slotObstacles.Add(new SlotObstacle(agent.GetInstanceID(), agent.transform.position, agent.Radius));
            }
            slots.Refresh(currentTarget.PlayerTrackingInfo.Position, Time.time, slotObstacles);
            slotsRefreshed = true;
            // 3) 동시 접근 허용 수(EngageCapacity)를 넘은 접근 링 예약은 번호가 큰 쪽부터 회수
            int maxApproach = Mathf.Min(settings.EngageCapacity, slots.ApproachCount);
            for (int id = slots.ApproachCount - 1; slots.ApproachReservations > maxApproach && id >= 0; id--)
            {
                SlotSnapshot slot = slots.GetSnapshot(id);
                if (slot.Use != SlotUse.Free) slots.Release(slot.OwnerId);
            }
            // 4) 디버그·HUD 집계: 토큰 대상/비대상 수, 근접 반경 안 인원 등
            int tokenAgents = 0, unrestricted = 0, uncontrolledInside = 0, engagedNear = 0;
            float nearSqr = settings.NearRadius * settings.NearRadius;
            for (int i = 0; i < agents.Count; i++)
            {
                MonsterMovement agent = agents[i];
                Vector3 delta = agent.transform.position - currentTarget.PlayerTrackingInfo.Position;
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

        /// <summary>
        /// 개체 1마리의 지시를 만든다 (MonsterMovement.Think 에서 호출).
        /// 군중 제어 비대상이면 Unrestricted, 슬롯 모드면 슬롯을 즉시 예약해 Engage(0번 링)/Wait(바깥 링) 지시를 돌려준다.
        /// 슬롯을 못 얻으면 제자리 대기 지시.
        /// </summary>
        public CrowdDirective RequestDirective(MonsterMovement agent, float now)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));

            // 군중 제어 대상이 아니면 슬롯 없이 자유 이동
            if (!settings.EnableCrowdControl || !agent.Profile.UsesEngageToken)
            {
                ReleaseSlot(agent);
                return new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f);
            }
            if (!settings.UseSlots) return agent.Directive;
            EnsureSlots();
            if (!slotsRefreshed) RefreshSlots();
            IPlayerTarget currentTarget = Target;
            if (currentTarget == null)
            {
                slots.ClearReservations();
                return CrowdDirective.WaitingForSlot(agent.transform.position);
            }
            // 접근 링(0번) 진입 허용 여부: 정원 안이거나 이미 접근 중이면 허용. 아니면 바깥 링에서만 슬롯을 찾는다
            int capacity = Mathf.Min(settings.EngageCapacity, slots.ApproachCount);
            bool alreadyApproaching = slots.HasApproachReservation(agent.GetInstanceID());
            bool allowApproach = capacity > 0 && (alreadyApproaching || slots.ApproachReservations < capacity);
            if (!slots.TryRequest(agent.GetInstanceID(), agent.transform.position, now, allowApproach, out SlotSnapshot slot))
                return CrowdDirective.WaitingForSlot(agent.transform.position);
            DirectiveKind kind = slot.RingIndex == 0 ? DirectiveKind.Engage : DirectiveKind.Wait;
            return new CrowdDirective(kind, in slot, settings.Slots.ArrivalDistance);
        }

        /// <summary>agent 가 쥔 슬롯을 반납한다 (없으면 무시)</summary>
        public void ReleaseSlot(MonsterMovement agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            slots?.Release(agent.GetInstanceID());
        }

        /// <summary>도달 불가 등으로 slotId 를 거절한다. 일정 시간 동안 이 개체에게 같은 슬롯을 다시 주지 않는다</summary>
        public void RejectSlot(MonsterMovement agent, int slotId, float now)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            slots?.Reject(agent.GetInstanceID(), slotId, now);
        }

        /// <summary>반응 지연으로 일부러 멈춘 시간(duration)만큼 "슬롯으로 진행이 없다" 판정 시계를 늦춘다</summary>
        public void PauseSlotProgress(MonsterMovement agent, float duration)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            slots?.PauseProgress(agent.GetInstanceID(), duration);
        }

        /// <summary>SlotRegistry 가 없거나 링 설정이 바뀌었으면 새로 만든다 (이 경우 다음 갱신 때 후보를 다시 계산)</summary>
        private void EnsureSlots()
        {
            if (slots != null && slots.ApproachCount == settings.Slots.ApproachCount &&
                slots.ApproachRadius == settings.Slots.ApproachRadius &&
                slots.MinimumRingGap == settings.Slots.MinimumRingGap &&
                slots.MaxRingCount == settings.Slots.MaxRingCount) return;
            slots = new SlotRegistry(settings.Slots, new NavMeshSlotNavigation(settings.Slots));
            slotsRefreshed = false;
        }

        /// <summary>
        /// MonsterMovement.OnDisable 에서 호출 (몬스터 사망·제거 시). 목록에서 빼고 슬롯·토큰을 회수한다.
        /// 판단 순번(thinkCursor)을 보정하고 즉시 재배분을 예약해 빈 자리를 다른 몬스터가 채우게 한다
        /// </summary>
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

        /// <summary>
        /// 매 프레임 군중 제어 루프. 일정 주기마다 배분(슬롯 갱신/토큰 배분)을 하고,
        /// 몬스터 판단(Think)은 ThinkCycle 동안 프레임에 나눠 라운드 로빈으로 호출한다.
        /// 목표가 무효면 예약을 비우고 전원에게 Think 를 호출해 Idle 로 만든다.
        /// 마지막에 등록 개체의 TickMovement 를 호출한다: 군중 모드 몬스터의 Update 는 비어 있으므로,
        /// "Coordinator(배분·Think) → 몬스터(예약 검증·지연 의도 적용)" 순서가 실행 순서 설정 없이 호출 순서로 고정된다.
        /// </summary>
        private void Update()
        {
            // A) 목표 무효: 예약 해제 + 전원 Think(→ Idle 정지)
            if (target == null)
            {
                slots?.ClearReservations();
                slotsRefreshed = false;
                lastSummary = default;
                lastEngagedInsideNear = 0;
                for (int i = 0; i < agents.Count; i++)
                    agents[i].Think(target, Time.time);
                TickAgents();
                return;
            }

            long startTs = Stopwatch.GetTimestamp();
            using (TickMarker.Auto())
            {
                // B) 배분 주기가 되면 모드에 따라 배분: 슬롯 모드 / 토큰 모드 / 군중 제어 꺼짐(전원 자유 이동)
                float now = Time.time;
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
                }

                // C) 개체 판단 분산: 이번 프레임에 판단할 수(thinkBudget)만큼 순번대로 Think 호출
                int n = agents.Count;
                if (n > 0)
                {
                    // 배분 프레임에도 전원 Think 를 몰아주지 않고 예산만 누적한다: 개체당 정확히 ThinkCycle 마다 1회, 프레임에 고르게 분산
                    // (대신 지시 변화·신규 슬롯 반영이 최대 ThinkCycle 만큼 늦어진다)
                    thinkBudget = Mathf.Min(thinkBudget + n * Time.deltaTime / settings.ThinkCycle, n);

                    int k = Mathf.Min(n, (int)thinkBudget);
                    thinkBudget -= k;
                    for (int i = 0; i < k; i++)
                    {
                        if (agents.Count != n) break; // 판단 중 등록/해제 발생
                        agents[thinkCursor % n].Think(target, now);
                        thinkCursor = (thinkCursor + 1) % n;
                    }
                }
            }
            lastTickMs = (Stopwatch.GetTimestamp() - startTs) * 1000f / Stopwatch.Frequency;
            TickAgents(); // lastTickMs 는 배분 + 판단 비용만 잰다 (기존과 동일)
        }

        /// <summary>등록된 모든 몬스터의 프레임 갱신(TickMovement)을 호출한다. Update 에서 Think 뒤에 불려 순서가 보장된다</summary>
        private void TickAgents()
        {
            for (int i = 0; i < agents.Count; i++)
                agents[i].TickMovement();
        }

        /// <summary>토큰 모드 배분: 개체 상태를 모아 EngageAllocator 에 넘기고, 결과를 각 개체에 반영한다</summary>
        private void Allocate(float now)
        {
            // 1) 버퍼 용량 확보 (부족하면 2배씩 확장)
            int n = agents.Count;
            if (agentBuffer.Length < n)
            {
                int size = agentBuffer.Length;
                while (size < n) size *= 2;
                agentBuffer = new AllocationAgent[size];
                resultBuffer = new AllocationResult[size];
            }

            // 2) 순수 계산기(EngageAllocator)로 배분
            for (int i = 0; i < n; i++)
                agentBuffer[i] = agents[i].BuildAllocationAgent();

            Vector3 targetPos = target.PlayerTrackingInfo.Position;
            var p = new AllocationParams
            {
                TargetPosition = targetPos,
                Capacity = settings.EngageCapacity,
                NearRadius = settings.NearRadius,
                Slots = settings.Slots,
                ReleaseRadius = settings.ReleaseRadius,
                Now = now,
                UseSlots = settings.UseSlots,
            };
            lastSummary = EngageAllocator.Allocate(p, agentBuffer, n, resultBuffer);

            // 3) 결과를 개체에 반영하고, 근접 반경 안 토큰 보유자 수 집계
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
