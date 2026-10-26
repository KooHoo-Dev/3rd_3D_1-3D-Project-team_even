using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    public sealed class ClypeosaurusDef
    {
        public const string Table = "ClypeosaurusTable";

        public int BossId { get; }
        
        // 짓밟기
        public int StompDamage { get; }
        public float StompTelegraphTime { get; }
        public float StompRadius { get; }
        public float StompRecoveryTime { get; }
        public float StompCooldown { get; }

        // 마그마 포격
        public int BarrageDamage { get; }
        public float BarrageRadius { get; }
        public float BarrageTelegraphTime { get; }
        public float BarrageInterval { get; }
        public float BarrageRecoveryTime { get; }
        public float BarrageCooldown { get; }

        // 안테나 전격
        public int ElectricDamage { get; }
        public float ElectricRange { get; }
        public float ElectricWidth { get; }
        public float ElectricChargeTime { get; }
        public float ElectricRecoveryTime { get; }
        public float ElectricCooldown { get; }

        // 본체 HP에 따른 안테나 개방 기준
        public float FourOpenHpRatio { get; }
        public float SixOpenHpRatio { get; }

        public ClypeosaurusDef(ClypeosaurusDto dto, DataLoadReport report)
        {
            BossId = dto.BossId;
            
            StompDamage = dto.StompDamage;
            StompTelegraphTime = dto.StompTelegraphTime;
            StompRadius = dto.StompRadius;
            StompRecoveryTime = dto.StompRecoveryTime;
            StompCooldown = dto.StompCooldown;

            BarrageDamage = dto.BarrageDamage;
            BarrageRadius = dto.BarrageRadius;
            BarrageTelegraphTime = dto.BarrageTelegraphTime;
            BarrageInterval = dto.BarrageInterval;
            BarrageRecoveryTime = dto.BarrageRecoveryTime;
            BarrageCooldown = dto.BarrageCooldown;

            ElectricDamage = dto.ElectricDamage;
            ElectricRange = dto.ElectricRange;
            ElectricWidth = dto.ElectricWidth;
            ElectricChargeTime = dto.ElectricChargeTime;
            ElectricRecoveryTime = dto.ElectricRecoveryTime;
            ElectricCooldown = dto.ElectricCooldown;

            FourOpenHpRatio = dto.FourOpenHpRatio;
            SixOpenHpRatio = dto.SixOpenHpRatio;

            report.Check(BossId > 0, Table, BossId, "BossId는 1 이상이어야 합니다.");
            
            // 짓밟기 검증
            report.Check(StompDamage >= 0, Table, "Stomp", "Damage는 0 이상이어야 합니다.");
            report.Check(StompTelegraphTime >= 0f, Table, "Stomp", "TelegraphTime은 0 이상이어야 합니다.");
            report.Check(StompRadius > 0f, Table, "Stomp", "Radius는 0보다 커야 합니다.");
            report.Check(StompRecoveryTime >= 0f, Table, "Stomp", "RecoveryTime은 0 이상이어야 합니다.");
            report.Check(StompCooldown >= 0f, Table, "Stomp", "Cooldown은 0 이상이어야 합니다.");

            // 마그마 포격 검증
            report.Check(BarrageDamage >= 0, Table, "Barrage", "Damage는 0 이상이어야 합니다.");
            report.Check(BarrageRadius > 0f, Table, "Barrage", "Radius는 0보다 커야 합니다.");
            report.Check(BarrageTelegraphTime >= 0f, Table, "Barrage", "TelegraphTime은 0 이상이어야 합니다.");
            report.Check(BarrageInterval > 0f, Table, "Barrage", "Interval은 0보다 커야 합니다.");
            report.Check(BarrageRecoveryTime >= 0f, Table, "Barrage", "RecoveryTime은 0 이상이어야 합니다.");
            report.Check(BarrageCooldown >= 0f, Table, "Barrage", "Cooldown은 0 이상이어야 합니다.");

            // 안테나 전격 검증
            report.Check(ElectricDamage >= 0, Table, "Electric", "Damage는 0 이상이어야 합니다.");
            report.Check(ElectricRange > 0f, Table, "Electric", "Range는 0보다 커야 합니다.");
            report.Check(ElectricWidth > 0f, Table, "Electric", "Width는 0보다 커야 합니다.");
            report.Check(ElectricChargeTime >= 0f, Table, "Electric", "ChargeTime은 0 이상이어야 합니다.");
            report.Check(ElectricRecoveryTime >= 0f, Table, "Electric", "RecoveryTime은 0 이상이어야 합니다.");
            report.Check(ElectricCooldown >= 0f, Table, "Electric", "Cooldown은 0 이상이어야 합니다.");

            // 안테나 개방 기준 검증
            report.Check(SixOpenHpRatio > 0f && SixOpenHpRatio < FourOpenHpRatio && FourOpenHpRatio < 1f, Table,
                "Antenna", "체력 기준은 0 < SixOpenHpRatio < FourOpenHpRatio < 1이어야 합니다.");
        }
    }
}
