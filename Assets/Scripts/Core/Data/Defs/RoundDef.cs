using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 라운드 한 개 = 군중전(1인칭) → (유형별 종료 조건) → 강화 선택. (E4, E5, E7, E9)
    /// CrowdToBoss 는 군중전 → 보스 등장(3인칭) → 보스전, Annihilation·Survival 은 보스 없이 바로 강화 선택으로 간다.
    /// 진행 번호(Round) 한 행이 유형·웨이브 편성·보스·시간을 함께 정한다.
    /// 조회는 DataManager.GetRound(n) 사용 (마지막 행 이후 라운드는 마지막 행을 재사용).
    /// </summary>
    public sealed class RoundDef
    {
        public const string Table = "RoundTable";

        public int Round { get; }
        public RoundType Type { get; }
        public int WaveRound { get; }  // MonsterWaveDirector.StartRound 에 넘길 WaveStageTable.RoundNo
        public float PhaseTime { get; }  // 초. Annihilation = 소환 시간 · Survival = 생존 시간 · CrowdToBoss = 안 씀
        public int KillTarget { get; }  // Annihilation 의 목표 처치 수 (라운드 누적, 종류 무관). 그 외 0
        public int BossId { get; }
        /// <summary>보스 등장 시점 범위(라운드 시작 기준, 초). 이 사이에서 무작위 (BL-021)</summary>
        public float BossSpawnTimeMin { get; }
        public float BossSpawnTimeMax { get; }
        /// <summary>첫 보급 투하 시점(초)과 이후 간격 (BL-018)</summary>
        public float SupplyStartTime { get; }
        public float SupplyInterval { get; }
        /// <summary>보스 처치 추가 보상 (BL-029, 보스 Reward 와 별도)</summary>
        public int BossReward { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public MonsterDef Boss { get; internal set; }
        /// <summary>이 라운드에 적용되는 군중 생성 규칙 (Round 0 규칙 + 해당 라운드 규칙), StartTime 오름차순</summary>
        public IReadOnlyList<CrowdSpawnRuleDef> SpawnRules { get; internal set; } = Array.Empty<CrowdSpawnRuleDef>();

        internal RoundDef(RoundDto d, DataLoadReport r)
        {
            Round = d.Round;
            Type = ParseUtil.Enum<RoundType>(d.Type, r, Table, Round, nameof(d.Type));
            WaveRound = d.WaveRound;
            PhaseTime = d.PhaseTime;
            KillTarget = d.KillTarget;
            BossId = d.BossId;
            BossSpawnTimeMin = d.BossSpawnTimeMin;
            BossSpawnTimeMax = d.BossSpawnTimeMax;
            SupplyStartTime = d.SupplyStartTime;
            SupplyInterval = d.SupplyInterval;
            BossReward = d.BossReward;

            r.Check(Round >= 1, Table, Round, "Round 는 1 부터");
            r.Check(WaveRound >= 1, Table, Round, "WaveRound 는 1 부터 (WaveStageTable.RoundNo)");
            r.Check(SupplyInterval > 0f, Table, Round, "SupplyInterval 은 0 초과");
            switch (Type)
            {
                case RoundType.CrowdToBoss:
                    r.Check(BossSpawnTimeMin > 0f && BossSpawnTimeMin <= BossSpawnTimeMax, Table, Round, "0 < BossSpawnTimeMin <= BossSpawnTimeMax");
                    break;
                case RoundType.Annihilation:
                    r.Check(PhaseTime > 0f, Table, Round, "Annihilation 은 PhaseTime(소환 시간) 0 초과");
                    r.Check(KillTarget >= 1, Table, Round, "Annihilation 은 KillTarget 1 이상");
                    break;
                case RoundType.Survival:
                    r.Check(PhaseTime > 0f, Table, Round, "Survival 은 PhaseTime(생존 시간) 0 초과");
                    break;
            }
        }

        // 보스가 나오는 라운드인지. 데이터 검증·테스터가 쓴다
        public bool HasBoss => Type == RoundType.CrowdToBoss;

        /// <summary>보스 등장 시각 결정. roll01 은 [0,1) 난수</summary>
        public float RollBossSpawnTime(float roll01) => BossSpawnTimeMin + (BossSpawnTimeMax - BossSpawnTimeMin) * roll01;
    }
}
