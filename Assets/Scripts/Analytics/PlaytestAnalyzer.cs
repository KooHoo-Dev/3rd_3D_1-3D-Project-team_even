using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Analytics
{
    /// <summary>
    /// 플레이 테스트 분석기 (씬에 1개). EventBus 를 구독해 기록만 하고 게임 동작은 건드리지 않는다.
    ///  - 군중전 30초당 받은 피해 → 난이도 판정 (기준값은 인스펙터, 플레이 테스트하며 조정)
    ///  - 피해 출처(몬스터·공격별), 필드 압박(생존 몬스터 수·스폰 대비 처치), 스테이지별 요약
    ///  - 몬스터 종류별 처치 시간, 위기 지표(최저 HP·쓰러짐·회복), 보스 진입 상태, 사격 명중률
    ///  - 몬스터 공격 시도·실행·명중 (피해가 적을 때: 빗나가는지 vs 접근을 못 하는지), 플레이어 반경 안 몬스터 수·최근접 거리
    /// 화면 표시는 PlaytestAnalyzer.Hud, 파일 저장은 PlaytestAnalyzer.Report.
    /// 시간은 라운드 시간(Time.deltaTime 누적)이라 강화 화면(timeScale 0) 동안은 멈춘다
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // 라운드 시계를 웨이브 진행(MonsterWaveDirector)보다 먼저 올려 스폰이 같은 스테이지로 잡히게
    public sealed partial class PlaytestAnalyzer : MonoBehaviour
    {
        [Header("난이도 기준 (플레이 테스트하며 조정)")]
        [Tooltip("판정 구간 길이(초)")]
        [SerializeField, Min(5f)] private float windowSeconds = 30f;
        [Tooltip("구간 동안 받은 피해가 이 값 이상이면 '중'")]
        [SerializeField, Min(1)] private int mediumDamage = 50;
        [Tooltip("구간 동안 받은 피해가 이 값 이상이면 '상'")]
        [SerializeField, Min(1)] private int hardDamage = 80;
        [Tooltip("보스 예고로 잘린 마지막 구간이 이보다 짧으면 판정하지 않는다")]
        [SerializeField, Min(1f)] private float minJudgeSeconds = 10f;

        [Header("근접 압박")]
        [Tooltip("플레이어로부터 이 수평 거리(m) 안의 몬스터를 '근접'으로 센다 (근접 공격 사거리 1.5~1.8m + 접근 여유)")]
        [SerializeField, Min(0.5f)] private float nearRadius = 3f;

        // ── 진행 ──
        private readonly List<RoundRecord> rounds = new List<RoundRecord>();
        private RoundRecord current;
        private GameState phase = GameState.Ready;
        private bool roundActive;
        private float roundTime;
        private float sessionStart;
        private List<WaveStageDef> stageDefs;

        // ── 최근 windowSeconds 초 피해 ──
        private readonly Queue<(float time, int amount)> window = new Queue<(float, int)>();
        private int windowDamage;

        // ── 몬스터 ──
        private readonly Dictionary<GameObject, AliveMonster> alive = new Dictionary<GameObject, AliveMonster>();
        private readonly HashSet<GameObject> killedByPlayer = new HashSet<GameObject>();
        // 풀이 개체를 재사용하므로 컨트롤러마다 한 번만 구독하고 비활성화 때 푼다
        private readonly Dictionary<MonsterController, Action<MonsterAttackDef, SkillPhase>> attackHandlers =
            new Dictionary<MonsterController, Action<MonsterAttackDef, SkillPhase>>();
        private readonly HashSet<MonsterController> inWindup = new HashSet<MonsterController>();
        private int nearCount;
        private float nearestDistance = -1f;

        // ── 플레이어 ──
        private PlayerController player;
        private PlayerItems items;
        private float nextPlayerSearch;
        private int hp = -1, maxHp, lives, maxLives, grenades = -1, healPacks = -1;
        private int grenadeId;
        private bool sawInvincible;

        // ── 1초 단위 기록 ──
        private readonly List<TimelineRow> timeline = new List<TimelineRow>();
        private readonly List<EventRow> events = new List<EventRow>();
        private int lastSampleSecond;
        private int damageThisSecond;

        private void Awake()
        {
            // unscaledTime 은 플레이 모드를 끌 때(OnDisable) 0 으로 읽혀서 실시간 시계를 쓴다
            sessionStart = Time.realtimeSinceStartup;
            stageDefs = new List<WaveStageDef>();
            // RunDirector 는 진행 횟수와 관계없이 1라운드 데이터를 재사용한다
            foreach (WaveStageDef stage in DataManager.WaveStages.All)
                if (stage.RoundNo == 1) stageDefs.Add(stage);
            stageDefs.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            grenadeId = DataManager.Player?.Grenade != null ? DataManager.Player.Grenade.Id : 0;
            InitReport();
        }

        private void OnEnable()
        {
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<GameOverEvent>.Subscribe(OnGameOver);
            EventBus<BossDefeatedEvent>.Subscribe(OnBossDefeated);
            EventBus<PlayerHpChangedEvent>.Subscribe(OnPlayerHpChanged);
            EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Subscribe(OnPlayerRevived);
            EventBus<PlayerFinalDeathEvent>.Subscribe(OnPlayerFinalDeath);
            EventBus<PlayerLivesChangedEvent>.Subscribe(OnPlayerLivesChanged);
            EventBus<HealPackCountChangedEvent>.Subscribe(OnHealPackCountChanged);
            EventBus<GrenadeCountChangedEvent>.Subscribe(OnGrenadeCountChanged);
            EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);
            EventBus<WeaponFiredEvent>.Subscribe(OnWeaponFired);
            EventBus<DamageDealtEvent>.Subscribe(OnDamageDealt);
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);
        }

        private void OnDisable()
        {
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<GameOverEvent>.Unsubscribe(OnGameOver);
            EventBus<BossDefeatedEvent>.Unsubscribe(OnBossDefeated);
            EventBus<PlayerHpChangedEvent>.Unsubscribe(OnPlayerHpChanged);
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Unsubscribe(OnPlayerRevived);
            EventBus<PlayerFinalDeathEvent>.Unsubscribe(OnPlayerFinalDeath);
            EventBus<PlayerLivesChangedEvent>.Unsubscribe(OnPlayerLivesChanged);
            EventBus<HealPackCountChangedEvent>.Unsubscribe(OnHealPackCountChanged);
            EventBus<GrenadeCountChangedEvent>.Unsubscribe(OnGrenadeCountChanged);
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
            EventBus<WeaponFiredEvent>.Unsubscribe(OnWeaponFired);
            EventBus<DamageDealtEvent>.Unsubscribe(OnDamageDealt);
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
            foreach (KeyValuePair<MonsterController, Action<MonsterAttackDef, SkillPhase>> pair in attackHandlers)
                if (pair.Key != null) pair.Key.AttackPhaseChanged -= pair.Value;
            attackHandlers.Clear();
            inWindup.Clear();
            SaveReport("비활성화");
        }

        private void Update()
        {
            FindPlayer();
            if (player != null && player.DebugInvincible) sawInvincible = true;

            if (roundActive && current != null)
            {
                roundTime += Time.deltaTime;
                current.EndTime = roundTime;
                TrimWindow();
                UpdateStage();
                UpdateNear(Time.deltaTime);
                SampleTimeline();
            }

            UpdateHudInput();
            UpdateAutoSave();
        }

        #region 라운드 흐름
        private void OnRoundStarted(RoundStartedEvent e)
        {
            current = new RoundRecord { Round = e.Round };
            rounds.Add(current);
            roundActive = true;
            roundTime = 0f;
            window.Clear();
            windowDamage = 0;
            lastSampleSecond = 0;
            damageThisSecond = 0;
            inWindup.Clear();
            phase = GameState.CrowdBattle; // 시퀀서가 이 직후 군중전으로 바꾼다 (기록의 상태 칸이 이전 상태로 남지 않게)
            AddEvent("라운드 시작", $"라운드 {e.Round}", e.Round);
            UpdateStage(); // 0초 스테이지는 이 직후 같은 프레임에 소환된다
        }

        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            phase = e.Current;
            if (current == null) return;

            switch (e.Current)
            {
                case GameState.BossIntro:
                    if (current.InCrowd) EndCrowd();
                    break;
                case GameState.BossBattle:
                    current.BossStartTime = roundTime;
                    break;
                case GameState.Reward:
                    current.Cleared = true;
                    roundActive = false;
                    SaveReport("라운드 클리어");
                    break;
                case GameState.GameOver:
                    // 바로 뒤 RoundCleanupEvent 가 생존 목록을 비우기 전에 스테이지를 닫는다
                    current.GameOver = true;
                    if (current.InCrowd) CloseStage(current.CurrentStage);
                    roundActive = false;
                    break;
                case GameState.Ready:
                    roundActive = false; // 재시작
                    break;
            }
            AddEvent("상태", PhaseName(e.Current), 0);
        }

        /// <summary>보스 예고 = 군중전 끝. 스테이지를 닫고 보스 진입 상태를 남긴다</summary>
        private void EndCrowd()
        {
            CloseStage(current.CurrentStage);
            current.CrowdEndTime = roundTime;

            var snapshot = new BossEntrySnapshot
            {
                Time = roundTime, Hp = hp, MaxHp = maxHp, Lives = lives, MaxLives = maxLives,
                Grenades = Mathf.Max(0, grenades), HealPacks = Mathf.Max(0, healPacks), Alive = alive.Count,
            };
            foreach (AliveMonster monster in alive.Values)
            {
                snapshot.AliveByName.TryGetValue(monster.Def.Name, out int count);
                snapshot.AliveByName[monster.Def.Name] = count + 1;
            }
            current.BossEntry = snapshot;
            AddEvent("보스 진입", $"남은 몬스터 {alive.Count}", alive.Count);
        }

        /// <summary>라운드 정리: 몬스터는 처치 이벤트 없이 회수되므로 생존 목록만 비운다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            alive.Clear();
            killedByPlayer.Clear();
        }

        private void OnGameOver(GameOverEvent e)
        {
            AddEvent("게임오버", $"도달 라운드 {e.ReachedRound}", e.ReachedRound);
            SaveReport("게임오버");
        }

        private void OnBossDefeated(BossDefeatedEvent e)
        {
            if (current == null) return;
            current.BossDefeatedTime = roundTime;
            AddEvent("보스 처치", e.Boss != null ? e.Boss.Name : "", 0);
        }

        /// <summary>라운드 시간이 지난 스테이지를 연다. 이전 스테이지는 이 시각에 닫는다</summary>
        private void UpdateStage()
        {
            if (current == null || current.InCrowd == false) return;
            while (current.Stages.Count < stageDefs.Count && stageDefs[current.Stages.Count].StartTime <= roundTime)
            {
                CloseStage(current.CurrentStage);
                WaveStageDef def = stageDefs[current.Stages.Count];
                current.Stages.Add(new StageRecord { Id = def.Id, StartTime = roundTime });
                AddEvent("스테이지", $"스테이지 {def.Id}", def.Id);
            }
        }

        private void CloseStage(StageRecord stage)
        {
            if (stage == null || stage.EndTime >= 0f) return;
            stage.EndTime = roundTime;
            stage.AliveAtEnd = alive.Count;
        }
        #endregion

        #region 받은 피해
        private void OnPlayerDamaged(PlayerDamagedEvent e)
        {
            if (current == null || roundActive == false) return;

            int amount = e.Info.Amount;
            string source = SourceName(e.Info);
            window.Enqueue((roundTime, amount));
            windowDamage += amount;
            damageThisSecond += amount;

            if (current.InCrowd)
            {
                current.CrowdDamage += amount;
                int bucket = Mathf.FloorToInt(roundTime / windowSeconds);
                while (current.CrowdBuckets.Count <= bucket) current.CrowdBuckets.Add(0);
                current.CrowdBuckets[bucket] += amount;
                if (windowDamage > current.MaxWindowDamage)
                {
                    current.MaxWindowDamage = windowDamage;
                    current.MaxWindowAt = roundTime;
                }
                if (current.CurrentStage != null) current.CurrentStage.Damage += amount;
                AddSource(current.CrowdSources, source, amount);
            }
            else
            {
                current.BossDamage += amount;
                AddSource(current.BossSources, source, amount);
            }

            if (e.Info.SourceDefId != 0 && DataManager.MonsterAttacks.TryGet(e.Info.SourceDefId, out MonsterAttackDef attack))
            {
                AttackStat stat = Attack(attack);
                if (e.Info.Kind == DamageKind.Status) stat.StatusTicks++;
                else stat.Hits++;
                stat.Damage += amount;
            }
            AddEvent("피격", source, amount);
        }

        private void TrimWindow()
        {
            float from = roundTime - windowSeconds;
            while (window.Count > 0 && window.Peek().time <= from) windowDamage -= window.Dequeue().amount;
        }

        private static void AddSource(Dictionary<string, SourceStat> sources, string source, int amount)
        {
            if (sources.TryGetValue(source, out SourceStat stat) == false) sources[source] = stat = new SourceStat();
            stat.Damage += amount;
            stat.Hits++;
        }

        /// <summary>몬스터 공격은 SourceDefId(MonsterAttackTable.Id) → "몬스터 · 공격". 보스 패턴은 Id 가 없어 "보스"</summary>
        private static string SourceName(in DamageInfo info)
        {
            if (info.SourceDefId != 0 && DataManager.MonsterAttacks.TryGet(info.SourceDefId, out MonsterAttackDef attack))
            {
                string monster = attack.Owner != null ? attack.Owner.Name : attack.MonsterId.ToString();
                return info.Kind == DamageKind.Status ? $"{monster} · {attack.Name}(지속)" : $"{monster} · {attack.Name}";
            }
            if (info.Kind == DamageKind.MonsterSkill) return "보스";
            return $"기타 · {info.Kind}";
        }
        #endregion

        #region 플레이어
        /// <summary>플레이어는 프리팹으로 늦게 생길 수 있어 1초마다 다시 찾는다</summary>
        private void FindPlayer()
        {
            if (player != null || Time.unscaledTime < nextPlayerSearch) return;
            nextPlayerSearch = Time.unscaledTime + 1f;
            player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;
            items = player.GetComponent<PlayerItems>();
            if (hp < 0)
            {
                hp = player.CurrentHealth;
                maxHp = player.MaxHealth;
            }
            lives = player.Lives;
            maxLives = player.MaxLives;
            if (items != null)
            {
                if (grenades < 0) grenades = items.Grenades;
                if (healPacks < 0) healPacks = items.HealPacks;
            }
        }

        private void OnPlayerHpChanged(PlayerHpChangedEvent e)
        {
            // 회복 팩 효과 중 오른 체력만 회복량으로 센다 (부활·재시작으로 차는 체력 제외)
            if (current != null && roundActive && hp >= 0 && e.Current > hp && items != null && items.IsHealing)
                current.HealedHp += e.Current - hp;
            hp = e.Current;
            maxHp = e.Max;
            if (current != null && roundActive && e.Current < current.MinHp)
            {
                current.MinHp = e.Current;
                current.MinHpAt = roundTime;
            }
        }

        private void OnPlayerDied(PlayerDiedEvent e)
        {
            if (current == null) return;
            current.DownTimes.Add(roundTime);
            AddEvent("쓰러짐", $"남은 목숨 {lives}", lives);
        }

        private void OnPlayerRevived(PlayerRevivedEvent e) => AddEvent("부활", "", 0);

        private void OnPlayerFinalDeath(PlayerFinalDeathEvent e) => AddEvent("최종 사망", "", 0);

        private void OnPlayerLivesChanged(PlayerLivesChangedEvent e)
        {
            lives = e.Current;
            maxLives = e.Max;
        }

        private void OnHealPackCountChanged(HealPackCountChangedEvent e)
        {
            if (current != null && roundActive && healPacks >= 0 && e.Current < healPacks)
            {
                current.HealPacksUsed += healPacks - e.Current;
                AddEvent("회복 팩 사용", $"남은 {e.Current}", healPacks - e.Current);
            }
            healPacks = e.Current;
        }

        private void OnGrenadeCountChanged(GrenadeCountChangedEvent e)
        {
            if (current != null && roundActive && grenades >= 0 && e.Current < grenades)
            {
                current.GrenadesThrown += grenades - e.Current;
                AddEvent("수류탄 투척", $"남은 {e.Current}", grenades - e.Current);
            }
            grenades = e.Current;
        }

        private void OnSupplyCollected(SupplyCollectedEvent e)
        {
            if (current == null || e.Def == null) return;
            string name = SupplyName(e.Def);
            current.Supplies.TryGetValue(name, out int count);
            current.Supplies[name] = count + 1;
            AddEvent("보급 획득", name, e.Def.Amount);
        }

        private static string SupplyName(SupplyDef def)
        {
            switch (def.SupplyType.ToString())
            {
                case "Heal": return "회복";
                case "Grenade": return "수류탄";
                default: return def.SupplyType.ToString();
            }
        }
        #endregion

        #region 사격
        private void OnWeaponFired(WeaponFiredEvent e)
        {
            if (current == null || roundActive == false || e.Weapon == null) return;
            Weapon(e.Weapon.Id, e.Weapon.Name).Shots++;
        }

        /// <summary>플레이어가 준 피해: 무기 명중·처치, 처치한 몬스터 표시 (MonsterDiedEvent 가 바로 뒤따른다)</summary>
        private void OnDamageDealt(DamageDealtEvent e)
        {
            if (e.Info.AttackerFaction != Faction.Player) return;
            if (e.IsKill && e.Target != null) killedByPlayer.Add(e.Target);
            if (current == null || roundActive == false) return;

            int id = e.Info.SourceDefId;
            if (id != 0 && id == grenadeId)
            {
                if (e.IsKill) current.GrenadeKills++;
                return;
            }

            WeaponStat stat = Weapon(id, DataManager.Weapons.TryGet(id, out WeaponDef def) ? def.Name : $"기타({id})");
            stat.Damage += e.Info.Amount;
            if (e.IsKill) stat.Kills++;
            if (stat.LastHitFrame != Time.frameCount)
            {
                stat.LastHitFrame = Time.frameCount;
                stat.HitShots++;
            }
        }

        private WeaponStat Weapon(int id, string name)
        {
            if (current.Weapons.TryGetValue(id, out WeaponStat stat) == false)
                current.Weapons[id] = stat = new WeaponStat { Name = name };
            return stat;
        }
        #endregion

        #region 몬스터
        private void OnMonsterSpawned(MonsterSpawnedEvent e)
        {
            if (current == null || roundActive == false || e.Def == null || e.Def.IsBoss || e.Instance == null) return;
            UpdateStage();

            alive[e.Instance] = new AliveMonster(e.Def, roundTime);
            WatchAttacks(e.Instance);
            current.Spawned++;
            if (current.CurrentStage != null) current.CurrentStage.Spawned++;
            Monster(e.Def).Spawned++;
            if (alive.Count > current.PeakAlive)
            {
                current.PeakAlive = alive.Count;
                current.PeakAliveAt = roundTime;
            }
            AddEvent("스폰", e.Def.Name, alive.Count);
        }

        private void OnMonsterDied(MonsterDiedEvent e)
        {
            if (e.Def == null || e.Def.IsBoss) return;
            bool byPlayer = e.Instance != null && killedByPlayer.Remove(e.Instance);
            AliveMonster spawned = default;
            bool tracked = e.Instance != null && alive.TryGetValue(e.Instance, out spawned);
            float lifetime = tracked ? roundTime - spawned.SpawnTime : 0f;
            if (tracked) alive.Remove(e.Instance);
            if (current == null || roundActive == false) return;

            MonsterStat stat = Monster(e.Def);
            if (byPlayer)
            {
                current.PlayerKills++;
                if (current.CurrentStage != null) current.CurrentStage.Kills++;
                stat.Kills++;
                if (tracked)
                {
                    stat.LifetimeSum += lifetime;
                    stat.LifetimeCount++;
                    stat.LifetimeMax = Mathf.Max(stat.LifetimeMax, lifetime);
                }
                if (player != null)
                {
                    Vector3 offset = e.Position - player.transform.position;
                    stat.AddKillDistance(new Vector2(offset.x, offset.z).magnitude);
                }
                AddEvent("처치", e.Def.Name, alive.Count);
            }
            else
            {
                // 보스 등장 공격 정리 등
                current.OtherDeaths++;
                stat.OtherDeaths++;
                AddEvent("기타 사망", e.Def.Name, alive.Count);
            }
        }

        private MonsterStat Monster(MonsterDef def)
        {
            if (current.Monsters.TryGetValue(def.Id, out MonsterStat stat) == false)
                current.Monsters[def.Id] = stat = new MonsterStat { Name = def.Name };
            return stat;
        }
        #endregion

        #region 몬스터 공격 · 근접
        /// <summary>몬스터 컨트롤러의 공격 단계 알림을 구독한다 (개체당 한 번)</summary>
        private void WatchAttacks(GameObject instance)
        {
            var controller = instance.GetComponent<MonsterController>();
            if (controller == null || attackHandlers.ContainsKey(controller)) return;
            Action<MonsterAttackDef, SkillPhase> handler = (def, attackPhase) => OnAttackPhase(controller, def, attackPhase);
            controller.AttackPhaseChanged += handler;
            attackHandlers[controller] = handler;
        }

        /// <summary>Windup = 시도. 그 뒤 Active 면 실행, Active 없이 Idle 이면 취소 (예고 중 사망·경직)</summary>
        private void OnAttackPhase(MonsterController controller, MonsterAttackDef def, SkillPhase attackPhase)
        {
            if (def == null || current == null || roundActive == false)
            {
                inWindup.Remove(controller);
                return;
            }

            switch (attackPhase)
            {
                case SkillPhase.Windup:
                    Attack(def).Attempts++;
                    inWindup.Add(controller);
                    break;
                case SkillPhase.Active:
                    if (inWindup.Remove(controller)) Attack(def).Executed++;
                    break;
                case SkillPhase.Idle:
                    if (inWindup.Remove(controller)) Attack(def).Cancelled++;
                    break;
            }
        }

        private AttackStat Attack(MonsterAttackDef def)
        {
            if (current.Attacks.TryGetValue(def.Id, out AttackStat stat) == false)
            {
                string monster = def.Owner != null ? def.Owner.Name : def.MonsterId.ToString();
                current.Attacks[def.Id] = stat = new AttackStat { Name = $"{monster} · {def.Name}" };
            }
            return stat;
        }

        /// <summary>
        /// 플레이어 수평 반경 안 몬스터 수와 최근접 거리를 매 프레임 잰다. 군중전 동안 시간 가중으로 쌓는다.
        /// 종류별로도 근접 수를 쌓고, 개체가 처음 반경에 들어오면 종류별 도달 수·도달 시간을 센다
        /// </summary>
        private void UpdateNear(float dt)
        {
            nearCount = 0;
            nearestDistance = -1f;
            if (player == null) return;

            foreach (MonsterStat stat in current.Monsters.Values) stat.NearNow = 0;
            Vector3 center = player.transform.position;
            float nearSqr = nearRadius * nearRadius;
            float nearestSqr = float.MaxValue;
            foreach (KeyValuePair<GameObject, AliveMonster> pair in alive)
            {
                GameObject monster = pair.Key;
                if (monster == null || monster.activeInHierarchy == false) continue;
                Vector3 offset = monster.transform.position - center;
                float sqr = offset.x * offset.x + offset.z * offset.z; // 공중 몬스터도 머리 위에 있으면 근접
                if (sqr < nearestSqr) nearestSqr = sqr;
                if (sqr > nearSqr) continue;

                nearCount++;
                MonsterStat stat = Monster(pair.Value.Def);
                stat.NearNow++;
                if (pair.Value.ReachedNear == false)
                {
                    pair.Value.ReachedNear = true;
                    stat.Reached++;
                    stat.ReachTimeSum += roundTime - pair.Value.SpawnTime;
                }
            }
            if (nearestSqr < float.MaxValue) nearestDistance = Mathf.Sqrt(nearestSqr);

            if (current.InCrowd == false || dt <= 0f) return;
            current.Near.Add(dt, nearCount, nearestDistance);
            current.CurrentStage?.Near.Add(dt, nearCount, nearestDistance);
            foreach (MonsterStat stat in current.Monsters.Values) stat.Near.Add(dt, stat.NearNow, -1f);
            if (nearCount > current.MaxNear)
            {
                current.MaxNear = nearCount;
                current.MaxNearAt = roundTime;
            }
        }
        #endregion

        #region 기록
        /// <summary>라운드 시간 1초마다 timeline 한 줄. 받은 피해는 (s-1, s] 구간 몫</summary>
        private void SampleTimeline()
        {
            int second = Mathf.FloorToInt(roundTime);
            while (lastSampleSecond < second)
            {
                lastSampleSecond++;
                StageRecord stage = current.CurrentStage;
                timeline.Add(new TimelineRow(current.Round, lastSampleSecond, PhaseName(phase), stage != null ? stage.Id : 0,
                                             hp, alive.Count, nearCount, nearestDistance,
                                             current.Spawned, current.PlayerKills, damageThisSecond, windowDamage));
                damageThisSecond = 0;
            }
        }

        private void AddEvent(string type, string detail, int value)
        {
            events.Add(new EventRow(Time.realtimeSinceStartup - sessionStart, current != null ? current.Round : 0, roundTime,
                                    PhaseName(phase), type, detail, value, hp));
        }

        private static string PhaseName(GameState state)
        {
            switch (state)
            {
                case GameState.Ready: return "준비";
                case GameState.CrowdBattle: return "군중전";
                case GameState.BossIntro: return "보스 예고";
                case GameState.BossBattle: return "보스전";
                case GameState.Reward: return "보상";
                case GameState.GameOver: return "게임오버";
                case GameState.Paused: return "일시정지";
                default: return state.ToString();
            }
        }

        /// <summary>구간 피해 → 하/중/상</summary>
        private string Grade(float damagePerWindow)
        {
            if (damagePerWindow >= hardDamage) return "상";
            if (damagePerWindow >= mediumDamage) return "중";
            return "하";
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }
        #endregion
    }
}
