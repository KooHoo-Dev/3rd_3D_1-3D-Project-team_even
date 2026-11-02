namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>TyranopodeTable 한 행 (두 번째 보스 전용 수치). 열 이름 = 필드 이름</summary>
    public sealed class TyranopodeDto
    {
        public int BossId;

        // 공통·페이즈
        public float Phase2HpRatio;
        public int Phase2BrokenParts;
        public float TransitionTime;
        public float MeleeRange;
        public float FarRange;
        public float CircleRadius;
        public float CircleSpeed;
        public float CircleTimeMin;
        public float CircleTimeMax;
        public float ComboChance;
        public float Phase2ComboChance;
        public float Phase2WindupMultiplier;
        public float Phase2DamageMultiplier;
        public float Phase2SpeedMultiplier;

        // 부위 (부위별 피해 배율·Hp 는 MonsterPartTable 의 BossId 행)
        public float TentacleBrokenDamageMultiplier;
        public float LegBrokenSpeedMultiplier;
        public float LegBrokenJumpWeightMultiplier;
        public float LegBrokenWallGroggyMultiplier;
        public float SacBrokenSpitDamageMultiplier;
        public int SacBrokenEggReduce;
        public float HeadBrokenSummonMultiplier;

        // 그로기
        public float GroggyTime;
        public float GroggyDamageMultiplier;
        public float WallGroggyTime;

        // 촉수 휘두르기
        public int TentacleDamage;
        public float TentacleRange;
        public float TentacleAngle;
        public float TentacleWindup;
        public float TentacleRecovery;

        // 물기·점프 물기
        public int BiteDamage;
        public float BiteRange;
        public float BiteWindup;
        public float BiteRecovery;
        public float JumpBiteMaxDistance;
        public float JumpBiteWidth;
        public float JumpBiteHitRadius;

        // 점프 내려찍기
        public int SmashDamage;
        public float SmashRadius;
        public float SmashWindup;
        public float SmashRecovery;
        public float Phase2SmashRadiusMultiplier;
        public float SmashMaxLeap;

        // 돌진
        public int ChargeDamage;
        public float ChargeSpeed;
        public float ChargeMaxDistance;
        public float ChargeHitRadius;
        public float ChargeWindup;
        public float ChargeRecovery;
        public int Phase2ChargeCount;

        // 침 뱉기
        public int SpitDamage;
        public int SpitCount;
        public float SpitInterval;
        public float SpitFlightTime;
        public float SpitArcHeight;
        public float SpitRadius;
        public float SpitWindup;
        public float SpitRecovery;
        public int Phase2SpitBonus;
        public float Phase2SpitInterval;
        public int HeadBrokenSpitReduce;

        // 알 던지기
        public int EggCount;
        public int Phase2EggBonus;
        public int EggHp;
        public float EggHatchTime;
        public int EggMinionId;
        public int EggMinionCount;
        public float EggScatterRadius;
        public float EggFlightTime;
        public float EggWindup;
        public float EggRecovery;
        public float EggCooldown;
        public float EggQuietTime;
        public float EggQuietCircleMultiplier;
        public float EggArcAngle;

        // 땅울림
        public float TremorRadius;
        public float TremorSpread;
        public float TremorDuration;
        public float TremorNearRadius;
        public int TremorNearDamage;
        public float TremorWindup;
        public float TremorRecovery;
        public float TremorCooldown;

        // 포효 소환·포자
        public float SummonStartHpRatio;
        public float SummonCooldown;
        public float SummonWindup;
        public float SummonInterval;
        public float SummonRate;
        public float MinionSpeedMultiplier;
        public float MinionApproachSpeedMultiplier;
        public float MinionApproachRatio;
        public int SummonMaxAlive;
        public string SummonMinionIds;
        public float BeaconHeight;
        public int BeaconHp;
        public float BeaconDuration;
        public float BeaconFailWarnTime;
        public int BeaconFailDamage;
        public int ImmuneHintHits;
        public float WaveSpeed;
        public int WaveCount;
        public float WaveInterval;

        // 기믹 중 플레이어 사망 → 잡몹 에너지 흡수 → 보호막
        public float ShieldPerMinion;
        public float ShieldMaxRatio;
        public int ShieldLayers;
        public float ShieldBreakGroggyTime;
        public float EnergyFlightTime;

        // 등장 연출: 남은 웨이브 몬스터 흡수 → 보호막
        public float IntroShieldPerMonster;
        public float IntroShieldMaxRatio;
        public bool IntroBriefAfterFirst;

        // 촉수 폭풍 (페이즈 2 기믹)
        public int StormWaves;
        public float StormSectorAngle;
        public float StormRadius;
        public float StormTelegraph;
        public float StormInterval;
        public float StormSweepTime;
        public float StormJumpClear;
        public float StormFirstExtraTelegraph;
        public int StormDamage;
        public float StormCooldown;
        public float StormWindup;
        public float StormRecovery;

        // 숨은 난이도 보정
        public float AssistMaxDelayMultiplier;
        public float AssistComboReduce;
        public float AssistHeavyReduce;
        public float AssistPerDeath;
        public float AssistSmoothTime;
    }
}
