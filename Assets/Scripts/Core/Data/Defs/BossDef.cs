using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    public sealed class BossDef
    {
        public const string Table = "BossTable";
        
        public int Id;
        public int MaxHp { get; }
        public float MoveSpeed { get; }
        public float PatternCooldown { get; }

        public BossDef(BossDto dto, DataLoadReport report)
        {
            Id = dto.Id;
            MaxHp = dto.MaxHp;
            MoveSpeed = dto.MoveSpeed;
            PatternCooldown = dto.PatternCooldown;

            report.Check(Id > 0, Table, Id, "Id는 1 이상이어야 합니다.");
            report.Check(MaxHp > 0, Table, Id, "MaxHP는 1 이상이어야 합니다.");
            report.Check(MoveSpeed >= 0f, Table, Id, "MoveSpeed는 0 이상이어야 합니다.");
            report.Check(PatternCooldown >= 0f, Table, Id, "PatternCooldown은 0 이상이어야 합니다.");
        }
    }
}
