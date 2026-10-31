using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 테스트용 보급 무기 픽업. 조준하고 상호작용 키(F)를 누르면 보급 칸(4번)에 weaponId 무기를 장착한다 (ISupplyWeaponUser).
    /// 콜라이더(트리거 가능)와 같은 오브젝트에 붙인다. 실제 보급 상자 연동은 보급 담당이 SupplyCrate 쪽에서 IInteractable 을 구현한다.
    /// </summary>
    public sealed class SupplyWeaponPickup : MonoBehaviour, IInteractable
    {
        [Tooltip("WeaponTable Id (Category = Supply). 예: 2007 로켓 발사기, 2008 유탄 발사기")]
        [SerializeField] private int weaponId = 2007;
        [Tooltip("획득하면 사라진다. 끄면 계속 남아 여러 번 주울 수 있다 (반복 테스트용)")]
        [SerializeField] private bool consumeOnUse = true;

        public Transform InteractTransform => transform;

        public bool CanInteract(GameObject user)
        {
            var receiver = user != null ? user.GetComponentInParent<ISupplyWeaponUser>() : null;
            return receiver != null && receiver.CanEquipSupplyWeapon(weaponId);
        }

        public InteractPrompt GetPrompt(GameObject user)
        {
            WeaponDef def = DataManager.Weapons.TryGet(weaponId, out var d) ? d : null;
            string weaponName = def != null ? def.Name : $"무기 {weaponId}";
            string ammo = def != null ? $" · 탄 {def.MagazineSize} + 예비 {def.StartMagazines}탄창" : "";

            string warning = "";
            var receiver = user != null ? user.GetComponentInParent<ISupplyWeaponUser>() : null;
            if (receiver != null && receiver.SupplyWeaponId != 0 && receiver.SupplyWeaponId != weaponId)
            {
                string current = DataManager.Weapons.TryGet(receiver.SupplyWeaponId, out var cur) ? cur.Name : $"무기 {receiver.SupplyWeaponId}";
                warning = $"현재 {current} 은(는) 버려집니다";
            }

            return new InteractPrompt($"{weaponName} 보급 상자", $"보급 무기 · 1회용{ammo}", "획득", def != null ? def.IconKey : "", warning);
        }

        public bool Interact(GameObject user)
        {
            var receiver = user != null ? user.GetComponentInParent<ISupplyWeaponUser>() : null;
            if (receiver == null || receiver.EquipSupplyWeapon(weaponId) == false) return false;
            if (consumeOnUse) Destroy(gameObject);
            return true;
        }
    }
}
