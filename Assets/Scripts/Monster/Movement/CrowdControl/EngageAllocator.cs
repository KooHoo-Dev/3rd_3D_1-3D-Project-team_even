using System;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 군중 밀도 판단 + 근접 허용 토큰 배분 (순수 로직, 상태 없음, GC 할당 없음).
    /// 규칙 명세: report/specs/allocator-spec.md
    /// </summary>
    public static class EngageAllocator
    {
        private const float GoldenAngleDeg = 137.5f;
        private const float SamePositionEpsilon = 1e-4f;

        private const int KindUnrestricted = 0;
        private const int KindEngage = 1;
        private const int KindWait = 2;

        // 호출 간 재사용 버퍼 (스레드 안전 불필요). 개수가 늘어날 때만 확장한다.
        private static float[] s_dist = new float[0];
        private static int[] s_kind = new int[0];
        private static bool[] s_revoked = new bool[0];
        private static int[] s_order = new int[0];
        private static int[] s_slotOf = new int[0];
        private static bool[] s_slotTaken = new bool[0];

        /// <summary>
        /// agents[0..count) 를 판단해 results[0..count) 에 쓴다.
        /// 규칙 순서: 입력 검증 -> 수평 거리 -> 비토큰 개체(Unrestricted, 과밀 집계)
        /// -> 기존 보유자 회수(ReleaseRadius 초과, 정원 초과 시 먼 순) -> 신규 배분(대기시간 자격, 거리 오름차순)
        /// -> 포위 슬롯(UseSlots) -> 결과 기록(Engage/Wait, WaitPoint, OrbitSign) -> 집계.
        /// results.Length &lt; count 이거나 파라미터가 잘못되면 예외.
        /// </summary>
        public static AllocationSummary Allocate(in AllocationParams p, AllocationAgent[] agents, int count, AllocationResult[] results)
        {
            Validate(p, agents, count, results);
            EnsureCapacity(count, p.UseSlots ? p.Capacity : 0);

            int uncontrolledInside = ResolveHolders(p, agents, count, out int maxHolders, out int keptHolders);
            GrantTokens(p, agents, count, maxHolders, keptHolders);
            AssignSlots(p, agents, count);
            return WriteResults(p, agents, count, results, uncontrolledInside);
        }

        private static void Validate(in AllocationParams p, AllocationAgent[] agents, int count, AllocationResult[] results)
        {
            if (agents == null) throw new ArgumentNullException(nameof(agents));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (count < 0 || count > agents.Length)
                throw new ArgumentException($"count({count}) 가 agents.Length({agents.Length}) 범위를 벗어남", nameof(count));
            if (results.Length < count)
                throw new ArgumentException($"results.Length({results.Length}) < count({count})", nameof(results));
            if (p.Capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(p), $"Capacity({p.Capacity}) < 0");
            if (p.Slots == null || !(0f <= p.NearRadius && p.NearRadius < p.Slots.WaitingRadius && p.Slots.WaitingRadius <= p.ReleaseRadius))
                throw new ArgumentException(
                    $"반경 순서 위반: 0 <= Near({p.NearRadius}) < Waiting({p.Slots?.WaitingRadius}) <= Release({p.ReleaseRadius})", nameof(p));
        }

        private static void EnsureCapacity(int count, int slotCount)
        {
            if (s_dist.Length < count)
            {
                s_dist = new float[count];
                s_kind = new int[count];
                s_revoked = new bool[count];
                s_order = new int[count];
                s_slotOf = new int[count];
            }
            if (s_slotTaken.Length < slotCount) s_slotTaken = new bool[slotCount];
        }

        /// <summary>거리 계산, 비토큰 집계, 기존 보유자 회수(범위 이탈 + 정원 초과). UncontrolledInside 반환</summary>
        private static int ResolveHolders(in AllocationParams p, AllocationAgent[] agents, int count, out int maxHolders, out int keptHolders)
        {
            int uncontrolledInside = 0;
            for (int i = 0; i < count; i++)
            {
                float dx = agents[i].Position.x - p.TargetPosition.x;
                float dz = agents[i].Position.z - p.TargetPosition.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                s_dist[i] = dist;
                s_revoked[i] = false;
                s_slotOf[i] = -1;
                s_kind[i] = agents[i].UsesToken ? KindWait : KindUnrestricted;
                if (!agents[i].UsesToken && dist <= p.NearRadius) uncontrolledInside++;
            }

            maxHolders = Mathf.Max(0, p.Capacity - uncontrolledInside);

            // 범위 이탈 회수 + 남은 보유자 수집
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (!agents[i].UsesToken || !agents[i].HasToken) continue;
                if (s_dist[i] > p.ReleaseRadius) s_revoked[i] = true;
                else s_order[n++] = i;
            }

            // 정원 초과: (거리, index) 오름차순 정렬 후 뒤(먼 쪽, 동률이면 index 큰 쪽)부터 회수
            SortByDistance(n);
            keptHolders = n;
            while (keptHolders > maxHolders)
            {
                keptHolders--;
                s_revoked[s_order[keptHolders]] = true;
            }
            for (int k = 0; k < keptHolders; k++) s_kind[s_order[k]] = KindEngage;

            return uncontrolledInside;
        }

        /// <summary>자격 있는 미보유자에게 빈 자리만큼 가까운 순으로 토큰 배분</summary>
        private static void GrantTokens(in AllocationParams p, AllocationAgent[] agents, int count, int maxHolders, int keptHolders)
        {
            int free = Mathf.Max(0, maxHolders - keptHolders);
            if (free == 0) return;

            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (!agents[i].UsesToken || agents[i].HasToken) continue; // 회수된 개체는 HasToken 이므로 자연히 제외
                float since = agents[i].WaitingSince;
                if (since < 0f || p.Now - since >= agents[i].MinWaitSeconds) s_order[n++] = i;
            }

            SortByDistance(n);
            int grant = Mathf.Min(free, n);
            for (int k = 0; k < grant; k++) s_kind[s_order[k]] = KindEngage;
        }

        /// <summary>토큰 보유자에게 포위 슬롯 배정 (UseSlots 일 때만)</summary>
        private static void AssignSlots(in AllocationParams p, AllocationAgent[] agents, int count)
        {
            if (!p.UseSlots || p.Capacity == 0) return;

            for (int s = 0; s < p.Capacity; s++) s_slotTaken[s] = false;

            // 1) 기존 슬롯 유지 (index 오름차순이므로 겹치면 index 작은 쪽이 유지)
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (s_kind[i] != KindEngage) continue;
                int old = agents[i].SlotIndex;
                if (old >= 0 && old < p.Capacity && !s_slotTaken[old])
                {
                    s_slotTaken[old] = true;
                    s_slotOf[i] = old;
                }
                else
                {
                    s_order[n++] = i;
                }
            }

            // 2) 나머지는 가까운 순으로 자기 방위에 가장 가까운 빈 슬롯 선택
            SortByDistance(n);
            float step = 360f / p.Capacity;
            for (int k = 0; k < n; k++)
            {
                int i = s_order[k];
                float bearing = Bearing(agents[i].Position, p.TargetPosition);
                int best = -1;
                float bestDiff = float.MaxValue;
                for (int s = 0; s < p.Capacity; s++)
                {
                    if (s_slotTaken[s]) continue;
                    float diff = AngleDiff(bearing, s * step);
                    if (diff < bestDiff) // 엄격 비교: 동률이면 작은 슬롯 번호 유지
                    {
                        bestDiff = diff;
                        best = s;
                    }
                }
                s_slotTaken[best] = true;
                s_slotOf[i] = best;
            }
        }

        private static AllocationSummary WriteResults(in AllocationParams p, AllocationAgent[] agents, int count,
                                                      AllocationResult[] results, int uncontrolledInside)
        {
            int engaged = 0, waiting = 0, unrestricted = 0;
            float step = p.Capacity > 0 ? 360f / p.Capacity : 0f;

            for (int i = 0; i < count; i++)
            {
                int kind = s_kind[i];
                if (kind == KindUnrestricted)
                {
                    unrestricted++;
                    results[i] = new AllocationResult
                    {
                        Directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f),
                        HasToken = false,
                        WaitingSince = -1f,
                        OrbitSign = 0,
                        SlotIndex = -1,
                    };
                    continue;
                }

                int orbit = agents[i].OrbitSign != 0 ? agents[i].OrbitSign : ((i & 1) == 0 ? 1 : -1);

                if (kind == KindEngage)
                {
                    engaged++;
                    int slot = s_slotOf[i];
                    bool hasSlot = slot >= 0;
                    results[i] = new AllocationResult
                    {
                        Directive = new CrowdDirective(DirectiveKind.Engage, default, orbit, hasSlot, hasSlot ? slot * step : 0f),
                        HasToken = true,
                        WaitingSince = -1f,
                        OrbitSign = orbit,
                        SlotIndex = hasSlot ? slot : -1,
                    };
                    continue;
                }

                waiting++;
                // 회수된 개체는 이번 판단 시각부터 대기 시작
                float since = !s_revoked[i] && agents[i].WaitingSince >= 0f ? agents[i].WaitingSince : p.Now;
                results[i] = new AllocationResult
                {
                    Directive = new CrowdDirective(DirectiveKind.Wait, WaitPoint(p, agents[i].Position, s_dist[i], i), orbit, false, 0f),
                    HasToken = false,
                    WaitingSince = since,
                    OrbitSign = orbit,
                    SlotIndex = -1,
                };
            }

            return new AllocationSummary
            {
                Engaged = engaged,
                Waiting = waiting,
                Unrestricted = unrestricted,
                UncontrolledInside = uncontrolledInside,
                FreeCapacity = Mathf.Max(0, p.Capacity - uncontrolledInside - engaged),
            };
        }

        /// <summary>대기 링 위 지점. 목표와 수평 위치가 같으면 index 기반 황금각 방위 사용</summary>
        private static Vector3 WaitPoint(in AllocationParams p, Vector3 pos, float dist, int index)
        {
            float dirX, dirZ;
            if (dist < SamePositionEpsilon)
            {
                float rad = index * GoldenAngleDeg * Mathf.Deg2Rad;
                dirX = Mathf.Sin(rad);
                dirZ = Mathf.Cos(rad);
            }
            else
            {
                dirX = (pos.x - p.TargetPosition.x) / dist;
                dirZ = (pos.z - p.TargetPosition.z) / dist;
            }
            return new Vector3(p.TargetPosition.x + dirX * p.Slots.WaitingRadius, p.TargetPosition.y, p.TargetPosition.z + dirZ * p.Slots.WaitingRadius);
        }

        /// <summary>목표 기준 방위각 [0, 360) 도, atan2(dx, dz)</summary>
        private static float Bearing(Vector3 pos, Vector3 target)
        {
            float deg = Mathf.Atan2(pos.x - target.x, pos.z - target.z) * Mathf.Rad2Deg;
            if (deg < 0f) deg += 360f;
            if (deg >= 360f) deg -= 360f;
            return deg;
        }

        /// <summary>원형 최소 각도 차 (0~180)</summary>
        private static float AngleDiff(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>s_order[0..n) 을 (거리, index) 오름차순으로 삽입 정렬 (할당 없음)</summary>
        private static void SortByDistance(int n)
        {
            for (int i = 1; i < n; i++)
            {
                int key = s_order[i];
                float kd = s_dist[key];
                int j = i - 1;
                while (j >= 0 && Less(kd, key, s_dist[s_order[j]], s_order[j]))
                {
                    s_order[j + 1] = s_order[j];
                    j--;
                }
                s_order[j + 1] = key;
            }
        }

        private static bool Less(float distA, int indexA, float distB, int indexB)
        {
            return distA < distB || (distA == distB && indexA < indexB);
        }
    }
}
