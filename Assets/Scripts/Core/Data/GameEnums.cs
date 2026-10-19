namespace HybridShooter.Core.Data
{
    // 테이블 문자열 값과 이름이 1:1 로 대응한다. (대소문자 무시)
    // 값을 추가할 땐 끝에 추가하고, 테이블 주석과 Docs/Tables/README.md 도 함께 갱신할 것.

    public enum FireMode { Single, Auto }

    /// <summary>몬스터 역할 (백로그 E3/E5)</summary>
    public enum MonsterRole
    {
        Melee,        // 기본 근접형 (BL-010)
        PoisonFog,    // 위치 압박형 (BL-011)
        Infiltrator,  // 침투형 (BL-012)
        Aerial,       // 공중 공격형 (BL-013)
        Charger,      // 일반 돌진형 (BL-014)
        Boss,         // 보스 (E5)
    }

    /// <summary>몬스터 스킬 슬롯</summary>
    public enum SkillSlot
    {
        Basic,     // 일반 몬스터 고유 공격 (독안개 등)
        Entrance,  // 보스 등장 공격 (BL-023)
        Pattern,   // 보스 반복 패턴 (BL-024~027)
    }

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

    public enum SoundCategory { Bgm, Sfx, Ui }
}
