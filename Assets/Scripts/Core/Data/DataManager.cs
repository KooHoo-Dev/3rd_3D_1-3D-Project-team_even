using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data
{
    /// <summary>
    /// 모든 기획 데이터의 단일 진입점.
    ///
    /// 파이프라인:  TSV(StreamingAssets/Tables) ─Read→ DTO ─Build→ Def ─Group/Link→ 참조 연결 ─Validate
    ///
    /// 사용 규칙
    ///  - 각 파트는 DataManager.Monsters.Get(3001) 처럼 Def 만 받아 쓴다. DTO/TSV 를 직접 읽지 않는다.
    ///  - Def 는 불변. 런타임 상태(현재 HP, 남은 탄약, 강화 레벨)는 각 파트가 따로 가진다.
    ///  - 어느 씬에서 Play 하든 첫 접근 시 자동 로드된다. (각자 Test_ 씬에서 바로 작업 가능)
    /// </summary>
    public static class DataManager
    {
        public const string TableFolderName = "Tables";

        public static bool IsLoaded { get; private set; }
        public static DataLoadReport LastReport { get; private set; }

        /// <summary>로드(또는 리로드)가 끝날 때마다 호출</summary>
        public static event Action Loaded;

        private static DefTable<int, PlayerDef> players;
        private static DefTable<int, WeaponDef> weapons;
        private static DefTable<int, GrenadeDef> grenades;
        private static DefTable<int, MonsterDef> monsters;
        private static DefTable<int, MonsterAttackDef> monsterAttacks;
        private static DefTable<int, MonsterPartDef> monsterParts;
        private static DefTable<int, BossPhaseDef> bossPhases;
        private static DefTable<int, StatusEffectDef> statusEffects;
        private static DefTable<int, RoundDef> rounds;
        private static DefTable<int, CrowdSpawnRuleDef> crowdSpawnRules;
        private static DefTable<int, WaveStageDef> waveStages;
        private static DefTable<int, WaveDef> waves;
        private static DefTable<int, WaveSpawnerDef> waveSpawners;
        private static DefTable<int, SupplyDef> supplies;
        private static DefTable<int, UpgradeDef> upgrades;
        private static DefTable<UpgradeType, UpgradeTrackDef> upgradeTracks;
        private static DefTable<UpgradeGrade, UpgradeGradeDef> upgradeGrades;
        private static DefTable<string, SoundDef> sounds;
        private static GameConfig config;
        private static DefTable<int, BossDef> bosses;
        private static ClypeosaurusDef clypeosaurus;
        private static TyranopodeDef tyranopode;
        

        public static DefTable<int, PlayerDef> Players { get { EnsureLoaded(); return players; } }
        public static DefTable<int, WeaponDef> Weapons { get { EnsureLoaded(); return weapons; } }
        public static DefTable<int, GrenadeDef> Grenades { get { EnsureLoaded(); return grenades; } }
        public static DefTable<int, MonsterDef> Monsters { get { EnsureLoaded(); return monsters; } }
        public static DefTable<int, MonsterAttackDef> MonsterAttacks { get { EnsureLoaded(); return monsterAttacks; } }
        public static DefTable<int, MonsterPartDef> MonsterParts { get { EnsureLoaded(); return monsterParts; } }
        public static DefTable<int, BossPhaseDef> BossPhases { get { EnsureLoaded(); return bossPhases; } }
        public static DefTable<int, StatusEffectDef> StatusEffects { get { EnsureLoaded(); return statusEffects; } }
        /// <summary>Key = Round 번호. 마지막 행 이후 라운드는 GetRound 사용</summary>
        public static DefTable<int, RoundDef> Rounds { get { EnsureLoaded(); return rounds; } }
        public static DefTable<int, CrowdSpawnRuleDef> CrowdSpawnRules { get { EnsureLoaded(); return crowdSpawnRules; } }
        public static DefTable<int, WaveStageDef> WaveStages { get { EnsureLoaded(); return waveStages; } }
        public static DefTable<int, WaveDef> Waves { get { EnsureLoaded(); return waves; } }
        public static DefTable<int, WaveSpawnerDef> WaveSpawners { get { EnsureLoaded(); return waveSpawners; } }
        public static DefTable<int, SupplyDef> Supplies { get { EnsureLoaded(); return supplies; } }
        public static DefTable<int, UpgradeDef> Upgrades { get { EnsureLoaded(); return upgrades; } }
        /// <summary>Key = UpgradeType. 레벨별 강화 묶음</summary>
        public static DefTable<UpgradeType, UpgradeTrackDef> UpgradeTracks { get { EnsureLoaded(); return upgradeTracks; } }
        /// <summary>Key = UpgradeGrade. 강화 카드 등급 이름·색·가중치</summary>
        public static DefTable<UpgradeGrade, UpgradeGradeDef> UpgradeGrades { get { EnsureLoaded(); return upgradeGrades; } }
        public static DefTable<string, SoundDef> Sounds { get { EnsureLoaded(); return sounds; } }
        public static GameConfig Config { get { EnsureLoaded(); return config; } }

        public static DefTable<int, BossDef> Bosses { get { EnsureLoaded(); return bosses; } }
        public static ClypeosaurusDef Clypeosaurus { get { EnsureLoaded(); return clypeosaurus; } }
        public static TyranopodeDef Tyranopode { get { EnsureLoaded(); return tyranopode; } }
        
        /// <summary>플레이어 정의 (테이블 첫 행)</summary>
        public static PlayerDef Player => Players.All.Count > 0 ? Players.All[0] : null;

        /// <summary>n 라운드 정의. 테이블보다 큰 라운드는 마지막 행을 재사용 (무한 라운드 대응, BL-032)</summary>
        public static RoundDef GetRound(int round)
        {
            var all = Rounds.All;
            if (all.Count == 0) return null;
            if (rounds.TryGet(round, out var def)) return def;
            return round < all[0].Round ? all[0] : all[all.Count - 1];
        }

        public static string DefaultTableFolder => Path.Combine(Application.streamingAssetsPath, TableFolderName);

        // ───────────────────────── Lifecycle ─────────────────────────

        // Enter Play Mode Options(도메인 리로드 끔)에서도 static 이 초기화되도록
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsLoaded = false;
            LastReport = null;
            Loaded = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoad() => EnsureLoaded();

        public static void EnsureLoaded()
        {
            if (IsLoaded == false) Load(DefaultTableFolder);
        }

        /// <summary>테이블 폴더 전체를 다시 읽는다. (에디터 메뉴 / 테스트에서 호출)</summary>
        public static DataLoadReport Load(string folder)
        {
            var r = new DataLoadReport();

            // 1) Read + Build : TSV → DTO → Def
            players         = Build<PlayerDto, int, PlayerDef>(folder, PlayerDef.Table, r, d => d.Id, d => new PlayerDef(d, r));
            weapons         = Build<WeaponDto, int, WeaponDef>(folder, WeaponDef.Table, r, d => d.Id, d => new WeaponDef(d, r));
            grenades        = Build<GrenadeDto, int, GrenadeDef>(folder, GrenadeDef.Table, r, d => d.Id, d => new GrenadeDef(d, r));
            monsters        = Build<MonsterDto, int, MonsterDef>(folder, MonsterDef.Table, r, d => d.Id, d => new MonsterDef(d, r));
            monsterAttacks  = Build<MonsterAttackDto, int, MonsterAttackDef>(folder, MonsterAttackDef.Table, r, d => d.Id, d => new MonsterAttackDef(d, r));
            monsterParts    = Build<MonsterPartDto, int, MonsterPartDef>(folder, MonsterPartDef.Table, r, d => d.Id, d => new MonsterPartDef(d, r));
            // 구형 보스 페이즈 로딩은 중단한다. 팀원 코드의 공개 타입·접근자 호환을 위해 빈 테이블은 유지한다.
            // bossPhases = Build<BossPhaseDto, int, BossPhaseDef>(folder, BossPhaseDef.Table, r, d => d.Id, d => new BossPhaseDef(d, r));
            bossPhases = new DefTable<int, BossPhaseDef>(BossPhaseDef.Table);
            statusEffects   = Build<StatusEffectDto, int, StatusEffectDef>(folder, StatusEffectDef.Table, r, d => d.Id, d => new StatusEffectDef(d, r));
            rounds          = Build<RoundDto, int, RoundDef>(folder, RoundDef.Table, r, d => d.Round, d => new RoundDef(d, r));
            crowdSpawnRules = Build<CrowdSpawnDto, int, CrowdSpawnRuleDef>(folder, CrowdSpawnRuleDef.Table, r, d => d.Id, d => new CrowdSpawnRuleDef(d, r));
            waveStages      = Build<WaveStageDto, int, WaveStageDef>(folder, WaveStageDef.Table, r, d => d.Id, d => new WaveStageDef(d, r));
            waves           = Build<WaveDto, int, WaveDef>(folder, WaveDef.Table, r, d => d.Id, d => new WaveDef(d, r));
            waveSpawners    = Build<WaveSpawnerDto, int, WaveSpawnerDef>(folder, WaveSpawnerDef.Table, r, d => d.Id, d => new WaveSpawnerDef(d, r));
            supplies        = Build<SupplyDto, int, SupplyDef>(folder, SupplyDef.Table, r, d => d.Id, d => new SupplyDef(d, r));
            upgrades        = Build<UpgradeDto, int, UpgradeDef>(folder, UpgradeDef.Table, r, d => d.Id, d => new UpgradeDef(d, r));
            upgradeGrades   = BuildUpgradeGrades(folder, r);
            sounds         = Build<SoundDto, string, SoundDef>(folder, SoundDef.Table, r, d => d.Key, d => new SoundDef(d, r));
            config = new GameConfig();
            foreach (var dto in TsvTableReader.Read<ConfigDto>(folder, GameConfig.Table, r)) config.Add(dto, r);
            bosses          = Build<BossDto, int, BossDef>(folder, BossDef.Table, r, dto => dto.Id, dto => new BossDef(dto, r));
            clypeosaurus    = ReadSingle<ClypeosaurusDto, ClypeosaurusDef>(folder, ClypeosaurusDef.Table, r, dto => new ClypeosaurusDef(dto, r));
            tyranopode      = ReadSingle<TyranopodeDto, TyranopodeDef>(folder, TyranopodeDef.Table, r, dto => new TyranopodeDef(dto, r));
            
            // 2) Link + Group : Id 참조 → Def 참조, 여러 행 → 상위 Def
            LinkSounds(r);
            LinkPlayer(r);
            LinkMonsters(r);
            LinkRounds(r);
            LinkWaves(r);
            upgradeTracks = GroupUpgrades(r);

            // 3) Validate : 테이블 간 규칙
            Validate(r);

            LastReport = r;
            IsLoaded = true; // 오류가 있어도 true. (매 접근마다 재로드되며 로그가 폭주하는 것 방지)

            if (r.HasError) Debug.LogError(r.BuildSummary());
            else if (r.Warnings.Count > 0) Debug.LogWarning(r.BuildSummary());
            else Debug.Log($"[DataManager] 테이블 로드 완료 (Monster {monsters.Count}, Round {rounds.Count}, Spawn {crowdSpawnRules.Count}, Upgrade {upgrades.Count})");

            Loaded?.Invoke();
            return r;
        }

        private static DefTable<TKey, TDef> Build<TDto, TKey, TDef>(
            string folder, string tableName, DataLoadReport r, Func<TDto, TKey> keyOf, Func<TDto, TDef> create)
            where TDef : class
        {
            var table = new DefTable<TKey, TDef>(tableName);
            foreach (var dto in TsvTableReader.Read<TDto>(folder, tableName, r))
                table.Add(keyOf(dto), create(dto), r);
            return table;
        }
        
        // 데이터가 한 행뿐인 테이블을 읽어오기 위한 메서드 (이찬영 추가) 
        private static TDef ReadSingle<TDto, TDef>(
            string folder, string tableName, DataLoadReport report, Func<TDto, TDef> create)
            where TDef : class
        {
            int errorCount = report.Errors.Count;

            var rows = TsvTableReader.Read<TDto>(folder, tableName, report);

            if (!report.Check(rows.Count == 1, tableName, "-", $"데이터는 정확히 1행이어야 합니다. 현재 {rows.Count}행입니다."))
            {
                return null;
            }

            var def = create(rows[0]);

            // 해당 데이터를 읽거나 검증하는 중 오류가 발생했다면 사용하지 않는다.
            return report.Errors.Count == errorCount ? def : null;
        }

        // ───────────────────────── Link / Group ─────────────────────────

        private static void LinkSounds(DataLoadReport r)
        {
            foreach (var w in weapons)
            {
                w.FireSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.FireSfx), w.FireSfxKey);
                w.ReloadSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.ReloadSfx), w.ReloadSfxKey);
                w.ReloadInsertSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.ReloadInsertSfx), w.ReloadInsertSfxKey);
                w.ReloadEndSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.ReloadEndSfx), w.ReloadEndSfxKey);
                w.SwitchSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.SwitchSfx), w.SwitchSfxKey);
                w.DryFireSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.DryFireSfx), w.DryFireSfxKey);
                w.ExplodeSfx = FindSound(r, WeaponDef.Table, w.Id, nameof(w.ExplodeSfx), w.ExplodeSfxKey);
            }
            foreach (var g in grenades)
                g.ExplodeSfx = FindSound(r, GrenadeDef.Table, g.Id, nameof(g.ExplodeSfx), g.ExplodeSfxKey);
            foreach (var m in monsters)
            {
                m.IdleSfx = FindSound(r, MonsterDef.Table, m.Id, nameof(m.IdleSfx), m.IdleSfxKey);
                m.LoopSfx = FindSound(r, MonsterDef.Table, m.Id, nameof(m.LoopSfx), m.LoopSfxKey);
                m.DeathSfx = FindSound(r, MonsterDef.Table, m.Id, nameof(m.DeathSfx), m.DeathSfxKey);
                m.StunSfx = FindSound(r, MonsterDef.Table, m.Id, nameof(m.StunSfx), m.StunSfxKey);
                m.HiddenSfx = FindSound(r, MonsterDef.Table, m.Id, nameof(m.HiddenSfx), m.HiddenSfxKey);
                if (m.LoopSfx != null && m.LoopSfx.Loop == false)
                    r.Warn(MonsterDef.Table, m.Id, $"LoopSfx '{m.LoopSfxKey}' 의 Loop 가 false 입니다");
            }
            foreach (var a in monsterAttacks)
            {
                a.Sfx = FindSound(r, MonsterAttackDef.Table, a.Id, nameof(a.Sfx), a.SfxKey);
                a.ImpactSfx = FindSound(r, MonsterAttackDef.Table, a.Id, nameof(a.ImpactSfx), a.ImpactSfxKey);
            }
            foreach (var e in statusEffects)
            {
                e.LoopSfx = FindSound(r, StatusEffectDef.Table, e.Id, nameof(e.LoopSfx), e.LoopSfxKey);
                if (e.LoopSfx != null && e.LoopSfx.Loop == false)
                    r.Warn(StatusEffectDef.Table, e.Id, $"LoopSfx '{e.LoopSfxKey}' 의 Loop 가 false 입니다");
            }
            foreach (var s in supplies)
            {
                s.DropSfx = FindSound(r, SupplyDef.Table, s.Id, nameof(s.DropSfx), s.DropSfxKey);
                s.AnnounceSfx = FindSound(r, SupplyDef.Table, s.Id, nameof(s.AnnounceSfx), s.AnnounceSfxKey);
                s.CollectSfx = FindSound(r, SupplyDef.Table, s.Id, nameof(s.CollectSfx), s.CollectSfxKey);
                if (s.IsWeapon)
                    r.Check(weapons.TryGet(s.WeaponId, out var w) && w.Category == WeaponCategory.Supply, SupplyDef.Table, s.Id, $"WeaponId {s.WeaponId} 는 WeaponTable 에 있고 Category = Supply 여야 합니다");
            }
        }

        /// <summary>키가 비었으면 null (사운드 없음). 키가 있는데 SoundTable 에 없으면 경고 후 null</summary>
        private static SoundDef FindSound(DataLoadReport r, string table, object id, string column, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (sounds.TryGet(key, out var sound)) return sound;
            r.Warn(table, id, $"{column} 키 '{key}' 가 SoundTable 에 없습니다");
            return null;
        }

        private static void LinkPlayer(DataLoadReport r)
        {
            foreach (var p in players)
            {
                if (weapons.TryGet(p.RifleId, out var w)) p.Rifle = w;
                else r.Error(PlayerDef.Table, p.Id, $"RifleId {p.RifleId} 가 WeaponTable 에 없습니다");

                if (grenades.TryGet(p.GrenadeId, out var g)) p.Grenade = g;
                else r.Error(PlayerDef.Table, p.Id, $"GrenadeId {p.GrenadeId} 가 GrenadeTable 에 없습니다");
            }
        }

        private static void LinkMonsters(DataLoadReport r)
        {
            foreach (var m in monsters)
            {
                CheckGradeRole(m, r);
            }

            foreach (var a in monsterAttacks)
            {
                if (monsters.TryGet(a.MonsterId, out var m)) a.Owner = m;
                else r.Error(MonsterAttackDef.Table, a.Id, $"MonsterId {a.MonsterId} 가 MonsterTable 에 없습니다");

                if (a.EffectId != 0)
                {
                    if (statusEffects.TryGet(a.EffectId, out var e)) a.Effect = e;
                    else r.Error(MonsterAttackDef.Table, a.Id, $"EffectId {a.EffectId} 가 StatusEffectTable 에 없습니다");
                }
            }

            foreach (var p in monsterParts)
            {
                // 보스 부위(BossId)는 BossTable 에, 일반 부위는 MonsterTable 에 붙는다
                if (p.BossId != 0)
                {
                    if (bosses.TryGet(p.BossId, out var boss)) p.BossOwner = boss;
                    else r.Error(MonsterPartDef.Table, p.Id, $"BossId {p.BossId} 가 BossTable 에 없습니다");
                }
                else if (monsters.TryGet(p.MonsterId, out var m)) p.Owner = m;
                else r.Error(MonsterPartDef.Table, p.Id, $"MonsterId {p.MonsterId} 가 MonsterTable 에 없습니다");

                if (p.LinkedAttackId != 0)
                {
                    if (monsterAttacks.TryGet(p.LinkedAttackId, out var a))
                    {
                        p.LinkedAttack = a;
                        if (a.MonsterId != p.MonsterId) r.Error(MonsterPartDef.Table, p.Id, $"LinkedAttackId {p.LinkedAttackId} 는 다른 몬스터의 공격입니다");
                    }
                    else r.Error(MonsterPartDef.Table, p.Id, $"LinkedAttackId {p.LinkedAttackId} 가 MonsterAttackTable 에 없습니다");
                }

                if (p.TransformToMonsterId != 0)
                {
                    if (monsters.TryGet(p.TransformToMonsterId, out var t))
                    {
                        p.TransformTo = t;
                        if (t.IsBoss || t.Id == p.MonsterId) r.Error(MonsterPartDef.Table, p.Id, "TransformToMonsterId 는 자기 자신이나 보스가 될 수 없습니다");
                    }
                    else r.Error(MonsterPartDef.Table, p.Id, $"TransformToMonsterId {p.TransformToMonsterId} 가 MonsterTable 에 없습니다");
                }
            }

            // 구형 보스 페이즈 연결은 후속 작업 참고용으로 주석 보관한다.
            /*
            foreach (var ph in bossPhases)
            {
                if (monsters.TryGet(ph.MonsterId, out var m))
                {
                    if (m.IsBoss == false) r.Error(BossPhaseDef.Table, ph.Id, $"MonsterId {ph.MonsterId} 는 보스가 아닙니다");
                }
                else r.Error(BossPhaseDef.Table, ph.Id, $"MonsterId {ph.MonsterId} 가 MonsterTable 에 없습니다");

                var seq = new List<MonsterAttackDef>();
                foreach (int id in ph.PatternSequenceIds)
                {
                    if (monsterAttacks.TryGet(id, out var a))
                    {
                        seq.Add(a);
                        if (a.MonsterId != ph.MonsterId) r.Error(BossPhaseDef.Table, ph.Id, $"PatternSequence 의 {id} 는 다른 몬스터의 공격입니다");
                        else if (a.IsSpecial == false || a.IsEntrance) r.Error(BossPhaseDef.Table, ph.Id, $"PatternSequence 의 {id} 는 반복 특수 공격이 아닙니다 (기본 공격/등장 공격 불가)");
                    }
                    else r.Error(BossPhaseDef.Table, ph.Id, $"PatternSequence 의 {id} 가 MonsterAttackTable 에 없습니다");
                }
                ph.PatternSequence = seq;
            }
            */

            var attacksBy = monsterAttacks.Where(a => a.Owner != null).GroupBy(a => a.MonsterId).ToDictionary(g => g.Key, g => g.ToList());
            var partsBy = monsterParts.Where(p => p.Owner != null).GroupBy(p => p.MonsterId).ToDictionary(g => g.Key, g => g.ToList());
            var phasesBy = bossPhases.GroupBy(p => p.MonsterId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Phase).ToList());
            foreach (var m in monsters)
            {
                var attacks = attacksBy.TryGetValue(m.Id, out var al) ? al : new List<MonsterAttackDef>();
                var parts = partsBy.TryGetValue(m.Id, out var pl) ? pl : new List<MonsterPartDef>();
                var phases = phasesBy.TryGetValue(m.Id, out var phl) ? phl : new List<BossPhaseDef>();
                m.SetLinks(attacks, parts, phases);

                CheckUniqueKeys(r, MonsterAttackDef.Table, m.Id, attacks.Select(a => a.AttackKey));
                CheckUniqueKeys(r, MonsterPartDef.Table, m.Id, parts.Select(p => p.PartKey));
                if (attacks.Count(a => a.IsEntrance) > 1)
                    r.Error(MonsterAttackDef.Table, $"Monster {m.Id}", "Entrance 공격은 1개만 가능합니다");
                if (m.IsBoss == false && (attacks.Any(a => a.IsSpecial) || phases.Count > 0))
                    r.Error(MonsterAttackDef.Table, $"Monster {m.Id}", "일반 몬스터는 기본 공격(PatternTag None)만 가질 수 있고 BossPhase 도 가질 수 없습니다");
            }

            var partsByBoss = monsterParts.Where(p => p.BossOwner != null).GroupBy(p => p.BossId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var boss in bosses)
            {
                var parts = partsByBoss.TryGetValue(boss.Id, out var bossParts) ? bossParts : new List<MonsterPartDef>();
                boss.SetParts(parts);

                foreach (var g in parts.GroupBy(p => p.PartKey).Where(g => g.Count() > 1))
                    r.Error(MonsterPartDef.Table, $"Boss {boss.Id}", $"Key '{g.Key}' 가 한 보스 안에서 중복됩니다");
            }
        }

        private static void CheckGradeRole(MonsterDef m, DataLoadReport r)
        {
            bool bossGrade = m.Grade == MonsterGrade.Boss;
            r.Check(bossGrade == m.IsBoss, MonsterDef.Table, m.Id, "Role Boss 와 Grade Boss 는 함께여야 합니다 (둘이 일치해야 함)");
        }

        private static void CheckUniqueKeys(DataLoadReport r, string table, int monsterId, IEnumerable<string> keys)
        {
            foreach (var g in keys.GroupBy(k => k).Where(g => g.Count() > 1))
                r.Error(table, $"Monster {monsterId}", $"Key '{g.Key}' 가 한 몬스터 안에서 중복됩니다");
        }

        private static void LinkRounds(DataLoadReport r)
        {
            foreach (var rule in crowdSpawnRules)
            {
                if (monsters.TryGet(rule.MonsterId, out var m)) rule.Monster = m;
                else r.Error(CrowdSpawnRuleDef.Table, rule.Id, $"MonsterId {rule.MonsterId} 가 MonsterTable 에 없습니다");
                if (rule.Monster != null && rule.Monster.IsBoss)
                    r.Error(CrowdSpawnRuleDef.Table, rule.Id, "보스는 군중 생성 규칙에 넣을 수 없습니다 (RoundTable.BossId 사용)");
                if (rule.Round != 0 && rounds.Contains(rule.Round) == false)
                    r.Error(CrowdSpawnRuleDef.Table, rule.Id, $"Round {rule.Round} 가 RoundTable 에 없습니다");
            }

            foreach (var round in rounds)
            {
                // 기존 MonsterTable 보스 연결은 라운드 연동 시 새 BossDef 기준으로 교체한다.
                // 주석 보관 중인 보스 행을 조회하지 않도록 기존 연결·검증을 함께 보류한다.
                /*
                if (monsters.TryGet(round.BossId, out var boss)) round.Boss = boss;
                else r.Error(RoundDef.Table, round.Round, $"BossId {round.BossId} 가 MonsterTable 에 없습니다");
                if (round.Boss != null && round.Boss.IsBoss == false)
                    r.Error(RoundDef.Table, round.Round, $"BossId {round.BossId} 의 Role 이 Boss 가 아닙니다");
                */

                round.SpawnRules = crowdSpawnRules
                    .Where(x => x.Round == 0 || x.Round == round.Round)
                    .OrderBy(x => x.StartTime)
                    .ToList();
            }
        }

        /// <summary>
        /// 군중전 웨이브 3개 테이블의 Id 참조를 Def 참조로 바꾼다. Load 의 Link 단계에서 LinkRounds 다음에 호출된다.
        /// 없는 Id·보스 몬스터·없는 라운드는 오류로 남기고, 없는 Id 는 연결 목록에서 뺀다.
        /// </summary>
        /// <param name="r">오류를 모으는 로드 보고서</param>
        private static void LinkWaves(DataLoadReport r)
        {
            // 스포너 → 몬스터 (V7, V8)
            foreach (var spawner in waveSpawners)
            {
                if (monsters.TryGet(spawner.MonsterId, out var monster)) spawner.Monster = monster;
                else r.Error(WaveSpawnerDef.Table, spawner.Id, $"MonsterId {spawner.MonsterId} 가 MonsterTable 에 없습니다");
                if (spawner.Monster != null && spawner.Monster.IsBoss)
                    r.Error(WaveSpawnerDef.Table, spawner.Id, $"MonsterId {spawner.MonsterId} 는 보스라 넣을 수 없습니다 (웨이브는 군중전 전용)");
            }

            // 웨이브 → 스포너 (V6)
            foreach (var wave in waves)
            {
                var spawnerList = new List<WaveSpawnerDef>();
                foreach (int spawnerId in wave.SpawnerIds)
                {
                    if (waveSpawners.TryGet(spawnerId, out var spawner)) spawnerList.Add(spawner);
                    else r.Error(WaveDef.Table, wave.Id, $"SpawnList 의 {spawnerId} 가 WaveSpawnerTable 에 없습니다");
                }
                wave.Spawners = spawnerList;
            }

            // 스테이지 → 라운드 확인, 웨이브 연결 후 StartTime 순 정렬 (V6, V15)
            foreach (var stage in waveStages)
            {
                if (rounds.Contains(stage.RoundNo) == false)
                    r.Error(WaveStageDef.Table, stage.Id, $"Round {stage.RoundNo} 가 RoundTable 에 없습니다");

                var waveList = new List<WaveDef>();
                foreach (int waveId in stage.WaveIds)
                {
                    if (waves.TryGet(waveId, out var wave)) waveList.Add(wave);
                    else r.Error(WaveStageDef.Table, stage.Id, $"WaveList 의 {waveId} 가 WaveTable 에 없습니다");
                }
                stage.Waves = waveList.OrderBy(w => w.StartTime).ToList();
            }
        }

        /// <summary>등급은 행 Key 가 enum 이라 Def 를 만든 뒤 Def.Grade 로 넣는다</summary>
        private static DefTable<UpgradeGrade, UpgradeGradeDef> BuildUpgradeGrades(string folder, DataLoadReport r)
        {
            var table = new DefTable<UpgradeGrade, UpgradeGradeDef>(UpgradeGradeDef.Table);
            foreach (var dto in TsvTableReader.Read<UpgradeGradeDto>(folder, UpgradeGradeDef.Table, r))
            {
                var def = new UpgradeGradeDef(dto, r);
                table.Add(def.Grade, def, r);
            }
            return table;
        }

        private static DefTable<UpgradeType, UpgradeTrackDef> GroupUpgrades(DataLoadReport r)
        {
            var table = new DefTable<UpgradeType, UpgradeTrackDef>("UpgradeTrack");
            foreach (var g in upgrades.GroupBy(u => u.UpgradeType))
            {
                var levels = g.OrderBy(u => u.Level).ToList();
                for (int i = 0; i < levels.Count; i++)
                {
                    if (levels[i].Level != i + 1)
                        r.Error(UpgradeDef.Table, levels[i].Id, $"{g.Key} 의 Level 이 1 부터 연속되지 않습니다 (발견: {levels[i].Level})");
                    if (levels[i].Weight != levels[0].Weight)
                        r.Error(UpgradeDef.Table, levels[i].Id, $"{g.Key} 의 Weight 가 레벨마다 다릅니다 (Lv1 = {levels[0].Weight})");
                }
                table.Add(g.Key, new UpgradeTrackDef(g.Key, levels), r);
            }
            return table;
        }

        // ───────────────────────── Validate ─────────────────────────

        private static void Validate(DataLoadReport r)
        {
            r.Check(players.Count > 0, PlayerDef.Table, "-", "플레이어 데이터가 최소 1개 필요합니다");
            r.Check(rounds.Count > 0, RoundDef.Table, "-", "라운드 데이터가 최소 1개 필요합니다");
            r.Check(supplies.Count > 0, SupplyDef.Table, "-", "보급 데이터가 최소 1개 필요합니다");
            r.Check(bosses.Count > 0, BossDef.Table, "-", "공통 보스 데이터가 최소 1개 필요합니다.");
            
            var roundList = rounds.All;
            for (int i = 0; i < roundList.Count; i++)
                if (roundList[i].Round != i + 1)
                    r.Error(RoundDef.Table, roundList[i].Round, "Round 는 1 부터 순서대로 적어야 합니다");

            foreach (var round in rounds)
            {
                foreach (var rule in round.SpawnRules)
                    if (rule.StartTime >= round.BossSpawnTimeMax)
                        r.Warn(CrowdSpawnRuleDef.Table, rule.Id, $"StartTime {rule.StartTime} 이 {round.Round} 라운드 보스 등장 이후라 실행되지 않습니다");
            }

            ValidateMonsters(r);

            int sidearmCount = weapons.Count(w => w.Category == WeaponCategory.Sidearm);
            if (sidearmCount != 1)
                r.Warn(WeaponDef.Table, "-", $"Category Sidearm(1번 칸 권총) 행이 {sidearmCount}개입니다 (정확히 1개여야 함)");

            ValidateUpgrades(r);
    
            if (clypeosaurus != null) r.Check(bosses.Contains(clypeosaurus.BossId), ClypeosaurusDef.Table, clypeosaurus.BossId, $"BossTable에 Id {clypeosaurus.BossId}인 보스가 없습니다.");
            if (tyranopode != null) r.Check(bosses.Contains(tyranopode.BossId), TyranopodeDef.Table, tyranopode.BossId, $"BossTable에 Id {tyranopode.BossId}인 보스가 없습니다.");
            if (tyranopode != null)
            {
                // 보스가 부르는 잡몹은 MonsterPool 이 프리팹으로 만든다 → 프리팹이 있어야 한다
                foreach (int id in tyranopode.SummonMinionIds)
                    r.Check(monsters.TryGet(id, out var minion) && minion.Prefab != null, TyranopodeDef.Table, "SummonMinionIds", $"MonsterTable Id {id} 가 없거나 프리팹이 없습니다.");
                r.Check(monsters.TryGet(tyranopode.EggMinionId, out var eggMinion) && eggMinion.Prefab != null, TyranopodeDef.Table, "EggMinionId", $"MonsterTable Id {tyranopode.EggMinionId} 가 없거나 프리팹이 없습니다.");
            }
            // 라운드 보스는 BossTable 에서 고른다 (RoundBossEncounter). 없으면 인스펙터 보스로 대신하므로 경고만
            foreach (var round in rounds)
                if (bosses.Contains(round.BossId) == false)
                    r.Warn(RoundDef.Table, round.Round, $"BossId {round.BossId} 가 BossTable 에 없습니다 (RoundBossEncounter 인스펙터 보스로 대신함)");

            ValidateWaves(r);
        }

        /// <summary>강화 · 등급 규칙. 상점에서 뺀 종류(GrenadeCapacity)는 데이터가 없어도 된다</summary>
        private static void ValidateUpgrades(DataLoadReport r)
        {
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
                if (type != UpgradeType.GrenadeCapacity && upgradeTracks.Contains(type) == false)
                    r.Warn(UpgradeDef.Table, type, "이 강화 종류의 데이터가 없습니다");

            // 감소율은 모든 레벨을 가장 높은 등급으로 사도 1 미만 (재장전 시간이 0 이하가 되지 않게)
            float reloadMax = upgradeTracks.TryGet(UpgradeType.ReloadSpeed, out var reload) ? reload.GetMaxTotalValue() : 0f;
            r.Check(reloadMax < 1f, UpgradeDef.Table, UpgradeType.ReloadSpeed, $"재장전 감소율 최대 합계가 {reloadMax} 입니다 (1 미만이어야 함)");

            int gradeWeight = 0;
            foreach (UpgradeGrade grade in Enum.GetValues(typeof(UpgradeGrade)))
            {
                if (upgradeGrades.TryGet(grade, out var def)) gradeWeight += def.Weight;
                else r.Error(UpgradeGradeDef.Table, grade, "이 등급의 데이터가 없습니다");
            }
            r.Check(gradeWeight > 0, UpgradeGradeDef.Table, "-", "등급 Weight 합계가 0 입니다 (카드 등급을 뽑을 수 없음)");
        }

        /// <summary>
        /// 군중전 웨이브 데이터의 테이블 간 규칙(V9~V13)을 경고로 검사한다. Validate 끝에서 호출된다.
        /// 실행되지 않는 웨이브, 같은 라운드 스테이지의 겹침·빈틈, 안 쓰이거나 여러 번 쓰인 웨이브·스포너를 찾는다.
        /// </summary>
        /// <param name="r">경고를 모으는 로드 보고서</param>
        private static void ValidateWaves(DataLoadReport r)
        {
            // V9 스테이지 길이 안에 시작하지 못하는 웨이브
            foreach (var stage in waveStages)
            {
                float stageLength = stage.EndTime - stage.StartTime;
                foreach (var wave in stage.Waves)
                    if (wave.StartTime >= stageLength)
                        r.Warn(WaveDef.Table, wave.Id, $"StartTime {wave.StartTime} 이 스테이지 {stage.Id} 길이 {stageLength} 이상이라 실행되지 않습니다");
            }

            // V10·V11 같은 라운드 스테이지끼리 시간 구간 겹침·빈틈 (StartTime 순으로 이웃끼리 비교). ClearBonus 는 시간표가 아니라 전멸 조건으로 시작하므로 뺀다
            foreach (var stagesInRound in waveStages.Where(s => s.Kind == WaveStageKind.Normal).GroupBy(s => s.RoundNo))
            {
                var orderedStages = stagesInRound.OrderBy(s => s.StartTime).ToList();
                for (int i = 1; i < orderedStages.Count; i++)
                {
                    var previous = orderedStages[i - 1];
                    var current = orderedStages[i];
                    if (current.StartTime < previous.EndTime)
                        r.Warn(WaveStageDef.Table, current.Id, $"스테이지 {previous.Id} 와 시간 구간이 겹칩니다 ({current.StartTime} < {previous.EndTime})");
                    if (current.StartTime > previous.EndTime)
                        r.Warn(WaveStageDef.Table, current.Id, $"스테이지 {previous.Id} 와 사이에 빈 시간이 있습니다 ({previous.EndTime}~{current.StartTime})");
                }
            }

            // V12·V13 사용 횟수: 0 = 안 쓰임, 2 이상 = 여러 곳에서 공유
            var waveUseCounts = waveStages.SelectMany(s => s.Waves).GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var wave in waves)
            {
                waveUseCounts.TryGetValue(wave.Id, out int useCount);  // 없으면 0
                if (useCount == 0) r.Warn(WaveDef.Table, wave.Id, "어떤 스테이지에도 쓰이지 않습니다");
                if (useCount > 1) r.Warn(WaveDef.Table, wave.Id, $"스테이지 WaveList 에 {useCount} 번 쓰였습니다 (한 번만 써야 합니다)");
            }

            var spawnerUseCounts = waves.SelectMany(w => w.Spawners).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var spawner in waveSpawners)
            {
                spawnerUseCounts.TryGetValue(spawner.Id, out int useCount);  // 없으면 0
                if (useCount == 0) r.Warn(WaveSpawnerDef.Table, spawner.Id, "어떤 웨이브에도 쓰이지 않습니다");
                if (useCount > 1) r.Warn(WaveSpawnerDef.Table, spawner.Id, $"웨이브 SpawnList 에 {useCount} 번 쓰였습니다 (한 번만 써야 합니다)");
            }
        }

        private static void ValidateMonsters(DataLoadReport r)
        {
            foreach (var m in monsters)
            {
                if (m.Attacks.Count == 0)
                    r.Error(MonsterAttackDef.Table, $"Monster {m.Id}", "공격이 하나도 없습니다");

                foreach (var p in m.Parts)
                    if (p.TransformTo != null && p.TransformTo.Prefab != m.Prefab)
                        r.Warn(MonsterPartDef.Table, p.Id, $"전환 대상 {p.TransformTo.Id} 의 Prefab 이 달라 같은 프리팹 변종이 아닙니다");

                // 구형 보스 등장 공격·페이즈 검증은 주석 보관한다. 일반 몬스터 검증은 위에서 유지한다.
                /*
                if (m.IsBoss == false) continue;

                if (m.EntranceAttack == null) r.Warn(MonsterDef.Table, m.Id, "보스에 Entrance(등장 공격) 가 없습니다 (BL-023)");
                if (m.Phases.Count == 0) { r.Error(BossPhaseDef.Table, $"Monster {m.Id}", "보스 페이즈가 없습니다"); continue; }

                for (int i = 0; i < m.Phases.Count; i++)
                {
                    var ph = m.Phases[i];
                    if (ph.Phase != i + 1) r.Error(BossPhaseDef.Table, ph.Id, "Phase 는 1 부터 연속이어야 합니다");
                    if (i == 0 && ph.HpThresholdPercent != 100) r.Error(BossPhaseDef.Table, ph.Id, "1 페이즈의 HpThresholdPercent 는 100 이어야 합니다");
                    if (i > 0 && ph.HpThresholdPercent >= m.Phases[i - 1].HpThresholdPercent) r.Error(BossPhaseDef.Table, ph.Id, "HpThresholdPercent 는 페이즈가 올라갈수록 작아져야 합니다");
                }
                */
            }
        }
    }
}
