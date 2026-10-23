using HybridShooter.Core.Contracts;
using Study.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;
using DG.Tweening.Core;
namespace HybridShooter.Player
{
    /// <summary>몸체 회전 방식. LockYaw = 카메라 방향에 고정(현재 1·3인칭 공통), FollowMove = 이동 방향을 바라봄(현재 미사용)</summary>
    public enum FacingMode { FollowMove, LockYaw }

    /// <summary>시점 모드</summary>
    public enum ViewMode { FirstPerson, ThirdPerson }

    /// <summary>
    /// 플레이어 하나를 통째로 다루는 컴포넌트. 입력 → 이동(물리) → 카메라 리그 → 체력·아이템.
    ///
    /// 몸은 하나 (True First Person)
    ///  - 1인칭·3인칭 모두 같은 몸(Body)과 같은 총을 쓴다. 카메라만 2대(FP_Camera / TP_Camera).
    ///  - 1인칭에서는 머리 렌더러만 "그림자만" 모드로 바꿔 화면에서 숨기고 그림자는 남긴다.
    ///  - 카메라는 리그(Rig = 좌우, Pitch = 상하) 를 돌려서 움직인다. 카메라 자체 위치는 건드리지 않는다.
    ///  - 상하 시선(pitch)은 척추 뼈에 나눠 실어 상체가 함께 숙이고 젖힌다.
    ///
    /// 이동 요약
    ///  - 터레인 위: SphereCast 로 바닥·경사를 판정, 걷기는 경사면을 따라 속도를 투영한다. 지상에서는 중력을 꺼서 미끄러짐/들썩임 방지.
    ///  - 마찰 0 콜라이더 → 벽에 비비며 걸려도 붙지 않음.
    ///  - 감속/가속: 이름(key)이 있는 속도 배율 목록. 곱해서 적용한다. (피격 둔화, 스태미나 바닥, 상태이상 등)
    ///    + 자동: 몸이 이동 방향과 어긋나 있을 때(뒤로 걷기, 뒤돌아 출발) 느려짐.
    ///  - 외부 제어: 벽넘기처럼 애니메이션이 몸을 직접 옮겨야 할 때 BeginExternalControl ~ EndExternalControl 동안 물리를 멈춘다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour, IPlayerTarget, IHealable, IGrenadeReceiver, IDamageable
    {
        #region 설정(인스펙터)
        [Header("이동")]
        [Tooltip("걷기 속도(m/s). PlayerController 가 PlayerTable.MoveSpeed 로 덮어쓴다")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float maxWalkSpeed = 10;
        [SerializeField] private float groundAcceleration = 45f;
        [SerializeField] private float groundDeceleration = 55f;
        [SerializeField] private float airAcceleration = 10f;
        [Tooltip("FollowMove 모드에서 몸이 이동 방향으로 도는 속도(도/초). LockYaw 에서는 쓰지 않음")]
        [SerializeField] private float turnSpeed = 720f;
        [Tooltip("몸 정면 기준 정반대로 움직일 때의 속도 배율. 옆걸음은 거의 감속 없음")]
        [SerializeField, Range(0.1f, 1f)] private float backwardMultiplier = 0.65f;

        [Header("점프")]
        [SerializeField] private float jumpHeight = 1.2f;
        [Tooltip("발이 떨어진 직후에도 점프를 허용하는 시간")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("착지 직전에 누른 점프를 기억해 주는 시간")]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("구르기")]
        [SerializeField] private float rollSpeed = 9f;
        [SerializeField] private float rollDuration = 0.45f;
        [SerializeField] private float rollCooldown = 0.35f;
        [Tooltip("구르기 시작 후 이 시간 동안 무적")]
        [SerializeField] private float rollInvulnerableTime = 0.3f;
        [Tooltip("구르기 진행도(0~1) → 속도 배율. 처음 빠르고 끝에서 느려짐")]
        [SerializeField] private AnimationCurve rollSpeedCurve = new AnimationCurve(new Keyframe(0f, 1.4f), new Keyframe(1f, 0.4f));

        [Header("바닥 판정")]
        [Tooltip("플레이어 자신 레이어는 빼 두는 것을 권장")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundCheckDistance = 0.15f;
        [SerializeField] private float maxSlopeAngle = 50f;

        [Header("시점")]
        [Tooltip("시작 시점")]
        [SerializeField] private ViewMode viewMode = ViewMode.ThirdPerson;
        [SerializeField] private float viewTransitionTime = 0.5f;

        [Header("마우스 회전")]
        [Tooltip("마우스 이동 1픽셀당 회전 각도(도)")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private bool invertY = false;
        [SerializeField] private float minPitch = -70f;
        [SerializeField] private float maxPitch = 80f;

        [Header("카메라 리그")]
        [Tooltip("1인칭 카메라 (FP_Pitch 의 자식)")]
        [SerializeField] private Transform FirstParsonCam;
        [Tooltip("3인칭 카메라 (TP_CameraArm 의 자식)")]
        [SerializeField] private Transform ThirdParsonCam;
        [Tooltip("1인칭 리그: 눈 높이. 좌우(yaw) 회전")]
        [SerializeField] private Transform fpRig;
        [Tooltip("1인칭 리그: 상하(pitch) 회전")]
        [SerializeField] private Transform fpPitch;
        [Tooltip("3인칭 리그: 어깨 높이. 좌우(yaw) 회전")]
        [SerializeField] private Transform tpRig;
        [Tooltip("3인칭 리그: 상하(pitch) 회전")]
        [SerializeField] private Transform tpPitch;
        [Tooltip("3인칭 카메라 팔. 벽에 막히면 로컬 z 만 앞으로 당긴다")]
        [SerializeField] private Transform tpCameraArm;
        [Tooltip("카메라가 벽을 뚫지 않도록 검사할 레이어. 플레이어 레이어는 빼 두는 것을 권장")]
        [SerializeField] private LayerMask cameraCollisionMask = ~0;
        [SerializeField] private float cameraCollisionRadius = 0.2f;
        [Tooltip("벽에서 벗어났을 때 카메라 팔이 원래 길이로 돌아가는 속도(m/s). 막힐 때는 즉시 당긴다")]
        [SerializeField] private float cameraReturnSpeed = 6f;

        [Header("조준점 / 기준점")]
        [Tooltip("1인칭일 때 AimPoint 로 쓸 지점")]
        [SerializeField] private Transform FirstParsonAim;
        [Tooltip("3인칭일 때 AimPoint 로 쓸 지점")]
        [SerializeField] private Transform ThirdParsonAim;
        [Tooltip("비워 두면 자기 Transform")]
        [SerializeField] private Transform rootTransform;
        [Tooltip("시점에 따라 자동으로 바뀐다 (FirstParsonAim / ThirdParsonAim)")]
        [SerializeField] private Transform aimPoint;
        [Tooltip("시야 기준점. 시점에 따라 현재 카메라로 자동으로 바뀐다")]
        [SerializeField] private Transform viewPoint;

        [Header("머리 숨김 (1인칭)")]
        [Tooltip("1인칭에서 숨길 머리 렌더러(얼굴, 헬멧, 안경, 후드 등). 그림자는 남긴다")]
        [SerializeField] private Renderer[] headRenderers;
        [Tooltip("머리 뼈. 이 뼈 아래 붙은 렌더러(머리카락 등)는 자동으로 숨김 목록에 들어간다")]
        [SerializeField] private Transform headBone;
        [Tooltip("시점 전환 중 카메라가 눈 위치에서 이 거리 안이면 머리를 숨긴다 (머리 뚫림 방지)")]
        [SerializeField] private float headHideRadius = 0.6f;

        [Header("상체 조준")]
        [Tooltip("상하 시선을 나눠 실을 척추 뼈 (spine_01, spine_02, spine_03 순)")]
        [SerializeField] private Transform[] spineBones;
        [Tooltip("각 척추 뼈가 받을 pitch 비율. 합이 1 이면 가슴이 시선과 같은 각도로 숙여진다")]
        [SerializeField] private float[] spineWeights = { 0.25f, 0.35f, 0.4f };
        [Tooltip("pitch 전체에 곱하는 배율. 0 이면 상체 조준 끔")]
        [SerializeField, Range(0f, 1f)] private float upperBodyPitchWeight = 1f;
        [Tooltip("몸 애니메이터. 꺼져 있으면 뼈 회전이 누적되므로 상체 조준을 건너뛴다. 비워 두면 자식에서 찾는다")]
        [SerializeField] private Animator bodyAnimator;
        [Tooltip("화면에 보이는 몸(Body). 매 프레임 시선 yaw 로 돌려 카메라와 박자를 맞춘다. 비워 두면 bodyAnimator 의 Transform")]
        [SerializeField] private Transform bodyVisual;

        [Header("1인칭 눈 위치")]
        [Tooltip("상체 조준으로 머리가 움직인 만큼 FP_Rig 도 같이 옮긴다. 끄면 숙일 때 카메라가 몸 뒤에 남아 등·몸 안쪽이 보인다. 애니메이션 흔들림은 포함되지 않는다")]
        [SerializeField] private bool followSpineAim = true;
        [Tooltip("눈을 몸 정면 쪽으로 이만큼 내민다(m). 숨긴 머리 자리에 남는 목 구멍이 카메라 뒤로 가게 한다")]
        [SerializeField] private float eyeForwardOffset = 0.12f;
        [Tooltip("켜면 FP_Rig 높이가 머리 뼈 위치를 부분적으로 따라간다 (걷기 흔들림이 약간 생김)")]
        [SerializeField] private bool followHeadBone = false;
        [Tooltip("0 = 원래 높이 고정, 1 = 머리 뼈 위치 그대로")]
        [SerializeField, Range(0f, 1f)] private float headFollowWeight = 0.5f;
        [Tooltip("머리 뼈 → 눈 위치 보정 (ViewRig 로컬 기준)")]
        [SerializeField] private Vector3 headEyeOffset = new Vector3(0f, 0.08f, 0.1f);
        [SerializeField] private float headFollowSmoothTime = 0.08f;

        [Header("체력")]
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int currentHealth = 100;

        [Header("수류탄")]
        [SerializeField] private int maxGrenades = 3;
        [SerializeField] private int grenades = 0;
        #endregion

        #region 상태
        private Rigidbody rb;
        private CapsuleCollider capsule;

        // 외부 입력
        private Vector3 moveDirection;
        private bool sprintRequested;
        private FacingMode facingMode = FacingMode.FollowMove;
        private float lockedYaw;
        private float jumpRequestedTime = float.NegativeInfinity;
        private Vector2 lastMoveInput;

        // 이동 상태
        private bool isGrounded;
        private Vector3 groundNormal = Vector3.up;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpTime = float.NegativeInfinity;
        private bool jumpedSinceGrounded;

        private bool isRolling;
        private Vector3 rollDirection;
        private float rollStartTime;
        private float rollReadyTime;

        private struct SpeedModifier { public string Key; public float Multiplier; public float EndTime; }
        private readonly List<SpeedModifier> modifiers = new List<SpeedModifier>();

        private bool externalControl;
        private RigidbodyInterpolation savedInterpolation;

        /// <summary>마우스로 누적한 시선 각도. 몸 yaw 와 카메라 yaw 가 항상 이 값을 따른다</summary>
        private float lookYaw;
        private float lookPitch;

        // 카메라 리그 (Awake 에서 1회 캐싱)
        private bool rigReady;
        private Camera fpCamera;
        private Camera tpCamera;
        private float fpFov, tpFov;
        private Vector3 fpCamHomePos, tpCamHomePos;
        private Quaternion fpCamHomeRot, tpCamHomeRot;
        private Vector3 fpRigHomePos;
        private Vector3 tpArmHomePos;
        private float tpArmDistance;
        private Vector3 headFollowVelocity;
        private Vector3 spineAimHeadDelta;     // 이번 프레임 상체 조준 때문에 머리 뼈가 움직인 양 (월드)
        private Transform currentCameraTransform;
        private readonly RaycastHit[] cameraHits = new RaycastHit[8];

        // 시점 전환 (트윈 콜백은 Awake 에서 만들어 두고 재사용 → 전환할 때마다 할당 없음)
        private Tween viewTween;
        private float transitionT;
        private ViewMode transitionTarget;
        private Transform transitionMover;     // 실제로 움직이는 카메라 (출발 카메라)
        private Transform transitionGoal;      // 도착 카메라 (위치만 참고, 비활성 상태)
        private Vector3 moverHomePos, goalHomePos;
        private Quaternion moverHomeRot, goalHomeRot;
        private Camera moverCamera;
        private float moverFovFrom, moverFovTo;
        private DOGetter<float> transitionGetter;
        private DOSetter<float> transitionSetter;
        private TweenCallback transitionComplete;

        // 머리 숨김
        private Renderer[] headRendererCache = Array.Empty<Renderer>();
        private bool headHidden;
        private bool headStateApplied;
        #endregion

        #region 유니티 메시지
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();

            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            capsule.sharedMaterial = new PhysicsMaterial("PlayerNoFriction")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            lockedYaw = rb.rotation.eulerAngles.y;
            lookYaw = lockedYaw;

            if (rootTransform == null) rootTransform = transform;
            if (bodyAnimator == null) bodyAnimator = GetComponentInChildren<Animator>();
            if (bodyVisual == null && bodyAnimator != null) bodyVisual = bodyAnimator.transform;
            if (bodyVisual == transform) bodyVisual = null; // 리지드바디 자신은 돌리면 안 된다

            CacheRig();
            CacheHeadRenderers();

            transitionGetter = () => transitionT;
            transitionSetter = x => { transitionT = x; ApplyTransitionPose(); };
            transitionComplete = OnViewTransitionComplete;

            IsDead = false;
        }

        private void Start()
        {
            // 시작할 때는 트윈 없이 바로 적용 (토글로 처리하면 시작하자마자 전환 연출이 돈다)
            ApplyViewModeImmediate(viewMode);
            SetCursorLocked(true);
        }

        private void OnDestroy()
        {
            viewTween?.Kill();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) SetCursorLocked(true);
        }

        private void Update()
        {
            if (externalControl) return;
            InputHandle();
        }

        private void FixedUpdate()
        {
            if (externalControl) return;
            float dt = Time.fixedDeltaTime;

            UpdateGround(dt);

            if (isRolling) UpdateRoll(dt);
            else UpdateWalk(dt);

            UpdateJump();
            UpdateFacing(dt);
        }

        /// <summary>
        /// 애니메이터가 뼈를 움직인 뒤, 몸(보간된 위치)이 움직인 뒤에 실행된다.
        /// 순서: 몸 회전 → 상체 조준 → 리그 회전 → 1인칭 눈 위치 → 3인칭 벽 충돌 → 시점 전환 중이면 전환 위치
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateBodyVisualYaw();
            ApplySpineAim();

            if (rigReady == false) return;
            UpdateRigRotation();
            UpdateEyePosition();
            UpdateCameraArm(dt);
            if (CameraChanging) ApplyTransitionPose();
        }
        #endregion

