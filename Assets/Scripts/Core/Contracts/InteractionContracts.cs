using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    // 에임으로 고르고 상호작용 키(기본 F)로 쓰는 대상의 계약.
    // 플레이어(PlayerInteractor)가 "사용"하고, 보급 상자 등 대상 파트가 "구현"한다.

    /// <summary>
    /// 플레이어가 조준하고 상호작용 키로 쓰는 대상 (보급 무기 상자 등). 콜라이더(트리거 가능)와 같은 오브젝트나 부모에 붙인다.
    ///  - 플레이어가 GetComponentInParent&lt;IInteractable&gt;() 로 찾는다
    ///  - 거리 판정은 플레이어 몸 ↔ InteractTransform (플레이어 쪽 사거리). 그 외 조건은 CanInteract 에서
    ///  - user = 플레이어 루트. 받는 쪽 기능은 user.GetComponentInParent&lt;ISupplyWeaponUser&gt;() 처럼 찾는다
    /// </summary>
    public interface IInteractable
    {
        /// <summary>거리 외 조건 (이미 획득됨 등). false 면 프롬프트도 뜨지 않는다</summary>
        bool CanInteract(GameObject user);
        /// <summary>HUD 표시 정보. 조준하는 동안 매 프레임 불릴 수 있으니 가볍게</summary>
        InteractPrompt GetPrompt(GameObject user);
        /// <summary>상호작용 실행. 성공하면 true</summary>
        bool Interact(GameObject user);
        /// <summary>거리 기준점</summary>
        Transform InteractTransform { get; }
    }

    /// <summary>
    /// 조준하지 않아도 근처에서 상호작용 키로 쓸 수 있는 대상 (보급 상자). 멤버 없는 표식 계약.
    ///  - 대상은 자기 획득 영역을 트리거 콜라이더(자식 가능)로 둔다. 영역 크기는 대상 쪽이 정한다
    ///  - 플레이어(PlayerInteractor)는 조준 대상이 없을 때, 몸이 들어가 있는 영역 중 가장 가까운 대상을 고른다
    ///  - 이 계약이 없는 IInteractable 은 지금처럼 조준해야만 쓸 수 있다
    /// </summary>
    public interface IProximityInteractable : IInteractable { }

    /// <summary>상호작용 프롬프트 HUD 에 표시할 정보</summary>
    public readonly struct InteractPrompt
    {
        /// <summary>대상 이름 (예: "로켓 발사기 보급 상자")</summary>
        public readonly string Title;
        /// <summary>설명 (무기명·1회용·탄 수 등). 비어도 됨</summary>
        public readonly string Description;
        /// <summary>행동 이름 (예: "획득"). HUD 가 "[F] 획득" 으로 표시</summary>
        public readonly string ActionLabel;
        /// <summary>Resources/UI/WeaponIcons/{IconKey} 등 아이콘 키. 비어도 됨</summary>
        public readonly string IconKey;
        /// <summary>경고 (예: "현재 ○○ 은(는) 버려집니다"). 비어 있으면 표시 안 함</summary>
        public readonly string Warning;

        public InteractPrompt(string title, string description, string actionLabel, string iconKey = "", string warning = "")
        {
            Title = title ?? "";
            Description = description ?? "";
            ActionLabel = actionLabel ?? "";
            IconKey = iconKey ?? "";
            Warning = warning ?? "";
        }

        public bool Equals(InteractPrompt other) =>
            Title == other.Title && Description == other.Description && ActionLabel == other.ActionLabel
            && IconKey == other.IconKey && Warning == other.Warning;
    }

    /// <summary>
    /// 보급 무기 받기 → 플레이어 (무기 슬롯 4번 = 보급 칸). ServiceLocator 에 등록되고,
    /// 플레이어 루트에 붙어 있어 GetComponentInParent&lt;ISupplyWeaponUser&gt;() 로도 찾을 수 있다.
    ///  - 보급 무기는 1회용: 보급 상자로 탄이 충전되지 않고, 탄창·예비 탄창을 모두 쓰면 칸에서 사라진다 (이전에 들던 칸으로 돌아감)
    ///  - 다른 보급 무기를 받으면 이전 보급 무기는 버려진다
    /// 예) 무기 상자: if (user.GetComponentInParent&lt;ISupplyWeaponUser&gt;()?.EquipSupplyWeapon(weaponId) == true) Remove();
    /// </summary>
    public interface ISupplyWeaponUser
    {
        /// <summary>지금 보급 칸의 무기 WeaponTable Id. 0 = 비어 있음</summary>
        int SupplyWeaponId { get; }
        /// <summary>받을 수 있는지. WeaponTable Category = Supply 이고 같은 무기를 이미 들고 있지 않아야 한다</summary>
        bool CanEquipSupplyWeapon(int weaponId);
        /// <summary>보급 칸에 장착 (시작 탄약, 바로 꺼내 듦). 이전 보급 무기는 버려진다. 실패하면 false</summary>
        bool EquipSupplyWeapon(int weaponId);
    }

    /// <summary>
    /// 무기 해금 여부 (출발 전 무기 선택 화면이 사용). 해금 시스템이 구현해 ServiceLocator 에 등록한다.
    /// 등록되지 않았으면 모든 무기가 해금된 것으로 본다.
    /// </summary>
    public interface IWeaponUnlocks
    {
        bool IsUnlocked(int weaponId);
    }

    /// <summary>
    /// 라운드(스테이지) 시작을 붙잡는 화면 (무기 선택 화면이 구현해 ServiceLocator 에 등록한다).
    /// RunDirector 는 HoldsStart 동안 startOnPlay 자동 시작과 다음 라운드 진행을 미룬다. 등록되지 않았으면 바로 진행
    /// </summary>
    public interface IRoundStartGate
    {
        bool HoldsStart { get; }
    }
}
