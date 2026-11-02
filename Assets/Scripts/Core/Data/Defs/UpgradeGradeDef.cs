using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>강화 카드 등급 (BL-030). 이름·글자/UI 색·뽑기 기본 가중치. Key = UpgradeGrade</summary>
    public sealed class UpgradeGradeDef
    {
        public const string Table = "UpgradeGradeTable";

        public UpgradeGrade Grade { get; }
        public string Name { get; }
        /// <summary>카드 이름·테두리 등 등급 표시 색</summary>
        public Color Color { get; }
        /// <summary>카드 등급 뽑기 기본 가중치 (러키걸 강화가 에픽에 배율을 곱한다)</summary>
        public int Weight { get; }

        internal UpgradeGradeDef(UpgradeGradeDto dto, DataLoadReport report)
        {
            Grade = ParseUtil.Enum<UpgradeGrade>(dto.Grade, report, Table, dto.Grade, nameof(dto.Grade));
            Name = dto.Name;
            bool hasColor = ColorUtility.TryParseHtmlString(dto.Color, out var color);
            Color = hasColor ? color : Color.white;
            Weight = dto.Weight;

            report.Check(hasColor, Table, dto.Grade, $"Color '{dto.Color}' 는 #RRGGBB 형식이 아닙니다");
            report.Check(Weight >= 0, Table, dto.Grade, "Weight 는 0 이상");
        }
    }
}
