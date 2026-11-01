using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Supply
{
    /// <summary>
    /// 투하된 보급 물자 한 개 (BL-018, BL-019).
    ///
    ///  - 하늘(지면 + SupplyDropHeight)에서 일정 속도로 떨어지다가, 지면 SupplyLandingHeight 이내로 들어오면
    ///    착지 속도(landingSpeed)까지 등감속하며 서서히 내려앉는다.
    ///  - Kinematic Rigidbody + Trigger Collider. 착지한 뒤 플레이어가 조준하거나, 근접 획득 영역(pickupRadius) 안에서 상호작용 키(F)를 누르면 획득
    ///    (IProximityInteractable — 영역은 Ignore Raycast 레이어 자식 트리거라 조준 레이·총알 판정에는 걸리지 않는다).
    ///    조준하는 동안의 정보(종류·지급량·가득 참 경고)는 GetPrompt → PlayerInteractor → InteractPromptHud 로 표시된다.
    ///  - 획득 대상은 구체 클래스가 아닌 계약(IItemUser, IPlayerAmmo)으로 찾는다.
    ///    가득 차서 못 먹으면(false) 물자는 그대로 남는다.
    ///  - SupplyType.Weapon 상자는 아이템 대신 1회용 보급 무기를 보급 칸에 장착하고, 1~3번 칸 총에 예비 탄창도 충전한다 (보급 무기에는 충전 안 함).
    ///  - 착지 후 ConfigTable.SupplyLifetime 초가 지나면 사라진다 (갈 수 없는 곳에 떨어진 상자 정리). 그 전에는 획득 또는 라운드 정리 때만 제거
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SupplyCrate : MonoBehaviour, IProximityInteractable
    {
        /// <summary>Idle = Launch 전, Falling = 낙하 중, Landed = 착지 완료</summary>
        public enum State { Idle, Falling, Landed }

        /// <summary>ConfigTable 에 SupplyLifetime 행이 없을 때의 착지 후 수명(초). 테이블 값과 같게 유지</summary>
        public const float DefaultLifetime = 90f;
        /// <summary>Unity 기본 Ignore Raycast 레이어 (근접 획득 영역용)</summary>
        private const int IgnoreRaycastLayer = 2;

        [Tooltip("낙하 속도 (m/s)")]
        [SerializeField] private float fallSpeed = 15f;
        [Tooltip("지면에 닿는 순간의 속도 (m/s). 감속 구간에서 이 속도까지 줄어든다")]
        [SerializeField] private float landingSpeed = 1f;
        [Tooltip("낙하 중 천천히 회전 (도/초)")]
        [SerializeField] private float fallSpinSpeed = 20f;
        [Tooltip("콜라이더 크기를 자식 렌더러 크기에 자동으로 맞춤")]
        [SerializeField] private bool fitColliderToRenderers = true;
        [Tooltip("콜라이더를 렌더러보다 넓혀서 줍기 쉽게 (m)")]
        [SerializeField] private float pickupPadding = 0.5f;
        [Tooltip("콜라이더를 렌더러 위로 늘려서 (m), 경사에 살짝 묻혀도 조준 레이가 지면보다 상자를 먼저 맞히게")]
        [SerializeField] private float pickupTopPadding = 0.5f;
        [Tooltip("근접 획득 반지름(m). 플레이어 몸 중심이 이 안에 들어오면 조준하지 않아도 F 로 획득 (IProximityInteractable)")]
        [SerializeField, Min(0.5f)] private float pickupRadius = 2f;
        [Tooltip("사라지기 전 빛기둥이 깜빡이는 시간 (초)")]
        [SerializeField] private float expireBlinkTime = 10f;

        [Header("위치 표시 (멀리서도 보이는 빛기둥, 색 = SupplyTable.BeaconColor)")]
        [SerializeField] private bool showBeacon = true;
        [SerializeField] private float beaconHeight = 25f;

        /// <summary>이 물자의 보급 정의 (종류·보충 수량). Launch 에서 설정</summary>
        public SupplyDef Def { get; private set; }
        /// <summary>현재 낙하 상태</summary>
        public State CurrentState { get; private set; }
        /// <summary>최종 착지 위치 (피벗 기준, 모델 바닥이 지면에 닿는 위치)</summary>
        public Vector3 LandingPosition => landingPosition;

        /// <summary>획득·정리로 사라질 때 (스포너가 목록에서 빼는 용도)</summary>
        public event Action<SupplyCrate> Removed;

        private Rigidbody body;
        private BoxCollider trigger;
        private Vector3 landingPosition;
        private float landingHeight;
        private float speedAtLandingZone;
        private bool removed;
        private Material beaconMaterial;
        private Renderer beaconRenderer;
        /// <summary>착지 시각 (ConfigTable.SupplyLifetime 이 지나면 사라짐). 착지 전 = Infinity</summary>
        private float landedTime = float.PositiveInfinity;

        /// <summary>
        /// 필수 컴포넌트 설정: Rigidbody 는 Kinematic(중력 X, 보간 O), BoxCollider 는 Trigger.
        /// fitColliderToRenderers 가 켜져 있으면 콜라이더 크기를 모델 크기에 맞춘다.
        /// </summary>
        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            if (fitColliderToRenderers) FitColliderToRenderers();
            CreatePickupZone();
        }

        /// <summary>
        /// 낙하 시작. groundPoint = 착지할 지면 지점, dropHeight = 지면 기준 시작 높이,
        /// landingZoneHeight = 감속을 시작할 높이(지면 기준).
        /// 착지 위치를 계산해 그 위 dropHeight 지점으로 옮기고, Falling 상태로 바꾼 뒤 종류별 색 빛기둥을 단다.
        /// </summary>
        public void Launch(SupplyDef def, Vector3 groundPoint, float dropHeight, float landingZoneHeight)
        {
            Def = def;
            landingHeight = Mathf.Max(0.01f, landingZoneHeight);

            // 피벗이 모델 바닥이 아닐 수 있으므로, 콜라이더 바닥이 지면에 닿도록 보정 (루트는 회전 없음 기준)
            float pivotToBottom = -(trigger.center.y - trigger.size.y * 0.5f) * transform.lossyScale.y;
            landingPosition = groundPoint + Vector3.up * pivotToBottom;

            Vector3 start = landingPosition + Vector3.up * dropHeight;
            transform.position = start;
            body.position = start;

            // 감속 구간 진입 속도 = 평소 낙하 속도 (착지 속도보다 느리면 감속 없이 그대로)
            speedAtLandingZone = Mathf.Max(fallSpeed, landingSpeed);
            CurrentState = State.Falling;

            if (showBeacon) CreateBeacon(def.BeaconColor);
        }

        /// <summary>
        /// 낙하 이동 (물리 프레임마다). 남은 높이에 맞는 속도로 아래로 이동하며 천천히 회전한다.
        /// 이번 프레임 이동량이 남은 높이 이상이면 착지 위치에 정확히 맞추고 착지 처리.
        /// </summary>
        private void FixedUpdate()
        {
            if (CurrentState != State.Falling) return;

            float height = body.position.y - landingPosition.y;
            float speed = GetFallSpeed(height);
            float step = speed * Time.fixedDeltaTime;

            if (step >= height)
            {
                body.MovePosition(landingPosition);
                Land();
                return;
            }

            body.MovePosition(body.position + Vector3.down * step);
            if (fallSpinSpeed != 0f)
                body.MoveRotation(body.rotation * Quaternion.Euler(0f, fallSpinSpeed * Time.fixedDeltaTime, 0f));
        }

        /// <summary>
        /// 현재 높이(착지 위치 기준)에서의 낙하 속도.
        /// 감속 구간 밖은 fallSpeed, 감속 구간(landingHeight 이내)은 등감속: v² = vLand² + (v0² - vLand²) · (h / H).
        /// h = H 에서 v0, h = 0 에서 vLand 가 되어 속도가 끊기지 않고 부드럽게 줄어든다.
        /// </summary>
        private float GetFallSpeed(float height)
        {
            if (height >= landingHeight) return fallSpeed;
            float t = Mathf.Clamp01(height / landingHeight);
            float v0Sq = speedAtLandingZone * speedAtLandingZone;
            float vLandSq = landingSpeed * landingSpeed;
            return Mathf.Sqrt(vLandSq + (v0Sq - vLandSq) * t);
        }

        /// <summary>착지 완료 처리. 이후 FixedUpdate 에서 더 이상 움직이지 않고, 수명(SupplyLifetime) 을 세기 시작한다</summary>
        private void Land()
        {
            CurrentState = State.Landed;
            landedTime = Time.time;
        }

        /// <summary>
        /// 착지 후 ConfigTable.SupplyLifetime 초가 지나면 사라진다 (0 이하 = 사라지지 않음).
        /// 갈 수 없는 곳에 떨어진 상자가 SupplyMaxActive 자리를 계속 차지해 투하가 끊기는 것을 막는 안전장치.
        /// 마지막 expireBlinkTime 초 동안 빛기둥이 깜빡인다. 획득이 아니므로 SupplyCollectedEvent 는 발행하지 않는다
        /// </summary>
        private void Update()
        {
            if (CurrentState != State.Landed || removed) return;
            float lifetime = DataManager.Config.GetFloat(GameConfig.SupplyLifetime, DefaultLifetime);
            if (lifetime <= 0f) return;

            float remaining = landedTime + lifetime - Time.time;
            if (remaining <= 0f)
            {
                Remove();
                return;
            }
            if (beaconRenderer != null)
                beaconRenderer.enabled = remaining > expireBlinkTime || Mathf.Repeat(remaining, 0.5f) > 0.2f;
        }

        // ── 상호작용 (IInteractable) ──

        public Transform InteractTransform => transform;

        /// <summary>착지한 뒤에만 (낙하 중에는 프롬프트도 뜨지 않는다). 가득 찬 경우는 프롬프트 경고로 알리고 Interact 가 실패한다</summary>
        public bool CanInteract(GameObject user) => removed == false && Def != null && CurrentState == State.Landed;

        /// <summary>
        /// 조준·근접 안내. 제목·설명은 SupplyTable.CrateName / CrateDescription (예: "수류탄 보급 상자" / "수류탄과 탄약이 들어 있는 보급 상자").
        /// 받을 수 없을 때만 짧은 경고를 붙인다. 무기 상세(무기명·설명)는 획득 후 SupplyWeaponPickupHud 가 보여 준다.
        /// 매 프레임 불릴 수 있어 계산은 가볍게 (지급은 하지 않음)
        /// </summary>
        public InteractPrompt GetPrompt(GameObject user) =>
            new InteractPrompt(Def.CrateName, Def.CrateDescription, "획득", "", GetWarning(user));

        /// <summary>받을 수 없거나 주의가 필요한 경우의 짧은 경고 (없으면 빈 문자열)</summary>
        private string GetWarning(GameObject user)
        {
            if (Def.IsWeapon == false) return CanReceiveAny(user) ? "" : "모두 가득 차 있습니다";

            var receiver = user != null ? user.GetComponentInParent<ISupplyWeaponUser>() : null;
            if (receiver == null || receiver.SupplyWeaponId == 0) return "";
            if (receiver.SupplyWeaponId != Def.WeaponId) return "들고 있는 보급 무기와 바뀝니다";
            // 같은 무기를 들고 있으면 탄창만 받는다
            var ammo = user.GetComponentInParent<IPlayerAmmo>();
            return ammo != null && ammo.CanAddSupplyMagazines ? "이미 가진 무기라 탄약만 받습니다" : "모두 가득 차 있습니다";
        }

        /// <summary>
        /// user(플레이어 루트)에게 보급을 지급한다.
        ///   아이템: 종류별 AmountMin~AmountMax 개 무작위 / 탄약: 슬롯의 모든 총에 무기별 비율만큼 (IPlayerAmmo.AddSupplyMagazines)
        ///   보급 무기: 보급 칸에 장착 (ISupplyWeaponUser.EquipSupplyWeapon) + 1~3번 칸 예비 탄창 충전
        /// 하나라도 실제로 늘었으면 SupplyCollectedEvent(실제 지급량) 발행 후 물자를 제거하고 true. 하나도 못 받으면 그대로 남고 false.
        /// </summary>
        public bool Interact(GameObject user)
        {
            if (user == null || CanInteract(user) == false) return false;
            if (Def.IsWeapon) return InteractWeapon(user);
            var itemUser = user.GetComponentInParent<IItemUser>();
            var ammo = user.GetComponentInParent<IPlayerAmmo>();
            if (itemUser == null && ammo == null) return false;

            int items = itemUser != null ? GrantItems(itemUser, Def.RollAmount(UnityEngine.Random.value)) : 0;
            int magazines = ammo != null ? ammo.AddSupplyMagazines() : 0;
            if (items == 0 && magazines == 0) return false;

            EventBus<SupplyCollectedEvent>.Publish(new SupplyCollectedEvent(Def, items, magazines, transform.position));
            Remove();
            return true;
        }

        /// <summary>
        /// 보급 무기 + 예비 탄창 지급. 탄창은 1~3번 칸 총에만 (IPlayerAmmo.AddSupplyMagazines 가 보급 칸을 뺀다 — 보급 무기는 1회용).
        /// 같은 보급 무기를 이미 들고 있으면 탄창만 받는다. 둘 다 못 받으면 상자는 그대로 남고 false.
        /// SupplyCollectedEvent 의 ItemAmount = 무기를 장착했으면 1, 탄창만이면 0
        /// </summary>
        private bool InteractWeapon(GameObject user)
        {
            var receiver = user.GetComponentInParent<ISupplyWeaponUser>();
            var ammo = user.GetComponentInParent<IPlayerAmmo>();
            bool equipped = receiver != null && receiver.SupplyWeaponId != Def.WeaponId && receiver.EquipSupplyWeapon(Def.WeaponId);
            int magazines = ammo != null ? ammo.AddSupplyMagazines() : 0;
            if (equipped == false && magazines == 0) return false;

            EventBus<SupplyCollectedEvent>.Publish(new SupplyCollectedEvent(Def, equipped ? 1 : 0, magazines, transform.position));
            Remove();
            return true;
        }

        /// <summary>아이템이나 예비 탄창 중 하나라도 더 받을 수 있는지 (프롬프트 경고용)</summary>
        private bool CanReceiveAny(GameObject user)
        {
            if (user == null) return false;
            var itemUser = user.GetComponentInParent<IItemUser>();
            if (itemUser != null)
            {
                if (Def.SupplyType == SupplyType.Grenade && itemUser.Grenades < itemUser.MaxGrenades) return true;
                if (Def.SupplyType == SupplyType.Heal && itemUser.HealPacks < itemUser.MaxHealPacks) return true;
            }
            var ammo = user.GetComponentInParent<IPlayerAmmo>();
            return ammo != null && ammo.CanAddSupplyMagazines;
        }

        /// <summary>종류에 맞는 아이템을 amount 개 보충하고, 최대치에 막히지 않고 실제로 늘어난 개수를 돌려준다</summary>
        private int GrantItems(IItemUser itemUser, int amount)
        {
            switch (Def.SupplyType)
            {
                case SupplyType.Grenade:
                {
                    int before = itemUser.Grenades;
                    itemUser.AddGrenades(amount);
                    return itemUser.Grenades - before;
                }
                case SupplyType.Heal:
                {
                    int before = itemUser.HealPacks;
                    itemUser.AddHealPacks(amount);
                    return itemUser.HealPacks - before;
                }
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 물자 제거. 획득·라운드 정리 시 호출. Removed 이벤트로 스포너에 알린 뒤 오브젝트를 파괴한다.
        /// 여러 번 호출돼도 한 번만 처리.
        /// </summary>
        public void Remove()
        {
            if (removed) return;
            removed = true;
            Removed?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        /// 근접 획득 영역: 상자 중심에 반지름 pickupRadius 의 트리거 SphereCollider 를 가진 자식을 만든다.
        /// Ignore Raycast 레이어라 기본 레이캐스트(조준·카메라 등)에는 걸리지 않고, 플레이어는 OverlapSphere 로 직접 찾는다
        /// (자식 콜라이더라 GetComponentInParent 로 이 상자를 찾는다. 루트의 Kinematic Rigidbody 에 묶인다)
        /// </summary>
        private void CreatePickupZone()
        {
            var zone = new GameObject("PickupZone");
            zone.layer = IgnoreRaycastLayer;
            zone.transform.SetParent(transform, false);
            zone.transform.localPosition = trigger.center;
            var sphere = zone.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            float scale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)));
            sphere.radius = pickupRadius / scale;
        }

        /// <summary>
        /// 자식 렌더러 전체를 감싸는 크기로 BoxCollider 를 맞춘다.
        /// 렌더러 월드 AABB 의 꼭짓점 8개를 로컬 공간으로 옮겨 감싸고, XZ 로 pickupPadding, 위로 pickupTopPadding 만큼 넓힌다.
        /// </summary>
        private void FitColliderToRenderers()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // 로컬 공간 기준으로 AABB 계산 (회전·스케일이 있어도 정확하게)
            Bounds local = default;
            bool first = true;
            foreach (var r in renderers)
            {
                Bounds world = r.bounds;
                Vector3 c = world.center, e = world.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = transform.InverseTransformPoint(corner);
                    if (first) { local = new Bounds(p, Vector3.zero); first = false; }
                    else local.Encapsulate(p);
                }
            }

            Vector3 lossy = transform.lossyScale;
            float topPad = pickupTopPadding / Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
            Vector3 pad = new Vector3(
                pickupPadding * 2f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                topPad,
                pickupPadding * 2f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            // 위로만 늘린다 (바닥은 그대로 → Launch 의 착지 높이 계산 불변)
            trigger.center = local.center + Vector3.up * (topPad * 0.5f);
            trigger.size = local.size + pad;
        }

        /// <summary>
        /// 멀리서도 위치와 종류를 알 수 있는 빛기둥을 자식으로 만든다.
        /// 콜라이더 없는 실린더(높이 beaconHeight, 그림자 X)에 종류별 색의 URP Unlit 머티리얼을 입힌다.
        /// </summary>
        private void CreateBeacon(Color color)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(transform, false);
            // 실린더 기본 높이 2m → y 스케일 = 높이/2, 바닥이 상자 피벗에 오도록
            beacon.transform.localPosition = Vector3.up * (beaconHeight * 0.5f);
            beacon.transform.localScale = new Vector3(0.3f, beaconHeight * 0.5f, 0.3f);

            beaconRenderer = beacon.GetComponent<Renderer>();
            beaconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                beaconMaterial = new Material(shader);
                beaconMaterial.SetColor("_BaseColor", color);
                beaconRenderer.sharedMaterial = beaconMaterial;
            }
        }

        /// <summary>코드로 만든 빛기둥 머티리얼 정리 (메모리 누수 방지)</summary>
        private void OnDestroy()
        {
            if (beaconMaterial != null) Destroy(beaconMaterial);
        }
    }
}
