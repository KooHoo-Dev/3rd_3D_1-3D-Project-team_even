using HybridShooter.Weapon;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 왼손 IK · 발 IK (Final IK). Player 루트에 붙인다.
    ///
    /// 왼손 (LimbIK, Final IK 데모 SecondHandOnGun 방식)
    ///  - 애니메이션에서는 두 손이 총을 함께 잡고 있다. AimIK 가 척추를 돌려 오른손(총)이 움직이면 왼손이 총에서 떨어진다
    ///  - 그래서 AimIK 전에 "오른손 기준 왼손 위치"를 기록해 두고, AimIK 가 끝난 직후(OnPostUpdate) 왼팔 LimbIK 로 그 자리에 되돌린다
    ///  - 지금 든 총에 "LeftHandGrip" 자식이 있으면 그 위치·회전을 우선 쓴다 (총 모델의 실제 손잡이. Weapon Pose Preview 창에서 만든다)
    ///  - 가중치 = 상체 자세 레이어 × 팔 동작 없음 (재장전·꺼내기 중에는 애니메이션 그대로)
    /// 발 (GrounderFBBIK): 경사·계단에서 발을 땅에 맞춘다. 공중·구르기·사망 중에는 끈다
    ///
    /// 실행 순서: 이 스크립트(9996, 기록) → AimIK(9997) → [왼손 LimbIK] → FullBodyBipedIK(9999, 반동·발)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9996)]
    public class PlayerBodyIK : MonoBehaviour
    {
        public const string LeftHandGripName = "LeftHandGrip";

        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimationDriver driver;
        [SerializeField] private GunSlot gunSlot;
        [Tooltip("몸(Body)의 AimIK. 이 IK 가 풀린 직후 왼손을 맞춘다")]
        [SerializeField] private AimIK aimIK;
        [Tooltip("왼팔 LimbIK (컴포넌트는 꺼 두고 이 스크립트가 직접 푼다). 빌더 메뉴가 붙인다")]
        [SerializeField] private LimbIK leftArmIK;
        [Tooltip("발 IK. 빌더 메뉴가 붙인다")]
        [SerializeField] private GrounderFBBIK grounder;
        [Tooltip("왼손 IK 를 켜고 끄는 시간(초)")]
        [SerializeField] private float leftHandBlendTime = 0.15f;
        [Tooltip("발 IK 를 켜고 끄는 시간(초)")]
        [SerializeField] private float footBlendTime = 0.2f;

        private Transform leftHand;
        private Transform rightHand;
        private Vector3 leftPosInRight;
        private Quaternion leftRotInRight = Quaternion.identity;
        private float leftHandWeight;
        private Gun gripGun;
        private Transform grip;

        public float LeftHandWeight => leftHandWeight;
        public float FootWeight => grounder != null ? grounder.weight : 0f;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (driver == null) driver = GetComponent<PlayerAnimationDriver>();
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();
            if (aimIK == null) aimIK = GetComponentInChildren<AimIK>();
            if (leftArmIK == null) leftArmIK = GetComponentInChildren<LimbIK>();
            if (grounder == null) grounder = GetComponentInChildren<GrounderFBBIK>();

            Animator animator = driver != null ? driver.Animator : GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
            // 왼팔 IK 는 AimIK 가 끝난 직후에 직접 푼다 (컴포넌트가 스스로 LateUpdate 에서 풀지 않게)
            if (leftArmIK != null) leftArmIK.enabled = false;
        }

        private void OnEnable()
        {
            if (aimIK != null) aimIK.solver.OnPostUpdate += AfterAimIK;
        }

        private void OnDisable()
        {
            if (aimIK != null) aimIK.solver.OnPostUpdate -= AfterAimIK;
        }

        private void LateUpdate()
        {
            if (driver == null || player == null) return;
            float dt = Time.deltaTime;

            // 애니메이션이 정한 "오른손 기준 왼손" 을 AimIK 전에 기록
            if (leftHand != null && rightHand != null)
            {
                leftPosInRight = rightHand.InverseTransformPoint(leftHand.position);
                leftRotInRight = Quaternion.Inverse(rightHand.rotation) * leftHand.rotation;
            }

            float target = driver.UpperPoseWeight * driver.ActionBlend;
            leftHandWeight = Mathf.MoveTowards(leftHandWeight, target, leftHandBlendTime > 0f ? dt / leftHandBlendTime : 1f);
            UpdateGrip();
            // AimIK 가 없으면 여기서 바로 맞춘다
            if (aimIK == null || aimIK.enabled == false) SolveLeftHand();

            if (grounder != null)
            {
                bool feet = player.IsOnGround && player.IsRolling == false && player.IsAlive && player.IsExternallyControlled == false;
                grounder.weight = Mathf.MoveTowards(grounder.weight, feet ? 1f : 0f, footBlendTime > 0f ? dt / footBlendTime : 1f);
            }
        }

        private void AfterAimIK() => SolveLeftHand();

        /// <summary>왼손을 총 손잡이(있으면) 또는 기록해 둔 오른손 기준 자리로</summary>
        private void SolveLeftHand()
        {
            if (leftArmIK == null || leftHand == null || rightHand == null || leftHandWeight <= 0f) return;

            if (grip != null)
            {
                leftArmIK.solver.IKPosition = grip.position;
                leftArmIK.solver.IKRotation = grip.rotation;
            }
            else
            {
                leftArmIK.solver.IKPosition = rightHand.TransformPoint(leftPosInRight);
                leftArmIK.solver.IKRotation = rightHand.rotation * leftRotInRight;
            }
            leftArmIK.solver.IKPositionWeight = leftHandWeight;
            leftArmIK.solver.IKRotationWeight = leftHandWeight;
            leftArmIK.solver.Update();
        }

        /// <summary>지금 든 총의 LeftHandGrip 을 찾아 둔다 (총이 바뀔 때만)</summary>
        private void UpdateGrip()
        {
            Gun gun = gunSlot != null ? gunSlot.CurrentGun : null;
            if (gun == gripGun) return;
            gripGun = gun;
            grip = gun != null ? gun.transform.Find(LeftHandGripName) : null;
        }
    }
}
