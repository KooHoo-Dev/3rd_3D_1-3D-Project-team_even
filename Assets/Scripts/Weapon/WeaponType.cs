namespace HybridShooter.Weapon
{
    /// <summary>
    /// 무기 종류. 숫자는 Animator 의 WeaponType 파라미터 값과 같다 (애니메이션 세트를 고르는 데 쓰임).
    /// 0 = 맨손, 1 = 라이플(Assault Rifle 애니), 2 = 샷건, 3 = 로켓런처(Bazooka 애니), 4 = 미니건
    /// </summary>
    public enum WeaponType
    {
        Unarmed = 0,
        Rifle = 1,
        Shotgun = 2,
        RocketLauncher = 3,
        Minigun = 4,
    }

    /// <summary>탄을 쏘는 방식</summary>
    public enum ShotKind
    {
        /// <summary>즉시 명중 판정 (라이플·샷건·미니건)</summary>
        Hitscan,
        /// <summary>날아가는 탄을 생성 (로켓)</summary>
        Projectile,
    }
}
