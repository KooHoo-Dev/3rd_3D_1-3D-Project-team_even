using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 웨이브 한 줄: "스테이지 시작 StartTime 초 뒤 Spawners 를 한꺼번에 시작".
    /// DataManager.Load 가 Build 단계에서 만들고, LinkWaves 가 Spawners 를 채운다.
    /// </summary>
    public sealed class WaveDef
    {
        public const string Table = "WaveTable";

        public int Id { get; }
        public float StartTime { get; }  // 초, 스테이지 시작 기준 상대 시간
        public IReadOnlyList<int> SpawnerIds { get; }

        // ── Link 단계에서 채워짐 ──
        public IReadOnlyList<WaveSpawnerDef> Spawners { get; internal set; } = Array.Empty<WaveSpawnerDef>();

        /// <summary>
        /// TSV 한 행을 웨이브 정의로 바꾸고 이 행 안의 규칙(V1, V3)을 검사한다.
        /// </summary>
        /// <param name="d">WaveTable 한 행</param>
        /// <param name="r">오류를 모으는 로드 보고서</param>
        internal WaveDef(WaveDto d, DataLoadReport r)
        {
            Id = d.Id;
            StartTime = d.StartTime;
            SpawnerIds = ParseUtil.IntList(d.SpawnList, r, Table, Id, nameof(d.SpawnList));

            r.Check(StartTime >= 0f, Table, Id, "StartTime 은 0 이상");
            r.Check(SpawnerIds.Count > 0, Table, Id, "SpawnList 가 비었습니다");
        }
    }
}
