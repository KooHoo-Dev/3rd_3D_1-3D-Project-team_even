using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 목표 둘레 동심원(링) 위 슬롯 후보를 만들고, 몬스터에게 슬롯을 예약·회수하는 관리자.
    /// 슬롯 id 는 0번 링부터 순서대로 이어 붙인 번호이며, id &lt; approachCount 이면 접근 링(0번, 인원 제한 대상).
    /// 한 몬스터는 슬롯 1개만 가진다. 슬롯 위치 투영과 도달 가능성은 NavMeshSlotNavigation 으로 판단한다.
    /// </summary>
    public sealed class SlotRegistry
    {
        /// <summary>슬롯 1개의 내부 상태</summary>
        private struct Slot
        {
            public int RingIndex;
            public Vector3 Candidate;   // 기하학적으로 계산한 후보 위치
            public Vector3 Position;    // NavMesh 에 투영된 실제 위치
            public SlotValidity Validity;
            public int Owner;           // 예약한 몬스터 id (0 = 없음)
            public SlotUse Use;
        }

        /// <summary>소유자가 슬롯에 다가가고 있는지 추적 (정체되면 슬롯 거절)</summary>
        private struct Progress
        {
            public float LastDistance;      // 마지막으로 기록한 슬롯까지 거리
            public float LastProgressTime;  // 마지막으로 진전이 있었던 시각
            public bool WasOccupied;        // 직전에 도착(Occupied) 상태였나
        }

        /// <summary>TryFindInRing 이 값싼 필터를 통과시킨 후보 (id, 개체와의 수평 거리²)</summary>
        private readonly struct RingCandidate
        {
            public readonly int Id;
            public readonly float DistanceSqr;

            public RingCandidate(int id, float distanceSqr)
            { Id = id; DistanceSqr = distanceSqr; }
        }

        /// <summary>링 1개의 슬롯 id 범위와 반경</summary>
        private readonly struct RingInfo
        {
            public readonly int StartId;
            public readonly int Count;
            public readonly float Radius;

            public RingInfo(int startId, int count, float radius)
            { StartId = startId; Count = count; Radius = radius; }
        }

        // ════════ 설정·의존성 (생성 시 고정) ════════
        private readonly SlotSettings settings;
        private readonly NavMeshSlotNavigation navigation;   // 투영·도달 가능성 판단
        private readonly int approachCount;            // 0번(접근) 링의 슬롯 수
        private readonly float approachRadius;         // 0번 링 반경
        private readonly float minimumRingGap;         // 링 사이 최소 간격
        private readonly int maxRingCount;             // 만들 수 있는 최대 링 수
        private readonly int countStep;                // 바깥 링으로 갈 때마다 늘어나는 슬롯 수

        // ════════ 슬롯·링 데이터 ════════
        private readonly List<Slot> slots = new List<Slot>();   // 전체 슬롯 (id = 인덱스)
        private readonly List<RingInfo> rings = new List<RingInfo>();
        private Vector3 lastCenter;                             // 마지막 Refresh 의 목표 위치
        private bool refreshed;                                 // Refresh 를 한 번이라도 했나

        // ════════ 예약·진행 추적 ════════
        private readonly Dictionary<int, int> ownerSlots = new Dictionary<int, int>();       // 몬스터 id → 슬롯 id
        private readonly Dictionary<int, Progress> progress = new Dictionary<int, Progress>(); // 몬스터 id → 진행 상황
        private readonly Dictionary<long, float> rejectedUntil = new Dictionary<long, float>(); // (몬스터, 슬롯) → 거절 만료 시각
        private readonly List<long> expiredRejections = new List<long>();                      // 만료 정리용 임시 목록

        // ════════ 혼잡 판정용 ════════
        private readonly List<SlotObstacle> obstacles = new List<SlotObstacle>();             // 이번 Refresh 의 몬스터 위치
        // 슬롯·장애물 위치는 Refresh(RefreshRange) 에서만 바뀌므로 그 시점에 미리 센다. 소유자 제외는 Reserve/Release 로 바뀌어 조회 시점에 보정한다
        private readonly List<int> congestCount = new List<int>();                            // 슬롯 id → 혼잡 반경 안 장애물 수 (소유자 제외 전)
        private readonly Dictionary<int, int> obstacleIndexByOwner = new Dictionary<int, int>(); // 몬스터 id → obstacles 인덱스 (OwnerId 는 유일하다고 가정)

        // ════════ TooClose 판정용 균일 격자 (RefreshRange 안에서만 사용, 호출마다 다시 만든다) ════════
        private readonly Dictionary<long, int> spacingGridHead = new Dictionary<long, int>(); // 셀 → 그 셀의 첫 슬롯 id
        private readonly List<int> spacingGridNext = new List<int>();                         // 슬롯 id → 같은 셀의 다음 슬롯 id (-1 = 끝)

        // ════════ 링 탐색용 임시 버퍼 ════════
        private readonly List<RingCandidate> ringCandidates = new List<RingCandidate>();      // TryFindInRing 후보 (매번 할당하지 않도록 재사용)

        // ════════ 외부 공개 ════════
        public int Count => slots.Count;
        public int ApproachCount => approachCount;
        public float ApproachRadius => approachRadius;
        public float MinimumRingGap => minimumRingGap;
        public int MaxRingCount => maxRingCount;
        public int ApproachReservations { get; private set; }
        /// <summary>ownerId 가 접근 링(0번) 슬롯을 예약 중인가</summary>
        public bool HasApproachReservation(int ownerId) =>
            ownerSlots.TryGetValue(ownerId, out int id) && id < approachCount;

        /// <summary>설정을 검증하고 링 간격에 맞는 슬롯 증가 수(countStep)를 계산한 뒤 시작 링 2개를 만든다</summary>
        public SlotRegistry(SlotSettings settings, NavMeshSlotNavigation navigation)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            if (settings.ApproachCount < 1 || settings.ApproachRadius <= 0f ||
                settings.MinimumRingGap <= 0f || settings.MaxRingCount < 2)
                throw new ArgumentOutOfRangeException(nameof(settings), "첫 링 후보 수·반경·간격은 양수이고 최대 링 수는 2 이상이어야 한다");
            approachCount = settings.ApproachCount;
            approachRadius = settings.ApproachRadius;
            minimumRingGap = settings.MinimumRingGap;
            maxRingCount = settings.MaxRingCount;
            float arc = 2f * Mathf.PI * approachRadius / approachCount;
            countStep = Mathf.CeilToInt(2f * Mathf.PI * minimumRingGap / arc);
            
            // 기본 시작 링 개수 2개
            AppendRingGeometry();
            AppendRingGeometry();
        }

        /// <summary>슬롯 id 의 현재 상태 복사본 (범위 밖이면 예외)</summary>
        public SlotSnapshot GetSnapshot(int id)
        {
            if ((uint)id >= (uint)slots.Count) throw new ArgumentOutOfRangeException(nameof(id));
            Slot slot = slots[id];
            return new SlotSnapshot(id, slot.RingIndex, slot.Candidate, slot.Position, slot.Validity,
                slot.Owner, slot.Use, IsCongested(id, slot.Owner));
        }

        /// <summary>
        /// slotId 가 아직 ownerId 의 예약이고 유효(Valid)한가. GetSnapshot 과 달리 혼잡 판정을 하지 않아 O(1).
        /// MonsterMovement.HasCurrentReservation 이 매 프레임 부르므로 범위 밖 id 는 예외 없이 false.
        /// </summary>
        public bool IsValidlyReservedBy(int slotId, int ownerId) =>
            (uint)slotId < (uint)slots.Count && slots[slotId].Owner == ownerId && slots[slotId].Validity == SlotValidity.Valid;

        /// <summary>
        /// 목표 위치(center)가 바뀐 뒤 슬롯 후보를 다시 계산하고 점유 상태를 갱신한다.
        /// 만료된 거절 기록 정리 → 모든 슬롯 위치 재계산(유효하지 않게 된 슬롯의 예약은 해제) → 소유자가 슬롯에 도착했는지(Occupied) 판정
        /// </summary>
        public void Refresh(Vector3 center, float now, IReadOnlyList<SlotObstacle> currentObstacles)
        {
            if (currentObstacles == null) throw new ArgumentNullException(nameof(currentObstacles));

            // 1) 이번 장애물 목록 저장 + 만료된 거절 기록 제거
            obstacles.Clear();
            obstacleIndexByOwner.Clear();
            for (int i = 0; i < currentObstacles.Count; i++)
            {
                obstacles.Add(currentObstacles[i]);
                obstacleIndexByOwner.Add(currentObstacles[i].OwnerId, i); // OwnerId 중복이면 예외: 혼잡 캐시가 틀려지므로 드러낸다
            }
            expiredRejections.Clear();
            foreach (KeyValuePair<long, float> rejection in rejectedUntil)
                if (now >= rejection.Value) expiredRejections.Add(rejection.Key);
            for (int i = 0; i < expiredRejections.Count; i++) rejectedUntil.Remove(expiredRejections[i]);

            // 2) 모든 슬롯 위치 재계산
            lastCenter = center;
            refreshed = true;
            RefreshRange(0);

            // 3) 예약 슬롯의 도착 판정
            // 스냅샷의 Occupied는 실제 소유자와 이동한 후보 사이의 현재 거리로 판단한다.
            for (int id = 0; id < slots.Count; id++)
            {
                Slot slot = slots[id];
                if (slot.Use == SlotUse.Free) continue;
                slot.Use = SlotUse.Reserved;
                for (int i = 0; i < obstacles.Count; i++)
                {
                    if (obstacles[i].OwnerId != slot.Owner) continue;
                    if (HorizontalSqr(obstacles[i].Position, slot.Position) <= settings.ArrivalDistance * settings.ArrivalDistance)
                        slot.Use = SlotUse.Occupied;
                    break;
                }
                slots[id] = slot;
            }
        }

        /// <summary>다음 링(바깥쪽)을 추가한다. 슬롯 수는 링마다 countStep 씩 늘고, 반경은 슬롯 수에 비례해 커진다</summary>
        private void AppendRingGeometry()
        {
            int ringIndex = rings.Count;
            int count = approachCount + ringIndex * countStep;
            float radius = approachRadius * count / approachCount;
            rings.Add(new RingInfo(slots.Count, count, radius));
            for (int i = 0; i < count; i++)
            {
                slots.Add(new Slot { RingIndex = ringIndex });
                congestCount.Add(0); // 위치 계산(RefreshRange) 전에는 0
            }
        }

        /// <summary>
        /// startId 이후 슬롯의 위치를 목표 중심으로 다시 계산한다. 후보를 NavMesh 에 투영해 유효 여부를 판정하고
        /// (NavMesh 밖 = OffNavMesh, 앞 번호 슬롯과 너무 가까움 = TooClose), 무효가 된 슬롯의 예약은 해제한다
        /// </summary>
        private void RefreshRange(int startId)
        {
            // 0) TooClose 판정용 격자 준비: startId 앞쪽의 Valid 슬롯(이번에 다시 계산하지 않는 슬롯)을 먼저 넣는다
            float minSpacing = settings.MinimumSpacing;
            float minSqr = minSpacing * minSpacing;
            // 셀 크기를 MinimumSpacing 보다 아주 조금 크게 잡아, 부동소수 반올림으로 이웃 셀을 놓치는 일을 막는다 (최종 판정은 정확한 거리 비교)
            float cellSize = minSpacing * 1.001f;
            bool useSpacing = minSpacing > 0f; // 0 이하면 기존 비교(dist² >= 0)가 항상 통과라 TooClose 가 없다
            if (useSpacing)
            {
                spacingGridHead.Clear();
                while (spacingGridNext.Count < slots.Count) spacingGridNext.Add(-1);
                for (int other = 0; other < startId; other++)
                    if (slots[other].Validity == SlotValidity.Valid) SpacingGridInsert(other, slots[other].Position, cellSize);
            }

            for (int id = startId; id < slots.Count; id++)
            {
                Slot slot = slots[id];
                RingInfo ring = rings[slot.RingIndex];
                int localIndex = id - ring.StartId;
                float angle = localIndex * (2f * Mathf.PI / ring.Count);
                slot.Candidate = lastCenter + new Vector3(Mathf.Sin(angle) * ring.Radius, 0f, Mathf.Cos(angle) * ring.Radius);
                slot.Position = slot.Candidate;
                slot.Validity = SlotValidity.OffNavMesh;
                if (navigation.TryProject(slot.Candidate, out Vector3 projected))
                {
                    slot.Position = projected;
                    slot.Validity = SlotValidity.Valid;
                    // 앞 번호 Valid 슬롯 중 MinimumSpacing 안에 하나라도 있으면 TooClose (격자의 이웃 셀만 비교)
                    if (useSpacing)
                    {
                        if (IsTooCloseInGrid(projected, cellSize, minSqr)) slot.Validity = SlotValidity.TooClose;
                        else SpacingGridInsert(id, projected, cellSize); // Valid 로 확정된 슬롯만 이후 슬롯의 비교 대상
                    }
                }
                slots[id] = slot;
                // 위치가 확정됐으니 혼잡 반경 안 장애물 수를 캐시 (링을 새로 만들 때의 RefreshRange(startId) 도 여기서 계산됨)
                congestCount[id] = slot.Validity == SlotValidity.Valid ? CountObstaclesNear(slot.Position) : 0;
                if (slot.Validity != SlotValidity.Valid && slot.Use != SlotUse.Free)
                    Release(slot.Owner);
            }
        }

        /// <summary>격자 셀 좌표(cx, cz)를 하나의 long 키로 합친다</summary>
        private static long SpacingCellKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        /// <summary>Valid 슬롯 id 를 position 이 속한 셀의 연결 리스트 맨 앞에 넣는다 (spacingGridNext[id] = 이전 head)</summary>
        private void SpacingGridInsert(int id, Vector3 position, float cellSize)
        {
            long key = SpacingCellKey(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.z / cellSize));
            spacingGridNext[id] = spacingGridHead.TryGetValue(key, out int head) ? head : -1;
            spacingGridHead[key] = id;
        }

        /// <summary>
        /// position 에서 minSqr(= MinimumSpacing²) 미만 거리에 격자 속 슬롯이 있는가.
        /// 거리가 MinimumSpacing 미만인 두 점은 셀 크기 ≥ MinimumSpacing 이므로 항상 인접 3×3 셀 안에 있어, 전수 비교와 결과가 같다
        /// </summary>
        private bool IsTooCloseInGrid(Vector3 position, float cellSize, float minSqr)
        {
            int cx = Mathf.FloorToInt(position.x / cellSize);
            int cz = Mathf.FloorToInt(position.z / cellSize);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!spacingGridHead.TryGetValue(SpacingCellKey(cx + dx, cz + dz), out int other)) continue;
                for (; other >= 0; other = spacingGridNext[other])
                    if (HorizontalSqr(position, slots[other].Position) < minSqr) return true;
            }
            return false;
        }

        /// <summary>
        /// 몬스터에게 슬롯을 주거나 기존 슬롯을 유지·승급시킨다. 슬롯이 있으면 true 와 함께 snapshot 반환.
        /// allowApproach 가 false 면 접근 링(0번)은 건너뛰고 바깥 링에서만 찾는다.
        /// </summary>
        public bool TryRequest(int ownerId, Vector3 position, float now, bool allowApproach, out SlotSnapshot snapshot)
        {
            // A) 이미 슬롯이 있는 경우: 아직 쓸 수 있는지 확인하고 진행 상황을 갱신
            if (ownerSlots.TryGetValue(ownerId, out int ownedId))
            {
                Slot owned = slots[ownedId];
                if (owned.Validity == SlotValidity.Valid &&
                    (allowApproach || ownedId >= approachCount) &&
                    navigation.CanReach(position, owned.Position))
                {
                    // A-1) 슬롯 도착/진전 판정: 도착하면 Occupied, 오래 진전이 없으면 슬롯을 거절하고 새로 찾는다
                    float distance = Mathf.Sqrt(HorizontalSqr(position, owned.Position));
                    Progress state = progress[ownerId];
                    if (distance <= settings.ArrivalDistance)
                    {
                        owned.Use = SlotUse.Occupied;
                        state.LastDistance = distance;
                        state.LastProgressTime = now;
                        state.WasOccupied = true;
                    }
                    else
                    {
                        owned.Use = SlotUse.Reserved;
                        if (state.WasOccupied || state.LastDistance - distance >= settings.ProgressDistance)
                        {
                            state.LastDistance = distance;
                            state.LastProgressTime = now;
                            state.WasOccupied = false;
                        }
                        else if (now - state.LastProgressTime >= settings.ProgressTimeout)
                        {
                            Reject(ownerId, ownedId, now);
                            ownedId = -1;
                        }
                    }
                    if (ownedId >= 0)
                    {
                        // A-2) 슬롯 유지. 더 안쪽 링에 빈 슬롯이 생겼으면 그쪽으로 승급 (안쪽 = 목표에 가까운 링)
                        slots[ownedId] = owned;
                        progress[ownerId] = state;
                        int firstRing = allowApproach ? 0 : 1;
                        int promoted = -1;
                        for (int ring = firstRing; ring < owned.RingIndex; ring++)
                            if (TryFindInRing(ownerId, position, now, ring, out promoted)) break;
                        if (promoted < 0)
                        {
                            snapshot = GetSnapshot(ownedId);
                            return true;
                        }
                        Release(ownerId);
                        Reserve(ownerId, promoted, position, now);
                        snapshot = GetSnapshot(promoted);
                        return true;
                    }
                }
                else
                    Release(ownerId);
            }

            // B) 슬롯이 없거나 잃은 경우: 안쪽 링부터 빈 슬롯을 찾는다. 필요하면 바깥 링을 새로 만든다 (최대 maxRingCount)
            int startRing = allowApproach ? 0 : 1;
            for (int ring = startRing; ring < maxRingCount; ring++)
            {
                if (ring == rings.Count)
                {
                    int startId = slots.Count;
                    AppendRingGeometry();
                    if (refreshed) RefreshRange(startId);
                }
                if (!TryFindInRing(ownerId, position, now, ring, out int available)) continue;
                Reserve(ownerId, available, position, now);
                snapshot = GetSnapshot(available);
                return true;
            }
            snapshot = default;
            return false;
        }

        /// <summary>ownerId 의 슬롯 예약을 해제한다 (없으면 무시)</summary>
        public void Release(int ownerId)
        {
            if (!ownerSlots.TryGetValue(ownerId, out int id)) return;
            Slot slot = slots[id];
            slot.Owner = 0;
            slot.Use = SlotUse.Free;
            slots[id] = slot;
            ownerSlots.Remove(ownerId);
            progress.Remove(ownerId);
            if (id < approachCount) ApproachReservations--;
        }

        /// <summary>모든 슬롯의 예약을 해제한다 (목표 소실·교체 시). 거절 기록은 유지</summary>
        public void ClearReservations()
        {
            for (int id = 0; id < slots.Count; id++)
            {
                Slot slot = slots[id];
                slot.Owner = 0;
                slot.Use = SlotUse.Free;
                slots[id] = slot;
            }
            ownerSlots.Clear();
            progress.Clear();
            ApproachReservations = 0;
        }

        /// <summary>ownerId 가 쥔 slotId 를 RejectionSeconds 동안 해당 소유자에게 다시 주지 않도록 기록하고 해제한다</summary>
        public void Reject(int ownerId, int slotId, float now)
        {
            if (!ownerSlots.TryGetValue(ownerId, out int ownedId) || ownedId != slotId) return;
            rejectedUntil[RejectionKey(ownerId, slotId)] = now + settings.RejectionSeconds;
            Release(ownerId);
        }

        /// <summary>의도적인 반응 대기 기간만큼 기존 정체 시계를 뒤로 민다.</summary>
        public void PauseProgress(int ownerId, float duration)
        {
            if (duration < 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            if (!progress.TryGetValue(ownerId, out Progress state)) return;
            state.LastProgressTime += duration;
            progress[ownerId] = state;
        }

        /// <summary>
        /// ringIndex 링에서 position 에 가장 가까운 사용 가능한 슬롯을 찾는다.
        /// 제외 조건: 무효, 이미 예약됨, 다른 몬스터로 혼잡, 최근 거절, 도달 불가.
        /// 값싼 필터를 먼저 모두 적용해 후보를 모으고, 가까운 후보부터 비싼 CanReach(NavMesh 경로 계산)를 확인한다.
        /// 거리가 같으면 id 가 작은 쪽이 먼저라 결과는 id 순 전수 탐색과 같고 CanReach 호출만 줄어든다
        /// </summary>
        private bool TryFindInRing(int ownerId, Vector3 position, float now, int ringIndex, out int best)
        {
            RingInfo ring = rings[ringIndex];
            int start = ring.StartId;
            int end = start + ring.Count;
            best = -1;

            // 1) 값싼 필터 통과 후보를 id 오름차순으로 (id, 거리²) 버퍼에 모은다
            ringCandidates.Clear();
            for (int id = start; id < end; id++)
            {
                Slot slot = slots[id];
                if (slot.Validity != SlotValidity.Valid || slot.Use != SlotUse.Free || IsCongested(id, ownerId)) continue;
                if (rejectedUntil.TryGetValue(RejectionKey(ownerId, id), out float until) && now < until) continue;
                ringCandidates.Add(new RingCandidate(id, HorizontalSqr(position, slot.Position)));
            }

            // 2) 가장 가까운 후보부터 도달 가능 여부 확인. 불가하면 그 후보를 버리고 다음으로 가까운 후보 (엄격한 < 라 동률은 작은 id 유지)
            while (ringCandidates.Count > 0)
            {
                int nearest = 0;
                for (int i = 1; i < ringCandidates.Count; i++)
                    if (ringCandidates[i].DistanceSqr < ringCandidates[nearest].DistanceSqr) nearest = i;
                int candidateId = ringCandidates[nearest].Id;
                if (navigation.CanReach(position, slots[candidateId].Position))
                {
                    best = candidateId;
                    return true;
                }
                ringCandidates.RemoveAt(nearest); // 순서 유지 (id 오름차순이 동률 판정 기준)
            }
            return false;
        }

        /// <summary>슬롯 id 를 ownerId 에게 예약하고 진행 추적을 시작한다. 이미 슬롯 위에 있으면 바로 Occupied</summary>
        private void Reserve(int ownerId, int id, Vector3 position, float now)
        {
            Slot slot = slots[id];
            slot.Owner = ownerId;
            slot.Use = HorizontalSqr(position, slot.Position) <= settings.ArrivalDistance * settings.ArrivalDistance
                ? SlotUse.Occupied : SlotUse.Reserved;
            slots[id] = slot;
            ownerSlots.Add(ownerId, id);
            progress[ownerId] = new Progress
            {
                LastDistance = Mathf.Sqrt(HorizontalSqr(position, slot.Position)),
                LastProgressTime = now,
                WasOccupied = slot.Use == SlotUse.Occupied
            };
            if (id < approachCount) ApproachReservations++;
        }

        /// <summary>
        /// 슬롯 위치 근처에 (excludedOwner·슬롯 소유자 외의) 다른 몬스터가 서 있어 쓰기 곤란한가. O(1).
        /// RefreshRange 가 미리 센 congestCount 에서, 제외 대상 몬스터가 반경 안에 있으면 그만큼 뺀다
        /// </summary>
        private bool IsCongested(int id, int excludedOwner)
        {
            Slot slot = slots[id];
            if (slot.Validity != SlotValidity.Valid) return false;
            int count = congestCount[id] - IsObstacleNear(excludedOwner, slot.Position);
            // 슬롯 소유자가 제외 대상과 같으면 이미 뺐으므로 한 번만 뺀다
            if (slot.Owner != excludedOwner) count -= IsObstacleNear(slot.Owner, slot.Position);
            return count > 0;
        }

        /// <summary>장애물 obstacle 이 slotPosition 의 혼잡 반경(장애물 반지름 + MinimumSpacing/2) 안에 있는가. 세기·제외 양쪽이 같은 식을 쓰게 한 곳에 둔다</summary>
        private bool IsInClearance(in SlotObstacle obstacle, Vector3 slotPosition)
        {
            float clearance = obstacle.Radius + settings.MinimumSpacing * 0.5f;
            return HorizontalSqr(obstacle.Position, slotPosition) < clearance * clearance;
        }

        /// <summary>slotPosition 혼잡 반경 안의 장애물 수 (RefreshRange 에서 슬롯마다 1회)</summary>
        private int CountObstaclesNear(Vector3 slotPosition)
        {
            int count = 0;
            for (int i = 0; i < obstacles.Count; i++)
                if (IsInClearance(obstacles[i], slotPosition)) count++;
            return count;
        }

        /// <summary>ownerId 의 장애물이 이번 Refresh 목록에 있고 slotPosition 혼잡 반경 안이면 1, 아니면 0</summary>
        private int IsObstacleNear(int ownerId, Vector3 slotPosition) =>
            obstacleIndexByOwner.TryGetValue(ownerId, out int index) && IsInClearance(obstacles[index], slotPosition) ? 1 : 0;

        /// <summary>(몬스터 id, 슬롯 id) 를 하나의 long 키로 합친다</summary>
        private static long RejectionKey(int ownerId, int slotId) => ((long)ownerId << 32) | (uint)slotId;

        /// <summary>수평(XZ) 거리의 제곱</summary>
        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
