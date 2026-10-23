using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace HybridShooter.Supply.Tester
{
    /// <summary>
    /// 보급 테스트용 임시 플레이어. 실제 플레이어(E1) 대신 보급이 쓰는 계약만 구현한다.
    ///  - WASD: 시점(yaw) 기준 이동, Shift: 달리기, 높이는 바닥(Terrain 및 다른 Collider)을 따라감
    ///  - 마우스 우클릭 드래그: 시점 회전 (카메라가 플레이어 주위를 돈다)
    ///  - G: 수류탄 1개 사용, H: 회복 팩 1개 사용 (보급으로 다시 채워지는지 확인용)
    ///  - IPlayerTarget 등록 → 보급 낙하 위치(플레이어 기준 30~150m) 계산에 사용됨
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TesterPlayer : MonoBehaviour, IPlayerTarget, IGrenadeReceiver, IHealPackReceiver
    {
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private float sprintMultiplier = 2.5f;

        [Header("회복 팩 (PlayerTable 에 컬럼이 생기기 전까지 여기서 설정)")]
        [SerializeField] private int startHealPacks = 1;
        [SerializeField] private int maxHealPacks = 3;

        [Header("바닥 높이 맞추기 (Terrain 포함 모든 Collider)")]
        [Tooltip("바닥 표면에서 transform.position.y 까지의 높이 (캡슐 피벗이 중앙이므로 기본 1)")]
        [FormerlySerializedAs("terrainHeightOffset")]
        [SerializeField] private float groundOffset = 1f;
        [Tooltip("현재 높이보다 이만큼 위까지의 표면은 올라설 수 있다 (지붕/천장 위로 순간이동 방지)")]
        [SerializeField] private float maxStepUp = 2f;
        [Tooltip("바닥으로 인식할 레이어")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("시점 (마우스 우클릭 드래그)")]
        [SerializeField] private float lookSensitivity = 0.15f;
        [SerializeField] private float minPitch = 10f;
        [SerializeField] private float maxPitch = 85f;
        [SerializeField] private float startPitch = 48f;

        [Tooltip("바닥으로 인식한 Collider 가 바뀔 때마다 콘솔에 출력하고, 레이를 Scene 뷰에 그린다")]
        [SerializeField] private bool debugGround;

        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private Collider lastGround;
        private Rigidbody body;
        private Vector3 moveInput;
        private bool sprint;
        private float viewYaw;
        private float viewPitch;

        /// <summary>우클릭 드래그로 바뀌는 시점 각도. 카메라(TesterFollowCamera)가 읽어서 플레이어 주위를 돈다</summary>
        public float ViewYaw => viewYaw;
        public float ViewPitch => viewPitch;

        public int Grenades { get; private set; }
        public int MaxGrenades
        {
            get
            {
                int baseMax = DataManager.Player != null ? DataManager.Player.MaxGrenades : 5;
                var upgrades = ServiceLocator.Get<IUpgradeState>();
                return baseMax + (upgrades != null ? Mathf.RoundToInt(upgrades.GetTotalValue(UpgradeType.GrenadeCapacity)) : 0);
            }
        }

        public int HealPacks { get; private set; }
        public int MaxHealPacks => maxHealPacks;

        // IPlayerTarget
        public Transform Root => transform;
        public Transform AimPoint => transform;
        public Transform ViewPoint => transform;
        public PlayerTrackingInfo playerTrackingInfo { get; }

        public Vector3 Position => Root.position;
        // 임시로 처리
        public Vector3 Velocity => Vector3.zero;
        public Vector3 Forward => ViewPoint.forward;
        public bool IsAlive => true;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            viewPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);
        }

        private void OnEnable() => ServiceLocator.Register<IPlayerTarget>(this);
        private void OnDisable() => ServiceLocator.Unregister<IPlayerTarget>(this);

        private void Start()
        {
            Grenades = DataManager.Player != null ? DataManager.Player.StartGrenades : 3;
            HealPacks = Mathf.Clamp(startHealPacks, 0, maxHealPacks);
            PublishGrenades();
            PublishHealPacks();

            // 시작 위치를 지형 위로 맞춘다
            Vector3 start = body.position;
            start.y = GetGroundedY(start);
            body.position = start;
            transform.position = start;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                viewYaw += delta.x * lookSensitivity;
                viewPitch = Mathf.Clamp(viewPitch - delta.y * lookSensitivity, minPitch, maxPitch);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            // 시점(yaw) 기준으로 이동 방향을 돌려서, 보는 쪽이 항상 W 가 되게 한다
            moveInput = Quaternion.Euler(0f, viewYaw, 0f) * Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
            sprint = keyboard.leftShiftKey.isPressed;

            if (keyboard.gKey.wasPressedThisFrame) UseGrenade();
            if (keyboard.hKey.wasPressedThisFrame) UseHealPack();
        }

        private void FixedUpdate()
        {
            if (moveInput == Vector3.zero) return;
            float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);
            Vector3 next = body.position + moveInput * (speed * Time.fixedDeltaTime);
            next.y = GetGroundedY(next);
            body.MovePosition(next);
        }

        /// <summary>
        /// 해당 XZ 에서 아래로 레이를 쏴 가장 가까운 바닥(Terrain, 메시 등 모든 Collider) 높이 + offset.
        /// 자기 자신과 Trigger 는 무시하고, 맞은 게 없으면 현재 높이를 유지한다
        /// </summary>
        private float GetGroundedY(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, position.y + maxStepUp, position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 2000f, groundMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            float groundY = position.y - groundOffset;
            Collider ground = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                groundY = hit.point.y;
                ground = hit.collider;
            }

            if (debugGround)
            {
                Debug.DrawRay(origin, Vector3.down * 30f, ground != null ? Color.green : Color.red);
                if (ground != lastGround)
                {
                    Debug.Log($"[TesterPlayer] ground: {(ground != null ? ground.name + " (y=" + groundY.ToString("F2") + ")" : "none")} / rayOriginY={origin.y:F2} hits={count}", ground);
                    lastGround = ground;
                }
            }
            return groundY + groundOffset;
        }

        public bool AddGrenades(int amount)
        {
            int max = MaxGrenades;
            if (amount <= 0 || Grenades >= max) return false;
            Grenades = Mathf.Min(max, Grenades + amount);
            PublishGrenades();
            return true;
        }

        public bool AddHealPacks(int amount)
        {
            if (amount <= 0 || HealPacks >= maxHealPacks) return false;
            HealPacks = Mathf.Min(maxHealPacks, HealPacks + amount);
            PublishHealPacks();
            return true;
        }

        private void UseGrenade()
        {
            if (Grenades <= 0) return;
            Grenades--;
            PublishGrenades();
        }

        private void UseHealPack()
        {
            if (HealPacks <= 0) return;
            HealPacks--;
            PublishHealPacks();
        }

        private void PublishGrenades() => EventBus<GrenadeCountChangedEvent>.Publish(new GrenadeCountChangedEvent(Grenades, MaxGrenades));
        private void PublishHealPacks() => EventBus<HealPackCountChangedEvent>.Publish(new HealPackCountChangedEvent(HealPacks, maxHealPacks));
    }
}
