using DG.Tweening;
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

            lockedYaw = rb.rotation.eulerAngles.y;
            lookYaw = lockedYaw;

            if (rootTransform == null) rootTransform = transform;
            if (bodyAnimator == null) bodyAnimator = GetComponentInChildren<Animator>();
            if (bodyVisual == null && bodyAnimator != null) bodyVisual = bodyAnimator.transform;
            if (bodyVisual == transform) bodyVisual = null; // 리지드바디 자신은 돌리면 안 된다
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();
            items = GetComponent<PlayerItems>();

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

            if (isSliding) UpdateSlide(dt);
            else if (isRolling) UpdateRoll(dt);
            else UpdateWalk(dt);

            UpdateJump();
            ClampFallSpeed();
            UpdateFacing(dt);
            SetPlayerTrackingInfo();
        }

        /// <summary>
        /// 애니메이터가 뼈를 움직인 뒤, 몸(보간된 위치)이 움직인 뒤에 실행된다.
        /// 순서: 몸 회전 → 리그 회전 → 1인칭 눈 위치 → 3인칭 벽 충돌 → 시점 전환 중이면 전환 위치
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateBodyVisualYaw();

            if (rigReady == false) return;
            UpdateRigRotation();
            UpdateEyePosition();
            UpdateCameraArm(dt);
            UpdateAimZoom(dt);
            if (CameraChanging) ApplyTransitionPose();
        }
    }
}
