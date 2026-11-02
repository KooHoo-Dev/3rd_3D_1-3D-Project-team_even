using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 두 번째 보스 Tyranopode 전용 수치 (TyranopodeTable, 단일 행). BossTable 의 MaxHp·MoveSpeed 와 함께 쓴다.
    /// 값은 열 이름 그대로 읽기 전용으로 내보낸다. 열 설명은 TyranopodeTable.tsv 주석 참고
    /// </summary>
    public sealed class TyranopodeDef
    {
        public const string Table = "TyranopodeTable";

        private readonly TyranopodeDto d;

        public int BossId => d.BossId;

        // 공통·페이즈
        public float Phase2HpRatio => d.Phase2HpRatio;
        public int Phase2BrokenParts => d.Phase2BrokenParts;
        public float TransitionTime => d.TransitionTime;
        public float MeleeRange => d.MeleeRange;
        public float FarRange => d.FarRange;
        public float CircleRadius => d.CircleRadius;
        public float CircleSpeed => d.CircleSpeed;
        public float CircleTimeMin => d.CircleTimeMin;
        public float CircleTimeMax => d.CircleTimeMax;
        public float ComboChance => d.ComboChance;
        public float Phase2ComboChance => d.Phase2ComboChance;
        public float Phase2WindupMultiplier => d.Phase2WindupMultiplier;
        public float Phase2DamageMultiplier => d.Phase2DamageMultiplier;
        public float Phase2SpeedMultiplier => d.Phase2SpeedMultiplier;

        // 부위 (부위별 피해 배율·Hp 는 MonsterPartTable 의 BossId 행 → BossDef.FindPart)
        public float TentacleBrokenDamageMultiplier => d.TentacleBrokenDamageMultiplier;
        public float LegBrokenSpeedMultiplier => d.LegBrokenSpeedMultiplier;
        public float LegBrokenJumpWeightMultiplier => d.LegBrokenJumpWeightMultiplier;
        public float LegBrokenWallGroggyMultiplier => d.LegBrokenWallGroggyMultiplier;
        public float SacBrokenSpitDamageMultiplier => d.SacBrokenSpitDamageMultiplier;
        /// <summary>발광 주머니가 부서지면 던지는 알이 이만큼 줄어든다</summary>
        public int SacBrokenEggReduce => d.SacBrokenEggReduce;
        public float HeadBrokenSummonMultiplier => d.HeadBrokenSummonMultiplier;

        // 그로기
        public float GroggyTime => d.GroggyTime;
        public float GroggyDamageMultiplier => d.GroggyDamageMultiplier;
        public float WallGroggyTime => d.WallGroggyTime;

        // 촉수 휘두르기
        public int TentacleDamage => d.TentacleDamage;
        public float TentacleRange => d.TentacleRange;
        public float TentacleAngle => d.TentacleAngle;
        public float TentacleWindup => d.TentacleWindup;
        public float TentacleRecovery => d.TentacleRecovery;

        // 물기·점프 물기
        public int BiteDamage => d.BiteDamage;
        public float BiteRange => d.BiteRange;
        public float BiteWindup => d.BiteWindup;
        public float BiteRecovery => d.BiteRecovery;
        public float JumpBiteMaxDistance => d.JumpBiteMaxDistance;
        /// <summary>점프 물기 예고 폭(m)</summary>
        public float JumpBiteWidth => d.JumpBiteWidth;
        /// <summary>점프 물기 착지 적중 반경(m, 몸 앞쪽)</summary>
        public float JumpBiteHitRadius => d.JumpBiteHitRadius;

        // 점프 내려찍기
        public int SmashDamage => d.SmashDamage;
        public float SmashRadius => d.SmashRadius;
        public float SmashWindup => d.SmashWindup;
        public float SmashRecovery => d.SmashRecovery;
        public float Phase2SmashRadiusMultiplier => d.Phase2SmashRadiusMultiplier;
        /// <summary>내려찍기 최대 도약 거리(m). 플레이어가 더 멀면 이만큼만 뛴다</summary>
        public float SmashMaxLeap => d.SmashMaxLeap;

        // 돌진
        public int ChargeDamage => d.ChargeDamage;
        public float ChargeSpeed => d.ChargeSpeed;
        public float ChargeMaxDistance => d.ChargeMaxDistance;
        public float ChargeHitRadius => d.ChargeHitRadius;
        public float ChargeWindup => d.ChargeWindup;
        public float ChargeRecovery => d.ChargeRecovery;
        public int Phase2ChargeCount => d.Phase2ChargeCount;

        // 침 뱉기
        public int SpitDamage => d.SpitDamage;
        /// <summary>연사 발 수 (페이즈 1, 머리 멀쩡)</summary>
        public int SpitCount => d.SpitCount;
        /// <summary>연사 간격(초)</summary>
        public float SpitInterval => d.SpitInterval;
        /// <summary>입에서 바닥까지 날아가는 시간(초) = 떨어질 자리 예고 시간</summary>
        public float SpitFlightTime => d.SpitFlightTime;
        /// <summary>포물선 꼭대기 높이(m, 출발·도착을 잇는 선 기준)</summary>
        public float SpitArcHeight => d.SpitArcHeight;
        public float SpitRadius => d.SpitRadius;
        public float SpitWindup => d.SpitWindup;
        public float SpitRecovery => d.SpitRecovery;
        /// <summary>페이즈 2 에서 더 쏘는 발 수</summary>
        public int Phase2SpitBonus => d.Phase2SpitBonus;
        /// <summary>페이즈 2 연사 간격(초)</summary>
        public float Phase2SpitInterval => d.Phase2SpitInterval;
        /// <summary>머리가 부서지면 덜 쏘는 발 수</summary>
        public int HeadBrokenSpitReduce => d.HeadBrokenSpitReduce;

        // 알 던지기
        public int EggCount => d.EggCount;
        public int Phase2EggBonus => d.Phase2EggBonus;
        public int EggHp => d.EggHp;
        public float EggHatchTime => d.EggHatchTime;
        public int EggMinionId => d.EggMinionId;
        public int EggMinionCount => d.EggMinionCount;
        public float EggScatterRadius => d.EggScatterRadius;
        public float EggFlightTime => d.EggFlightTime;
        public float EggWindup => d.EggWindup;
        public float EggRecovery => d.EggRecovery;
        public float EggCooldown => d.EggCooldown;
        /// <summary>알을 던진 뒤 공격하지 않는 시간(초): 플레이어가 돌아서서 알을 깰 틈</summary>
        public float EggQuietTime => d.EggQuietTime;
        /// <summary>그동안 맴도는 반경 배율 (멀찍이 물러난다)</summary>
        public float EggQuietCircleMultiplier => d.EggQuietCircleMultiplier;
        /// <summary>알이 떨어지는 범위: 플레이어 시선 앞쪽 이 각도(도) 안</summary>
        public float EggArcAngle => d.EggArcAngle;

        // 땅울림
        public float TremorRadius => d.TremorRadius;
        public float TremorSpread => d.TremorSpread;
        public float TremorDuration => d.TremorDuration;
        public float TremorNearRadius => d.TremorNearRadius;
        public int TremorNearDamage => d.TremorNearDamage;
        public float TremorWindup => d.TremorWindup;
        public float TremorRecovery => d.TremorRecovery;
        public float TremorCooldown => d.TremorCooldown;

        // 포효 소환·포자
        public float SummonStartHpRatio => d.SummonStartHpRatio;
        public float SummonCooldown => d.SummonCooldown;
        public float SummonWindup => d.SummonWindup;
        public float SummonInterval => d.SummonInterval;
        /// <summary>포효 소환 중 초당 벽을 넘어오는 잡몹 수</summary>
        public float SummonRate => d.SummonRate;
        /// <summary>보스가 부른 잡몹(소환·알)의 이동 속도 배율 (MonsterTable 값에 곱함, 웨이브 잡몹에는 영향 없음)</summary>
        public float MinionSpeedMultiplier => d.MinionSpeedMultiplier;
        /// <summary>포효로 벽을 넘어온 잡몹이 처음 구간에서 달리는 속도 배율 (MonsterTable 값에 곱함)</summary>
        public float MinionApproachSpeedMultiplier => d.MinionApproachSpeedMultiplier;
        /// <summary>빠른 구간 = 소환 위치에서 (그때 플레이어까지 거리 × 이 비율) 만큼</summary>
        public float MinionApproachRatio => d.MinionApproachRatio;
        public int SummonMaxAlive => d.SummonMaxAlive;
        /// <summary>SummonMinionIds("1001|1002") 를 나눈 MonsterTable.Id 목록</summary>
        public IReadOnlyList<int> SummonMinionIds { get; }
        public float BeaconHeight => d.BeaconHeight;
        public int BeaconHp => d.BeaconHp;
        public float BeaconDuration => d.BeaconDuration;
        public float BeaconFailWarnTime => d.BeaconFailWarnTime;
        public int BeaconFailDamage => d.BeaconFailDamage;
        /// <summary>포자 기믹 중 무적인 보스를 이만큼 맞히면 분석 안내를 다시 띄운다</summary>
        public int ImmuneHintHits => d.ImmuneHintHits;
        public float WaveSpeed => d.WaveSpeed;
        /// <summary>포자 파괴 시 퍼지는 파동 횟수. i 번째 파동은 경기장 반지름의 (i+1)/WaveCount 까지 휩쓴다</summary>
        public int WaveCount => d.WaveCount;
        public float WaveInterval => d.WaveInterval;

        // 기믹 중 플레이어 사망 → 잡몹 에너지 흡수 → 보호막
        public float ShieldPerMinion => d.ShieldPerMinion;
        public float ShieldMaxRatio => d.ShieldMaxRatio;
        public int ShieldLayers => d.ShieldLayers;
        public float ShieldBreakGroggyTime => d.ShieldBreakGroggyTime;
        public float EnergyFlightTime => d.EnergyFlightTime;

        // 등장 연출: 남은 웨이브 몬스터 흡수 → 보호막
        public float IntroShieldPerMonster => d.IntroShieldPerMonster;
        public float IntroShieldMaxRatio => d.IntroShieldMaxRatio;
        /// <summary>두 번째 등장부터 짧은 연출</summary>
        public bool IntroBriefAfterFirst => d.IntroBriefAfterFirst;

        /// <summary>보호막이 생길 때마다 흡수량과 별도로(상한 밖) 더하는 기본량 (MaxHp 비율)</summary>
        public float ShieldBaseRatio => d.ShieldBaseRatio;

        // 촉수 폭풍
        /// <summary>촉수 쓸기 횟수 (좌우 번갈아)</summary>
        public int StormWaves => d.StormWaves;
        /// <summary>한 번 쓸 때 지나가는 각도(도, 보스 정면 기준 좌우 절반씩)</summary>
        public float StormSectorAngle => d.StormSectorAngle;
        /// <summary>촉수가 닿는 최소 거리(m)</summary>
        public float StormRadius => d.StormRadius;
        /// <summary>쓸기 예고 때 플레이어 거리에 더하는 여유(m): 예고 동안 물러나도 닿게</summary>
        public float StormChaseMargin => d.StormChaseMargin;
        /// <summary>플레이어를 따라 늘어나는 촉수 거리 상한(m)</summary>
        public float StormMaxRadius => d.StormMaxRadius;
        public float StormTelegraph => d.StormTelegraph;
        /// <summary>쓸기 뒤 촉수를 거두고 다음 예고까지(초)</summary>
        public float StormInterval => d.StormInterval;
        /// <summary>촉수가 한 번 쓸고 지나가는 시간(초)</summary>
        public float StormSweepTime => d.StormSweepTime;
        /// <summary>이 높이(m) 이상 떠 있으면 촉수를 넘는다</summary>
        public float StormJumpClear => d.StormJumpClear;
        /// <summary>처음 쓰는 촉수 폭풍의 첫 쓸기 예고에 더하는 시간(초)</summary>
        public float StormFirstExtraTelegraph => d.StormFirstExtraTelegraph;
        public int StormDamage => d.StormDamage;
        public float StormCooldown => d.StormCooldown;
        public float StormWindup => d.StormWindup;
        public float StormRecovery => d.StormRecovery;

        // 숨은 난이도 보정
        public float AssistMaxDelayMultiplier => d.AssistMaxDelayMultiplier;
        public float AssistComboReduce => d.AssistComboReduce;
        public float AssistHeavyReduce => d.AssistHeavyReduce;
        public float AssistPerDeath => d.AssistPerDeath;
        public float AssistSmoothTime => d.AssistSmoothTime;

        public TyranopodeDef(TyranopodeDto dto, DataLoadReport r)
        {
            d = dto ?? throw new ArgumentNullException(nameof(dto));
            SummonMinionIds = ParseIds(dto.SummonMinionIds, r);

            r.Check(BossId > 0, Table, BossId, "BossId는 1 이상이어야 합니다.");

            r.Check(Phase2HpRatio > 0f && Phase2HpRatio < 1f, Table, "Phase", "Phase2HpRatio는 0과 1 사이여야 합니다.");
            r.Check(Phase2BrokenParts >= 1, Table, "Phase", "Phase2BrokenParts는 1 이상이어야 합니다.");
            r.Check(TransitionTime >= 0f, Table, "Phase", "TransitionTime은 0 이상이어야 합니다.");
            r.Check(MeleeRange > 0f && MeleeRange < FarRange, Table, "Range", "0 < MeleeRange < FarRange 여야 합니다.");
            r.Check(CircleRadius > 0f && CircleSpeed > 0f, Table, "Circle", "CircleRadius·CircleSpeed는 0보다 커야 합니다.");
            r.Check(CircleTimeMin >= 0f && CircleTimeMin <= CircleTimeMax, Table, "Circle", "0 <= CircleTimeMin <= CircleTimeMax 여야 합니다.");
            r.Check(InUnit(ComboChance) && InUnit(Phase2ComboChance), Table, "Combo", "ComboChance는 0~1이어야 합니다.");
            r.Check(Phase2WindupMultiplier > 0f && Phase2DamageMultiplier > 0f && Phase2SpeedMultiplier > 0f, Table, "Phase", "페이즈 2 배율은 0보다 커야 합니다.");

            r.Check(SacBrokenEggReduce >= 0 && EggCount - SacBrokenEggReduce >= 1, Table, "Part", "SacBrokenEggReduce는 0 이상, EggCount - SacBrokenEggReduce >= 1 이어야 합니다.");

            r.Check(GroggyTime > 0f && WallGroggyTime > 0f && GroggyDamageMultiplier > 0f, Table, "Groggy", "그로기 시간·배율은 0보다 커야 합니다.");

            r.Check(TentacleDamage >= 0 && TentacleRange > 0f && TentacleAngle > 0f && TentacleAngle <= 360f, Table, "Tentacle", "피해 >= 0, 사거리 > 0, 0 < 각도 <= 360");
            r.Check(BiteDamage >= 0 && BiteRange > 0f && JumpBiteMaxDistance > BiteRange && JumpBiteWidth > 0f && JumpBiteHitRadius > 0f, Table, "Bite", "피해 >= 0, 0 < BiteRange < JumpBiteMaxDistance, JumpBiteWidth·JumpBiteHitRadius > 0");
            r.Check(SmashDamage >= 0 && SmashRadius > 0f && SmashMaxLeap > 0f, Table, "Smash", "피해 >= 0, 반경·최대 도약 > 0");
            r.Check(ChargeDamage >= 0 && ChargeSpeed > 0f && ChargeMaxDistance > 0f && ChargeHitRadius > 0f && Phase2ChargeCount >= 1, Table, "Charge", "피해 >= 0, 속도·거리·반경 > 0, Phase2ChargeCount >= 1");
            r.Check(SpitDamage >= 0 && SpitRadius > 0f && SpitCount - HeadBrokenSpitReduce >= 1 && HeadBrokenSpitReduce >= 0 && Phase2SpitBonus >= 0
                    && SpitInterval > 0f && Phase2SpitInterval > 0f && SpitFlightTime > 0f && SpitArcHeight >= 0f, Table, "Spit",
                "피해 >= 0, 반경·간격·비행 시간 > 0, SpitCount - HeadBrokenSpitReduce >= 1, Phase2SpitBonus >= 0, SpitArcHeight >= 0");
            r.Check(EggQuietTime >= 0f && EggQuietCircleMultiplier >= 1f && EggArcAngle > 0f && EggArcAngle <= 360f, Table, "Egg", "EggQuietTime >= 0, EggQuietCircleMultiplier >= 1, 0 < EggArcAngle <= 360");
            r.Check(MinionSpeedMultiplier > 0f && MinionApproachSpeedMultiplier > 0f && InUnit(MinionApproachRatio), Table, "Summon",
                "MinionSpeedMultiplier·MinionApproachSpeedMultiplier > 0, MinionApproachRatio 0~1");
            r.Check(ImmuneHintHits >= 1, Table, "Beacon", "ImmuneHintHits는 1 이상이어야 합니다.");
            r.Check(EggCount >= 1 && EggHp > 0 && EggHatchTime > 0f && EggMinionCount >= 0 && EggFlightTime > 0f, Table, "Egg", "EggCount >= 1, Hp > 0, 부화·비행 시간 > 0");
            r.Check(TremorRadius > 0f && TremorSpread >= 0f && TremorDuration >= 0f, Table, "Tremor", "반경 > 0, 퍼짐·지속 >= 0");
            r.Check(InUnit(SummonStartHpRatio) && SummonInterval > 0f && SummonRate > 0f && SummonMaxAlive > 0, Table, "Summon", "SummonStartHpRatio 0~1, 간격 > 0, 개수 >= 0");
            r.Check(SummonMinionIds.Count > 0, Table, "SummonMinionIds", "포효 소환 잡몹 Id 가 하나 이상 있어야 합니다.");
            r.Check(BeaconHp > 0 && BeaconDuration > 0f && BeaconFailWarnTime >= 0f && BeaconFailWarnTime < BeaconDuration, Table, "Beacon", "Hp > 0, 0 <= FailWarnTime < Duration");
            r.Check(WaveSpeed > 0f && WaveCount >= 1 && WaveInterval >= 0f, Table, "Beacon", "WaveSpeed > 0, WaveCount >= 1, WaveInterval >= 0");
            r.Check(ShieldPerMinion > 0f && ShieldMaxRatio > 0f && ShieldMaxRatio < 1f && ShieldLayers >= 1 && ShieldBreakGroggyTime > 0f && EnergyFlightTime > 0f,
                Table, "Shield", "ShieldPerMinion > 0, 0 < ShieldMaxRatio < 1, ShieldLayers >= 1, 그로기·비행 시간 > 0");
            r.Check(IntroShieldPerMonster >= 0f && IntroShieldMaxRatio >= 0f && IntroShieldMaxRatio < 1f, Table, "Intro", "IntroShieldPerMonster >= 0, 0 <= IntroShieldMaxRatio < 1");
            r.Check(ShieldBaseRatio >= 0f && ShieldBaseRatio < 1f, Table, "Shield", "0 <= ShieldBaseRatio < 1");
            r.Check(StormWaves >= 1 && StormSectorAngle > 0f && StormSectorAngle < 180f && StormRadius > 0f && StormTelegraph >= 0f && StormInterval >= 0f
                    && StormSweepTime > 0f && StormJumpClear > 0f && StormFirstExtraTelegraph >= 0f
                    && StormChaseMargin >= 0f && StormMaxRadius >= StormRadius, Table, "Storm",
                "횟수 >= 1, 0 < 각도 < 180 (정면 반원 안), 반경·쓸기 시간·점프 높이 > 0, 예고·간격·추격 여유 >= 0, MaxRadius >= Radius");
            r.Check(AssistMaxDelayMultiplier >= 1f && InUnit(AssistComboReduce) && InUnit(AssistHeavyReduce) && AssistPerDeath >= 0f && AssistSmoothTime >= 0f,
                Table, "Assist", "MaxDelayMultiplier >= 1, Reduce 0~1, PerDeath·SmoothTime >= 0");
        }

        private static bool InUnit(float v) => v >= 0f && v <= 1f;

        private static int[] ParseIds(string text, DataLoadReport r)
        {
            var ids = new List<int>();
            if (string.IsNullOrWhiteSpace(text)) return ids.ToArray();
            foreach (var part in text.Split('|'))
            {
                if (int.TryParse(part.Trim(), out int id)) ids.Add(id);
                else r.Error(Table, "SummonMinionIds", $"'{part}' 는 숫자가 아닙니다 (예: 1001|1002)");
            }
            return ids.ToArray();
        }
    }
}
