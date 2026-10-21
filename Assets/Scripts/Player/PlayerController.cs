using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 입력 · 스태미나 · 모터 · 카메라 리그를 이어주는 중개 클래스.
    /// 네 컴포넌트는 서로를 모르고, "누가 누구에게 무엇을 넘기는가"는 전부 여기에만 있다.
    ///
    ///  입력 Look  ──────────────→ 카메라 리그.AddLook
    ///  입력 Move  + 리그 yaw ───→ 월드 이동 방향 → 모터.SetMoveInput
    ///  입력 Sprint/Roll + 스태미나 → 가능 여부 판단 → 모터
    ///  리그.Mode ───────────────→ 몸체 회전 방식 (1인칭 = 카메라 방향 고정, 3인칭 = 이동 방향)
    ///  PlayerDamagedEvent ──────→ 모터 피격 감속
    ///
    /// 배치: 플레이어 루트(Rigidbody, CapsuleCollider, PlayerMotor, PlayerStamina, PlayerInputReader, PlayerController),
    ///       카메라 리그는 별도 오브젝트(PlayerCameraRig, CameraModeSwitcher + 카메라).
    ///
    /// 일단 애니메이션도 담당한다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerStamina))]
    public class PlayerController : MonoBehaviour
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
        private bool exhaustedApplied;

        private readonly PlayerTrackingInfo trackingInfo = new PlayerTrackingInfo();

        public bool IsInvulnerable => motor.IsInvulnerable;
        public PlayerMotor Motor => motor;
        public PlayerCameraRig CameraRig => cameraRig;


        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            stamina = GetComponent<PlayerStamina>();
            input = GetComponent<IPlayerInputSource>();

            if (input == null) Debug.LogError("[PlayerController] IPlayerInputSource 구현(PlayerInputReader)이 같은 오브젝트에 필요합니다.", this);
            if (cameraRig == null) Debug.LogError("[PlayerController] cameraRig 가 연결되지 않았습니다.", this);
        }

        private void Start()
        {
            var def = DataManager.Player;
            if (def == null) return;
            motor.WalkSpeed = def.MoveSpeed;
            cameraRig.Configure(def.LookSensitivity, def.PitchMin, def.PitchMax);
        }

        private void OnEnable() => EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
        private void OnDisable() => EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);

        private void Update()
        {
            if (input == null || cameraRig == null) return;

            cameraRig.AddLook(input.Look);

            Vector2 move = input.Move;
            Quaternion yawRotation = Quaternion.Euler(0f, cameraRig.Yaw, 0f);

            HandleMove(move, yawRotation);
            HandleRoll(move, yawRotation);
            if (input.ConsumeJump()) motor.RequestJump();

            if (cameraRig.Mode == CameraMode.FirstPerson) motor.LockFacing(cameraRig.Yaw);
            else motor.FollowMoveFacing();

        }

        private void HandleMove(Vector2 move, Quaternion yawRotation)
        {
            Vector3 worldMove = yawRotation * new Vector3(move.x, 0f, move.y);
            bool wantSprint = input.SprintHeld && move.sqrMagnitude > 0.04f && stamina.IsExhausted == false;

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

        private void OnPlayerDamaged(PlayerDamagedEvent evt)
            => motor.AddTimedSpeedModifier(HitSlowKey, hitSpeedMultiplier, hitSlowDuration);

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
