namespace HybridShooter.Core.Data.Dtos
{
    public sealed class ClypeosaurusDto
    {
        public int BossId;

        // 일반 공격
        public int BasicAttackDamage;
        public float BasicAttackCooldown;
        
        // 등장 공격
        public int EntranceDamage;
        public float EntranceRadius;
        public float EntranceTelegraphTime;
        public float EntranceRecoveryTime;
        
        // 짓밟기
        public int StompDamage;
        public float StompTelegraphTime;
        public float StompRadius;
        public float StompRecoveryTime;
        public float StompStartMaxDistance;
        // public float StompCooldown;

        // 마그마 포격
        public int BarrageDamage;
        public float BarrageRadius;
        public float BarrageTelegraphTime;
        public float BarrageInterval;
        public float BarrageRecoveryTime;
        // public float BarrageCooldown;
        public float BarrageSpreadRadius;
        public float BarrageFlightTime;
        public int BarrageVolleyCount;

        // 본체 HP에 따른 안테나 개방 기준
        public float FourOpenHpRatio; // 예: 0.7 = HP 70% 이하
        public float SixOpenHpRatio;  // 예: 0.4 = HP 40% 이하
        
        // 공기 흡입
        public float SuctionTelegraphTime;
        public float SuctionMaxDuration;
        public float SuctionRecoveryTime;
        public float SuctionNearRadius;
        public float SuctionFarExtraSpeed;
        public float SuctionNearExtraSpeed;
        public float SuctionNearDwellTime;
        public float SuctionStartMinDistance;
    }
}
