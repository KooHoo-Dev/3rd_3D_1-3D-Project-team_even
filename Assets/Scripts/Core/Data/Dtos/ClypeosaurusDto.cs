namespace HybridShooter.Core.Data.Dtos
{
    public sealed class ClypeosaurusDto
    {
        public int BossId;
        
        // 짓밟기
        public int StompDamage;
        public float StompTelegraphTime;
        public float StompRadius;
        public float StompRecoveryTime;
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

        // 안테나 전격
        public int ElectricDamage;
        public float ElectricRange;
        public float ElectricWidth;
        public float ElectricChargeTime;
        public float ElectricRecoveryTime;
        // public float ElectricCooldown;

        // 본체 HP에 따른 안테나 개방 기준
        public float FourOpenHpRatio; // 예: 0.7 = HP 70% 이하
        public float SixOpenHpRatio;  // 예: 0.4 = HP 40% 이하
    }
}
