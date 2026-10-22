using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>안정 ID 후보와 단일 소유 예약. Unity 메인 스레드에서 순차적으로 요청한다.</summary>
    public sealed class SlotRegistry
    {
        private struct Slot
        {
            public int RingIndex;
            public Vector3 Candidate;
            public Vector3 Position;
            public SlotValidity Validity;
            public int Owner;
            public SlotUse Use;
        }

        private struct Progress
        {
            public float LastDistance;
            public float LastProgressTime;
            public bool WasOccupied;
        }

        private readonly struct RingInfo
        {
            public readonly int StartId;
            public readonly int Count;
            public readonly float Radius;

            public RingInfo(int startId, int count, float radius)
            { StartId = startId; Count = count; Radius = radius; }
        }

        private readonly SlotSettings settings;
        private readonly ISlotNavigation navigation;
        private readonly int approachCount;
        private readonly float approachRadius;
        private readonly float minimumRingGap;
        private readonly int maxRingCount;
        private readonly int countStep;
        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<RingInfo> rings = new List<RingInfo>();
        private readonly Dictionary<int, int> ownerSlots = new Dictionary<int, int>();
        private readonly Dictionary<int, Progress> progress = new Dictionary<int, Progress>();
        private readonly Dictionary<long, float> rejectedUntil = new Dictionary<long, float>();
        private readonly List<long> expiredRejections = new List<long>();
        private readonly List<SlotObstacle> obstacles = new List<SlotObstacle>();

        public int Count => slots.Count;
        public int ApproachCount => approachCount;
        public float ApproachRadius => approachRadius;
        public float MinimumRingGap => minimumRingGap;
        public int MaxRingCount => maxRingCount;
        public int ApproachReservations { get; private set; }
        public bool HasApproachReservation(int ownerId) =>
            ownerSlots.TryGetValue(ownerId, out int id) && Kind(id) == SlotKind.Approach;

        public SlotRegistry(SlotSettings settings, ISlotNavigation navigation)
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

        public SlotSnapshot GetSnapshot(int id)
        {
            if ((uint)id >= (uint)slots.Count) throw new ArgumentOutOfRangeException(nameof(id));
            Slot slot = slots[id];
            return new SlotSnapshot(id, Kind(id), slot.Candidate, slot.Position, slot.Validity,
                slot.Owner, slot.Use, IsCongested(id, slot.Owner), slot.RingIndex);
        }

        public void Refresh(Vector3 center, float now, IReadOnlyList<SlotObstacle> currentObstacles)
        {
            if (currentObstacles == null) throw new ArgumentNullException(nameof(currentObstacles));
            obstacles.Clear();
            for (int i = 0; i < currentObstacles.Count; i++) obstacles.Add(currentObstacles[i]);
            expiredRejections.Clear();
            foreach (KeyValuePair<long, float> rejection in rejectedUntil)
                if (now >= rejection.Value) expiredRejections.Add(rejection.Key);
            for (int i = 0; i < expiredRejections.Count; i++) rejectedUntil.Remove(expiredRejections[i]);

            lastCenter = center;
            refreshed = true;
            RefreshRange(0);

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

        private Vector3 lastCenter;
        private bool refreshed;

        private void AppendRingGeometry()
        {
            int ringIndex = rings.Count;
            int count = approachCount + ringIndex * countStep;
            float radius = approachRadius * count / approachCount;
            rings.Add(new RingInfo(slots.Count, count, radius));
            for (int i = 0; i < count; i++) slots.Add(new Slot { RingIndex = ringIndex });
        }

        private void RefreshRange(int startId)
        {
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
                    float minSqr = settings.MinimumSpacing * settings.MinimumSpacing;
                    for (int other = 0; other < id; other++)
                    {
                        if (slots[other].Validity != SlotValidity.Valid) continue;
                        if (HorizontalSqr(projected, slots[other].Position) >= minSqr) continue;
                        slot.Validity = SlotValidity.TooClose;
                        break;
                    }
                }
                slots[id] = slot;
                if (slot.Validity != SlotValidity.Valid && slot.Use != SlotUse.Free)
                    Release(slot.Owner);
            }
        }

        public bool TryRequest(int ownerId, Vector3 position, float now, bool allowApproach, out SlotSnapshot snapshot)
        {
            if (ownerSlots.TryGetValue(ownerId, out int ownedId))
            {
                Slot owned = slots[ownedId];
                if (owned.Validity == SlotValidity.Valid &&
                    (allowApproach || Kind(ownedId) == SlotKind.Waiting) &&
                    navigation.CanReach(position, owned.Position))
                {
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

        public void Release(int ownerId)
        {
            if (!ownerSlots.TryGetValue(ownerId, out int id)) return;
            Slot slot = slots[id];
            slot.Owner = 0;
            slot.Use = SlotUse.Free;
            slots[id] = slot;
            ownerSlots.Remove(ownerId);
            progress.Remove(ownerId);
            if (Kind(id) == SlotKind.Approach) ApproachReservations--;
        }

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

        private bool TryFindInRing(int ownerId, Vector3 position, float now, int ringIndex, out int best)
        {
            RingInfo ring = rings[ringIndex];
            int start = ring.StartId;
            int end = start + ring.Count;
            best = -1;
            float bestSqr = float.PositiveInfinity;
            for (int id = start; id < end; id++)
            {
                Slot slot = slots[id];
                if (slot.Validity != SlotValidity.Valid || slot.Use != SlotUse.Free || IsCongested(id, ownerId)) continue;
                if (rejectedUntil.TryGetValue(RejectionKey(ownerId, id), out float until) && now < until) continue;
                float distance = HorizontalSqr(position, slot.Position);
                if (distance >= bestSqr || !navigation.CanReach(position, slot.Position)) continue;
                best = id;
                bestSqr = distance;
            }
            return best >= 0;
        }

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
            if (Kind(id) == SlotKind.Approach) ApproachReservations++;
        }

        private bool IsCongested(int id, int excludedOwner)
        {
            Slot slot = slots[id];
            if (slot.Validity != SlotValidity.Valid) return false;
            for (int i = 0; i < obstacles.Count; i++)
            {
                SlotObstacle obstacle = obstacles[i];
                if (obstacle.OwnerId == excludedOwner || obstacle.OwnerId == slot.Owner) continue;
                float clearance = obstacle.Radius + settings.MinimumSpacing * 0.5f;
                if (HorizontalSqr(obstacle.Position, slot.Position) < clearance * clearance) return true;
            }
            return false;
        }

        private SlotKind Kind(int id) => slots[id].RingIndex == 0 ? SlotKind.Approach : SlotKind.Waiting;
        private static long RejectionKey(int ownerId, int slotId) => ((long)ownerId << 32) | (uint)slotId;
        private static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
