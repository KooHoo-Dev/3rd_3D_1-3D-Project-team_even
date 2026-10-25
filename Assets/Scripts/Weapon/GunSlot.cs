using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 플레이어 무기 슬롯 (4칸). 플레이어 루트에 붙는다.
    ///
    ///  - 게임 시작: weaponSocket 아래의 모든 Gun 을 찾아 Initialize(테이블 + 강화 값) → startingGuns 순서대로 슬롯에 넣는다.
    ///    슬롯에 안 들어간 총은 "예비"로 꺼 둔다 (ReplacePrimary(weaponId) 로 꺼내 쓸 수 있음).
    ///  - 0번 슬롯(주무기)만 게임 중 교체된다. 교체된 총은 파괴된다.
    ///  - PlayerController 의 입력을 지금 든 총(CurrentGun)으로 넘긴다. 실제 동작은 각 총이 덮어쓴 함수가 정한다.
    ///  - 총이 필요로 하는 조준선·반동·확대·이동 상태를 PlayerController 와 이어준다.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class GunSlot : MonoBehaviour
    {
        public const int SlotCount = 4;
        public const int PrimarySlot = 0;

        [Tooltip("총이 붙는 곳 (오른손 WeaponSocket). 이 아래의 Gun 을 모두 찾는다")]
        [SerializeField] private Transform weaponSocket;
        [Tooltip("시작할 때 슬롯에 넣을 총 (0 = 주무기, 교체 가능)")]
        [SerializeField] private Gun[] startingGuns = new Gun[SlotCount];
        [Tooltip("시작 시 들고 있을 슬롯")]
        [SerializeField, Range(0, SlotCount - 1)] private int startSlot = 0;

        private readonly Gun[] slots = new Gun[SlotCount];
        private readonly List<Gun> reserve = new List<Gun>();
        private PlayerController player;
        private int currentIndex = -1;

        private const string AimSpeedKey = "GunAim";

        #region 유니티 메시지
        private void Awake()
        {
            player = GetComponent<PlayerController>();
            if (weaponSocket == null) weaponSocket = transform;
        }

        private void Start() => LoadLoadout();

        private void Update()
        {
            if (CurrentGun != null) CurrentGun.Tick(Time.deltaTime);
        }
        #endregion

        #region 외부 API
        /// <summary>인자 = (슬롯 번호, 새로 든 총). UI 무기 표시용</summary>
        public event Action<int, Gun> GunChanged;

        public Gun CurrentGun => currentIndex >= 0 ? slots[currentIndex] : null;
        public int CurrentIndex => currentIndex;
        public Gun GetGun(int index) => index >= 0 && index < SlotCount ? slots[index] : null;
        public IReadOnlyList<Gun> Reserve => reserve;
        public bool IsAiming => CurrentGun != null && CurrentGun.IsAiming;

        // ── 총이 읽는 플레이어 정보 ──
        public Faction Faction => Faction.Player;
        public Transform OwnerRoot => transform;
        public bool IsGrounded => player.IsGrounded;
        /// <summary>0 = 정지, 1 = 달리기 최고 속도</summary>
        public float MoveRatio => Mathf.Clamp01(player.HorizontalSpeed / Mathf.Max(0.01f, player.WalkSpeed * player.SprintMultiplier));
        /// <summary>구르는 중, 수류탄을 던지는 중이거나 죽었으면 쏠 수 없다</summary>
        public bool CanUseWeapon => player.IsAlive && player.IsRolling == false && player.IsExternallyControlled == false && player.IsThrowingGrenade == false;

        /// <summary>
        /// 조준선 = 지금 카메라의 정면. 3인칭에서는 출발점을 플레이어 옆까지 앞당겨
        /// 카메라와 플레이어 사이(등 뒤)의 물체를 맞히지 않게 한다
        /// </summary>
        public Ray AimRay
        {
            get
            {
                Transform cam = player.CurrentCameraTransform;
                if (cam == null) return new Ray(weaponSocket.position, transform.forward);

                Vector3 forward = cam.forward;
                Vector3 origin = cam.position;
                if (player.IsFirstPerson == false)
                {
                    float toPlayer = Vector3.Dot(transform.position - origin, forward);
                    if (toPlayer > 0f) origin += forward * toPlayer;
                }
                return new Ray(origin, forward);
            }
        }

        /// <summary>조준선이 range 안에서 처음 맞는 지점 (없으면 range 끝). 투사체는 총구에서 이 지점을 향해 쏜다</summary>
        public Vector3 GetAimPoint(float range, LayerMask mask)
        {
            Ray ray = AimRay;
            var hits = Physics.RaycastAll(ray, range, mask, QueryTriggerInteraction.Ignore);
            float best = range;
            foreach (var hit in hits)
                if (hit.collider.transform.IsChildOf(transform) == false && hit.distance < best) best = hit.distance;
            return ray.GetPoint(best);
        }

        /// <summary>반동: up = 위로(도), right = 오른쪽(도)</summary>
        public void AddRecoil(float up, float right) => player.AddLookOffset(-up, right);

        public void SetAimZoom(float fovMultiplier, float sensitivityMultiplier, float moveSpeedMultiplier)
        {
            player.SetAimZoom(fovMultiplier, sensitivityMultiplier);
            player.SetSpeedModifier(AimSpeedKey, moveSpeedMultiplier);
        }

        public void ClearAimZoom()
        {
            player.SetAimZoom(1f, 1f);
            player.RemoveSpeedModifier(AimSpeedKey);
        }

        // ── 입력 (PlayerController → 지금 든 총) ──
        public void TriggerDown() { if (CurrentGun != null) CurrentGun.TriggerDown(); }
        public void TriggerUp() { if (CurrentGun != null) CurrentGun.TriggerUp(); }
        public void AimDown() { if (CurrentGun != null) CurrentGun.AimDown(); }
        public void AimUp() { if (CurrentGun != null) CurrentGun.AimUp(); }
        public void Reload() { if (CurrentGun != null) CurrentGun.Reload(); }
        public void ToggleFireMode() { if (CurrentGun != null) CurrentGun.ToggleFireMode(); }

        /// <summary>슬롯 번호로 바꿔 들기. 비어 있거나 이미 든 슬롯이면 false</summary>
        public bool Select(int index)
        {
            if (index < 0 || index >= SlotCount || index == currentIndex || slots[index] == null) return false;

            bool wasAiming = IsAiming;
            if (CurrentGun != null) CurrentGun.Unequip();
            currentIndex = index;
            CurrentGun.Equip();
            if (wasAiming) CurrentGun.AimDown(); // 우클릭을 누른 채 바꾸면 새 총도 조준 유지

            GunChanged?.Invoke(currentIndex, CurrentGun);
            return true;
        }

        /// <summary>휠: 다음(+1)/이전(-1) 의 비어 있지 않은 슬롯</summary>
        public bool SelectNext(int direction)
        {
            if (direction == 0) return false;
            int step = direction > 0 ? 1 : -1;
            for (int i = 1; i < SlotCount; i++)
            {
                int index = ((currentIndex + step * i) % SlotCount + SlotCount) % SlotCount;
                if (slots[index] != null) return Select(index);
            }
            return false;
        }

        /// <summary>
        /// 주무기(0번) 교체. 씬에 있는 총이면 그대로 옮겨 쓰고, 프리팹이면 소켓 아래에 새로 만든다.
        /// 원래 0번 총은 파괴된다. 0번을 들고 있었으면 새 총을 바로 든다
        /// </summary>
        public Gun ReplacePrimary(Gun gunOrPrefab)
        {
            if (gunOrPrefab == null) return null;

            Gun gun = gunOrPrefab.gameObject.scene.IsValid() ? gunOrPrefab : Instantiate(gunOrPrefab, weaponSocket);
            if (gun == slots[PrimarySlot]) return gun;

            reserve.Remove(gun);
            gun.transform.SetParent(weaponSocket, false);
            gun.Initialize(this);
            gun.gameObject.SetActive(false);

            Gun old = slots[PrimarySlot];
            bool holdingPrimary = currentIndex == PrimarySlot;
            if (old != null)
            {
                if (holdingPrimary) old.Unequip();
                Destroy(old.gameObject);
            }

            slots[PrimarySlot] = gun;
            if (holdingPrimary || CurrentGun == null)
            {
                currentIndex = -1;
                Select(PrimarySlot);
            }
            return gun;
        }

        /// <summary>예비 총 중 weaponId 가 같은 총으로 주무기 교체 (보급·보상에서 호출)</summary>
        public Gun ReplacePrimary(int weaponId)
        {
            foreach (var gun in reserve)
                if (gun != null && gun.WeaponId == weaponId) return ReplacePrimary(gun);
            Debug.LogWarning($"{name}: 예비 총 중 WeaponId {weaponId} 가 없습니다", this);
            return null;
        }

        /// <summary>
        /// 편집 도구(IK Pose Lab)용: 지금 든 칸의 총을 다른 총으로 바꿔 든다. 슬롯에 있는 총이면 그 칸을 들고,
        /// 예비 총이면 지금 칸의 총과 맞바꾼다 (원래 총은 예비로. 파괴하지 않는다)
        /// </summary>
        public bool DebugEquip(Gun gun)
        {
            if (gun == null || gun == CurrentGun || currentIndex < 0) return false;
            int index = Array.IndexOf(slots, gun);
            if (index >= 0) return Select(index);
            if (reserve.Remove(gun) == false) return false;

            Gun old = CurrentGun;
            if (old != null)
            {
                old.Unequip();
                reserve.Add(old);
            }
            slots[currentIndex] = gun;
            gun.Equip();
            GunChanged?.Invoke(currentIndex, gun);
            return true;
        }

        /// <summary>테스트용: 예비 총 중 첫 번째로 주무기 교체 (원래 주무기는 사라지므로 예비가 하나씩 줄어든다)</summary>
        public Gun ReplacePrimaryWithNextReserve()
        {
            reserve.RemoveAll(gun => gun == null);
            if (reserve.Count == 0) return null;
            Gun gun = ReplacePrimary(reserve[0]);
            Debug.Log($"주무기 교체 → {gun.DisplayName} (남은 예비 {reserve.Count})");
            return gun;
        }
        #endregion

        #region 내부
        /// <summary>소켓 아래 총 전부 초기화 → 시작 슬롯 배치 → 나머지는 예비로 꺼 둠</summary>
        private void LoadLoadout()
        {
            var all = weaponSocket.GetComponentsInChildren<Gun>(true);
            foreach (var gun in all)
            {
                gun.Initialize(this);
                gun.gameObject.SetActive(false);
            }

            for (int i = 0; i < SlotCount; i++)
                slots[i] = startingGuns != null && i < startingGuns.Length ? startingGuns[i] : null;

            reserve.Clear();
            foreach (var gun in all)
                if (Array.IndexOf(slots, gun) < 0) reserve.Add(gun);

            if (Select(startSlot) == false) SelectNext(1);
        }
        #endregion
    }
}
