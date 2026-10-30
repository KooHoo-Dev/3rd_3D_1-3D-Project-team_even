using System.Collections.Generic;
using HybridShooter.Core.Data.Defs;

namespace HybridShooter.Analytics
{
    // PlaytestAnalyzer 가 모으는 기록. 시간은 모두 라운드 시간(초, 라운드 시작 기준, 강화 화면 정지 시간 제외)

    /// <summary>라운드 1회 기록</summary>
    internal sealed class RoundRecord
    {
        public int Round;
        /// <summary>보스 예고 시작 = 군중전 끝. -1 = 아직 군중전</summary>
        public float CrowdEndTime = -1f;
        public float BossStartTime = -1f;
        public float BossDefeatedTime = -1f;
        /// <summary>마지막으로 기록한 라운드 시간</summary>
        public float EndTime;
        public bool Cleared, GameOver;
        public bool InCrowd => CrowdEndTime < 0f;

        // ── 받은 피해 ──
        /// <summary>군중전 고정 구간(windowSeconds 단위)별 받은 피해</summary>
        public readonly List<int> CrowdBuckets = new List<int>();
        public int CrowdDamage, BossDamage;
        /// <summary>군중전 중 '최근 windowSeconds 초' 피해의 최댓값과 그 시각</summary>
        public int MaxWindowDamage;
        public float MaxWindowAt;
        public readonly Dictionary<string, SourceStat> CrowdSources = new Dictionary<string, SourceStat>();
        public readonly Dictionary<string, SourceStat> BossSources = new Dictionary<string, SourceStat>();

        // ── 필드 압박 ──
        public readonly List<StageRecord> Stages = new List<StageRecord>();
        public StageRecord CurrentStage => Stages.Count > 0 && InCrowd ? Stages[Stages.Count - 1] : null;
        public int Spawned, PlayerKills, OtherDeaths;
        public int PeakAlive;
        public float PeakAliveAt;
        public readonly Dictionary<int, MonsterStat> Monsters = new Dictionary<int, MonsterStat>();

        // ── 위기 ──
        public int MinHp = int.MaxValue;
        public float MinHpAt;
        public readonly List<float> DownTimes = new List<float>();
        public int HealPacksUsed, HealedHp, GrenadesThrown, GrenadeKills;
        public readonly Dictionary<string, int> Supplies = new Dictionary<string, int>();

        public BossEntrySnapshot BossEntry;
        public readonly Dictionary<int, WeaponStat> Weapons = new Dictionary<int, WeaponStat>();

        // ── 몬스터 공격 (MonsterAttackTable.Id 별) ──
        public readonly Dictionary<int, AttackStat> Attacks = new Dictionary<int, AttackStat>();

        // ── 근접 압박 (군중전, 시간 가중) ──
        public readonly NearStat Near = new NearStat();
        public int MaxNear;
        public float MaxNearAt;
    }

    /// <summary>몬스터 공격 1종의 시도·실행·명중</summary>
    internal sealed class AttackStat
    {
        public string Name;
        /// <summary>Attempts = 예고 시작, Executed = 판정 단계 도달, Cancelled = 예고 중 끊김 (사망·경직 포함)</summary>
        public int Attempts, Executed, Cancelled;
        /// <summary>Hits = 직접 명중 피해 횟수, StatusTicks = 지속 피해(독장판) 틱 수</summary>
        public int Hits, StatusTicks, Damage;
    }

    /// <summary>플레이어 반경 안 몬스터 수·가장 가까운 몬스터 거리의 시간 가중 합</summary>
    internal sealed class NearStat
    {
        public float Time;          // 잰 시간
        public float CountSum;      // 근접 수 × 시간
        public float OccupiedTime;  // 근접 1마리 이상인 시간
        public float NearestSum;    // 최근접 거리 × 시간 (몬스터가 있을 때만)
        public float NearestTime;
        public float AvgCount => Time > 0f ? CountSum / Time : 0f;
        public float OccupiedRatio => Time > 0f ? OccupiedTime / Time : 0f;
        public float AvgNearest => NearestTime > 0f ? NearestSum / NearestTime : -1f;

        public void Add(float dt, int count, float nearest)
        {
            Time += dt;
            CountSum += count * dt;
            if (count > 0) OccupiedTime += dt;
            if (nearest >= 0f)
            {
                NearestSum += nearest * dt;
                NearestTime += dt;
            }
        }
    }

    internal sealed class SourceStat
    {
        public int Damage, Hits;
    }

