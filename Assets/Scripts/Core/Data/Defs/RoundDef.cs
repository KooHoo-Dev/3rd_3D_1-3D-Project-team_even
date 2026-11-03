using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 라운드 한 개 = 군중전(1인칭) → (유형별 종료 조건) → 강화 선택. (E4, E5, E7, E9)
    /// CrowdToBoss 는 군중전 → 보스 등장(3인칭) → 보스전, AnnihilationToBoss 는 섬멸 목표 달성 → 보스 등장 → 보스전,
    /// Annihilation·Survival 은 보스 없이 바로 강화 선택으로 간다.
    /// 진행 번호(Round) 한 행이 유형·웨이브 편성 후보·보스·시간을 함께 정한다.
    /// 조회는 DataManager.GetRound(n) 사용 (마지막 행 이후 라운드는 마지막 행을 재사용).
    /// </summary>
    public sealed class RoundDef
    {
        public const string Table = "RoundTable";

        public int Round { get; }
        public RoundType Type { get; }
        public IReadOnlyList<int> WaveRounds { get; }  // 웨이브 편성 후보 (WaveStageTable.RoundNo). RoundSequencer 가 라운드 시작마다 하나를 고른다
        public float PhaseTime { get; }  // 초. Annihilation·AnnihilationToBoss = 소환 시간 · Survival = 생존 시간 · CrowdToBoss = 안 씀
        public float KillRatio { get; }  // 섬멸 목표 비율 (0~1). 목표 처치 수는 DataManager.GetKillTarget 이 고른 편성으로 계산. 그 외 0
        public int BossId { get; }
        /// <summary>보스 등장 시점 범위(라운드 시작 기준, 초). 이 사이에서 무작위 (BL-021)</summary>
        public float BossSpawnTimeMin { get; }
        public float BossSpawnTimeMax { get; }
        /// <summary>첫 보급 투하 시점(초)과 이후 간격 (BL-018)</summary>
        public float SupplyStartTime { get; }
        public float SupplyInterval { get; }
        /// <summary>보스 처치 추가 보상 (BL-029, 보스 Reward 와 별도)</summary>
        public int BossReward { get; }
        /// <summary>라운드 배경음 SoundTable 키 (비면 null = GameSoundRouter 기본 곡). 군중전 = 라운드 시작부터, 보스 = 보스 등장부터</summary>
        public string CrowdBgmKey { get; }
        public string BossBgmKey { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public MonsterDef Boss { get; internal set; }
        /// <summary>Link 단계에서 채워짐 (키가 비었거나 SoundTable 에 없으면 null)</summary>
        public SoundDef CrowdBgm { get; internal set; }
        public SoundDef BossBgm { get; internal set; }
        /// <summary>이 라운드에 적용되는 군중 생성 규칙 (Round 0 규칙 + 해당 라운드 규칙), StartTime 오름차순</summary>
        public IReadOnlyList<CrowdSpawnRuleDef> SpawnRules { get; internal set; } = Array.Empty<CrowdSpawnRuleDef>();

        internal RoundDef(RoundDto d, DataLoadReport r)
        {
            Round = d.Round;
            Type = ParseUtil.Enum<RoundType>(d.Type, r, Table, Round, nameof(d.Type));
            WaveRounds = ParseUtil.IntList(d.WaveRounds, r, Table, Round, nameof(d.WaveRounds));
            PhaseTime = d.PhaseTime;
            KillRatio = d.KillRatio;
            BossId = d.BossId;
            BossSpawnTimeMin = d.BossSpawnTimeMin;
            BossSpawnTimeMax = d.BossSpawnTimeMax;
            SupplyStartTime = d.SupplyStartTime;
            SupplyInterval = d.SupplyInterval;
            BossReward = d.BossReward;
            CrowdBgmKey = string.IsNullOrWhiteSpace(d.CrowdBgm) ? null : d.CrowdBgm.Trim();
            BossBgmKey = string.IsNullOrWhiteSpace(d.BossBgm) ? null : d.BossBgm.Trim();

            r.Check(Round >= 1, Table, Round, "Round 는 1 부터");
            r.Check(WaveRounds.Count > 0, Table, Round, "WaveRounds 가 비었습니다 (WaveStageTable.RoundNo 를 ';' 로 구분)");
            foreach (int waveRound in WaveRounds)
                r.Check(waveRound >= 1, Table, Round, $"WaveRounds 의 {waveRound}: 편성 번호는 1 부터 (WaveStageTable.RoundNo)");
            r.Check(SupplyInterval > 0f, Table, Round, "SupplyInterval 은 0 초과");
            switch (Type)
            {
                case RoundType.CrowdToBoss:
                    r.Check(BossSpawnTimeMin > 0f && BossSpawnTimeMin <= BossSpawnTimeMax, Table, Round, "0 < BossSpawnTimeMin <= BossSpawnTimeMax");
                    break;
                case RoundType.Annihilation:
                case RoundType.AnnihilationToBoss:
                    r.Check(PhaseTime > 0f, Table, Round, $"{Type} 은 PhaseTime(소환 시간) 0 초과");
                    r.Check(KillRatio > 0f && KillRatio <= 1f, Table, Round, $"{Type} 은 0 < KillRatio <= 1");
                    break;
                case RoundType.Survival:
                    r.Check(PhaseTime > 0f, Table, Round, "Survival 은 PhaseTime(생존 시간) 0 초과");
                    break;
            }
        }

        // 보스가 나오는 라운드인지. 데이터 검증·테스터·RoundSequencer 가 쓴다
        public bool HasBoss => Type == RoundType.CrowdToBoss || Type == RoundType.AnnihilationToBoss;

        // 처치 목표로 끝나는 섬멸 유형인지 (보스 유무 무관)
        public bool IsAnnihilation => Type == RoundType.Annihilation || Type == RoundType.AnnihilationToBoss;

        /// <summary>보스 등장 시각 결정. roll01 은 [0,1) 난수</summary>
        public float RollBossSpawnTime(float roll01) => BossSpawnTimeMin + (BossSpawnTimeMax - BossSpawnTimeMin) * roll01;
    }
}
