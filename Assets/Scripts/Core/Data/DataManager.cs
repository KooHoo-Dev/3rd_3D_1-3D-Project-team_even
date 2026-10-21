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
        private static DefTable<int, SupplyDef> supplies;
        private static DefTable<int, UpgradeDef> upgrades;
        private static DefTable<UpgradeType, UpgradeTrackDef> upgradeTracks;
        private static DefTable<string, SoundDef> sounds;
        private static GameConfig config;

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
        public static DefTable<int, SupplyDef> Supplies { get { EnsureLoaded(); return supplies; } }
        public static DefTable<int, UpgradeDef> Upgrades { get { EnsureLoaded(); return upgrades; } }
        /// <summary>Key = UpgradeType. 레벨별 강화 묶음</summary>
        public static DefTable<UpgradeType, UpgradeTrackDef> UpgradeTracks { get { EnsureLoaded(); return upgradeTracks; } }
        public static DefTable<string, SoundDef> Sounds { get { EnsureLoaded(); return sounds; } }
        public static GameConfig Config { get { EnsureLoaded(); return config; } }

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
            bossPhases      = Build<BossPhaseDto, int, BossPhaseDef>(folder, BossPhaseDef.Table, r, d => d.Id, d => new BossPhaseDef(d, r));
            statusEffects   = Build<StatusEffectDto, int, StatusEffectDef>(folder, StatusEffectDef.Table, r, d => d.Id, d => new StatusEffectDef(d, r));
            rounds          = Build<RoundDto, int, RoundDef>(folder, RoundDef.Table, r, d => d.Round, d => new RoundDef(d, r));
            crowdSpawnRules = Build<CrowdSpawnDto, int, CrowdSpawnRuleDef>(folder, CrowdSpawnRuleDef.Table, r, d => d.Id, d => new CrowdSpawnRuleDef(d, r));
            supplies        = Build<SupplyDto, int, SupplyDef>(folder, SupplyDef.Table, r, d => d.Id, d => new SupplyDef(d, r));
            upgrades        = Build<UpgradeDto, int, UpgradeDef>(folder, UpgradeDef.Table, r, d => d.Id, d => new UpgradeDef(d, r));
            sounds          = Build<SoundDto, string, SoundDef>(folder, SoundDef.Table, r, d => d.Key, d => new SoundDef(d, r));

            config = new GameConfig();
            foreach (var dto in TsvTableReader.Read<ConfigDto>(folder, GameConfig.Table, r)) config.Add(dto, r);

            // 2) Link + Group : Id 참조 → Def 참조, 여러 행 → 상위 Def
            LinkPlayer(r);
            LinkMonsters(r);
            LinkRounds(r);
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

        // ───────────────────────── Link / Group ─────────────────────────

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
                if (monsters.TryGet(p.MonsterId, out var m)) p.Owner = m;
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
                if (monsters.TryGet(round.BossId, out var boss)) round.Boss = boss;
                else r.Error(RoundDef.Table, round.Round, $"BossId {round.BossId} 가 MonsterTable 에 없습니다");
                if (round.Boss != null && round.Boss.IsBoss == false)
                    r.Error(RoundDef.Table, round.Round, $"BossId {round.BossId} 의 Role 이 Boss 가 아닙니다");

                round.SpawnRules = crowdSpawnRules
                    .Where(x => x.Round == 0 || x.Round == round.Round)
                    .OrderBy(x => x.StartTime)
                    .ToList();
            }
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

            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
                if (upgradeTracks.Contains(type) == false) r.Warn(UpgradeDef.Table, type, "이 강화 종류의 데이터가 없습니다");

            float reloadTotal = upgradeTracks.TryGet(UpgradeType.ReloadSpeed, out var reload) ? reload.GetTotalValue(reload.MaxLevel) : 0f;
            r.Check(reloadTotal < 1f, UpgradeDef.Table, UpgradeType.ReloadSpeed, $"재장전 감소율 합계가 {reloadTotal} 입니다 (1 미만이어야 함)");

            CheckSound(r, WeaponDef.Table, weapons.Select(w => (w.Id as object, w.FireSfxKey)).Concat(weapons.Select(w => (w.Id as object, w.ReloadSfxKey))));
            CheckSound(r, GrenadeDef.Table, grenades.Select(g => (g.Id as object, g.ExplodeSfxKey)));
        }

        private static void ValidateMonsters(DataLoadReport r)
        {
            foreach (var m in monsters)
            {
                if (m.Attacks.Count == 0)
                    r.Error(MonsterAttackDef.Table, $"Monster {m.Id}", "공격이 하나도 없습니다");

                foreach (var p in m.Parts)
                    if (p.TransformTo != null && p.TransformTo.PrefabKey != m.PrefabKey)
                        r.Warn(MonsterPartDef.Table, p.Id, $"전환 대상 {p.TransformTo.Id} 의 PrefabKey 가 달라 같은 프리팹 변종이 아닙니다");

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
            }
        }

        private static void CheckSound(DataLoadReport r, string table, IEnumerable<(object id, string key)> refs)
        {
            foreach (var (id, key) in refs)
                if (string.IsNullOrEmpty(key) == false && sounds.Contains(key) == false)
                    r.Warn(table, id, $"사운드 키 '{key}' 가 SoundTable 에 없습니다");
        }
    }
}
