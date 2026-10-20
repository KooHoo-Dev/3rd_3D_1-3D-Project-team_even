using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 보스 페이즈. 체력이 HpThresholdPercent(%) 이하가 되면 진입한다.
    /// 특수 공격은 PatternSequence 순서대로 반복한다 (GetPattern(index) 가 순환).
    /// </summary>
    public sealed class BossPhaseDef
    {
        public const string Table = "BossPhaseTable";

        public int Id { get; }
        public int MonsterId { get; }
        public int Phase { get; }
        public int HpThresholdPercent { get; }
        public IReadOnlyList<int> PatternSequenceIds { get; }
        /// <summary>쿨다운 배율 (0.8 = 20% 단축)</summary>
        public float CooldownMultiplier { get; }
        public bool ElectricSweep { get; }
        public string VisualState { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public IReadOnlyList<MonsterAttackDef> PatternSequence { get; internal set; } = Array.Empty<MonsterAttackDef>();

        internal BossPhaseDef(BossPhaseDto d, DataLoadReport r)
        {
            Id = d.Id;
            MonsterId = d.MonsterId;
            Phase = d.Phase;
            HpThresholdPercent = d.HpThresholdPercent;
            PatternSequenceIds = ParseUtil.IntList(d.PatternSequence, r, Table, Id, nameof(d.PatternSequence));
            CooldownMultiplier = d.CooldownMultiplier;
            ElectricSweep = d.ElectricSweep;
            VisualState = d.VisualState;

            r.Check(Phase >= 1, Table, Id, "Phase 는 1 부터");
            r.Check(HpThresholdPercent >= 1 && HpThresholdPercent <= 100, Table, Id, "HpThresholdPercent 는 1~100");
            r.Check(PatternSequenceIds.Count > 0, Table, Id, "PatternSequence 가 비었습니다");
            r.Check(CooldownMultiplier > 0f, Table, Id, "CooldownMultiplier 는 0 초과");
        }

        /// <summary>index 번째 특수 공격 (순서대로 반복)</summary>
        public MonsterAttackDef GetPattern(int index)
        {
            int n = PatternSequence.Count;
            return n == 0 ? null : PatternSequence[((index % n) + n) % n];
        }
    }
}
