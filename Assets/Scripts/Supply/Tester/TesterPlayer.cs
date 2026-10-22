using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Supply.Tester
{
    /// <summary>
    /// 보급 테스트용 임시 플레이어. 실제 플레이어(E1) 대신 보급이 쓰는 계약만 구현한다.
    ///  - WASD: 월드 기준 상하좌우 이동, Shift: 달리기
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

        private Rigidbody body;
        private Vector3 moveInput;
        private bool sprint;

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
        }

        private void OnEnable() => ServiceLocator.Register<IPlayerTarget>(this);
        private void OnDisable() => ServiceLocator.Unregister<IPlayerTarget>(this);

        private void Start()
        {
            Grenades = DataManager.Player != null ? DataManager.Player.StartGrenades : 3;
            HealPacks = Mathf.Clamp(startHealPacks, 0, maxHealPacks);
            PublishGrenades();
            PublishHealPacks();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            moveInput = Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
            sprint = keyboard.leftShiftKey.isPressed;

            if (keyboard.gKey.wasPressedThisFrame) UseGrenade();
            if (keyboard.hKey.wasPressedThisFrame) UseHealPack();
        }

        private void FixedUpdate()
        {
            if (moveInput == Vector3.zero) return;
            float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);
            body.MovePosition(body.position + moveInput * (speed * Time.fixedDeltaTime));
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
