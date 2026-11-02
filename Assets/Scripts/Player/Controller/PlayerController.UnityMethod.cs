using DG.Tweening;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>유니티 메시지 (Awake / Start / Update / FixedUpdate / LateUpdate 등)</summary>
    public partial class PlayerController
    {
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
            InitMonsterContact();

            lockedYaw = rb.rotation.eulerAngles.y;
            lookYaw = lockedYaw;

            if (rootTransform == null) rootTransform = transform;
            if (bodyAnimator == null) bodyAnimator = GetComponentInChildren<Animator>();
            if (bodyVisual == null && bodyAnimator != null) bodyVisual = bodyAnimator.transform;
            if (bodyVisual == transform) bodyVisual = null; // 리지드바디 자신은 돌리면 안 된다
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();
            items = GetComponent<PlayerItems>();
            // 씬·프리팹 어디서 만든 플레이어든 1인칭 목 숨김이 빠지지 않게
            if (GetComponent<PlayerFirstPersonNeck>() == null) gameObject.AddComponent<PlayerFirstPersonNeck>();
            interactor = GetComponent<PlayerInteractor>();
            if (interactor == null) interactor = gameObject.AddComponent<PlayerInteractor>();
            stamina = MaxStamina;
            InitLives();

            CacheRig();
            CacheHeadRenderers();

            transitionGetter = () => transitionT;
            transitionSetter = x => { transitionT = x; ApplyTransitionPose(); };
            transitionComplete = OnViewTransitionComplete;
        }

        private void Start()
        {
            // 시작할 때는 트윈 없이 바로 적용 (토글로 처리하면 시작하자마자 전환 연출이 돈다)
            ApplyViewModeImmediate(viewMode);
            SetCursorLocked(true);
            RefreshUpgradeStats(false); // 씬을 다시 열어도 이미 산 강화가 반영되게 (Publish 전에)
            PublishStamina();
            PublishLives();
            LoadBossViewConfig();
            spawnPosition = rb.position;
            spawnYaw = rb.rotation.eulerAngles.y;
        }

        private void OnEnable()
        {
            SubscribeViewEvents();
            SubscribeLifeEvents();
            SubscribeExternalVelocityEvents();
            SubscribeGroundTremor();
            SubscribeMonsterContact();
            SubscribeCinematic();
            SubscribeUpgradeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeViewEvents();
            UnsubscribeLifeEvents();
            UnsubscribeExternalVelocityEvents();
            UnsubscribeGroundTremor();
            UnsubscribeMonsterContact();
            UnsubscribeCinematic();
            UnsubscribeUpgradeEvents();
        }

        private void OnDestroy()
        {
            viewTween?.Kill();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && IsGameplayInputBlocked == false) SetCursorLocked(true);
        }

        private void Update()
        {
            lookInputPitchDelta = 0f; // 입력을 읽지 않고 return 하는 프레임에 지난 값이 남지 않게
            UpdateLifeCycle();
            if (externalControl || isReviving) return;
            InputHandle();
        }

        private void FixedUpdate()
        {
            ScanNearbyMonsters(); // 외부 제어 중에도 몬스터에 눌려 땅 밑으로 빠지지 않게
            if (externalControl) return;
            float dt = Time.fixedDeltaTime;

            UpdateGround(dt);

            RemoveAppliedExternal(); // 모터는 지난 프레임 rb 속도에서 출발한다 → 외부 속도 몫을 먼저 뺀다
            if (isSliding) UpdateSlide(dt);
            else if (isRolling) UpdateRoll(dt);
            else UpdateWalk(dt);
            ApplyExternal(dt);

            UpdateJump();
            UpdateStamina(dt);
            ClampFallSpeed();
            UpdateFacing();
            SetPlayerTrackingInfo();
        }

        /// <summary>
        /// 애니메이터가 뼈를 움직인 뒤, 몸(보간된 위치)이 움직인 뒤에 실행된다.
        /// 순서: 몸 회전 → 리그 회전 → 1인칭 눈 위치 → 3인칭 벽 충돌 → 시점 전환 중이면 전환 위치 → 흔들림 계산 (렌더 직전에만 더한다)
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateBodyVisualYaw();

            if (rigReady == false) return;
            UpdateRigRotation();
            UpdateEyePosition();
            UpdateBossFraming(dt);
            UpdateCameraArm(dt);
            UpdateAimZoom(dt);
            if (CameraChanging) ApplyTransitionPose();
            UpdateCameraShake();
        }
    }
}
