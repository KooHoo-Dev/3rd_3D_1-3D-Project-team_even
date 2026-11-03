namespace HybridShooter.Core.Data
{
    // 테이블 문자열 값과 이름이 1:1 로 대응한다. (대소문자 무시)
    // 값을 추가할 땐 끝에 추가하고, 테이블 주석과 Docs/Tables/README.md 도 함께 갱신할 것.

    public enum FireMode { Single, Auto }

    public enum MonsterGrade { Weak, Mid, Boss }

    /// <summary>몬스터 AI 역할. Boss 는 Grade 가 Boss 인 몬스터에만 쓴다</summary>
    public enum MonsterRole { Melee, Air, Rusher, Infiltrator, ZonePressure, Boss, SelfDestruct }

    /// <summary>같은 프리팹의 메시 변종 (무스코모프 날개 ON/OFF)</summary>
    public enum MeshVariant { Default, WingsOff, WingsOn }

    /// <summary>공격 분류. None = 기본 공격, 나머지는 보스 특수 공격 (Entrance = 등장 공격, 1회성)</summary>
    public enum BossPatternTag { None, Entrance, GroundSlam, Bombard, Unique }

    public enum AttackShape { Melee, Circle, Cone, Line, Projectile }

    /// <summary>공격 발동 조건. None = 쿨다운만 만족하면 사용</summary>
    public enum TriggerType { None, PlayerStationary }

    public enum EffectType { DamageOverTime }

    public enum PartExposure { Always, WhileAttacking, WhileRecovering }

    /// <summary>군중 생성 방식 (BL-015)</summary>
    public enum SpawnPattern
    {
        Surround,  // 여러 방향에서
        Flank,     // 플레이어 측면/시야 밖
        Air,       // 공중
        Herd,      // 한 방향에 밀집한 무리
        Nest,      // 둥지: 같은 웨이브·같은 StartDelay 의 Nest 스포너들이 링 위 한 지점 주위에서 종류를 섞어 계속 나온다
        Crowd,     // 군중 묶음: 같은 웨이브·같은 StartDelay 의 Crowd 스포너들이 한 지점 주위에서 나오고, 묶음 전용 CrowdController 의 슬롯 포위 통제를 받는다
    }

    /// <summary>Weapon = 1회용 보급 무기 (SupplyTable.WeaponId)</summary>
    public enum SupplyType { Grenade, Heal, Weapon }

    /// <summary>
    /// 강화 종류 (UpgradeTable). 새 값은 끝에 추가한다.
    /// GrenadeCapacity 는 v3 에서 상점에서 뺐다 (테이블 행 없음 · 코드 참조 때문에 값은 유지)
    /// </summary>
    public enum UpgradeType
    {
        ReloadSpeed, MagazineSize, GrenadeCapacity,
        GrenadeRadius, MaxHealth, MaxStamina, StaminaRegen, HealCapacity, LuckyGirl,
    }

    /// <summary>강화 카드 등급 (UpgradeGradeTable). 순서 = 낮은 등급 → 높은 등급</summary>
    public enum UpgradeGrade { Poor, Common, Epic }

    /// <summary>믹서 그룹·볼륨 단위. Monster 는 보스 포효 때 따로 줄이기(덕킹) 위해 Sfx 에서 분리</summary>
    public enum SoundCategory { Bgm, Sfx, Monster, Ui }

    /// <summary>무기 슬롯 분류. Sidearm = 1번 칸 고정 권총 · Loadout = 출발 전 2·3번 칸 선택 · Supply = 4번 보급 칸 전용(1회용)</summary>
    public enum WeaponCategory { Sidearm, Loadout, Supply }

    /// <summary>군중전 스테이지 종류. Normal = StartTime 에 시작 · ClearBonus = EndTime 전에 본 웨이브를 전멸시키면 1번 시작하는 특수 웨이브</summary>
    public enum WaveStageKind { Normal, ClearBonus }

    /// <summary>
    /// 라운드 유형 = 군중전이 끝나는 조건. CrowdToBoss = 보스 시각에 보스 등장 → 보스 처치로 클리어 ·
    /// Annihilation(섬멸) = PhaseTime 동안 소환, 누적 처치 목표(KillRatio) 달성으로 클리어 · Survival(생존) = PhaseTime 생존으로 클리어 ·
    /// AnnihilationToBoss(섬멸 후 보스) = 섬멸 목표 달성 → 보스 등장 → 보스 처치로 클리어
    /// </summary>
    public enum RoundType { CrowdToBoss, Annihilation, Survival, AnnihilationToBoss }
}