    internal sealed class StageRecord
    {
        public int Id;
        public float StartTime;
        /// <summary>-1 = 진행 중</summary>
        public float EndTime = -1f;
        public int Spawned, Kills, Damage;
        /// <summary>스테이지가 끝날 때 살아 있던 몬스터 수. -1 = 진행 중</summary>
        public int AliveAtEnd = -1;
        public readonly NearStat Near = new NearStat();
    }

    internal sealed class MonsterStat
    {
        public string Name;
        public int Spawned, Kills, OtherDeaths;
        /// <summary>플레이어가 처치한 개체의 스폰 → 사망 시간 합 / 최대 (스폰을 본 개체만)</summary>
        public float LifetimeSum, LifetimeMax;
        public int LifetimeCount;
        public float AvgLifetime => LifetimeCount > 0 ? LifetimeSum / LifetimeCount : 0f;

        // ── 접근 (플레이어 수평 반경 nearRadius 기준) ──
        /// <summary>한 번이라도 반경 안에 들어온 개체 수, 스폰 → 첫 도달 시간 합</summary>
        public int Reached;
        public float ReachTimeSum;
        public float AvgReachTime => Reached > 0 ? ReachTimeSum / Reached : 0f;
        /// <summary>이 종류의 근접 마리 수 (군중전, 시간 가중). NearNow 는 매 프레임 세는 임시 값</summary>
        public readonly NearStat Near = new NearStat();
        public int NearNow;

        // ── 플레이어가 처치한 순간의 수평 거리 ──
        public static readonly float[] KillDistanceEdges = { 3f, 10f, 20f };  // <3 / 3~10 / 10~20 / 20+
        public readonly int[] KillDistanceBins = new int[4];
        public float KillDistanceSum;
        public int KillDistanceCount;
        public float AvgKillDistance => KillDistanceCount > 0 ? KillDistanceSum / KillDistanceCount : -1f;

        public void AddKillDistance(float distance)
        {
            KillDistanceSum += distance;
            KillDistanceCount++;
            int bin = 0;
            while (bin < KillDistanceEdges.Length && distance >= KillDistanceEdges[bin]) bin++;
            KillDistanceBins[bin]++;
        }
    }

    internal sealed class WeaponStat
    {
        public string Name;
        /// <summary>Shots = 발사 수, HitShots = 1개 이상 맞힌 발사 수 (같은 프레임의 여러 명중은 1발로 본다: 산탄·폭발)</summary>
        public int Shots, HitShots, Damage, Kills;
        public int LastHitFrame = -1;
    }

    /// <summary>보스 예고가 시작된 순간의 플레이어·필드 상태</summary>
    internal sealed class BossEntrySnapshot
    {
        public float Time;
        public int Hp, MaxHp, Grenades, HealPacks, Lives, MaxLives, Alive;
        /// <summary>남은 몬스터 이름 → 수</summary>
        public readonly Dictionary<string, int> AliveByName = new Dictionary<string, int>();
    }

    /// <summary>timeline.csv 한 줄 (1초마다)</summary>
    internal readonly struct TimelineRow
    {
        public readonly int Round, Second, StageId, Hp, Alive, Near, Spawned, Kills, Damage, WindowDamage;
        /// <summary>가장 가까운 몬스터까지 수평 거리 (m). -1 = 몬스터 없음</summary>
        public readonly float Nearest;
        public readonly string Phase;

        public TimelineRow(int round, int second, string phase, int stageId, int hp, int alive, int near, float nearest,
                           int spawned, int kills, int damage, int windowDamage)
        {
            Round = round; Second = second; Phase = phase; StageId = stageId; Hp = hp; Alive = alive;
            Near = near; Nearest = nearest; Spawned = spawned; Kills = kills; Damage = damage; WindowDamage = windowDamage;
        }
    }

    /// <summary>events.csv 한 줄</summary>
    internal readonly struct EventRow
    {
        public readonly float SessionTime, RoundTime;
        public readonly int Round, Value, Hp;
        public readonly string Phase, Type, Detail;

        public EventRow(float sessionTime, int round, float roundTime, string phase, string type, string detail, int value, int hp)
        {
            SessionTime = sessionTime; Round = round; RoundTime = roundTime; Phase = phase;
            Type = type; Detail = detail; Value = value; Hp = hp;
        }
    }

    /// <summary>살아 있는 몬스터 1마리</summary>
    internal sealed class AliveMonster
    {
        public readonly MonsterDef Def;
        public readonly float SpawnTime;
        /// <summary>플레이어 반경 안에 한 번이라도 들어왔나 (종류별 도달 수는 처음 들어올 때 한 번 센다)</summary>
        public bool ReachedNear;
        public AliveMonster(MonsterDef def, float spawnTime) { Def = def; SpawnTime = spawnTime; }
    }
}
