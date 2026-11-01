using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 보스가 부른 잡몹만 따로 관리한다 (포효 소환·알 부화). 웨이브 잡몹은 건드리지 않는다.
    ///  - 경기장: 중심·반지름을 인스펙터로 받거나, 없으면 보스 위치에서 사방으로 레이를 쏴 벽까지 거리로 잰다
    ///  - SpawnAtWall: 벽 바로 안쪽 NavMesh 위에서 소환 (벽을 넘어온 것처럼)
    ///  - WaveKill: 진원에서 퍼지는 파동이 닿는 순서대로 날려 보내며 보상 없이 처치
    ///  - CollapseCohort: 포효 무리를 시간 차를 두고 그 자리에 쓰러뜨리고 쓰러진 자리를 기록 (플레이어 사망 → 에너지 흡수)
    /// 날려 보낸 몸은 NavMeshAgent 의 위치 갱신을 잠시 끄고 직접 움직이며, 풀로 돌아가 꺼지면 되돌린다.
    /// 보스가 부른 잡몹은 speedMultiplier 만큼 느리게 움직인다 (풀로 돌아가 다시 쓰일 때는 MonsterController 가 원래 속도로 되돌린다).
    /// 포효로 벽을 넘어온 잡몹은 "포효 무리"로 따로 묶는다:
    ///  - 소환 위치에서 그때 플레이어까지 거리 × approachRatio 만큼은 approachMultiplier 로 빠르게 달려와 멀리서도 다가오는 게 보이고, 그 뒤 느려진다
    ///  - 무리가 살아 있는 동안은 포효 패턴이 이어지는 것으로 본다 (TyranopodeController 가 플레이어 사망 시 무리를 흡수)
    /// 등장 연출에서는 씬에 남은 웨이브 몬스터도 넘겨받는다 (AdoptWaveMonsters → 기절 → DrainAdopted 로 하나둘 쓰러짐)
    /// 소환 자리는 모두 TryFindSpawnPoint 로 고른다: 근처 NavMesh 에서 플레이어까지 걸어서 이어지는 자리만
    /// (맵 밖·구덩이처럼 끊긴 곳, 바깥으로 크게 돌아오는 곳은 버리고 다른 자리로 다시 시도)
    /// </summary>
    public sealed class TyranopodeMinions
    {
        private const int MeasureRays = 24;
        private const float MeasureHeight = 1.5f;
        private const float MeasureRange = 120f;
        private const float WallInset = 3f;
        private const float SpawnSample = 3f;     // 후보에서 NavMesh 를 찾는 반경(m)
        private const float PlayerSample = 4f;    // 점프 중인 플레이어 발밑 NavMesh 를 찾는 반경(m)
        private const float MaxDetour = 3f;       // 길 길이가 직선 거리의 이 배 + DetourSlack 을 넘으면 바깥으로 도는 길
        private const float DetourSlack = 10f;
        private const int SpawnTries = 6;

        private enum KillStyle { Fling, Collapse }

        private struct PendingKill
        {
            public MonsterController Monster;
            public float Time;
            public KillStyle Style;
            public Vector3 Origin;
        }

        /// <summary>날아가는 시체 하나</summary>
        private sealed class Flung
        {
            public MonsterController Monster;
            public NavMeshAgent Agent;
            public Vector3 Velocity;
            public Vector3 SpinAxis;
            public float SpinSpeed;
            public float GroundY;
            public bool Landed;
        }

        /// <summary>파동에 맞은 잡몹이 날아가는 세기</summary>
        [Serializable]
        public struct FlingSettings
        {
            [Tooltip("바깥으로 날아가는 수평 속도(m/s)")] public float HorizontalSpeed;
            [Tooltip("튀어 오르는 속도(m/s)")] public float UpSpeed;
            [Tooltip("떨어지는 가속도(m/s²)")] public float Gravity;
            [Tooltip("회전 속도(도/초)")] public float SpinSpeed;
        }

        private readonly List<MonsterController> alive = new List<MonsterController>();
        private readonly Dictionary<MonsterController, Action> deathHandlers = new Dictionary<MonsterController, Action>();
        private readonly List<PendingKill> pendingKills = new List<PendingKill>();
        private readonly HashSet<MonsterController> scheduled = new HashSet<MonsterController>();
        private readonly List<Flung> flung = new List<Flung>();
        private readonly List<Vector3> collapsed = new List<Vector3>();
        /// <summary>속도를 아직 정하지 않았거나 빠른 구간을 달리는 잡몹</summary>
        private sealed class SpeedState
        {
            public MonsterController Monster;
            public bool Approaching;   // true = 빠른 구간 (포효 무리)
            public bool Applied;       // 지금 단계 속도를 넣었는지
            public Vector3 Start;
            public float ApproachDistance;
        }

        private readonly List<SpeedState> speeds = new List<SpeedState>();
        private readonly HashSet<MonsterController> cohort = new HashSet<MonsterController>();
        private readonly List<MonsterController> adopted = new List<MonsterController>();
        private readonly List<MonsterController> pendingStuns = new List<MonsterController>();
        private float stunSeconds;
        private readonly float speedMultiplier;
        private readonly float approachMultiplier;
        private readonly float approachRatio;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly NavMeshPath path = new NavMeshPath();
        private readonly Transform ignoreRoot;
        private readonly GameObject spawnVfx;
        private readonly GameObject deathVfx;
        private readonly FlingSettings flingSettings;

        public Vector3 Center { get; private set; }
        public float Radius { get; private set; }
        public int AliveCount => alive.Count;
        /// <summary>살아 있는 포효 무리 수. 0 보다 크면 포효 패턴이 이어지는 중</summary>
        public int CohortAliveCount => cohort.Count;

        /// <summary>버린 소환 후보 수 (디버그 HUD): NavMesh 없음 / 플레이어까지 길이 끊김 / 바깥으로 크게 도는 길</summary>
        public int RejectedNoNavMesh { get; private set; }
        public int RejectedDisconnected { get; private set; }
        public int RejectedDetour { get; private set; }

        public TyranopodeMinions(Transform ignoreRoot, GameObject spawnVfx, GameObject deathVfx, FlingSettings fling,
            float speedMultiplier, float approachMultiplier, float approachRatio)
        {
            this.speedMultiplier = speedMultiplier;
            this.approachMultiplier = approachMultiplier;
            this.approachRatio = approachRatio;
            this.ignoreRoot = ignoreRoot;
            this.spawnVfx = spawnVfx;
            this.deathVfx = deathVfx;
            flingSettings = fling;
        }

        /// <summary>경기장을 정한다. radius &lt;= 0 이면 from 근처에서 잰다 (fallbackRadius = 잴 수 없을 때)</summary>
        public void SetupArena(Vector3 from, float radius, float fallbackRadius)
        {
            if (radius > 0f)
            {
                Center = from;
                Radius = radius;
                return;
            }

            // 두 번 잰다: 처음 맞은 벽 점들의 중심에서 다시 재면 원형 경기장의 중심에 가까워진다
            Vector3 center = from;
            float measured = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                if (TryMeasure(center, out Vector3 hitCenter, out measured) == false) break;
                center = new Vector3(hitCenter.x, from.y, hitCenter.z);
            }
            Center = center;
            Radius = measured > 0f ? measured : fallbackRadius;
        }

        private bool TryMeasure(Vector3 center, out Vector3 hitCenter, out float radius)
        {
            var distances = new List<float>(MeasureRays);
            Vector3 sum = Vector3.zero;
            Vector3 origin = center + Vector3.up * MeasureHeight;
            for (int i = 0; i < MeasureRays; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, 360f * i / MeasureRays, 0f) * Vector3.forward;
                if (TryRayToWall(origin, dir, out float distance) == false) continue;
                distances.Add(distance);
                sum += origin + dir * distance;
            }

            hitCenter = center;
            radius = 0f;
            if (distances.Count < MeasureRays / 2) return false;
            distances.Sort();
            radius = distances[distances.Count / 2];
            hitCenter = sum / distances.Count;
            return true;
        }

        private bool TryRayToWall(Vector3 origin, Vector3 dir, out float distance)
        {
            distance = float.MaxValue;
            int count = Physics.RaycastNonAlloc(origin, dir, hits, MeasureRange, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = hits[i].collider;
                if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;
                if (col.TryGetDamageable(out _)) continue; // 플레이어·몬스터는 벽이 아니다
                if (hits[i].distance < distance) distance = hits[i].distance;
            }
            return distance < float.MaxValue;
        }

        /// <summary>
        /// 포효 무리: 벽 바로 안쪽 무작위 위치에 count 마리. maxAlive 를 넘으면 그만큼 덜 부른다. 반환 = 부른 수.
        /// playerPosition 까지 거리의 approachRatio 만큼은 빠르게 달려온다
        /// </summary>
        public int SpawnAtWall(IReadOnlyList<int> monsterIds, int count, int maxAlive, Vector3 playerPosition)
        {
            int spawned = 0;
            for (int i = 0; i < count && AliveCount < maxAlive; i++)
            {
                if (TryWallPoint(playerPosition, out Vector3 point, out Vector3 dir) == false) continue;
                int id = monsterIds[UnityEngine.Random.Range(0, monsterIds.Count)];
                var monster = Spawn(id, point, Quaternion.LookRotation(-dir));
                cohort.Add(monster);
                var state = speeds.Find(s => s.Monster == monster);
                state.Approaching = true;
                state.ApproachDistance = Flat(playerPosition - monster.transform.position).magnitude * approachRatio;
                spawned++;
            }
            return spawned;
        }

        /// <summary>
        /// 벽 바로 안쪽 소환 자리: 무작위 방향의 실제 벽까지 재서(원형이 아닌 경기장도 벽 너머로 나가지 않게) 그 안쪽 WallInset.
        /// 플레이어까지 길이 이어지는 자리가 나올 때까지 SpawnTries 번 방향을 바꾼다
        /// </summary>
        private bool TryWallPoint(Vector3 playerPosition, out Vector3 point, out Vector3 dir)
        {
            for (int t = 0; t < SpawnTries; t++)
            {
                dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                float wall = TryRayToWall(Center + Vector3.up * MeasureHeight, dir, out float distance) ? distance : Radius;
                Vector3 candidate = Center + dir * Mathf.Max(1f, wall - WallInset);
                if (TryFindSpawnPoint(candidate, playerPosition, out point)) return true;
            }
            point = Center;
            dir = Vector3.forward;
            return false;
        }

        /// <summary>
        /// 소환 자리 검사: candidate 근처 NavMesh 위로 옮기고, 그 자리에서 플레이어 발밑까지 걸어서 끝까지 이어지는지(맵 밖·구덩이 X),
        /// 길이 직선보다 지나치게 돌아가지 않는지(벽 바깥을 크게 도는 길 X) 본다. 플레이어가 높이 떠 있어 발밑을 못 찾으면 NavMesh 위면 된다
        /// </summary>
        public bool TryFindSpawnPoint(Vector3 candidate, Vector3 playerPosition, out Vector3 point)
        {
            point = candidate;
            if (NavMesh.SamplePosition(candidate, out var hit, SpawnSample, NavMesh.AllAreas) == false)
            {
                RejectedNoNavMesh++;
                return false;
            }
            point = hit.position;
            if (NavMesh.SamplePosition(playerPosition, out var feet, PlayerSample, NavMesh.AllAreas) == false) return true;

            if (NavMesh.CalculatePath(point, feet.position, NavMesh.AllAreas, path) == false || path.status != NavMeshPathStatus.PathComplete)
            {
                RejectedDisconnected++;
                return false;
            }
            float length = 0f;
            var corners = path.corners;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            if (length > Flat(feet.position - point).magnitude * MaxDetour + DetourSlack)
            {
                RejectedDetour++;
                return false;
            }
            return true;
        }

        /// <summary>position 주변에 count 마리 (알 부화). 갈 수 없는 자리면 알 자리에서</summary>
        public void SpawnAround(int monsterId, Vector3 position, int count, Vector3 playerPosition)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * 1.5f;
                if (TryFindSpawnPoint(position + new Vector3(offset.x, 0f, offset.y), playerPosition, out Vector3 point) == false
                    && TryFindSpawnPoint(position, playerPosition, out point) == false)
                {
                    Debug.LogWarning($"[Tyranopode] 알 자리 {position} 에서 플레이어까지 길이 없어 부화하지 못했습니다.");
                    continue;
                }
                Vector3 look = Center - point;
                look.y = 0f;
                Spawn(monsterId, point, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            }
        }

        /// <summary>디버그: 웨이브 몬스터처럼 경기장에 흩어 놓는다 (보스 잡몹으로 관리하지 않는다)</summary>
        public void SpawnLoose(IReadOnlyList<int> monsterIds, int count, Vector3 playerPosition)
        {
            var factory = ServiceLocator.Get<IMonsterFactory>();
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 candidate = Center + dir * UnityEngine.Random.Range(6f, Mathf.Max(7f, Radius - WallInset));
                if (TryFindSpawnPoint(candidate, playerPosition, out Vector3 point) == false) continue;
                var def = DataManager.Monsters.Get(monsterIds[UnityEngine.Random.Range(0, monsterIds.Count)]);
                factory.Spawn(def, point, Quaternion.LookRotation(-dir));
            }
        }

        /// <summary>
        /// 등장 연출: 씬에 살아 있는 몬스터(웨이브 몬스터 포함)를 모두 넘겨받아 seconds 동안 그 자리에 기절시킨다. 반환 = 넘겨받은 수
        /// </summary>
        public int AdoptWaveMonsters(float seconds)
        {
            adopted.Clear();
            stunSeconds = seconds;
            foreach (var monster in UnityEngine.Object.FindObjectsByType<MonsterController>(FindObjectsSortMode.None))
            {
                if (monster.IsAlive == false || monster.gameObject.activeInHierarchy == false) continue;
                Track(monster, tuneSpeed: false);
                adopted.Add(monster);
                // 소환 직후 첫 프레임이라 상태 머신이 아직 없으면 Tick 에서
                pendingStuns.Add(monster);
            }
            return adopted.Count;
        }

        /// <summary>
        /// 등장 연출: origin(보스 발밑)에서 퍼지는 땅 혈관이 닿는 순서대로 넘겨받은 몬스터가 그 자리에 쓰러진다.
        /// 가장 먼 몬스터가 delay + spread 초에 쓰러진다. 쓰러진 자리는 TakeCollapsedPositions 로 (보스로 가는 에너지)
        /// </summary>
        public void DrainAdopted(Vector3 origin, float delay, float spread)
        {
            float farthest = 1f;
            foreach (var monster in adopted)
                if (monster != null) farthest = Mathf.Max(farthest, Flat(monster.transform.position - origin).magnitude);
            foreach (var monster in adopted)
            {
                if (monster == null || monster.IsAlive == false || scheduled.Contains(monster)) continue;
                float distance = Flat(monster.transform.position - origin).magnitude;
                Schedule(monster, Time.time + delay + spread * distance / farthest + UnityEngine.Random.Range(0f, 0.15f), KillStyle.Collapse, Vector3.zero);
            }
            adopted.Clear();
        }

        /// <summary>point = TryFindSpawnPoint 로 고른 NavMesh 위 자리</summary>
        private MonsterController Spawn(int monsterId, Vector3 point, Quaternion rotation)
        {
            var factory = ServiceLocator.Get<IMonsterFactory>(); // 씬에 MonsterPool 이 없으면 기본 ResourcesMonsterFactory
            var def = DataManager.Monsters.Get(monsterId);
            var monster = factory.Spawn(def, point, rotation).GetComponent<MonsterController>();
            Track(monster, tuneSpeed: true);
            SpawnVfx(spawnVfx, point);
            return monster;
        }

        /// <param name="tuneSpeed">보스가 부른 잡몹 = true (느리게). 넘겨받은 웨이브 몬스터는 속도를 건드리지 않는다</param>
        private void Track(MonsterController monster, bool tuneSpeed)
        {
            if (deathHandlers.ContainsKey(monster)) return;
            Action handler = null;
            handler = () => Untrack(monster);
            deathHandlers[monster] = handler;
            monster.Died += handler;
            alive.Add(monster);
            if (tuneSpeed) speeds.Add(new SpeedState { Monster = monster, Start = monster.transform.position });
        }

        private void Untrack(MonsterController monster)
        {
            if (deathHandlers.TryGetValue(monster, out var handler))
            {
                monster.Died -= handler;
                deathHandlers.Remove(monster);
            }
            alive.Remove(monster);
            cohort.Remove(monster);
        }

        /// <summary>
        /// origin 에서 speed(m/s)로 퍼지는 파동: radius 안의 잡몹을 닿는 순서대로 바깥으로 날려 보내며 처치한다.
        /// 앞 파동이 이미 맡은 잡몹은 건너뛴다. 반환 = 이번 파동이 맡은 수
        /// </summary>
        public int WaveKill(Vector3 origin, float speed, float radius)
        {
            int count = 0;
            foreach (var monster in alive)
            {
                if (monster == null || scheduled.Contains(monster)) continue;
                float distance = Flat(monster.transform.position - origin).magnitude;
                if (distance > radius) continue;
                Schedule(monster, Time.time + distance / Mathf.Max(0.1f, speed), KillStyle.Fling, origin);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 포효 무리만 minDelay~maxDelay 사이 무작위 시각에 그 자리에 쓰러진다 (알 잡몹은 그대로). 반환 = 쓰러질 수
        /// </summary>
        public int CollapseCohort(float minDelay, float maxDelay)
        {
            int count = 0;
            foreach (var monster in cohort)
            {
                if (scheduled.Contains(monster)) continue;
                Schedule(monster, Time.time + UnityEngine.Random.Range(minDelay, maxDelay), KillStyle.Collapse, Vector3.zero);
                count++;
            }
            return count;
        }

        /// <summary>CollapseCohort 로 쓰러진 자리들 (받으면 비운다)</summary>
        public List<Vector3> TakeCollapsedPositions()
        {
            var positions = new List<Vector3>(collapsed);
            collapsed.Clear();
            return positions;
        }

        /// <summary>남은 잡몹을 바로 처치 (보스 사망)</summary>
        public void KillAllNow()
        {
            pendingKills.Clear();
            scheduled.Clear();
            for (int i = alive.Count - 1; i >= 0; i--) Kill(alive[i], KillStyle.Collapse, Vector3.zero);
        }

        private void Schedule(MonsterController monster, float time, KillStyle style, Vector3 origin)
        {
            scheduled.Add(monster);
            pendingKills.Add(new PendingKill { Monster = monster, Time = time, Style = style, Origin = origin });
        }

        public void Tick()
        {
            alive.RemoveAll(m => m == null || m.gameObject.activeInHierarchy == false);
            cohort.RemoveWhere(m => m == null || m.gameObject.activeInHierarchy == false);

            for (int i = pendingKills.Count - 1; i >= 0; i--)
            {
                var pending = pendingKills[i];
                if (Time.time < pending.Time) continue;
                // 소환 직후 첫 프레임에는 상태 머신이 아직 없다 (MonsterPool.Spawn 주석) → 다음 프레임에
                if (pending.Monster != null && pending.Monster.StateMachine == null) continue;
                Kill(pending.Monster, pending.Style, pending.Origin);
                scheduled.Remove(pending.Monster);
                pendingKills.RemoveAt(i);
            }

            TickStuns();
            TickFlung();
            TickSpeeds();
        }

        private void TickStuns()
        {
            for (int i = pendingStuns.Count - 1; i >= 0; i--)
            {
                var monster = pendingStuns[i];
                if (monster != null && monster.IsAlive && monster.StateMachine == null) continue;
                if (monster != null && monster.IsAlive) monster.StateMachine.Stun(stunSeconds);
                pendingStuns.RemoveAt(i);
            }
        }

        /// <summary>
        /// 상태 머신이 조립된 뒤(처음 소환된 개체는 다음 프레임 Start) 전투 이동 속도를 정한다.
        /// MonsterController 가 조립할 때 쓰는 값(Def.MoveSpeed, 첫 기본 공격 사거리)에 배율만 곱한다.
        /// 포효 무리는 빠른 구간을 다 달리면 느린 속도로 한 번 더 바꾼다
        /// </summary>
        private void TickSpeeds()
        {
            for (int i = speeds.Count - 1; i >= 0; i--)
            {
                var state = speeds[i];
                var monster = state.Monster;
                if (monster == null || monster.IsAlive == false) { speeds.RemoveAt(i); continue; }
                if (monster.StateMachine == null) continue;

                if (state.Approaching && Flat(monster.transform.position - state.Start).magnitude >= state.ApproachDistance)
                {
                    state.Approaching = false;
                    state.Applied = false;
                }
                if (state.Applied) continue;

                SetSpeed(monster, state.Approaching ? approachMultiplier : speedMultiplier);
                state.Applied = true;
                if (state.Approaching == false) speeds.RemoveAt(i);
            }
        }

        private static void SetSpeed(MonsterController monster, float multiplier)
        {
            var def = monster.Def;
            monster.GetComponent<MonsterMovement>().SetCombatTuning(def.MoveSpeed * multiplier, def.BasicAttacks[0].Range);
        }

        /// <summary>정리: 이벤트 연결을 끊고 날아가던 몸의 이동 갱신을 되돌린다 (잡몹은 그대로)</summary>
        public void Release()
        {
            foreach (var pair in deathHandlers)
                if (pair.Key != null) pair.Key.Died -= pair.Value;
            deathHandlers.Clear();
            alive.Clear();
            pendingKills.Clear();
            scheduled.Clear();
            speeds.Clear();
            cohort.Clear();
            adopted.Clear();
            pendingStuns.Clear();
            foreach (var body in flung)
                if (body.Agent != null) body.Agent.updatePosition = true;
            flung.Clear();
        }

        private void Kill(MonsterController monster, KillStyle style, Vector3 origin)
        {
            if (monster == null || monster.IsAlive == false) return;
            if (deathHandlers.ContainsKey(monster) == false) return; // 이미 죽어 풀로 돌아간 뒤 재사용된 개체
            Vector3 position = monster.transform.position;
            monster.StateMachine.Die(false);
            SpawnVfx(deathVfx, position);

            if (style == KillStyle.Collapse) collapsed.Add(position);
            else Launch(monster, origin);
        }

        /// <summary>시체를 진원 반대쪽으로 날린다. Agent 가 위치를 덮어쓰지 않게 위치 갱신만 끈다</summary>
        private void Launch(MonsterController monster, Vector3 origin)
        {
            var agent = monster.GetComponent<NavMeshAgent>();
            agent.updatePosition = false;

            Vector3 away = Flat(monster.transform.position - origin);
            away = away.sqrMagnitude > 0.01f ? away.normalized : Flat(UnityEngine.Random.onUnitSphere).normalized;
            var s = flingSettings;
            flung.Add(new Flung
            {
                Monster = monster,
                Agent = agent,
                Velocity = away * UnityEngine.Random.Range(s.HorizontalSpeed * 0.7f, s.HorizontalSpeed)
                           + Vector3.up * UnityEngine.Random.Range(s.UpSpeed * 0.7f, s.UpSpeed),
                SpinAxis = UnityEngine.Random.onUnitSphere,
                SpinSpeed = UnityEngine.Random.Range(s.SpinSpeed * 0.5f, s.SpinSpeed),
                GroundY = monster.transform.position.y,
            });
        }

        private void TickFlung()
        {
            float dt = Time.deltaTime;
            for (int i = flung.Count - 1; i >= 0; i--)
            {
                var body = flung[i];
                // 풀로 돌아가 꺼졌으면 위치 갱신을 되돌린다 (다시 켜질 때 Agent 가 새 위치에서 시작)
                if (body.Monster == null || body.Monster.gameObject.activeInHierarchy == false)
                {
                    if (body.Agent != null) body.Agent.updatePosition = true;
                    flung.RemoveAt(i);
                    continue;
                }
                if (body.Landed) continue;

                var t = body.Monster.transform;
                body.Velocity += Vector3.down * flingSettings.Gravity * dt;
                Vector3 position = t.position + body.Velocity * dt;
                t.Rotate(body.SpinAxis, body.SpinSpeed * dt, Space.World);
                if (position.y <= body.GroundY && body.Velocity.y < 0f)
                {
                    position.y = body.GroundY;
                    body.Landed = true;
                }
                t.position = position;
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static void SpawnVfx(GameObject prefab, Vector3 position)
        {
            var vfx = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            UnityEngine.Object.Destroy(vfx, 4f);
        }
    }
}
