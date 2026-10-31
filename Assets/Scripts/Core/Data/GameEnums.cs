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
    }

    public enum SupplyType { Grenade, Heal }

    public enum UpgradeType { ReloadSpeed, MagazineSize, GrenadeCapacity }

    /// <summary>믹서 그룹·볼륨 단위. Monster 는 보스 포효 때 따로 줄이기(덕킹) 위해 Sfx 에서 분리</summary>
    public enum SoundCategory { Bgm, Sfx, Monster, Ui }

    /// <summary>무기 슬롯 분류. Sidearm = 1번 칸 고정 권총 · Loadout = 출발 전 2·3번 칸 선택 · Supply = 4번 보급 칸 전용(1회용)</summary>
    public enum WeaponCategory { Sidearm, Loadout, Supply }
}
