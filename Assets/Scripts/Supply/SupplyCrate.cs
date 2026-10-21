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
    ///  - Kinematic Rigidbody + Trigger Collider. 플레이어 콜라이더가 닿으면 키 입력 없이 자동 획득.
    ///  - 획득 대상은 구체 클래스가 아닌 계약(IGrenadeReceiver / IHealPackReceiver)으로 찾는다.
    ///    가득 차서 못 먹으면(false) 물자는 그대로 남는다.
    ///  - 착지 후에는 시간이 지나도 사라지지 않는다. (획득 또는 라운드 정리 때만 제거)
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SupplyCrate : MonoBehaviour
    {
        public enum State { Idle, Falling, Landed }

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

        [Header("위치 표시 (멀리서도 보이는 빛기둥)")]
        [SerializeField] private bool showBeacon = true;
        [SerializeField] private float beaconHeight = 25f;
        [SerializeField] private Color grenadeColor = new Color(1f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color healColor = new Color(0.2f, 1f, 0.35f, 1f);

        public SupplyDef Def { get; private set; }
        public State CurrentState { get; private set; }
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

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            if (fitColliderToRenderers) FitColliderToRenderers();
        }

        /// <summary>
        /// 낙하 시작. groundPoint = 착지할 지면 지점, dropHeight = 지면 기준 시작 높이.
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

            if (showBeacon) CreateBeacon(def.SupplyType == SupplyType.Grenade ? grenadeColor : healColor);
        }

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
        /// 감속 구간(landingHeight 이내)은 등감속: v² = vLand² + (v0² - vLand²) · (h / H).
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

        private void Land() => CurrentState = State.Landed;

        private void OnTriggerEnter(Collider other) => TryCollect(other);

        // 가득 차서 못 먹은 뒤, 자리를 벗어나지 않고 수량이 줄어든 경우 대비
        private void OnTriggerStay(Collider other) => TryCollect(other);

        private void TryCollect(Collider other)
        {
            if (removed || Def == null) return;
            if (ApplyTo(other) == false) return;

            EventBus<SupplyCollectedEvent>.Publish(new SupplyCollectedEvent(Def));
            Remove();
        }

        /// <summary>보급 종류에 맞는 계약을 찾아 실제 수량을 보충. 대상이 아니거나 가득 차면 false</summary>
        private bool ApplyTo(Collider other)
        {
            switch (Def.SupplyType)
            {
                case SupplyType.Grenade:
                    var grenadeReceiver = other.GetComponentInParent<IGrenadeReceiver>();
                    return grenadeReceiver != null && grenadeReceiver.AddGrenades(Def.Amount);
                case SupplyType.Heal:
                    var healPackReceiver = other.GetComponentInParent<IHealPackReceiver>();
                    return healPackReceiver != null && healPackReceiver.AddHealPacks(Def.Amount);
                default:
                    return false;
            }
        }

        /// <summary>획득·라운드 정리 시 호출</summary>
        public void Remove()
        {
            if (removed) return;
            removed = true;
            Removed?.Invoke(this);
            Destroy(gameObject);
        }

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
            Vector3 pad = new Vector3(
                pickupPadding * 2f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                0f,
                pickupPadding * 2f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            trigger.center = local.center;
            trigger.size = local.size + pad;
        }

        private void CreateBeacon(Color color)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(transform, false);
            // 실린더 기본 높이 2m → y 스케일 = 높이/2, 바닥이 상자 피벗에 오도록
            beacon.transform.localPosition = Vector3.up * (beaconHeight * 0.5f);
            beacon.transform.localScale = new Vector3(0.3f, beaconHeight * 0.5f, 0.3f);

            var beaconRenderer = beacon.GetComponent<Renderer>();
            beaconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                beaconMaterial = new Material(shader);
                beaconMaterial.SetColor("_BaseColor", color);
                beaconRenderer.sharedMaterial = beaconMaterial;
            }
        }

        private void OnDestroy()
        {
            if (beaconMaterial != null) Destroy(beaconMaterial);
        }
    }
}
