using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 아이템 사용 모습 (Player 루트에 붙인다).
    ///  - 수류탄: 던지기 시작(ThrowStarted)부터 손을 떠날 때(GrenadeReleased)까지 던지는 손에 수류탄 모형을 쥐여 준다
    ///    (던지는 손·놓는 순간은 드라이버가 클립 분석값으로 정한다. 모형 자리 = 프로필 grenadeInHandOffset)
    ///    모형은 IK(AimIK·1인칭 손·FBBIK)가 손을 옮긴 뒤(FBBIK OnPostUpdate)에 붙인다 → 보이는 손과 Lab 에서 저장한 자리가 같은 기준.
    ///    날아가는 수류탄도 이 모형 자리에서 출발한다 (PlayerItems.ThrowSpawn)
    ///  - 회복 팩: 사용(HealStarted) 때 총은 오른손에 든 채 왼손만 잠깐 프로필 healPose 자리로 갔다 온다 (PlayerBodyIK 왼손 목표에 섞는다)
    ///  - 다른 아이템(아드레날린 등)은 Play(자리) 로 같은 동작을 쓸 수 있다
    /// 자리는 IK Pose Lab 의 "아이템" 칸에서 보며 고친다 (HoldPose / HoldGrenade 로 자세를 붙잡아 둔다)
    /// 실행 순서: 이 스크립트(9995) → PlayerBodyIK(9996, 왼손 목표) → … → PlayerFirstPersonIK(9998)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9995)]
    public class PlayerItemIK : MonoBehaviour
    {
        [SerializeField] private PlayerAnimationDriver driver;
        [SerializeField] private PlayerItems items;
        [SerializeField] private PlayerBodyIK bodyIK;
        [Tooltip("몸(Body)의 FullBodyBipedIK. 손 IK 가 끝난 뒤 모형을 붙인다. 비워 두면 자식에서 찾는다")]
        [SerializeField] private FullBodyBipedIK fbbik;

        private Transform body;
        private Transform chest;
        private GameObject grenadeModel;
        private bool grenadeShown;
        private PlayerAnimationProfile.ItemHandPose playing;
        private float playStartTime;

        /// <summary>편집용: 회복 자세를 계속 붙잡아 둔다</summary>
        public bool HoldPose { get; set; }
        /// <summary>편집용: 수류탄 모형을 손에 계속 쥐여 둔다</summary>
        public bool HoldGrenade { get; set; }
        /// <summary>편집용: 모형을 씬 뷰에서 직접 옮기는 중이면 손에 다시 붙이지 않는다</summary>
        public bool EditingModel { get; set; }
        /// <summary>지금 왼손을 아이템 자리로 옮기는 정도</summary>
        public float PoseWeight { get; private set; }
        public Transform GrenadeModel => grenadeModel != null ? grenadeModel.transform : null;
        private PlayerAnimationProfile Profile => driver != null ? driver.Profile : null;

        private void Awake()
        {
            if (driver == null) driver = GetComponent<PlayerAnimationDriver>();
            if (items == null) items = GetComponent<PlayerItems>();
            if (bodyIK == null) bodyIK = GetComponent<PlayerBodyIK>();
            if (fbbik == null) fbbik = GetComponentInChildren<FullBodyBipedIK>();
            Animator animator = driver != null && driver.Animator != null ? driver.Animator : GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                body = animator.transform;
                chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            }
        }

        private void OnEnable()
        {
            if (fbbik != null) fbbik.solver.OnPostUpdate += UpdateGrenadeModel;
            if (items == null) return;
            items.ThrowStarted += OnThrowStarted;
            items.GrenadeReleased += OnGrenadeReleased;
            items.HealStarted += OnHealStarted;
        }

        private void OnDisable()
        {
            if (fbbik != null) fbbik.solver.OnPostUpdate -= UpdateGrenadeModel;
            if (items == null) return;
            items.ThrowStarted -= OnThrowStarted;
            items.GrenadeReleased -= OnGrenadeReleased;
            items.HealStarted -= OnHealStarted;
        }

        private void OnThrowStarted() => grenadeShown = true;
        private void OnGrenadeReleased() => grenadeShown = false;
        private void OnHealStarted() { if (Profile != null) Play(Profile.healPose); }

        /// <summary>왼손을 pose 자리로 갔다 오게 한다 (moveTime 동안 가서 holdTime 머물고 moveTime 동안 돌아온다)</summary>
        public void Play(PlayerAnimationProfile.ItemHandPose pose)
        {
            playing = pose;
            playStartTime = Time.time;
        }

        /// <summary>pose 의 월드 자리 (가슴 뼈 위치 + 몸 회전 × offset)</summary>
        public Pose WorldPose(PlayerAnimationProfile.ItemHandPose pose)
        {
            if (body == null || chest == null) return new Pose(transform.position, transform.rotation);
            return new Pose(chest.position + body.rotation * pose.offset, body.rotation * pose.rotation);
        }

        /// <summary>world 자리를 pose 값(몸 기준)으로 바꿔 넣는다 (Lab 편집용)</summary>
        public void StorePose(PlayerAnimationProfile.ItemHandPose pose, Vector3 position, Quaternion rotation)
        {
            if (body == null || chest == null) return;
            Quaternion toBody = Quaternion.Inverse(body.rotation);
            pose.offset = toBody * (position - chest.position);
            pose.rotation = toBody * rotation;
            pose.useRotation = true;
        }

        private void LateUpdate()
        {
            if (bodyIK == null) return;
            UpdateHandPose();
            // FBBIK 가 돌면 풀린 직후(OnPostUpdate)에 붙인다. 이 시점(9995)은 IK 전이라 손이 아직 애니메이션 자리다
            if (fbbik == null || fbbik.isActiveAndEnabled == false) UpdateGrenadeModel();
        }

        private void UpdateHandPose()
        {
            PlayerAnimationProfile.ItemHandPose pose = HoldPose && Profile != null ? Profile.healPose : playing;
            float weight = 0f;
            if (HoldPose) weight = 1f;
            else if (playing != null)
            {
                float move = Mathf.Max(0.01f, playing.moveTime);
                float t = Time.time - playStartTime;
                if (t >= move * 2f + playing.holdTime) playing = null;
                else if (t < move) weight = t / move;
                else if (t < move + playing.holdTime) weight = 1f;
                else weight = 1f - (t - move - playing.holdTime) / move;
            }
            PoseWeight = Mathf.SmoothStep(0f, 1f, weight);
            if (pose == null || PoseWeight <= 0f)
            {
                bodyIK.SetLeftHandOverride(Vector3.zero, Quaternion.identity, false, 0f);
                return;
            }
            Pose world = WorldPose(pose);
            bodyIK.SetLeftHandOverride(world.position, world.rotation, pose.useRotation, PoseWeight);
        }

        /// <summary>던지는 손 바닥에 수류탄 모형 (프리팹의 메쉬만 복사해 만든다)</summary>
        private void UpdateGrenadeModel()
        {
            bool show = (grenadeShown || HoldGrenade) && items != null && items.ThrowHand != null && items.GrenadePrefab != null;
            if (show && grenadeModel == null)
            {
                grenadeModel = CreateGrenadeModel();
                items.ThrowSpawn = grenadeModel.transform;
            }
            if (grenadeModel == null) return;
            if (grenadeModel.activeSelf != show) grenadeModel.SetActive(show);
            if (show == false || EditingModel) return;

            Transform hand = items.ThrowHand;
            Vector3 offset = Profile != null ? Profile.grenadeInHandOffset : Vector3.zero;
            grenadeModel.transform.SetPositionAndRotation(PalmCenter(hand) + hand.rotation * offset, hand.rotation);
        }

        /// <summary>손 바닥 중심 = 손목과 가운뎃손가락 첫 마디의 중간</summary>
        public Vector3 PalmCenter(Transform hand)
        {
            Animator animator = driver != null ? driver.Animator : null;
            bool left = animator != null && hand == animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform middle = animator != null ? animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal) : null;
            return middle != null ? (hand.position + middle.position) * 0.5f : hand.position;
        }

        private GameObject CreateGrenadeModel()
        {
            Transform source = items.GrenadePrefab.transform;
            var model = new GameObject("GrenadeInHand") { layer = items.ThrowHand.gameObject.layer };
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                var part = new GameObject(filter.name) { layer = model.layer };
                part.transform.SetParent(model.transform, false);
                part.transform.localPosition = source.InverseTransformPoint(filter.transform.position);
                part.transform.localRotation = Quaternion.Inverse(source.rotation) * filter.transform.rotation;
                part.transform.localScale = filter.transform.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            return model;
        }

        private void OnDestroy()
        {
            if (grenadeModel != null) Destroy(grenadeModel);
        }
    }
}