        #region 입력
        public class InputPlayerSettings
        {
            public Key jumpKey = Key.Space;
            public Key sprintKey = Key.LeftShift;
            public Key RollKey = Key.LeftCtrl;
            public Key frontKey = Key.W;
            public Key backKey = Key.S;
            public Key leftKey = Key.A;
            public Key rightKey = Key.D;
            public Key viewToggleKey = Key.V;
            public Key cursorToggleKey = Key.Escape;


            public InputPlayerSettings() { }

            public InputPlayerSettings(Key jump, Key sprint, Key roll, Key front, Key back, Key left, Key right, Key viewToggle = Key.V)
            {
                viewToggleKey = viewToggle;
                jumpKey = jump;
                sprintKey = sprint;
                RollKey = roll;
                frontKey = front;
                backKey = back;
                leftKey = left;
                rightKey = right;
            }

        }
        InputPlayerSettings keySettings = new InputPlayerSettings();

        private void InputHandle()
        {

            // 마우스 → 시선 회전.
            UpdateLook();
            LockFacing(lookYaw);
            if(IsDead) return;
            // 0) 커서 잠금 해제/복귀 (좌클릭은 조준용으로 비워 둔다)
            if (SimpleInput.GetKeyDown(keySettings.cursorToggleKey))
                SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

            // 1) 시점 전환
            if (SimpleInput.GetKeyDown(keySettings.viewToggleKey)) ToggleViewMode();


            // 이동 키 → 로컬 입력 (x = 좌우, y = 앞뒤)
            Vector2 input = Vector2.zero;
            if (SimpleInput.GetKey(keySettings.frontKey)) input.y += 1f;
            if (SimpleInput.GetKey(keySettings.backKey)) input.y -= 1f;
            if (SimpleInput.GetKey(keySettings.rightKey)) input.x += 1f;
            if (SimpleInput.GetKey(keySettings.leftKey)) input.x -= 1f;
            lastMoveInput = Vector2.ClampMagnitude(input, 1f); // 대각선이 더 빨라지지 않게

            // 로컬 입력 → 월드 방향
            Vector3 worldMove = InputToWorld(lastMoveInput);

            // 이동 + 달리기 (누르고 있는 동안)
            bool sprint = SimpleInput.GetKey(keySettings.sprintKey);
            SetMoveInput(worldMove, sprint);

            // 점프
            if (SimpleInput.GetKeyDown(keySettings.jumpKey)) RequestJump();

            // 구르기: 이동 입력이 있으면 그 방향, 없으면 몸 정면
            if (SimpleInput.GetKeyDown(keySettings.RollKey))
            {
                Vector3 rollDir = worldMove.sqrMagnitude > 0.0001f ? worldMove : Forward;
                TryRoll(rollDir);
            }
        }

