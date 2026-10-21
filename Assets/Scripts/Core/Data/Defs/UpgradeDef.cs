using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>강화 한 단계 (BL-030, BL-031)</summary>
    public sealed class UpgradeDef
    {
        public const string Table = "UpgradeTable";

        public int Id { get; }
        public UpgradeType UpgradeType { get; }
        public int Level { get; }
        public int Cost { get; }
        /// <summary>이 레벨에서 추가되는 값. ReloadSpeed=감소율(0.1=10%), MagazineSize=+발, GrenadeCapacity=+개</summary>
        public float Value { get; }
        public string Name { get; }
        public string Description { get; }

        internal UpgradeDef(UpgradeDto upgradeDto, DataLoadReport dataLoadReport)
        {
            Id = upgradeDto.Id;
            UpgradeType = ParseUtil.Enum<UpgradeType>(upgradeDto.UpgradeType, dataLoadReport, Table, Id, nameof(upgradeDto.UpgradeType));
            Level = upgradeDto.Level;
            Cost = upgradeDto.Cost;
            Value = upgradeDto.Value;
            Name = upgradeDto.Name;
            Description = upgradeDto.Description;

            dataLoadReport.Check(Level >= 1, Table, Id, "Level 은 1 부터");
            dataLoadReport.Check(Cost >= 0, Table, Id, "Cost 는 0 이상");
            dataLoadReport.Check(Value > 0f, Table, Id, "Value 는 0 초과");
        }
    }

    /// <summary>같은 UpgradeType 의 레벨 묶음. Key = UpgradeType</summary>
    public sealed class UpgradeTrackDef
    {
        public UpgradeType UpgradeType { get; }
        /// <summary>Level 오름차순 (Levels[0] = Lv1)</summary>
        public IReadOnlyList<UpgradeDef> Levels { get; }
        public int MaxLevel => Levels.Count;

        internal UpgradeTrackDef(UpgradeType type, List<UpgradeDef> levels)
        {
            UpgradeType = type;
            Levels = levels;
        }

        /// <summary>현재 레벨에서 다음에 살 강화. 최대 레벨이면 null</summary>
        public UpgradeDef GetNext(int currentLevel) => currentLevel < Levels.Count ? Levels[currentLevel] : null;

        /// <summary>level 까지 누적된 값 (0 = 강화 없음)</summary>
        public float GetTotalValue(int level)
        {
            float sum = 0f;
            for (int i = 0; i < level && i < Levels.Count; i++) sum += Levels[i].Value;
            return sum;
        }
    }
}
