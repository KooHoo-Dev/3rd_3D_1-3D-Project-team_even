using System;
using System.Collections.Generic;
using DG.Tweening;
using DG.Tweening.Core;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>인스펙터 설정과 내부 상태 필드</summary>
    public partial class PlayerController
    {
        #region 설정(인스펙터)
        [Header("이동")]
        [Tooltip("걷기 속도(m/s). PlayerController 가 PlayerTable.MoveSpeed 로 덮어쓴다")]
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float maxWalkSpeed = 10;
        [SerializeField] private float groundAcceleration = 45f;
        [SerializeField] private float groundDeceleration = 55f;
        [SerializeField] private float airAcceleration = 10f;
        [Tooltip("몸 정면 기준 정반대로 움직일 때의 속도 배율. 옆걸음은 거의 감속 없음")]
        [SerializeField, Range(0.1f, 1f)] private float backwardMultiplier = 0.65f;

        [Header("점프")]
        [SerializeField] private float jumpHeight = 1.2f;
        [Tooltip("발이 떨어진 직후에도 점프를 허용하는 시간")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("착지 직전에 누른 점프를 기억해 주는 시간")]
        [SerializeField] private float jumpBufferTime = 0.12f;
        [Tooltip("최대 하강 속도(m/s). 이보다 빨리 떨어지지 않는다. 이 속도에 닿으면 낙하 애니메이션 (중력 9.81 기준 15 m/s ≈ 11m 이상 떨어질 때)")]
        [SerializeField] private float maxFallSpeed = 15f;

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
        [Tooltip("지상이던 발밑에서 땅이 이 거리 안으로만 떨어졌으면 땅에 다시 붙인다 (능선 꼭대기·내리막 턱에서 붕 뜨지 않게). 0 = 끔")]
        [SerializeField] private float groundSnapDistance = 0.4f;
        [Tooltip("이동 방향으로 이만큼(m) 더 앞까지 벽을 미리 본다. 벽에 박고 튕기기 전에 벽을 따라 미끄러지게")]
        [SerializeField] private float wallProbeDistance = 0.05f;

        [Header("급경사 미끄럼 (maxSlopeAngle 보다 가파른 면)")]
        [Tooltip("면을 따라 미끄러지는 최고 속도(m/s)")]
        [SerializeField] private float steepSlideMaxSpeed = 4f;
        [Tooltip("미끄럼 속도·방향이 목표로 바뀌는 가속도(m/s²)")]
        [SerializeField] private float steepSlideAcceleration = 20f;
        [Tooltip("미끄러지는 중 좌우·내리막 조작 비율 (걷기 속도 기준). 오르막 조작은 받지 않는다")]
        [SerializeField, Range(0f, 1f)] private float steepSlideSteer = 0.5f;

        [Header("시점")]
        [Tooltip("시작 시점")]
        [SerializeField] private ViewMode viewMode = ViewMode.ThirdPerson;
        [SerializeField] private float viewTransitionTime = 0.5f;
        [Tooltip("보스 등장 등 자동 전환 시간(초). ConfigTable.CameraTransitionTime 이 있으면 그 값")]
        [SerializeField] private float autoTransitionTime = 1.2f;

        [Header("보스전 카메라")]
        [Tooltip("보스전 3인칭 카메라 팔 위치 (TP_Pitch 기준). 어깨 시점보다 멀고 높게 → 플레이어·보스 전신·바닥 예고 범위가 한 화면에")]
        [SerializeField] private Vector3 bossArmPosition = new Vector3(0.6f, 0.9f, -5.5f);
        [Tooltip("보스전 3인칭 FOV 추가(도). ConfigTable.BossCameraFovAdd 가 있으면 그 값")]
        [SerializeField] private float bossFovAdd = 10f;

        [Header("마우스 회전")]
        [Tooltip("마우스 이동 1픽셀당 회전 각도(도)")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private bool invertY = false;
        [SerializeField] private float minPitch = -70f;
        [SerializeField] private float maxPitch = 80f;
        [Tooltip("1인칭에서 아래로 내려 볼 수 있는 최대 각도(도). 더 내리면 총 든 팔이 꺾여 화면에 들어온다 (maxPitch 보다 크면 maxPitch)")]
        [SerializeField] private float fpMaxPitch = 55f;

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

        [Header("몸")]
        [Tooltip("몸 애니메이터. bodyVisual 을 비워 두면 이 Transform 을 쓴다. 비워 두면 자식에서 찾는다")]
        [SerializeField] private Animator bodyAnimator;
        [Tooltip("화면에 보이는 몸(Body). 매 프레임 시선 yaw 로 돌려 카메라와 박자를 맞춘다. 비워 두면 bodyAnimator 의 Transform")]
        [SerializeField] private Transform bodyVisual;

        [Header("1인칭 눈 위치")]
        [Tooltip("눈을 몸 정면 쪽으로 이만큼 내민다(m). 숨긴 머리 자리에 남는 목 구멍이 카메라 뒤로 가게 한다")]
        [SerializeField] private float eyeForwardOffset = 0.12f;
        [Tooltip("아래를 볼 때 눈이 목을 축으로 도는 정도. 0 = 끔(눈 고정), 1 = 실제 머리처럼 앞·아래로")]
        [SerializeField, Range(0f, 1f)] private float neckPivotWeight = 0f;
        [Tooltip("목 축 → 눈 (FP_Rig 로컬, m). 위·앞으로 멀수록 아래를 볼 때 눈이 많이 나간다")]
        [SerializeField] private Vector3 eyeFromNeck = new Vector3(0f, 0.1f, 0.08f);
        [Tooltip("켜면 FP_Rig 높이가 머리 뼈 위치를 부분적으로 따라간다 (걷기 흔들림이 약간 생김)")]
        [SerializeField] private bool followHeadBone = false;
        [Tooltip("0 = 원래 높이 고정, 1 = 머리 뼈 위치 그대로")]
        [SerializeField, Range(0f, 1f)] private float headFollowWeight = 0.5f;
        [Tooltip("머리 뼈 → 눈 위치 보정 (ViewRig 로컬 기준)")]
        [SerializeField] private Vector3 headEyeOffset = new Vector3(0f, 0.08f, 0.1f);
        [SerializeField] private float headFollowSmoothTime = 0.08f;
        [Tooltip("눈이 목 뼈보다 시선 수평 정면으로 최소 이만큼(m) 앞에 있게 한다. 걷기·달리기로 허리가 굽어 목이 앞으로 나오면 눈을 그만큼 앞으로 민다. 0 = 끔")]
        [SerializeField] private float neckClearance = 0.15f;
        [Tooltip("목 때문에 눈을 미는 양이 따라가는 시간(초). 작을수록 빠르게 (애니메이션 흔들림이 카메라에 더 전해진다)")]
        [SerializeField] private float neckGuardSmoothTime = 0.06f;

        [Header("무기")]
        [Tooltip("비워 두면 같은 오브젝트에서 찾는다")]
        [SerializeField] private GunSlot gunSlot;
        [Tooltip("조준 확대/축소 속도 (FOV 배율 변화량/초)")]
        [SerializeField] private float aimZoomSpeed = 6f;

        [Header("체력")]
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int currentHealth = 100;

        [Header("부활")]
        [Tooltip("Revive() 연출 길이(초). 이 동안 무적이고 입력을 받지 않는다")]
        [SerializeField, Min(0f)] private float reviveDuration = 2.4f;
        [Tooltip("연출 중 이 진행도(0~1)에서 사망 자세를 풀고 선 자세로 되돌린다. 이 뒤로 체력이 차오른다")]
        [SerializeField, Range(0f, 1f)] private float reviveBodyRestoreAt = 0.4f;
        [Tooltip("마지막 목숨으로 죽은 뒤 최종 사망(PlayerFinalDeathEvent)까지(초). 사망 동작을 보여 주는 시간이고, 이 사이 Revive·AddLives 가 오면 부활한다. 0 = 즉시")]
        [SerializeField, Min(0f)] private float reviveWindow = 2f;

        [Header("목숨 (PlayerTable.StartLives / MaxLives 가 우선)")]
        [Tooltip("테이블이 없을 때 시작 목숨 (지금 목숨 포함)")]
        [SerializeField, Min(1)] private int fallbackStartLives = 3;
        [Tooltip("테이블이 없을 때 기본 최대 목숨")]
        [SerializeField, Min(1)] private int fallbackMaxLives = 3;
        [Tooltip("목숨이 남아 있을 때 사망 후 자동 부활까지(초). 사망 동작을 보여 주는 시간")]
        [SerializeField, Min(0f)] private float livesReviveDelay = 1.5f;

        [Header("부활 직후 보호 (부활 연출이 끝난 뒤)")]
        [Tooltip("보호 시간(초). 0 = 없음")]
        [SerializeField, Min(0f)] private float reviveBuffDuration = 5f;
        [Tooltip("받는 피해 배율 (0.2 = 80% 경감)")]
        [SerializeField, Range(0f, 1f)] private float reviveBuffDamageMultiplier = 0.2f;
        [Tooltip("이동 속도 배율")]
        [SerializeField, Min(1f)] private float reviveBuffSpeedMultiplier = 1.35f;

        [Header("스태미나")]
        [SerializeField] private float maxStamina = 100f;
        [Tooltip("달리는 동안 초당 소모량")]
        [SerializeField] private float sprintStaminaPerSecond = 20f;
        [Tooltip("구르기 한 번에 드는 양. 이보다 적게 남았으면 구를 수 없다")]
        [SerializeField] private float rollStaminaCost = 25f;
        [Tooltip("초당 회복량 (걷기·정지)")]
        [SerializeField] private float staminaRegenPerSecond = 15f;
        [Tooltip("마지막으로 쓴 뒤 회복이 시작되기까지 걸리는 시간(초)")]
        [SerializeField] private float staminaRegenDelay = 1f;
        [Tooltip("바닥난 뒤 이 비율까지 회복해야 다시 달릴 수 있다")]
        [SerializeField, Range(0f, 1f)] private float sprintResumeRatio = 0.2f;

        #endregion

        #region 상태
        private Rigidbody rb;
        private CapsuleCollider capsule;

        // 외부 입력
        private Vector3 moveDirection;
        private bool sprintRequested;
        private float lockedYaw;
        private float jumpRequestedTime = float.NegativeInfinity;

        // 이동 상태
        private bool isGrounded;
        private bool isSliding;                // 급경사에 닿아 미끄러지는 중 (지상도 공중도 아님)
        private float snapDownSpeed;           // 지면 스냅: 이번 물리 프레임에 땅으로 끌어내리는 속도
        private float appliedSnapDown;         // 지난 물리 프레임에 rb 속도에 실제로 더한 스냅 속도 (걷기가 속도를 읽기 전에 뺀다)
        private bool velocityAlongGround;      // 지난 물리 프레임에 걷기·구르기가 속도를 땅을 따라 눕혔다 (착지 순간의 낙하 속도는 경사로 읽지 않게)
        private Vector3 motorTargetVelocity;   // 이번 물리 프레임 모터가 가려는 수평 속도 (외부 속도 소스에 넘긴다)
        private const float MaxSnapDownSpeed = 20f;
        private const float GroundProbeDepth = 3f;  // 발밑 땅 거리를 이 깊이까지 잰다
        private float groundDistance = float.PositiveInfinity; // 발 → 발밑 땅 거리
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
        /// <summary>이번 프레임 마우스로 돌린 pitch (Update 처음에 0 → UpdateLook 이 채운다)</summary>
        private float lookInputPitchDelta;

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
        private Vector3 neckLocal;             // 지난 프레임 IK 뒤 목 뼈 위치 (FP_Rig 부모 기준)
        private bool hasNeckSample;
        private float neckPush;                // 목 때문에 눈을 앞으로 민 거리(m)
        private float neckPushVelocity;
        private Transform currentCameraTransform;
        private readonly RaycastHit[] cameraHits = new RaycastHit[8];
        private readonly RaycastHit[] wallHits = new RaycastHit[8];

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

        // 보스전 시점
        private bool viewLocked;               // F1 전환 막힘 (BossIntro ~ 다음 CrowdBattle)
        private bool bossView;
        private float bossBlend;               // 0 = 어깨 시점, 1 = 보스전 카메라
        private float bossBlendTarget;
        private float bossBlendSpeed;
        private float bossCameraScale = 1f;       // 보스별 카메라 거리 배율 (IBossCameraProfile). 천천히 따라간다
        private float bossCameraScaleTarget = 1f;

        // 머리 숨김
        private Renderer[] headRendererCache = Array.Empty<Renderer>();
        private bool headHidden;
        private bool headStateApplied;

        // 조준 확대 (총이 SetAimZoom 으로 요청)
        private float aimFovTarget = 1f;
        private float aimFovCurrent = 1f;
        private float aimSensitivity = 1f;

        // 소모품 (수류탄 · 회복 팩). 같은 오브젝트의 PlayerItems
        private PlayerItems items;
        private PlayerInteractor interactor;

        // 키 배치
        private InputPlayerSettings keySettings = new InputPlayerSettings();

        // 체력
        private bool IsDead = false;

        // 부활 연출
        private bool isReviving;
        private bool reviveBodyRestored;
        private float reviveStartTime;
        private float reviveCurrentDuration;
        private float deathTime;
        private bool finalDead;                 // 부활 대기 시간이 지나 최종 사망 확정 (더는 부활하지 않음)
        private float reviveBuffEndTime = float.NegativeInfinity;
        private const string ReviveBuffSpeedKey = "ReviveBuff";

        // 목숨 (최대 = 난이도 기준 + 스테이지 클리어로 늘어난 몫)
        private int lives;
        private int baseMaxLives;
        private int bonusMaxLives;

        // 처음부터 재시작(RoundCleanupEvent.FullReset) 때 돌아갈 자리
        private Vector3 spawnPosition;
        private float spawnYaw;

        // 스태미나
        private float stamina;
        private float lastStaminaUseTime = float.NegativeInfinity;
        private bool staminaExhausted;          // 바닥난 뒤 sprintResumeRatio 까지 회복 전 (달리기 막힘)
        private float publishedStamina = -1f;
        #endregion
    }
}
