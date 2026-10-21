using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 군중전 생성 규칙 한 줄: "StartTime~EndTime 동안 Interval 마다 CountPerSpawn 마리, 동시 MaxAlive 까지" (BL-015)
    /// 시간대별로 행을 겹쳐 쓰면 "1분 이후 침투형 합류, 2분 이후 돌진형 합류, 후반 밀도 증가"를 표현할 수 있다.
    /// </summary>
    public sealed class CrowdSpawnRuleDef
    {
        public const string Table = "CrowdSpawnTable";

        public int Id { get; }
        /// <summary>0 = 모든 라운드</summary>
        public int Round { get; }
        public int MonsterId { get; }
        public float StartTime { get; }
        /// <summary>0 = 보스 등장까지</summary>
        public float EndTime { get; }
        public bool UntilBoss => EndTime <= 0f;
        public float Interval { get; }
        public int CountPerSpawn { get; }
        /// <summary>이 규칙으로 생성된 몬스터의 동시 생존 상한</summary>
        public int MaxAlive { get; }
        public SpawnPattern SpawnPattern { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public MonsterDef Monster { get; internal set; }

        internal CrowdSpawnRuleDef(CrowdSpawnDto d, DataLoadReport r)
        {
            Id = d.Id;
            Round = d.Round;
            MonsterId = d.MonsterId;
            StartTime = d.StartTime;
            EndTime = d.EndTime;
            Interval = d.Interval;
            CountPerSpawn = d.CountPerSpawn;
            MaxAlive = d.MaxAlive;
            SpawnPattern = ParseUtil.Enum<SpawnPattern>(d.SpawnPattern, r, Table, Id, nameof(d.SpawnPattern));

            r.Check(Round >= 0, Table, Id, "Round 는 0(전체) 이상");
            r.Check(StartTime >= 0f, Table, Id, "StartTime 은 0 이상");
            r.Check(UntilBoss || EndTime > StartTime, Table, Id, "EndTime 은 StartTime 보다 커야 합니다 (0 = 보스까지)");
            r.Check(Interval > 0f, Table, Id, "Interval 은 0 초과");
            r.Check(CountPerSpawn >= 1 && MaxAlive >= CountPerSpawn, Table, Id, "CountPerSpawn >= 1, MaxAlive >= CountPerSpawn");
        }

        /// <summary>라운드 경과 시간 t 에 이 규칙이 활성인가 (보스 등장 전 기준)</summary>
        public bool IsActiveAt(float t) => t >= StartTime && (UntilBoss || t < EndTime);
    }
}
