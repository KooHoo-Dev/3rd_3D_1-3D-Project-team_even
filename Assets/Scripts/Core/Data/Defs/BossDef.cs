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
        /// <summary>보스 프리팹의 Resources 경로 (확장자 없음). RoundBossEncounter 가 라운드 보스를 만들 때 쓴다</summary>
        public string Prefab { get; }

        public BossDef(BossDto dto, DataLoadReport report)
        {
            Id = dto.Id;
            MaxHp = dto.MaxHp;
            MoveSpeed = dto.MoveSpeed;
            PatternCooldown = dto.PatternCooldown;
            Prefab = dto.Prefab?.Trim() ?? string.Empty;

            report.Check(Id > 0, Table, Id, "Id는 1 이상이어야 합니다.");
            report.Check(MaxHp > 0, Table, Id, "MaxHP는 1 이상이어야 합니다.");
            report.Check(MoveSpeed >= 0f, Table, Id, "MoveSpeed는 0 이상이어야 합니다.");
            report.Check(PatternCooldown >= 0f, Table, Id, "PatternCooldown은 0 이상이어야 합니다.");
            report.Check(Prefab.Length > 0, Table, Id, "Prefab(Resources 경로)이 비어 있습니다.");
        }
    }
}
