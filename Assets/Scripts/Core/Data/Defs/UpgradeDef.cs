using System;
using System.Collections.Generic;
using System.Globalization;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>강화 한 단계 (BL-030, BL-031). 증가량은 산 카드의 등급에 따라 GradeValues 중 하나</summary>
    public sealed class UpgradeDef
    {
        public const string Table = "UpgradeTable";

        public static readonly int GradeCount = Enum.GetValues(typeof(UpgradeGrade)).Length;

        public int Id { get; }
        public UpgradeType UpgradeType { get; }
        public int Level { get; }
        public int Cost { get; }
        /// <summary>
        /// 등급별 이 레벨에서 추가되는 값 (UpgradeGrade 순서: 하급·일반·에픽). ReloadSpeed=감소율(0.1=10%), MagazineSize=+발,
        /// GrenadeRadius·StaminaRegen=증가율, MaxHealth·MaxStamina=+수치, HealCapacity=+개, LuckyGirl=에픽 가중치 증가율
        /// </summary>
        public IReadOnlyList<float> GradeValues { get; }
        /// <summary>상점 카드 뽑기 가중치 (같은 UpgradeType 의 모든 레벨이 같아야 함)</summary>
        public int Weight { get; }
        public string Name { get; }
        /// <summary>{0} = 등급 값 (UpgradeScreen 이 종류에 맞는 단위로 채운다)</summary>
        public string Description { get; }

        internal UpgradeDef(UpgradeDto upgradeDto, DataLoadReport dataLoadReport)
        {
            Id = upgradeDto.Id;
            UpgradeType = ParseUtil.Enum<UpgradeType>(upgradeDto.UpgradeType, dataLoadReport, Table, Id, nameof(upgradeDto.UpgradeType));
            Level = upgradeDto.Level;
            Cost = upgradeDto.Cost;
            GradeValues = ParseGradeValues(upgradeDto.GradeValues, dataLoadReport, Id);
            Weight = upgradeDto.Weight;
            Name = upgradeDto.Name;
            Description = upgradeDto.Description;

            dataLoadReport.Check(Level >= 1, Table, Id, "Level 은 1 부터");
            dataLoadReport.Check(Cost >= 0, Table, Id, "Cost 는 0 이상");
            dataLoadReport.Check(Weight >= 0, Table, Id, "Weight 는 0 이상");
        }

        /// <summary>grade 등급으로 샀을 때 이 레벨에서 추가되는 값</summary>
        public float GetValue(UpgradeGrade grade)
        {
            int i = (int)grade;
            return i >= 0 && i < GradeValues.Count ? GradeValues[i] : 0f;
        }

        /// <summary>가장 높은 등급의 값 (합계 상한 검사용)</summary>
        public float MaxValue
        {
            get
            {
                float max = 0f;
                foreach (float v in GradeValues) if (v > max) max = v;
                return max;
            }
        }

        /// <summary>"0.06;0.08;…" → 등급 수만큼. 개수가 다르거나 0 이하가 있으면 오류</summary>
        private static float[] ParseGradeValues(string raw, DataLoadReport report, int id)
        {
            var values = new float[GradeCount];
            var parts = string.IsNullOrWhiteSpace(raw) ? Array.Empty<string>() : raw.Split(ParseUtil.ListSeparator);
            if (parts.Length != GradeCount)
            {
                report.Error(Table, id, $"GradeValues 는 등급 수({GradeCount})만큼 '{ParseUtil.ListSeparator}' 로 구분해야 합니다 (발견: {parts.Length}개)");
                return values;
            }
            for (int i = 0; i < parts.Length; i++)
            {
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f) values[i] = v;
                else report.Error(Table, id, $"GradeValues 의 '{parts[i]}' 는 0 초과 숫자가 아닙니다");
            }
            return values;
        }
    }

    /// <summary>같은 UpgradeType 의 레벨 묶음. Key = UpgradeType</summary>
    public sealed class UpgradeTrackDef
    {
        public UpgradeType UpgradeType { get; }
        /// <summary>Level 오름차순 (Levels[0] = Lv1)</summary>
        public IReadOnlyList<UpgradeDef> Levels { get; }
        public int MaxLevel => Levels.Count;
        /// <summary>상점 카드 뽑기 가중치 (Lv1 행 기준)</summary>
        public int Weight => Levels.Count > 0 ? Levels[0].Weight : 0;

        internal UpgradeTrackDef(UpgradeType type, List<UpgradeDef> levels)
        {
            UpgradeType = type;
            Levels = levels;
        }

        /// <summary>현재 레벨에서 다음에 살 강화. 최대 레벨이면 null</summary>
        public UpgradeDef GetNext(int currentLevel) => currentLevel < Levels.Count ? Levels[currentLevel] : null;

        /// <summary>레벨마다 산 등급(grades[0] = Lv1)으로 누적된 값 (빈 목록 = 강화 없음)</summary>
        public float GetTotalValue(IReadOnlyList<UpgradeGrade> grades)
        {
            float sum = 0f;
            for (int i = 0; i < grades.Count && i < Levels.Count; i++) sum += Levels[i].GetValue(grades[i]);
            return sum;
        }

        /// <summary>모든 레벨을 가장 높은 등급으로 샀을 때의 합계 (상한 검사용)</summary>
        public float GetMaxTotalValue()
        {
            float sum = 0f;
            foreach (var level in Levels) sum += level.MaxValue;
            return sum;
        }
    }
}