        /// <summary>WASD 입력(x=좌우, y=앞뒤)을 시선 yaw 로 돌려 월드 수평 방향으로 바꾼다.
        /// 1인칭·3인칭 모두 "카메라 정면 = 플레이어 정면 = W"</summary>
        private Vector3 InputToWorld(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;
            return Quaternion.Euler(0f, lookYaw, 0f) * new Vector3(input.x, 0f, input.y);
        }

        /// <summary>마우스 이동량을 yaw / pitch 에 누적. 커서가 풀려 있으면 회전하지 않는다</summary>
        private void UpdateLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            // delta 는 이미 "이번 프레임 이동량" 이라 deltaTime 을 곱하지 않는다
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            lookYaw = Mathf.Repeat(lookYaw + delta.x, 360f);
            lookPitch = Mathf.Clamp(lookPitch + (invertY ? delta.y : -delta.y), minPitch, maxPitch);
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = locked == false;
        }
        #endregion

        #region 시점 전환
        /// <summary>
        /// 시점을 바꾼다. 출발 카메라가 도착 카메라 자리로 날아간 뒤 카메라를 교체한다.
        /// 전환 중이거나 이미 그 시점이면 무시 (연타 방지)
        /// </summary>
        public void SwitchViewMode(ViewMode mode)
        {
            if (rigReady == false || CameraChanging || mode == viewMode) return;

            bool toFirst = mode == ViewMode.FirstPerson;
            transitionTarget = mode;
            transitionMover = toFirst ? ThirdParsonCam : FirstParsonCam;
            transitionGoal = toFirst ? FirstParsonCam : ThirdParsonCam;
            moverHomePos = toFirst ? tpCamHomePos : fpCamHomePos;
            moverHomeRot = toFirst ? tpCamHomeRot : fpCamHomeRot;
            goalHomePos = toFirst ? fpCamHomePos : tpCamHomePos;
            goalHomeRot = toFirst ? fpCamHomeRot : tpCamHomeRot;
            moverCamera = toFirst ? tpCamera : fpCamera;
            moverFovFrom = toFirst ? tpFov : fpFov;
            moverFovTo = toFirst ? fpFov : tpFov;

            LockFacing(lookYaw);
            CameraChanging = true;
            transitionT = 0f;

            viewTween?.Kill();
            viewTween = DOTween.To(transitionGetter, transitionSetter, 1f, viewTransitionTime)
                .SetEase(Ease.InOutSine)
                .SetUpdate(UpdateType.Late)
                .OnComplete(transitionComplete);
        }

        public void ToggleViewMode() =>
            SwitchViewMode(viewMode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson);

        /// <summary>
        /// 전환 중 매 프레임: 출발점·도착점을 둘 다 "각 카메라의 원래 자리(부모 기준 home 포즈)" 로 다시 계산해 보간한다.
        /// (자기 현재 위치를 출발점으로 쓰면 매 프레임 출발점이 따라와서 감속 곡선처럼 보인다)
        /// 트윈 콜백과 LateUpdate 양쪽에서 불리지만 같은 값을 다시 쓰는 것이라 순서에 상관없이 결과가 같다.
        /// </summary>
        private void ApplyTransitionPose()
        {
            if (CameraChanging == false) return;
            float t = transitionT;

            Transform moverParent = transitionMover.parent;
            Transform goalParent = transitionGoal.parent;
            Vector3 startPos = moverParent.TransformPoint(moverHomePos);
            Quaternion startRot = moverParent.rotation * moverHomeRot;
            Vector3 goalPos = goalParent.TransformPoint(goalHomePos);
            Quaternion goalRot = goalParent.rotation * goalHomeRot;

            Vector3 pos = Vector3.LerpUnclamped(startPos, goalPos, t);
            transitionMover.SetPositionAndRotation(pos, Quaternion.SlerpUnclamped(startRot, goalRot, t));
            moverCamera.fieldOfView = Mathf.LerpUnclamped(moverFovFrom, moverFovTo, t);

            // 카메라가 눈 근처면 머리를 숨기고, 밖이면 보여준다
            Vector3 eyePos = FirstParsonCam.parent.TransformPoint(fpCamHomePos);
            bool insideHead = (pos - eyePos).sqrMagnitude < headHideRadius * headHideRadius;
            SetHeadHidden(insideHead);
        }

        private void OnViewTransitionComplete()
        {
            ApplyViewModeImmediate(transitionTarget);
            ViewModeChanged?.Invoke(viewMode);
        }

        /// <summary>
        /// 트윈 없이 바로 그 시점으로 만든다 (시작 시, 전환 완료 시).
        /// 두 카메라의 로컬 포즈·FOV 원복, 카메라 활성 교체, 조준점·시야 기준점·머리 숨김 갱신
        /// </summary>
        private void ApplyViewModeImmediate(ViewMode mode)
        {
            viewTween?.Kill();
            viewTween = null;
            CameraChanging = false;
            viewMode = mode;
            if (rigReady == false) return;

            bool first = mode == ViewMode.FirstPerson;

            FirstParsonCam.SetLocalPositionAndRotation(fpCamHomePos, fpCamHomeRot);
            ThirdParsonCam.SetLocalPositionAndRotation(tpCamHomePos, tpCamHomeRot);
            fpCamera.fieldOfView = fpFov;
            tpCamera.fieldOfView = tpFov;

            FirstParsonCam.gameObject.SetActive(first);
            ThirdParsonCam.gameObject.SetActive(first == false);

            currentCameraTransform = first ? FirstParsonCam : ThirdParsonCam;
            viewPoint = currentCameraTransform;
            aimPoint = first ? FirstParsonAim : ThirdParsonAim;

            SetHeadHidden(first);
        }
        #endregion

        #region 카메라 리그
        /// <summary>카메라·리그의 원래 자리와 FOV 를 1회 저장한다. 빠진 참조가 있으면 카메라 기능을 끈다</summary>
        private void CacheRig()
        {
            rigReady = FirstParsonCam != null && ThirdParsonCam != null
                && fpRig != null && fpPitch != null && tpRig != null && tpPitch != null;
            if (rigReady == false)
            {
                Debug.LogError($"{name}: PlayerController 카메라 리그 참조가 비어 있습니다 (FP/TP Camera, Rig, Pitch)", this);
                return;
            }

            fpCamera = FirstParsonCam.GetComponent<Camera>();
            tpCamera = ThirdParsonCam.GetComponent<Camera>();
            if (fpCamera == null || tpCamera == null)
            {
                rigReady = false;
                Debug.LogError($"{name}: FirstParsonCam / ThirdParsonCam 에 Camera 컴포넌트가 없습니다", this);
                return;
            }

            fpFov = fpCamera.fieldOfView;
            tpFov = tpCamera.fieldOfView;
            FirstParsonCam.GetLocalPositionAndRotation(out fpCamHomePos, out fpCamHomeRot);
            ThirdParsonCam.GetLocalPositionAndRotation(out tpCamHomePos, out tpCamHomeRot);
            fpRigHomePos = fpRig.localPosition;
            if (tpCameraArm != null)
            {
                tpArmHomePos = tpCameraArm.localPosition;
                tpArmDistance = Mathf.Max(0f, -tpArmHomePos.z);
            }
            currentCameraTransform = viewMode == ViewMode.FirstPerson ? FirstParsonCam : ThirdParsonCam;
        }

        /// <summary>
        /// 리그 좌우 = 시선 yaw, 상하 = 시선 pitch.
        /// 리그는 Player 의 자식이지만 월드 yaw 를 직접 넣는다 → 몸(리지드바디 보간 회전)을 기다리지 않고 마우스에 바로 반응
        /// </summary>
        private void UpdateRigRotation()
        {
            Quaternion yaw = Quaternion.Euler(0f, lookYaw, 0f);
            Quaternion pitch = Quaternion.Euler(lookPitch, 0f, 0f);
            fpRig.rotation = yaw;
            fpPitch.localRotation = pitch;
            tpRig.rotation = yaw;
            tpPitch.localRotation = pitch;
        }

        /// <summary>
        /// 1인칭 리그 위치 = 원래 높이 + 정면으로 조금 내밀기 + 상체 조준으로 머리가 움직인 양.
        /// (선택) followHeadBone 이면 머리 뼈 위치를 부분적으로, 부드럽게 따라간다 (애니메이션 흔들림 포함)
        /// </summary>
        private void UpdateEyePosition()
        {
            Vector3 basePos = fpRigHomePos + new Vector3(0f, 0f, eyeForwardOffset);
            if (followSpineAim) basePos += fpRig.parent.InverseTransformVector(spineAimHeadDelta);

            if (followHeadBone == false || headBone == null)
            {
                fpRig.localPosition = basePos;
                headFollowVelocity = Vector3.zero;
                return;
            }

            Vector3 headLocal = fpRig.parent.InverseTransformPoint(headBone.position) + headEyeOffset;
            Vector3 target = Vector3.Lerp(basePos, headLocal, headFollowWeight);
            fpRig.localPosition = Vector3.SmoothDamp(fpRig.localPosition, target, ref headFollowVelocity, headFollowSmoothTime);
        }

        /// <summary>
        /// 3인칭 벽 충돌: 어깨(팔의 x·y, z=0) 에서 뒤로 SphereCast → 막히면 팔의 로컬 z 만 당긴다.
        /// 막힐 때는 즉시, 풀릴 때는 cameraReturnSpeed 로 천천히 돌아간다. 1인칭(전환 중 아님)에서는 검사하지 않는다
        /// </summary>
        private void UpdateCameraArm(float dt)
        {
            if (tpCameraArm == null) return;
            if (viewMode == ViewMode.FirstPerson && CameraChanging == false) return;

            float maxDistance = Mathf.Max(0f, -tpArmHomePos.z);
            Vector3 origin = tpPitch.TransformPoint(new Vector3(tpArmHomePos.x, tpArmHomePos.y, 0f));
            Vector3 back = -tpPitch.forward;
            float target = CameraCollisionDistance(origin, back, maxDistance);

            tpArmDistance = target < tpArmDistance
                ? target
                : Mathf.MoveTowards(tpArmDistance, target, cameraReturnSpeed * dt);
            tpCameraArm.localPosition = new Vector3(tpArmHomePos.x, tpArmHomePos.y, -tpArmDistance);
        }

        /// <summary>어깨 → 카메라 사이에 벽이 있으면 그 앞까지의 거리. 자기 콜라이더는 무시</summary>
        private float CameraCollisionDistance(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (maxDistance <= 0f) return 0f;
            int count = Physics.SphereCastNonAlloc(origin, cameraCollisionRadius, direction, cameraHits,
                maxDistance, cameraCollisionMask, QueryTriggerInteraction.Ignore);

            float distance = maxDistance;
            for (int i = 0; i < count; i++)
            {
                var hit = cameraHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance <= 0f) continue; // 시작부터 겹친 경우는 무시
                distance = Mathf.Min(distance, hit.distance);
            }
            return distance;
        }
        #endregion

        #region 머리 숨김
        /// <summary>인스펙터 목록 + 머리 뼈 아래 렌더러(머리카락 등)를 합쳐 1회 저장</summary>
        private void CacheHeadRenderers()
        {
            var list = new List<Renderer>();
            if (headRenderers != null)
            {
                for (int i = 0; i < headRenderers.Length; i++)
                    if (headRenderers[i] != null && list.Contains(headRenderers[i]) == false) list.Add(headRenderers[i]);
            }
            if (headBone != null)
            {
                var children = headBone.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < children.Length; i++)
                    if (list.Contains(children[i]) == false) list.Add(children[i]);
            }
            headRendererCache = list.ToArray();
        }

        /// <summary>숨김 = 그림자만 그림(ShadowsOnly), 보임 = 원래대로(On). 상태가 바뀔 때만 렌더러를 돈다</summary>
        private void SetHeadHidden(bool hidden)
        {
            if (headStateApplied && headHidden == hidden) return;
            headHidden = hidden;
            headStateApplied = true;

            var mode = hidden ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            for (int i = 0; i < headRendererCache.Length; i++)
                headRendererCache[i].shadowCastingMode = mode;
        }
        #endregion

        #region 상체 조준
        /// <summary>
        /// 보이는 몸의 yaw 를 매 프레임 시선 yaw 로 맞춘다.
        /// 리지드바디 회전은 물리 박자(FixedUpdate)로만 바뀌어 카메라(매 프레임)보다 늦게, 들쭉날쭉 따라온다 → 3인칭에서 몸이 떨려 보임.
        /// 캡슐은 세로축 대칭이라 물리 회전이 조금 늦어도 충돌은 같다. 물리 회전(rb.MoveRotation)은 Forward·뒷걸음 감속 계산용으로 그대로 둔다
        /// </summary>
        private void UpdateBodyVisualYaw()
        {
            if (bodyVisual == null) return;
            if (facingMode == FacingMode.LockYaw && externalControl == false)
                bodyVisual.rotation = Quaternion.Euler(0f, lookYaw, 0f);
            else if (bodyVisual.localRotation != Quaternion.identity)
                bodyVisual.localRotation = Quaternion.identity; // FollowMove·외부 제어 중에는 리지드바디 회전을 그대로 따른다
        }

        /// <summary>
        /// 시선 pitch 를 척추 뼈들에 비율대로 나눠 몸의 오른쪽 축으로 돌린다 (1·3인칭 공통).
        /// 애니메이터가 이번 프레임 포즈를 쓴 뒤(LateUpdate)에 덧붙이는 방식이라 애니메이션 위에 얹힌다
        /// </summary>
        private void ApplySpineAim()
        {
            spineAimHeadDelta = Vector3.zero;
            if (spineBones == null || spineBones.Length == 0 || upperBodyPitchWeight <= 0f) return;
            // 애니메이터가 멈추면 뼈가 매 프레임 덮어써지지 않아 회전이 계속 누적된다 → 건너뜀
            if (bodyAnimator != null && bodyAnimator.isActiveAndEnabled == false) return;

            Vector3 axis = bodyVisual != null ? bodyVisual.right : transform.right;
            float pitch = lookPitch * upperBodyPitchWeight;
            Vector3 headBefore = headBone != null ? headBone.position : Vector3.zero;
            for (int i = 0; i < spineBones.Length; i++)
            {
                Transform bone = spineBones[i];
                if (bone == null || spineWeights == null || i >= spineWeights.Length) continue;
                bone.rotation = Quaternion.AngleAxis(pitch * spineWeights[i], axis) * bone.rotation;
            }
            // 숙인 전후 머리 위치 차이 → 1인칭 눈이 머리를 따라가게 (같은 프레임끼리 비교하니 애니메이션 흔들림은 빠진다)
            if (headBone != null) spineAimHeadDelta = headBone.position - headBefore;
        }
        #endregion

        #region 이동(물리)
        private void UpdateGround(float dt)
        {
            bool wasGrounded = isGrounded;
            float radius = capsule.radius;
            Vector3 origin = rb.position + Vector3.up * radius;
            bool hitSomething = Physics.SphereCast(origin, radius * 0.9f, Vector3.down, out var hit,
                groundCheckDistance + radius * 0.1f, groundMask, QueryTriggerInteraction.Ignore);

            bool walkable = hitSomething && Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle;
            // 점프 직후엔 아직 바닥이 가까워도 공중으로 취급
            if (Time.time - lastJumpTime < 0.1f) walkable = false;

            isGrounded = walkable;
            groundNormal = walkable ? hit.normal : Vector3.up;

            if (isGrounded)
            {
                lastGroundedTime = Time.time;
                jumpedSinceGrounded = false;
                if (wasGrounded == false) Landed?.Invoke();
            }

            rb.useGravity = isGrounded == false;
        }

        private void UpdateWalk(float dt)
        {
            float speed = walkSpeed * (IsSprintingInput ? sprintMultiplier : 1f) * ModifierProduct() * FacingSpeedFactor();
            speed = Mathf.Clamp(speed, 0, maxWalkSpeed);
            Vector3 target = moveDirection * speed;

            Vector3 velocity = rb.linearVelocity;
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);

            float accel = isGrounded ? (target.sqrMagnitude > 0.0001f ? groundAcceleration : groundDeceleration) : airAcceleration;
            horizontal = Vector3.MoveTowards(horizontal, target, accel * dt);

            if (isGrounded)
                rb.linearVelocity = AlongGround(horizontal);
            else
                rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        // IsSprinting 은 "지금 속도" 기준이라 출발 순간이 false → 입력 기준으로 따로 본다
        private bool IsSprintingInput => sprintRequested && isGrounded && moveDirection.sqrMagnitude > 0.0001f;

        private void UpdateRoll(float dt)
        {
            float t = (Time.time - rollStartTime) / rollDuration;
            if (t >= 1f)
            {
                isRolling = false;
                RollEnded?.Invoke();
                return;
            }

            Vector3 horizontal = rollDirection * (rollSpeed * Mathf.Max(0f, rollSpeedCurve.Evaluate(t)));
            if (isGrounded)
            {
                rb.linearVelocity = AlongGround(horizontal);
            }
            else
            {
                float y = rb.linearVelocity.y;
                rb.linearVelocity = new Vector3(horizontal.x, y, horizontal.z);
            }
        }

        private void UpdateJump()
        {
            if (isRolling) return;
            if (Time.time - jumpRequestedTime > jumpBufferTime) return;

            bool canJump = isGrounded || (jumpedSinceGrounded == false && Time.time - lastGroundedTime <= coyoteTime);
            if (canJump == false) return;

            float gravity = Mathf.Abs(Physics.gravity.y);
            Vector3 v = rb.linearVelocity;
            v.y = Mathf.Sqrt(2f * gravity * jumpHeight);
            rb.linearVelocity = v;

            jumpRequestedTime = float.NegativeInfinity;
            jumpedSinceGrounded = true;
            lastJumpTime = Time.time;
            isGrounded = false;
            rb.useGravity = true;
            Jumped?.Invoke();
        }

        private void UpdateFacing(float dt)
        {
            if (facingMode == FacingMode.LockYaw)
            {
                rb.MoveRotation(Quaternion.Euler(0f, lockedYaw, 0f));
                return;
            }

            Vector3 dir = isRolling ? rollDirection : moveDirection;
            if (dir.sqrMagnitude < 0.01f) return;

            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(rb.rotation.eulerAngles.y, targetYaw, turnSpeed * dt);
            rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
        }
        #endregion

        #region 외부 API
        public event Action Jumped;
        public event Action Landed;
        /// <summary>인자 = 구르는 월드 방향</summary>
        public event Action<Vector3> RollStarted;
        public event Action RollEnded;
        /// <summary>시점 전환이 끝났을 때 알림 (UI 등)</summary>
        public event Action<ViewMode> ViewModeChanged;

        public bool IsGrounded => isGrounded;
        public bool IsRolling => isRolling;
        public bool IsInvulnerable => isRolling && Time.time - rollStartTime < rollInvulnerableTime;
        public bool IsSprinting => sprintRequested && isGrounded && isRolling == false && HorizontalSpeed > 0.1f;
        public bool CanRoll => isRolling == false && isGrounded && Time.time >= rollReadyTime;
        public Vector3 Velocity => rb.linearVelocity;
        public float HorizontalSpeed { get { var v = rb.linearVelocity; return new Vector2(v.x, v.z).magnitude; } }
        public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }
        public Vector3 Forward => rb.rotation * Vector3.forward;
        public float RollDuration => rollDuration;
        public float SprintMultiplier => sprintMultiplier;
        /// <summary>벽넘기 등으로 바깥(애니메이션 루트모션)이 위치를 움직이는 중. 이 동안 모터는 물리를 건드리지 않는다</summary>
        public bool IsExternallyControlled => externalControl;

        public ViewMode CurrentViewMode => viewMode;
        public bool IsFirstPerson => viewMode == ViewMode.FirstPerson;
        /// <summary>시점 전환 연출 중 (이 동안 전환 입력은 무시된다)</summary>
        public bool CameraChanging { get; private set; }
        /// <summary>지금 화면을 그리는 카메라의 Transform</summary>
        public Transform CurrentCameraTransform => currentCameraTransform;

        public float LookYaw => lookYaw;
        public float LookPitch => lookPitch;
        /// <summary>현재 시선 회전 (yaw + pitch). 조준·발사 방향 계산에 쓰면 된다</summary>
        public Quaternion LookRotation => Quaternion.Euler(lookPitch, lookYaw, 0f);

        public Transform Root => rootTransform;
        public Transform AimPoint => aimPoint;
        public Transform ViewPoint => viewPoint != null ? viewPoint : (aimPoint != null ? aimPoint : transform);

        /// <summary>worldDirection: 월드 기준, 수평, 크기 0~1 (스틱 기울기 반영)</summary>
        public void SetMoveInput(Vector3 worldDirection, bool sprint)
        {
            worldDirection.y = 0f;
            moveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
            sprintRequested = sprint;
        }

        /// <summary>몸을 이 방향(yaw)에 고정. 1인칭·3인칭 공통으로 카메라 yaw 를 넘긴다</summary>
        public void LockFacing(float yaw) { facingMode = FacingMode.LockYaw; lockedYaw = yaw; }

        /// <summary>이동(구르기) 방향으로 몸을 돌림. 지금은 쓰지 않지만 나중에 자유 시점이 필요하면 호출</summary>
        public void FollowMoveFacing() => facingMode = FacingMode.FollowMove;

        public void RequestJump() => jumpRequestedTime = Time.time;

        /// <summary>방향은 호출 쪽이 정한다 (4방향 스냅 등). 지상이 아니거나 쿨다운이면 false</summary>
        public bool TryRoll(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (CanRoll == false || worldDirection.sqrMagnitude < 0.0001f) return false;

            isRolling = true;
            rollDirection = worldDirection.normalized;
            rollStartTime = Time.time;
            rollReadyTime = Time.time + rollDuration + rollCooldown;
            RollStarted?.Invoke(rollDirection);
            return true;
        }

        /// <summary>
        /// 지속되는 속도 배율 (스태미나 바닥 등). 같은 key 는 덮어쓰고, 1 이면 해제.
        /// </summary>
        public void SetSpeedModifier(string key, float multiplier)
        {
            RemoveModifier(key);
            if (Mathf.Approximately(multiplier, 1f) == false)
                modifiers.Add(new SpeedModifier { Key = key, Multiplier = multiplier, EndTime = -1f });
        }

        /// <summary>
        /// 시간제한 속도 배율. multiplier &lt; 1 감속, &gt; 1 가속. 같은 key 를 다시 걸면 시간이 갱신된다 (피격 연타 시 중첩되지 않음)
        /// </summary>
        public void AddTimedSpeedModifier(string key, float multiplier, float duration)
        {
            RemoveModifier(key);
            modifiers.Add(new SpeedModifier { Key = key, Multiplier = multiplier, EndTime = Time.time + duration });
        }

        public void RemoveSpeedModifier(string key) => RemoveModifier(key);

        public void Teleport(Vector3 position)
        {
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            isRolling = false;
        }

        /// <summary>
        /// 위치 제어를 바깥에 넘긴다 (벽넘기). Rigidbody 를 kinematic 으로 바꿔 충돌에 밀리지 않게 하고,
        /// 이후 <see cref="MoveExternal"/> 로 옮긴다. 끝나면 반드시 <see cref="EndExternalControl"/>.
        /// </summary>
        public void BeginExternalControl()
        {
            if (externalControl) return;
            externalControl = true;
            isRolling = false;
            moveDirection = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            savedInterpolation = rb.interpolation;
            // kinematic + 직접 위치 지정 시 보간이 켜져 있으면 한 프레임 늦게 따라와 떨린다
            rb.interpolation = RigidbodyInterpolation.None;
            rb.isKinematic = true;
        }

        /// <summary>외부 제어 중에만 의미 있음. 월드 기준으로 이만큼 이동 (루트모션 delta)</summary>
        public void MoveExternal(Vector3 delta)
        {
            if (externalControl == false) return;
            Vector3 p = rb.position + delta;
            rb.position = p;
            transform.position = p;
        }

        /// <summary>외부 제어 중 몸 방향을 즉시 바꾼다</summary>
        public void SetRotationImmediate(float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            rb.rotation = r;
            transform.rotation = r;
            lockedYaw = yaw;
            lookYaw = yaw;
        }

        public void EndExternalControl()
        {
            if (externalControl == false) return;
            externalControl = false;
            rb.isKinematic = false;
            rb.interpolation = savedInterpolation;
            rb.linearVelocity = Vector3.zero;
            jumpRequestedTime = float.NegativeInfinity;
        }
        #endregion

        #region 체력·아이템
        public bool IsAlive => currentHealth > 0;
        public int Grenades => grenades;
        public int MaxGrenades => maxGrenades;

        public Faction Faction => throw new NotImplementedException();

        /// <summary>죽었거나, 이미 가득 찼거나, amount 가 0 이하면 false (아이템을 소모하지 않게)</summary>
        public bool Heal(int amount)
        {
            if (amount <= 0 || IsAlive == false || currentHealth >= maxHealth) return false;
            currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
            return true;
        }

        /// <summary>이미 최대치거나 amount 가 0 이하면 false (아이템을 소모하지 않게)</summary>
        public bool AddGrenades(int amount)
        {
            if (amount <= 0 || grenades >= maxGrenades) return false;
            grenades = Mathf.Min(grenades + amount, maxGrenades);
            return true;
        }
        #endregion

        #region 보조
        /// <summary>수평 속도를 크기는 유지한 채 경사면 접선 방향으로 바꾼다 (오르막에서 느려지지 않고, 내리막에서 바닥에 붙음)</summary>
        private Vector3 AlongGround(Vector3 horizontal)
        {
            float magnitude = horizontal.magnitude;
            if (magnitude < 0.0001f) return Vector3.zero;
            return Vector3.ProjectOnPlane(horizontal, groundNormal).normalized * magnitude;
        }

        /// <summary>몸 정면과 이동 방향이 어긋날수록 느려짐: 정면/옆 1.0 → 정반대 backwardMultiplier</summary>
        private float FacingSpeedFactor()
        {
            if (moveDirection.sqrMagnitude < 0.0001f) return 1f;
            // FollowMove 는 몸이 곧 이동 방향으로 돌기 때문에 뒷걸음 감속을 걸지 않는다 (현재 미사용)
            if (facingMode == FacingMode.FollowMove) return 1f;
            float dot = Vector3.Dot(rb.rotation * Vector3.forward, moveDirection.normalized);
            return Mathf.Lerp(backwardMultiplier, 1f, Mathf.InverseLerp(-0.6f, 0.2f, dot));
        }

        private float ModifierProduct()
        {
            float product = 1f;
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                var m = modifiers[i];
                if (m.EndTime >= 0f && Time.time >= m.EndTime) { modifiers.RemoveAt(i); continue; }
                product *= m.Multiplier;
            }
            return product;
        }

        private void RemoveModifier(string key)
        {
            for (int i = modifiers.Count - 1; i >= 0; i--)
                if (modifiers[i].Key == key) modifiers.RemoveAt(i);
        }
        bool IsDead = false;
        public void TakeDamage(in DamageInfo info)
        {
            currentHealth = Mathf.Max(0, currentHealth - info.Amount);
            if (IsDead == false && currentHealth == 0)
            {
                Die();
            }
        }

        private void Die()
        {
            IsDead = true;
        }

        #endregion
    }
}
