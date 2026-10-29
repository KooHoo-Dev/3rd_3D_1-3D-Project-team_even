using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 스포너 한 줄: "웨이브 시작 StartDelay 초 뒤부터 SpawnInterval 간격으로 Monster 를 Count 마리, SpawnPattern 위치에".
    /// DataManager.Load 가 Build 단계에서 만들고, LinkWaves 가 Monster 를 채운다. 보스는 넣을 수 없다.
    /// </summary>
    public sealed class WaveSpawnerDef
    {
        public const string Table = "WaveSpawnerTable";

        public int Id { get; }
        public int MonsterId { get; }
        public int Count { get; }
        public float StartDelay { get; }  // 초, 웨이브 시작 후 첫 스폰까지
        public float SpawnInterval { get; }  // 초, 0 = 동시 스폰
        public SpawnPattern SpawnPattern { get; }

        // ── Link 단계에서 채워짐 ──
        public MonsterDef Monster { get; internal set; }

        /// <summary>
        /// TSV 한 행을 스포너 정의로 바꾸고 이 행 안의 규칙(V4, V5)을 검사한다.
        /// </summary>
        /// <param name="d">WaveSpawnerTable 한 행</param>
        /// <param name="r">오류를 모으는 로드 보고서</param>
        internal WaveSpawnerDef(WaveSpawnerDto d, DataLoadReport r)
        {
            Id = d.Id;
            MonsterId = d.MonsterId;
            Count = d.Count;
            StartDelay = d.StartDelay;
            SpawnInterval = d.SpawnInterval;
            SpawnPattern = ParseUtil.Enum<SpawnPattern>(d.SpawnPattern, r, Table, Id, nameof(d.SpawnPattern));

            r.Check(Count >= 1, Table, Id, "Count 는 1 이상");
            r.Check(StartDelay >= 0f, Table, Id, "StartDelay 는 0 이상");
            r.Check(SpawnInterval >= 0f, Table, Id, "SpawnInterval 은 0 이상 (0 = 동시 스폰)");
        }
    }
}
