namespace HybridShooter.Player
{
    /// <summary>시점 모드</summary>
    public enum ViewMode { FirstPerson, ThirdPerson }

    /// <summary>
    /// 입력으로 할 수 있는 행동 (PlayerController.SetAllowedAbilities). 튜토리얼처럼 지시한 행동만 쓰게 할 때 끈다.
    /// 시선(마우스)·커서 토글(Esc)·시점 전환은 항상 허용이라 여기에 없다
    /// </summary>
    [System.Flags]
    public enum PlayerAbility
    {
        None = 0,
        Move = 1 << 0,
        Sprint = 1 << 1,
        Jump = 1 << 2,
        Roll = 1 << 3,
        Fire = 1 << 4,
        Aim = 1 << 5,
        Reload = 1 << 6,
        FireMode = 1 << 7,
        SwitchWeapon = 1 << 8,
        Grenade = 1 << 9,
        Heal = 1 << 10,
        Interact = 1 << 11,

        /// <summary>이동 · 질주 · 점프 · 구르기</summary>
        Movement = Move | Sprint | Jump | Roll,
        /// <summary>사격 · 조준 · 재장전 · 사격 모드 (무기 교체 제외)</summary>
        Shooting = Fire | Aim | Reload | FireMode,
        All = ~0,
    }
}
