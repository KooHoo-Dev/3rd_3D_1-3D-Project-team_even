using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 플레이어 무기 슬롯 (4칸). 플레이어 루트에 붙는다.
    ///
    ///  - 게임 시작: weaponSocket 아래의 모든 Gun 을 찾아 Initialize(테이블 + 강화 값) → WeaponTable.Category 로 배치한다.
    ///    0번 = Sidearm(권총, 고정) · 1·2번 = startingGuns 중 Loadout 순서대로 (출발 전 선택 화면이 ApplyLoadout 으로 덮어씀) · 3번 = 보급 칸(비움).
    ///    슬롯에 안 들어간 총은 "예비"로 꺼 둔다. 총은 파괴하지 않고 예비 ↔ 슬롯을 오간다 (같은 무기를 다시 주울 수 있게).
    ///  - 게임 중 교체는 보급 칸만 (ISupplyWeaponUser.EquipSupplyWeapon). 이전 보급 무기는 탄을 초기화해 예비로 반납(= 버림).
    ///    보급 무기는 1회용: 예비 탄창 보충(보급 상자 등)에서 제외되고, 탄창·예비 탄창을 모두 쓰면 SupplyDepletedDiscardDelay 뒤 칸에서 사라지고 이전에 들던 칸으로 돌아간다. 라운드(스테이지)가 끝나면(재시작·다음 라운드) 칸을 비운다.
    ///  - PlayerController 의 입력을 지금 든 총(CurrentGun)으로 넘긴다. 실제 동작은 각 총이 덮어쓴 함수가 정한다.
    ///  - 총이 필요로 하는 조준선·반동·확대·이동 상태를 PlayerController 와 이어준다.
    ///  - 부활(PlayerRevivedEvent)하면 보급 칸을 뺀 총의 탄창을 채우고 예비 탄창을 최소 시작값까지 올린다 (Gun.RestoreAmmoForRevive)
    ///  - 예비 탄창 보충·장전 취소 창구 (IPlayerAmmo, ServiceLocator 등록). 피격(PlayerDamagedEvent)이 reloadInterruptOnHit 규칙에 맞으면 장전을 끊는다
    /// 실행 순서 20: PlayerController.Update(0, 시선 입력 → 리그 회전) 다음에 총을 Tick → 연사도 이번 프레임 시선으로 쏜다
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [DefaultExecutionOrder(20)]
    public class GunSlot : MonoBehaviour, IPlayerAmmo, ISupplyWeaponUser
    {
        public const int SlotCount = 4;
        /// <summary>1번 키. Sidearm(권총) 고정</summary>
        public const int SidearmSlot = 0;
        /// <summary>4번 키. 보급 무기 전용 (시작 시 비어 있음)</summary>
        public const int SupplySlot = SlotCount - 1;
        /// <summary>2·3번 키. 출발 전 선택 화면에서 고르는 칸</summary>
        public static readonly IReadOnlyList<int> LoadoutSlots = new[] { 1, 2 };

        /// <summary>어떤 피격에 장전이 취소되는지</summary>
        public enum ReloadInterruptRule
        {
            /// <summary>피격으로는 취소하지 않음</summary>
            None,
            /// <summary>넉백이 있는 몬스터 공격 (MonsterAttackTable.KnockbackDistance &gt; 0: 돌진·덮치기 등)</summary>
            Knockback,
            /// <summary>몬스터의 모든 직접 공격 (지속 피해·폭발 제외)</summary>
            AnyMonsterAttack,
            /// <summary>피해량이 reloadInterruptDamage 이상인 모든 피격</summary>
            DamageAtLeast,
        }

        [Tooltip("총이 붙는 곳 (오른손 WeaponSocket). 이 아래의 Gun 을 모두 찾는다")]
        [SerializeField] private Transform weaponSocket;
        [Tooltip("2·3번 칸 기본 무기 후보. 이 중 Category = Loadout 인 총이 순서대로 들어간다 (Sidearm·Supply 는 분류대로 따로 배치)")]
        [SerializeField] private Gun[] startingGuns = new Gun[SlotCount];
        [Tooltip("시작 시 들고 있을 슬롯")]
        [SerializeField, Range(0, SlotCount - 1)] private int startSlot = 0;

        [Header("장전 취소 (피격)")]
        [Tooltip("이 규칙에 맞는 피격을 받으면 진행 중인 재장전이 취소된다 (탄·예비 탄창 그대로). 구르기·투척·사망은 규칙과 상관없이 항상 취소")]
        [SerializeField] private ReloadInterruptRule reloadInterruptOnHit = ReloadInterruptRule.Knockback;
        [Tooltip("DamageAtLeast 규칙의 기준 피해량")]
        [SerializeField, Min(1)] private int reloadInterruptDamage = 20;

        private readonly Gun[] slots = new Gun[SlotCount];
        private readonly List<Gun> reserve = new List<Gun>();
        private Gun[] allGuns = Array.Empty<Gun>();
        private PlayerController player;
        private int currentIndex = -1;
        /// <summary>보급 무기를 장착하기 전에 들던 칸 (탄을 다 써서 사라지면 이 칸으로 돌아간다)</summary>
        private int slotBeforeSupply = -1;
        /// <summary>보급 무기 탄이 바닥난 시각. 바닥나지 않았으면 Infinity</summary>
        private float supplyDepletedAt = float.PositiveInfinity;

        private const string AimSpeedKey = "GunAim";
        /// <summary>보급 무기 탄을 다 쓴 뒤 버리기까지 기다리는 시간(초). 마지막 발사의 반동·투사체 발사 동작이 끝나도록</summary>
        private const float SupplyDepletedDiscardDelay = 0.6f;

        #region 유니티 메시지
        private void Awake()
        {
            player = GetComponent<PlayerController>();
            if (weaponSocket == null) weaponSocket = transform;
        }

        private void OnEnable()
        {
            ServiceLocator.Register<IPlayerAmmo>(this);
            ServiceLocator.Register<ISupplyWeaponUser>(this);
            EventBus<PlayerDamagedEvent>.Subscribe(OnDamaged);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<PlayerRevivedEvent>.Subscribe(OnPlayerRevived);
        }

        private void OnDisable()
        {
            ServiceLocator.Unregister<IPlayerAmmo>(this);
            ServiceLocator.Unregister<ISupplyWeaponUser>(this);
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnDamaged);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<PlayerRevivedEvent>.Unsubscribe(OnPlayerRevived);
        }

        private void Start() => LoadLoadout();

        private void Update()
        {
            if (CurrentGun != null) CurrentGun.Tick(Time.deltaTime);
            UpdateSupplyDepletion();
        }
        #endregion

        #region 외부 API
        /// <summary>인자 = (슬롯 번호, 새로 든 총). 바꿔 들 때만 발생. UI 무기 표시용</summary>
        public event Action<int, Gun> GunChanged;
        /// <summary>슬롯 구성이 바뀜 (시작 배치, 출발 전 선택 적용, 보급 무기 획득·버림). 들고 있는 총이 바뀌었으면 GunChanged 도 따로 발생</summary>
        public event Action LoadoutChanged;
        /// <summary>번호 키(SelectByKey)로 빈 칸을 골랐음. 인자 = 슬롯 번호. UI 경고용 (WeaponSlotHud)</summary>
        public event Action<int> EmptySlotSelected;

        public Gun CurrentGun => currentIndex >= 0 ? slots[currentIndex] : null;
        public int CurrentIndex => currentIndex;
        public Gun GetGun(int index) => index >= 0 && index < SlotCount ? slots[index] : null;
        public IReadOnlyList<Gun> Reserve => reserve;
        /// <summary>소켓 아래 모든 총 (슬롯 + 예비). 출발 전 선택 화면 후보 목록용</summary>
        public IReadOnlyList<Gun> AllGuns => allGuns;
        public bool IsAiming => CurrentGun != null && CurrentGun.IsAiming;

        public sealed class CheckpointState
        {
            internal readonly int[] WeaponIds;
            internal readonly int[] Ammo;
            internal readonly int[] SpareMagazines;
            internal readonly int CurrentIndex;
            internal readonly int SlotBeforeSupply;

            internal CheckpointState(int[] weaponIds, int[] ammo, int[] spareMagazines, int currentIndex, int slotBeforeSupply)
            {
                WeaponIds = weaponIds;
                Ammo = ammo;
                SpareMagazines = spareMagazines;
                CurrentIndex = currentIndex;
                SlotBeforeSupply = slotBeforeSupply;
            }
        }

        public CheckpointState CaptureCheckpoint()
        {
            var weaponIds = new int[SlotCount];
            var ammo = new int[SlotCount];
            var spareMagazines = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == null) continue;
                weaponIds[i] = slots[i].WeaponId;
                ammo[i] = slots[i].Ammo;
                spareMagazines[i] = slots[i].SpareMagazines;
            }
            return new CheckpointState(weaponIds, ammo, spareMagazines, currentIndex, slotBeforeSupply);
        }

        public void RestoreCheckpoint(CheckpointState state)
        {
            int previousSupplyId = SupplyWeaponId;
            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == null) continue;
                slots[i].Unequip();
                if (reserve.Contains(slots[i]) == false) reserve.Add(slots[i]);
                slots[i] = null;
            }
            currentIndex = -1;
            slotBeforeSupply = state.SlotBeforeSupply;
            supplyDepletedAt = float.PositiveInfinity;

            for (int i = 0; i < SlotCount; i++)
            {
                if (state.WeaponIds[i] == 0) continue;
                Gun gun = FindReserve(state.WeaponIds[i]);
                if (gun == null)
                {
                    Debug.LogError($"[GunSlot] 체크포인트 무기 {state.WeaponIds[i]}를 찾지 못했습니다.", this);
                    continue;
                }
                PlaceFromReserve(i, gun);
                gun.RestoreCheckpointAmmo(state.Ammo[i], state.SpareMagazines[i]);
            }

            if (Select(state.CurrentIndex) == false && Select(startSlot) == false && SelectNext(1) == false) GunChanged?.Invoke(-1, null);
            LoadoutChanged?.Invoke();
            if (previousSupplyId != SupplyWeaponId) EventBus<SupplyWeaponChangedEvent>.Publish(new SupplyWeaponChangedEvent(previousSupplyId, SupplyWeaponId));
        }

        // ── 총이 읽는 플레이어 정보 ──
        public Faction Faction => Faction.Player;
        public Transform OwnerRoot => transform;
        public bool IsGrounded => player.IsGrounded;
        /// <summary>0 = 정지, 1 = 달리기 최고 속도</summary>
        public float MoveRatio => Mathf.Clamp01(player.HorizontalSpeed / Mathf.Max(0.01f, player.WalkSpeed * player.SprintMultiplier));
        /// <summary>땅울림으로 더해진 퍼짐(도). 없으면 0</summary>
        public float TremorSpread => player.TremorSpread;
        /// <summary>구르는 중, 수류탄을 던지는 중이거나 죽었으면 쏠 수 없다</summary>
        public bool CanUseWeapon => player.IsAlive && player.IsReviving == false && player.IsRolling == false && player.IsExternallyControlled == false && player.IsThrowingGrenade == false && player.IsGameplayInputBlocked == false;

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

        /// <summary>반동: up = 위로(도), right = 오른쪽(도). 반환: 실제로 위로 돌아간 각도 (시선 한계에 막히면 덜 돈다)</summary>
        public float AddRecoil(float up, float right) => -player.AddLookOffset(-up, right).x;

        /// <summary>이번 프레임 플레이어가 마우스로 시선을 내린 각도(도). 올렸으면 음수</summary>
        public float LookInputPitchDown => player.LookInputPitchDelta;

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

        // ── 예비 탄창 (IPlayerAmmo) ──
        public int SpareMagazines => CurrentGun != null ? CurrentGun.SpareMagazines : 0;
        public int MaxMagazines => CurrentGun != null ? CurrentGun.MaxMagazines : 0;

        /// <summary>지금 든 총에 예비 탄창 보충. 이미 최대·무한 탄창·총 없음·보급 무기(1회용)면 false</summary>
        public bool AddMagazines(int amount) => CurrentGun != null && currentIndex != SupplySlot && CurrentGun.AddMagazines(amount);

        /// <summary>슬롯의 모든 총에 예비 탄창 보충 (보급 칸 제외). 반환: 늘어난 예비 탄창 합</summary>
        public int AddMagazinesToAll(int amount)
        {
            int added = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (i == SupplySlot || slots[i] == null) continue;
                int before = slots[i].SpareMagazines;
                slots[i].AddMagazines(amount);
                added += slots[i].SpareMagazines - before;
            }
            return added;
        }

        /// <summary>보급 칸을 뺀 슬롯의 총 중 보급 충전으로 예비 탄창이 늘어날 총이 있는지</summary>
        public bool CanAddSupplyMagazines
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (i != SupplySlot && slots[i] != null && slots[i].CanAddSupplyMagazines) return true;
                return false;
            }
        }

        /// <summary>보급 1회분: 슬롯의 총에 무기별 비율만큼 예비 탄창 보충 (보급 칸·예비로 꺼 둔 총 제외). 반환: 늘어난 예비 탄창 합</summary>
        public int AddSupplyMagazines()
        {
            int added = 0;
            for (int i = 0; i < SlotCount; i++)
                if (i != SupplySlot && slots[i] != null) added += slots[i].AddSupplyMagazines();
            return added;
        }

        /// <summary>지금 든 총의 재장전을 취소 (탄·예비 탄창 그대로)</summary>
        public void InterruptReload() { if (CurrentGun != null) CurrentGun.CancelReload(); }

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

        /// <summary>번호 키 입력: Select 와 같고, 빈 칸이면 EmptySlotSelected 를 알린다 (휠·내부 교체는 빈 칸을 건너뛰므로 알리지 않음)</summary>
        public bool SelectByKey(int index)
        {
            if (index >= 0 && index < SlotCount && slots[index] == null)
            {
                EmptySlotSelected?.Invoke(index);
                return false;
            }
            return Select(index);
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

        // ── 출발 전 무기 선택 ──
        /// <summary>WeaponTable.Category (테이블에 없는 총은 Loadout 으로 본다)</summary>
        public static WeaponCategory CategoryOf(Gun gun) =>
            gun != null && gun.Def != null ? gun.Def.Category : WeaponCategory.Loadout;

        /// <summary>해금 여부. IWeaponUnlocks 가 등록되지 않았으면 모두 해금</summary>
        public static bool IsUnlocked(int weaponId) =>
            ServiceLocator.TryGet<IWeaponUnlocks>(out var unlocks) == false || unlocks.IsUnlocked(weaponId);

        /// <summary>
        /// 2·3번 칸(LoadoutSlots 순서)을 weaponIds 로 바꾼다. 빠진 총은 예비로 되돌린다 (파괴 X).
        /// Id 0 = 빈 칸 (해금 전엔 3번 칸이 빌 수 있다). 적어도 한 칸은 채워야 한다.
        /// 채운 칸은 모두 Category = Loadout · 해금 · 서로 다름이어야 하며, 하나라도 어긋나면 아무것도 바꾸지 않고 false
        /// </summary>
        public bool ApplyLoadout(IReadOnlyList<int> weaponIds)
        {
            if (weaponIds == null || weaponIds.Count != LoadoutSlots.Count)
            {
                Debug.LogWarning($"{name}: ApplyLoadout 는 무기 {LoadoutSlots.Count}개가 필요합니다 (빈 칸은 0)", this);
                return false;
            }

            var picks = new Gun[LoadoutSlots.Count];
            bool any = false;
            for (int i = 0; i < picks.Length; i++)
            {
                if (weaponIds[i] == 0) continue;
                any = true;
                Gun gun = FindGun(weaponIds[i]);
                string error = gun == null ? "가 없습니다"
                    : CategoryOf(gun) != WeaponCategory.Loadout ? "는 Loadout 분류가 아닙니다"
                    : IsUnlocked(gun.WeaponId) == false ? "는 해금되지 않았습니다"
                    : Array.IndexOf(picks, gun) >= 0 ? "가 중복됩니다"
                    : null;
                if (error != null)
                {
                    Debug.LogWarning($"{name}: ApplyLoadout - WeaponId {weaponIds[i]} {error}", this);
                    return false;
                }
                picks[i] = gun;
            }
            if (any == false)
            {
                Debug.LogWarning($"{name}: ApplyLoadout - 2·3번 칸이 모두 비었습니다 (한 칸 이상 필요)", this);
                return false;
            }

            Gun held = CurrentGun;
            foreach (int index in LoadoutSlots)
            {
                if (slots[index] == null) continue;
                reserve.Add(slots[index]);
                slots[index] = null;
            }
            for (int i = 0; i < picks.Length; i++) PlaceFromReserve(LoadoutSlots[i], picks[i]);

            // 들고 있던 칸의 총이 바뀌었으면 새 총으로 바꿔 든다 (꺼내기 동작·교체음 정상)
            if (held != null && slots[currentIndex] != held)
            {
                held.Unequip();
                int index = currentIndex;
                currentIndex = -1;
                if (Select(index) == false) SelectNext(1);
            }

            LoadoutChanged?.Invoke();
            return true;
        }

        // ── 보급 칸 (ISupplyWeaponUser) ──
        public int SupplyWeaponId => slots[SupplySlot] != null ? slots[SupplySlot].WeaponId : 0;

        public bool CanEquipSupplyWeapon(int weaponId)
        {
            Gun gun = FindReserve(weaponId);
            return gun != null && CategoryOf(gun) == WeaponCategory.Supply;
        }

        /// <summary>보급 칸에 장착하고 바로 꺼내 든다. 이전 보급 무기는 탄을 초기화해 예비로 반납(= 버림)</summary>
        public bool EquipSupplyWeapon(int weaponId)
        {
            if (CanEquipSupplyWeapon(weaponId) == false) return false;
            Gun gun = FindReserve(weaponId);
            int previousId = SupplyWeaponId;
            if (currentIndex >= 0 && currentIndex != SupplySlot) slotBeforeSupply = currentIndex;

            ReturnSupplyWeapon();
            reserve.Remove(gun);
            gun.ResetForPickup();
            slots[SupplySlot] = gun;
            supplyDepletedAt = float.PositiveInfinity;
            Select(SupplySlot);

            LoadoutChanged?.Invoke();
            EventBus<SupplyWeaponChangedEvent>.Publish(new SupplyWeaponChangedEvent(previousId, weaponId));
            return true;
        }

        /// <summary>테스트용(T 키): Supply 분류 총을 돌아가며 보급 칸에 장착</summary>
        public bool DebugCycleSupplyWeapon()
        {
            var supplyGuns = new List<Gun>();
            foreach (var gun in allGuns)
                if (CategoryOf(gun) == WeaponCategory.Supply) supplyGuns.Add(gun);
            if (supplyGuns.Count == 0)
            {
                Debug.Log("보급 무기(Category = Supply) 가 없습니다");
                return false;
            }

            int start = supplyGuns.IndexOf(slots[SupplySlot]);
            for (int i = 1; i <= supplyGuns.Count; i++)
            {
                Gun next = supplyGuns[(start + i) % supplyGuns.Count];
                if (EquipSupplyWeapon(next.WeaponId))
                {
                    Debug.Log($"보급 무기 → {next.DisplayName}");
                    return true;
                }
            }
            return false;
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
        #endregion

        #region 내부
        private void OnDamaged(PlayerDamagedEvent evt)
        {
            if (CurrentGun != null && CurrentGun.IsReloading && InterruptsReload(evt.Info)) InterruptReload();
        }

        private bool InterruptsReload(in DamageInfo info)
        {
            bool monsterAttack = info.Kind == DamageKind.MonsterAttack || info.Kind == DamageKind.MonsterSkill;
            switch (reloadInterruptOnHit)
            {
                case ReloadInterruptRule.Knockback:
                    return monsterAttack && DataManager.MonsterAttacks.TryGet(info.SourceDefId, out var attack) && attack.KnockbackDistance > 0f;
                case ReloadInterruptRule.AnyMonsterAttack:
                    return monsterAttack;
                case ReloadInterruptRule.DamageAtLeast:
                    return info.Amount >= reloadInterruptDamage;
                default:
                    return false;
            }
        }

        /// <summary>라운드(스테이지) 정리마다 보급 칸을 비운다 (재시작·다음 라운드 모두, 보급 무기는 그 스테이지 한정). 2·3번 선택은 유지</summary>
        private void OnRoundCleanup(RoundCleanupEvent evt)
        {
            if (slots[SupplySlot] == null) return;
            int previousId = SupplyWeaponId;
            ReturnSupplyWeapon();
            if (CurrentGun == null && Select(startSlot) == false) SelectNext(1);
            LoadoutChanged?.Invoke();
            EventBus<SupplyWeaponChangedEvent>.Publish(new SupplyWeaponChangedEvent(previousId, 0));
        }

        /// <summary>부활 → 보급 칸을 뺀 총의 탄창을 채우고 예비 탄창을 최소 시작값까지 (보급 무기는 1회용이라 그대로)</summary>
        private void OnPlayerRevived(PlayerRevivedEvent evt)
        {
            for (int i = 0; i < SlotCount; i++)
                if (i != SupplySlot && slots[i] != null) slots[i].RestoreAmmoForRevive();
        }

        /// <summary>
        /// 보급 무기(1회용)의 탄창·예비 탄창이 모두 바닥나면 SupplyDepletedDiscardDelay 뒤 칸에서 버리고,
        /// 들고 있었다면 장착 전에 들던 칸(없으면 시작 칸·다음 칸)으로 돌아간다. 슬롯 HUD 는 SupplyWeaponChangedEvent(이전, 0) 로 갱신
        /// </summary>
        private void UpdateSupplyDepletion()
        {
            Gun supply = slots[SupplySlot];
            if (supply == null || supply.IsOutOfAmmo == false)
            {
                supplyDepletedAt = float.PositiveInfinity;
                return;
            }
            if (float.IsPositiveInfinity(supplyDepletedAt))
            {
                supplyDepletedAt = Time.time;
                return;
            }
            if (Time.time - supplyDepletedAt < SupplyDepletedDiscardDelay) return;

            supplyDepletedAt = float.PositiveInfinity;
            int previousId = SupplyWeaponId;
            bool wasHolding = currentIndex == SupplySlot;
            ReturnSupplyWeapon();
            if (wasHolding && Select(slotBeforeSupply) == false && Select(startSlot) == false) SelectNext(1);
            LoadoutChanged?.Invoke();
            EventBus<SupplyWeaponChangedEvent>.Publish(new SupplyWeaponChangedEvent(previousId, 0, depleted: true));
        }

        /// <summary>보급 칸 총을 탄 초기화 후 예비로 반납. 들고 있었으면 내려놓기만 한다 (currentIndex = -1, 바꿔 들기는 호출한 쪽에서)</summary>
        private void ReturnSupplyWeapon()
        {
            Gun old = slots[SupplySlot];
            if (old == null) return;
            if (currentIndex == SupplySlot)
            {
                old.Unequip();
                currentIndex = -1;
            }
            slots[SupplySlot] = null;
            old.ResetForPickup();
            reserve.Add(old);
        }

        private Gun FindGun(int weaponId)
        {
            foreach (var gun in allGuns)
                if (gun != null && gun.WeaponId == weaponId) return gun;
            return null;
        }

        private Gun FindReserve(int weaponId)
        {
            foreach (var gun in reserve)
                if (gun != null && gun.WeaponId == weaponId) return gun;
            return null;
        }

        /// <summary>
        /// 소켓 아래 총 전부 초기화 → 분류대로 배치 (0 = Sidearm, 1·2 = startingGuns 중 해금된 Loadout, 3 = 비움) → 나머지는 예비로 꺼 둠
        /// </summary>
        private void LoadLoadout()
        {
            allGuns = weaponSocket.GetComponentsInChildren<Gun>(true);
            foreach (var gun in allGuns)
            {
                gun.Initialize(this);
                gun.gameObject.SetActive(false);
            }

            Array.Clear(slots, 0, SlotCount);
            reserve.Clear();
            reserve.AddRange(allGuns);

            // startingGuns 순서 우선, 그다음 소켓 아래 순서
            var candidates = new List<Gun>();
            if (startingGuns != null)
                foreach (var gun in startingGuns)
                    if (gun != null && reserve.Contains(gun) && candidates.Contains(gun) == false) candidates.Add(gun);
            foreach (var gun in allGuns)
                if (candidates.Contains(gun) == false) candidates.Add(gun);

            Gun sidearm = candidates.Find(gun => CategoryOf(gun) == WeaponCategory.Sidearm);
            if (sidearm == null && candidates.Count > 0)
            {
                sidearm = candidates[0];
                Debug.LogWarning($"{name}: Category = Sidearm 인 총이 없어 {sidearm.DisplayName} 을(를) 1번 칸에 넣습니다", this);
            }
            PlaceFromReserve(SidearmSlot, sidearm);

            int next = 0;
            foreach (var gun in candidates)
            {
                if (next >= LoadoutSlots.Count) break;
                if (CategoryOf(gun) == WeaponCategory.Loadout && IsUnlocked(gun.WeaponId) && reserve.Contains(gun)) PlaceFromReserve(LoadoutSlots[next++], gun);
            }

            if (Select(startSlot) == false) SelectNext(1);
            LoadoutChanged?.Invoke();
        }

        private void PlaceFromReserve(int index, Gun gun)
        {
            if (gun == null) return;
            reserve.Remove(gun);
            slots[index] = gun;
        }
        #endregion
    }
}
