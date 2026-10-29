using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 군중전 스테이지 한 줄: "Round 라운드의 StartTime~EndTime 동안 Waves 를 StartTime 순으로 실행" (보스 없음).
    /// DataManager.Load 가 Build 단계에서 만들고, LinkWaves 가 Waves 를 채운다.
    /// </summary>
    public sealed class WaveStageDef
    {
        public const string Table = "WaveStageTable";

        public int Id { get; }
        public int RoundNo { get; }  // RoundTable.Round
        public float StartTime { get; }  // 초, 라운드 시작 기준
        public float EndTime { get; }  // 초, 라운드 시작 기준
        public IReadOnlyList<int> WaveIds { get; }

        // ── Link 단계에서 채워짐 ──
        public IReadOnlyList<WaveDef> Waves { get; internal set; } = Array.Empty<WaveDef>();  // 웨이브 StartTime 순

        /// <summary>
        /// TSV 한 행을 스테이지 정의로 바꾸고 이 행 안의 규칙(V1~V3)을 검사한다.
        /// </summary>
        /// <param name="d">WaveStageTable 한 행</param>
        /// <param name="r">오류를 모으는 로드 보고서</param>
        internal WaveStageDef(WaveStageDto d, DataLoadReport r)
        {
            Id = d.Id;
            RoundNo = d.RoundNo;
            StartTime = d.StartTime;
            EndTime = d.EndTime;
            WaveIds = ParseUtil.IntList(d.WaveList, r, Table, Id, nameof(d.WaveList));

            r.Check(StartTime >= 0f, Table, Id, "StartTime 은 0 이상");
            r.Check(EndTime > StartTime, Table, Id, "EndTime 은 StartTime 보다 커야 합니다");
            r.Check(WaveIds.Count > 0, Table, Id, "WaveList 가 비었습니다");
        }
    }
}
