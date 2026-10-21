using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 입력 · 스태미나 · 모터 · 카메라 리그 · 무기 · 체력을 이어주는 중개 클래스.
    /// 각 컴포넌트는 서로를 모르고, "누가 누구에게 무엇을 넘기는가"는 전부 여기에만 있다.
    ///
    ///  입력 Look  ──────────────→ 카메라 리그.AddLook
    ///  입력 Move  + 리그 yaw ───→ 월드 이동 방향 → 모터.SetMoveInput
    ///  입력 Sprint/Roll + 스태미나 → 가능 여부 판단 → 모터
    ///  입력 Jump ───────────────→ 앞에 장애물이 있으면 벽넘기(PlayerVault), 없으면 점프(모터)
    ///  입력 Fire/Aim + 카메라 광선 → 무기(PlayerWeapons.Tick)
    ///  입력 1~4 / R / G / V ─────→ 무기 교체 / 재장전 / 수류탄 / 회복
    ///  리그.Yaw ────────────────→ 몸체 회전 (1인칭·3인칭 모두 카메라가 보는 방향에 고정)
    ///  PlayerDamagedEvent ──────→ 모터 피격 감속
    ///  체력 0 ──────────────────→ 모든 행동 중단 (사망 애니메이션은 PlayerAnimator)
    ///
    /// "지금 무엇을 할 수 있나" 규칙
    ///  - 구르기·벽넘기·투척·회복 중에는 사격·재장전·무기 교체·다른 행동 불가
    ///  - 재장전·무기 꺼내는 중에는 투척·회복 불가 (구르기·이동은 가능)
    ///  - 사격·조준 버튼을 누르고 있으면 달리기 안 됨
    ///
    /// 배치: 플레이어 루트(Rigidbody, CapsuleCollider, PlayerMotor, PlayerStamina, PlayerInputReader, PlayerController,
    ///       PlayerHealth, PlayerAnimator, PlayerWeapons, PlayerGrenadeThrower, PlayerHealPacks, PlayerVault, PlayerHandIK),
    ///       카메라 리그는 별도 오브젝트(PlayerCameraRig, CameraModeSwitcher + 카메라).
    ///       "Tools > Player > 3. Setup Player Prefab" 이 빠진 컴포넌트를 붙여 준다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerStamina), typeof(PlayerHealth))]
    [RequireComponent(typeof(PlayerAnimator))]
    public class PlayerController : MonoBehaviour, IPlayerTarget
    {
        private const string HitSlowKey = "hit";
        private const string ExhaustedKey = "exhausted";

        [SerializeField] private PlayerCameraRig cameraRig;

        [Header("스태미나")]
        [SerializeField] private float sprintStaminaPerSecond = 20f;
        [SerializeField] private float rollStaminaCost = 25f;

        [Header("감속")]
        [Tooltip("스태미나가 바닥난 동안의 이동 속도 배율")]
        [SerializeField, Range(0.1f, 1f)] private float exhaustedSpeedMultiplier = 0.7f;
        [Tooltip("피격 시 이동 속도 배율과 지속 시간")]
        [SerializeField, Range(0.1f, 1f)] private float hitSpeedMultiplier = 0.5f;
        [SerializeField] private float hitSlowDuration = 0.35f;

        private IPlayerInputSource input;
        private PlayerMotor motor;
        private PlayerStamina stamina;
        private PlayerHealth health;
        private PlayerAnimator anim;
        private PlayerWeapons weapons;
        private PlayerGrenadeThrower grenades;
        private PlayerHealPacks healPacks;
        private PlayerVault vault;
        private Transform aimPoint;
        private bool exhaustedApplied;

        private readonly PlayerTrackingInfo trackingInfo = new PlayerTrackingInfo();

        public bool IsInvulnerable => health.IsInvulnerable;
        public PlayerMotor Motor => motor;
        public PlayerCameraRig CameraRig => cameraRig;
        public PlayerHealth Health => health;
        public PlayerWeapons Weapons => weapons;

        // ── IPlayerTarget (몬스터·보스가 참조) ──
        public Transform Root => transform;
        public Transform AimPoint => aimPoint != null ? aimPoint : transform;
        public Transform ViewPoint => cameraRig != null ? cameraRig.ViewPoint : transform;
        public bool IsAlive => health.IsAlive;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            stamina = GetComponent<PlayerStamina>();
            health = GetComponent<PlayerHealth>();
            anim = GetComponent<PlayerAnimator>();
            weapons = GetComponent<PlayerWeapons>();
            grenades = GetComponent<PlayerGrenadeThrower>();
            healPacks = GetComponent<PlayerHealPacks>();
            vault = GetComponent<PlayerVault>();
            input = GetComponent<IPlayerInputSource>();
            cameraRig = cameraRig ? cameraRig : FindAnyObjectByType<PlayerCameraRig>();
            if (input == null) Debug.LogError("[PlayerController] IPlayerInputSource 구현(PlayerInputReader)이 같은 오브젝트에 필요합니다.", this);
            if (cameraRig == null) Debug.LogError("[PlayerController] cameraRig 가 연결되지 않았습니다.", this);
        }

        private void Start()
        {
            // PlayerAnimator 가 Awake 에서 Animator 를 찾은 뒤라야 가슴 뼈를 알 수 있다
            aimPoint = FindAimPoint();

            var def = DataManager.Player;
            if (def == null) return;
            motor.WalkSpeed = def.MoveSpeed;
            if (cameraRig != null) cameraRig.Configure(def.LookSensitivity, def.PitchMin, def.PitchMax);
        }

        private void OnEnable()
        {
            EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
            health.Died += OnDied;
            health.Revived += OnRevived;
            ServiceLocator.Register<IPlayerTarget>(this);
        }

        private void OnDisable()
        {
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);
            health.Died -= OnDied;
            health.Revived -= OnRevived;
            ServiceLocator.Unregister<IPlayerTarget>(this);
        }

        private void Update()
        {
            if (input == null || cameraRig == null) return;

            cameraRig.AddLook(input.Look);

            if (health.IsAlive == false)
            {
                // 죽은 뒤엔 시점만 돌릴 수 있다. 쌓인 버튼 입력은 버린다
                DiscardActionInputs();
                motor.SetMoveInput(Vector3.zero, false);
                cameraRig.SetAiming(false);
                return;
            }

            Vector2 move = input.Move;
            Quaternion yawRotation = Quaternion.Euler(0f, cameraRig.Yaw, 0f);

            bool vaulting = vault != null && vault.IsVaulting;
            bool usingItem = (grenades != null && grenades.IsThrowing) || (healPacks != null && healPacks.IsUsing);
            // 총을 쏘거나 다른 손동작을 할 수 있는 상태인가
            bool canAct = vaulting == false && motor.IsRolling == false && usingItem == false;

            if (vaulting == false)
            {
                HandleMove(move, yawRotation);
                if (usingItem == false) HandleRoll(move, yawRotation);
                else input.ConsumeRoll();
                HandleJump();
            }
            else
            {
                input.ConsumeRoll();
                input.ConsumeJump();
            }

            HandleActions(canAct);

            Ray aimRay = GetAimRay();
            if (weapons != null) weapons.Tick(input.FireHeld, input.AimHeld, aimRay, canAct && motor.IsSprinting == false);

            // 조준 줌: 실제로 조준 중일 때만 (구르기·투척·회복·벽넘기 중엔 풀림)
            bool aimingNow = weapons != null && weapons.IsAiming;
            cameraRig.SetAiming(aimingNow, aimingNow && weapons.Current != null ? weapons.Current.AimFovMultiplier : -1f);
            if (grenades != null) grenades.SetAimDirection(aimRay.direction);

            // 시점(1인칭/3인칭)과 무관하게 몸은 항상 카메라가 보는 방향을 향한다 (벽넘기 중엔 장애물 방향 유지)
            if (vaulting == false) motor.LockFacing(cameraRig.Yaw);
        }

        private void HandleMove(Vector2 move, Quaternion yawRotation)
        {
            Vector3 worldMove = yawRotation * new Vector3(move.x, 0f, move.y);
            // 쏘거나 조준하는 동안에는 달리지 않는다
            bool wantSprint = input.SprintHeld && input.FireHeld == false && input.AimHeld == false
                              && move.sqrMagnitude > 0.04f && stamina.IsExhausted == false;

            motor.SetMoveInput(worldMove, wantSprint);

            // 실제로 달리고 있을 때만 소모. 바닥나면 이번 프레임부터 걷기로
            if (motor.IsSprinting && stamina.Drain(sprintStaminaPerSecond) == false)
                motor.SetMoveInput(worldMove, false);

            // 스태미나 바닥 감속은 상태가 바뀔 때만 갱신
            if (stamina.IsExhausted != exhaustedApplied)
            {
                exhaustedApplied = stamina.IsExhausted;
                motor.SetSpeedModifier(ExhaustedKey, exhaustedApplied ? exhaustedSpeedMultiplier : 1f);
            }
        }

        private void HandleRoll(Vector2 move, Quaternion yawRotation)
        {
            if (input.ConsumeRoll() == false) return;
            if (move.sqrMagnitude < 0.09f || motor.CanRoll == false || stamina.Has(rollStaminaCost) == false) return;

            // 누르고 있는 방향 중 더 강한 축 하나로 스냅 (대각선 없음)
            Vector3 local = Mathf.Abs(move.x) > Mathf.Abs(move.y)
                ? new Vector3(Mathf.Sign(move.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(move.y));

            if (motor.TryRoll(yawRotation * local)) stamina.TrySpend(rollStaminaCost);
        }

        private void HandleJump()
        {
            if (input.ConsumeJump() == false) return;
            // 앞에 넘을 장애물이 있으면 벽넘기, 아니면 점프 (뒤로 걷는 중엔 벽넘기 안 함)
            if (vault != null && input.Move.y > -0.1f && vault.TryVault()) return;
            motor.RequestJump();
        }

        /// <summary>무기 교체 · 재장전 · 수류탄 · 회복. 버튼은 항상 소비해서, 못 하는 상태에서 누른 입력이 나중에 튀어나오지 않게 한다</summary>
        private void HandleActions(bool canAct)
        {
            bool slotPressed = input.ConsumeWeaponSlot(out int slot);
            bool reloadPressed = input.ConsumeReload();
            bool grenadePressed = input.ConsumeGrenade();
            bool healPressed = input.ConsumeHeal();
            if (canAct == false) return;

            if (weapons != null)
            {
                if (slotPressed) weapons.TrySelect(slot);
                if (reloadPressed) weapons.TryReload();
            }

            bool handsFree = weapons == null || weapons.IsBusy == false;
            if (handsFree == false) return;

            if (grenadePressed && grenades != null) grenades.TryThrow();
            else if (healPressed && healPacks != null) healPacks.TryUse();
        }

        private void DiscardActionInputs()
        {
            input.ConsumeJump();
            input.ConsumeRoll();
            HandleActions(false);
        }

        /// <summary>화면 정중앙으로 나가는 광선 = 조준선</summary>
        private Ray GetAimRay()
        {
            Camera cam = cameraRig.ViewCamera;
            if (cam != null) return cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return new Ray(transform.position + Vector3.up * 1.6f, Quaternion.Euler(cameraRig.Pitch, cameraRig.Yaw, 0f) * Vector3.forward);
        }

        private void OnPlayerDamaged(PlayerDamagedEvent evt)
            => motor.AddTimedSpeedModifier(HitSlowKey, hitSpeedMultiplier, hitSlowDuration);

        private void OnDied()
        {
            if (vault != null) vault.Cancel();
            if (grenades != null) grenades.Cancel();
            if (healPacks != null) healPacks.Cancel();
            if (weapons != null) weapons.CancelReload();
            motor.SetMoveInput(Vector3.zero, false);
        }

        private void OnRevived()
        {
            stamina.Refill();
            if (weapons != null) weapons.ResetAll();
            if (grenades != null) grenades.ResetCount();
            if (healPacks != null) healPacks.ResetCount();
        }

        /// <summary>몬스터가 조준할 지점: 가슴 뼈. 없으면 가슴 높이에 빈 오브젝트를 만든다</summary>
        private Transform FindAimPoint()
        {
            Animator animator = anim.Animator;
            if (animator != null && animator.isHuman)
            {
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null) return chest;
            }
            var point = new GameObject("AimPoint").transform;
            point.SetParent(transform, false);
            point.localPosition = Vector3.up * 1.3f;
            return point;
        }

        /// <summary>군집(몬스터 무리)에게 넘겨줄 플레이어 정보. 같은 인스턴스를 갱신해 돌려주므로 보관만 하지 말고 매번 읽을 것</summary>
        public PlayerTrackingInfo GetTrackingInfo()
        {
            trackingInfo.Position = transform.position;
            trackingInfo.Velocity = motor.Velocity;
            Vector3 forward = motor.Forward;
            forward.y = 0f;
            trackingInfo.Forward = forward.normalized;
            return trackingInfo;
        }
    }
}
