using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    public sealed class ClypeosaurusDef
    {
        public const string Table = "ClypeosaurusTable";

        public int BossId { get; }
        
        // 등장 공격
        public int EntranceDamage { get; }
        public float EntranceRadius { get; }
        public float EntranceTelegraphTime { get; }
        public float EntranceRecoveryTime { get; }
        
        // 짓밟기
        public int StompDamage { get; }
        public float StompTelegraphTime { get; }
        public float StompRadius { get; }
        public float StompRecoveryTime { get; }
        public float StompStartMaxDistance { get; }
        // public float StompCooldown { get; }

        // 마그마 포격
        public int BarrageDamage { get; }
        public float BarrageRadius { get; }
        public float BarrageTelegraphTime { get; }
        public float BarrageInterval { get; }
        public float BarrageRecoveryTime { get; }
        // public float BarrageCooldown { get; }
        public float BarrageSpreadRadius { get; }
        public float BarrageFlightTime { get; }
        public int BarrageVolleyCount { get; }

        // 본체 HP에 따른 안테나 개방 기준
        public float FourOpenHpRatio { get; }
        public float SixOpenHpRatio { get; }
        
        // 공기 흡입
        public float SuctionTelegraphTime { get; }
        public float SuctionMaxDuration { get; }
        public float SuctionRecoveryTime { get; }
        public float SuctionNearRadius { get; }
        public float SuctionFarExtraSpeed { get; }
        public float SuctionNearExtraSpeed { get; }
        public float SuctionNearDwellTime { get; }
        public float SuctionStartMinDistance { get; }

        public ClypeosaurusDef(ClypeosaurusDto dto, DataLoadReport report)
        {
            BossId = dto.BossId;
            
            EntranceDamage = dto.EntranceDamage;
            EntranceRadius = dto.EntranceRadius;
            EntranceTelegraphTime = dto.EntranceTelegraphTime;
            EntranceRecoveryTime = dto.EntranceRecoveryTime;
            
            StompDamage = dto.StompDamage;
            StompTelegraphTime = dto.StompTelegraphTime;
            StompRadius = dto.StompRadius;
            StompRecoveryTime = dto.StompRecoveryTime;
            StompStartMaxDistance = dto.StompStartMaxDistance;
            // StompCooldown = dto.StompCooldown;

            BarrageDamage = dto.BarrageDamage;
            BarrageRadius = dto.BarrageRadius;
            BarrageTelegraphTime = dto.BarrageTelegraphTime;
            BarrageInterval = dto.BarrageInterval;
            BarrageRecoveryTime = dto.BarrageRecoveryTime;
            // BarrageCooldown = dto.BarrageCooldown;
            BarrageSpreadRadius = dto.BarrageSpreadRadius;
            BarrageFlightTime = dto.BarrageFlightTime;
            BarrageVolleyCount = dto.BarrageVolleyCount;

            FourOpenHpRatio = dto.FourOpenHpRatio;
            SixOpenHpRatio = dto.SixOpenHpRatio;
            
            SuctionTelegraphTime = dto.SuctionTelegraphTime;
            SuctionMaxDuration = dto.SuctionMaxDuration;
            SuctionRecoveryTime = dto.SuctionRecoveryTime;
            SuctionNearRadius = dto.SuctionNearRadius;
            SuctionFarExtraSpeed = dto.SuctionFarExtraSpeed;
            SuctionNearExtraSpeed = dto.SuctionNearExtraSpeed;
            SuctionNearDwellTime = dto.SuctionNearDwellTime;
            SuctionStartMinDistance = dto.SuctionStartMinDistance;

            report.Check(BossId > 0, Table, BossId, "BossId는 1 이상이어야 합니다.");
            
            // 등장 공격
            report.Check(EntranceDamage >= 0, Table, "Entrance", "Damage는 0 이상이어야 합니다.");
            report.Check(EntranceRadius > 0f, Table, "Entrance", "Radius는 0보다 커야 합니다.");
            report.Check(EntranceTelegraphTime >= 0f, Table, "Entrance", "TelegraphTime은 0 이상이어야 합니다.");
            report.Check(EntranceRecoveryTime >= 0f, Table, "Entrance", "RecoveryTime은 0 이상이어야 합니다.");
            
            // 짓밟기 검증
            report.Check(StompDamage >= 0, Table, "Stomp", "Damage는 0 이상이어야 합니다.");
            report.Check(StompTelegraphTime >= 0f, Table, "Stomp", "TelegraphTime은 0 이상이어야 합니다.");
            report.Check(StompRadius > 0f, Table, "Stomp", "Radius는 0보다 커야 합니다.");
            report.Check(StompRecoveryTime >= 0f, Table, "Stomp", "RecoveryTime은 0 이상이어야 합니다.");
            report.Check(StompStartMaxDistance >= 0f, Table, "Stomp", "StompStartMaxDistance는 0 이상이어야 합니다.");
            // report.Check(StompCooldown >= 0f, Table, "Stomp", "Cooldown은 0 이상이어야 합니다.");

            // 마그마 포격 검증
            report.Check(BarrageDamage >= 0, Table, "Barrage", "Damage는 0 이상이어야 합니다.");
            report.Check(BarrageRadius > 0f, Table, "Barrage", "Radius는 0보다 커야 합니다.");
            report.Check(BarrageTelegraphTime >= 0f, Table, "Barrage", "TelegraphTime은 0 이상이어야 합니다.");
            report.Check(BarrageInterval > 0f, Table, "Barrage", "Interval은 0보다 커야 합니다.");
            report.Check(BarrageRecoveryTime >= 0f, Table, "Barrage", "RecoveryTime은 0 이상이어야 합니다.");
            // report.Check(BarrageCooldown >= 0f, Table, "Barrage", "Cooldown은 0 이상이어야 합니다.");
            report.Check(BarrageSpreadRadius > 0f, Table, "Barrage", "BarrageSpreadRadius는 0보다 커야 합니다.");
            report.Check(BarrageFlightTime > 0f, Table, "Barrage", "BarrageFlightTime은 0보다 커야 합니다.");
            report.Check(BarrageVolleyCount > 0, Table, "Barrage", "VolleyCount는 1 이상이어야 합니다.");
            // 안테나 개방 기준 검증
            report.Check(SixOpenHpRatio > 0f && SixOpenHpRatio < FourOpenHpRatio && FourOpenHpRatio < 1f, Table, "Antenna", "체력 기준은 0 < SixOpenHpRatio < FourOpenHpRatio < 1이어야 합니다.");
            
            // 공기 흡입 검증
            report.Check(SuctionTelegraphTime >= 0f, Table, "Suction", "TelegraphTime은 0 이상이어야 합니다.");
            report.Check(SuctionMaxDuration > 0f, Table, "Suction", "MaxDuration은 0보다 커야 합니다.");
            report.Check(SuctionRecoveryTime >= 0f, Table, "Suction", "RecoveryTime은 0 이상이어야 합니다.");
            report.Check(SuctionNearRadius > 0f, Table, "Suction", "NearRadius는 0보다 커야 합니다.");
            report.Check(SuctionFarExtraSpeed > SuctionNearExtraSpeed, Table, "Suction", "FarExtraSpeed는 NearExtraSpeed보다 커야 합니다.");
            report.Check(SuctionNearExtraSpeed >= 0f, Table, "Suction", "NearExtraSpeed는 0 이상이어야 합니다.");
            report.Check(SuctionNearDwellTime > 0f && SuctionNearDwellTime <= SuctionMaxDuration, Table, "Suction", "NearDwellTime은 0보다 크고 MaxDuration 이하여야 합니다.");
            report.Check(SuctionStartMinDistance >= 0f, Table, "Suction", "SuctionStartMinDistance는 0 이상이어야 합니다.");
        }
    }
}
