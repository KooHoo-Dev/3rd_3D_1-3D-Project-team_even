using System.Collections.Generic;
using HybridShooter.Weapon;
using RootMotion;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// Final IK 로 "어떤 총이든 두 손으로 잘 잡고, 조준점을 향하게" 만든다. 애니메이션이 끝난 뒤(LateUpdate) 자세를 덧칠한다.
    ///
    /// ■ 총 기준 모드 (GunDriven, 기본)
    ///   총이 손에 붙는 게 아니라, 손이 총에 붙는다.
    ///    ① 상체 (AimIK)       : 척추를 조준점 쪽으로 살짝 숙이고 젖힌다
    ///    ② 총 위치             : 오른쪽 어깨 앞 "Hold" 위치에 총을 놓고, 총구가 조준점을 향하게 돌린다
    ///                            (조준하지 않을 때는 loweredPitch 만큼 내린 자세)
    ///    ③ 두 손 (FullBodyBipedIK): 오른손 → RightHandGrip, 왼손 → LeftHandGrip 으로 끌어당긴다. 팔꿈치 힌트도 여기서
    ///    ④ 손가락              : WeaponHandPose 의 굽힘 값
    ///   → 총 위치(Hold 핸들) · 오른손(RightHandGrip) · 왼손(LeftHandGrip) 을 각각 따로 옮겨 맞출 수 있다.
    ///   재장전·투척·회복·사망처럼 손이 다른 일을 할 때는 IK 가 꺼지고, 총은 애니메이션 오른손을 따라간다 (부드럽게 전환).
    ///
    /// ■ 손 기준 모드 (HandDriven, 예전 방식)
    ///   총이 오른손 뼈에 붙어 있고, 오른손은 애니메이션대로. 왼손만 IK. 오른손 위치는 직접 못 바꾼다.
    ///
    /// 켜고 끄기는 PlayerAnimator 가 상태 태그로 판단한다 (재장전·투척·회복·벽넘기·사망 = 끔, 달리기·구르기 = 조준만 끔).
    /// IK 컴포넌트(FullBodyBipedIK, AimIK)는 모델(Animator 오브젝트)에 없으면 실행 시 자동으로 붙인다.
    /// </summary>
    [DefaultExecutionOrder(100)] // 카메라(기본 0) 이후 → 이번 프레임 조준점 사용
    [RequireComponent(typeof(PlayerAnimator))]
    public class PlayerHandIK : MonoBehaviour
    {
        public enum GripMode
        {
            /// <summary>총이 어깨 앞에 떠 있고 두 손이 IK 로 총을 잡는다</summary>
            GunDriven,
            /// <summary>총이 오른손에 붙어 있고 왼손만 IK</summary>
            HandDriven,
        }

        [Header("잡는 방식")]
        [Tooltip("GunDriven = 총 위치·오른손·왼손을 각각 맞출 수 있음 (추천). HandDriven = 총이 오른손 애니메이션을 따라감 (예전 방식)")]
        [SerializeField] private GripMode gripMode = GripMode.GunDriven;

        [Header("세기")]
        [Tooltip("총 기준 모드에서 오른손을 RightHandGrip 으로 당기는 세기")]
        [SerializeField, Range(0f, 1f)] private float rightHandWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float leftHandWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float aimWeight = 1f;
        [Tooltip("왼손 방향까지 leftHandGrip 회전에 맞출지. 끄면 위치만 맞추고 손목 방향은 애니메이션을 따른다. 켜면 총 위치에 따라 팔이 꺾일 수 있어 기본은 끔")]
        [SerializeField] private bool matchLeftHandRotation;
        [Tooltip("IK 세기가 바뀌는 속도 (초당)")]
        [SerializeField] private float blendSpeed = 6f;

        [Header("조준 IK: 상체를 조준점 쪽으로")]
        [Tooltip("척추 · 가슴 · 윗가슴을 얼마나 돌릴지. 팔은 돌리지 않는다 (팔을 IK 로 돌리면 어깨가 비틀려 얇아진다)")]
        [SerializeField] private float[] spineAimWeights = { 0.35f, 0.55f, 0.8f };
        [Tooltip("(손 기준 모드) 마지막에 오른손 손목으로 조금만 더 맞추는 세기. 크면 손목이 꺾여 보인다")]
        [SerializeField, Range(0f, 1f)] private float handAimWeight = 0.3f;
        [Tooltip("조준점이 총구 방향과 너무 다르면 덜 돌린다 (0 = 제한 없음). 크면 화면 가운데까지 못 돈다")]
        [SerializeField, Range(0f, 1f)] private float aimClamp = 0.05f;
        [Tooltip("반복 횟수. 높을수록 정확하지만 무거움")]
        [SerializeField, Range(1, 6)] private int aimIterations = 3;

        [Header("팔 비틀림 풀기")]
        [Tooltip("(실험용) 팔의 비틀림 보조 뼈(upperarm_twist, lowerarm_twist 등)를 중간값으로 돌린다. 이 캐릭터에서는 팔 관절이 이상해져 기본은 끔")]
        [SerializeField] private bool relaxTwistBones;

        /// <summary>총 기준 모드에서 총 한 자루의 실행 중 정보</summary>
        private sealed class GunRig
        {
            public WeaponItem Item;
            public WeaponHandPose Pose;
            /// <summary>WeaponAimFrame 아래의 빈 오브젝트. 이 위치·회전 = 총이 놓일 곳 (플레이 중 옮겨서 맞춘다)</summary>
            public Transform Hold;
            public Transform RightGrip;
            public bool HoldReady;
        }

        private PlayerAnimator anim;
        private PlayerWeapons weapons;
        private FullBodyBipedIK fbbik;
        private AimIK aimIK;
        private bool ready;
        private float currentHandWeight;
        private float currentAimWeight;
        private float currentPoseWeight;
        private HandFingerPoser rightFingers;
        private HandFingerPoser leftFingers;
        private WeaponItem cachedWeapon;
        private WeaponHandPose cachedPose;

        // 총 기준 모드
        private readonly Dictionary<WeaponItem, GunRig> rigs = new Dictionary<WeaponItem, GunRig>();
        private Transform aimFrame;
        private Transform rightShoulder;
        private Transform rightHandBone;

        public FullBodyBipedIK FullBodyIK => fbbik;
        public AimIK AimIK => aimIK;
        public GripMode Mode => gripMode;

        /// <summary>에디터 도구용: 이 총의 Hold 핸들 (총 기준 모드, 실행 중에만)</summary>
        public Transform GetHoldHandle(WeaponItem item) => item != null && rigs.TryGetValue(item, out var rig) ? rig.Hold : null;

        private void Awake()
        {
            anim = GetComponent<PlayerAnimator>();
            weapons = GetComponent<PlayerWeapons>();
        }

        private void Start() => ready = Setup();

        // ───────────────────────── 준비 ─────────────────────────

        private bool Setup()
        {
            if (anim.HasAnimator == false || anim.Animator.isHuman == false)
            {
                Debug.LogWarning("[PlayerHandIK] Humanoid Animator 가 없어 IK 를 쓰지 않습니다.", this);
                return false;
            }
            Animator animator = anim.Animator;
            GameObject model = animator.gameObject;

            // 손가락: 애니메이션이 아직 손대지 않은 지금(펴진 기본 자세)을 기준으로 저장
            rightFingers = new HandFingerPoser(animator, true);
            leftFingers = new HandFingerPoser(animator, false);

            // ── 손: FullBodyBipedIK ──
            fbbik = model.GetComponent<FullBodyBipedIK>();
            if (fbbik == null) fbbik = model.AddComponent<FullBodyBipedIK>();
            if (fbbik.references.isFilled == false)
            {
                BipedReferences refs = null;
                BipedReferences.AutoDetectReferences(ref refs, model.transform, BipedReferences.AutoDetectParams.Default);
                fbbik.SetReferences(refs, null); // 실행 중이면 여기서 초기화까지 된다
            }
            if (fbbik.solver.initiated == false) fbbik.solver.Initiate(model.transform);
            // 손을 당겨도 몸통이 끌려가지 않게
            fbbik.solver.leftArmChain.pull = 0f;
            fbbik.solver.rightArmChain.pull = 0f;
            fbbik.solver.leftHandEffector.maintainRelativePositionWeight = 0f;
            fbbik.solver.rightHandEffector.maintainRelativePositionWeight = 0f;
            // 직접 순서대로 Update 하기 위해 끈다. (뼈 설정을 끝낸 뒤에 꺼야 Final IK 가 빈 설정으로 초기화하며 경고를 내지 않는다)
            fbbik.enabled = false;

            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);

            // ── 상체 조준: AimIK ──
            aimIK = model.GetComponent<AimIK>();
            if (aimIK == null) aimIK = model.AddComponent<AimIK>();
            if (aimIK.solver.bones == null || aimIK.solver.bones.Length == 0) BuildAimChain(animator, model.transform);
            else if (aimIK.solver.initiated == false) aimIK.solver.Initiate(model.transform);
            aimIK.enabled = false;
            aimIK.solver.clampWeight = aimClamp;
            aimIK.solver.maxIterations = aimIterations;
            aimIK.solver.axis = Vector3.forward;

            if (gripMode == GripMode.GunDriven) SetupGunRigs();
            if (relaxTwistBones) SetupTwistRelax(animator);
            return fbbik.solver.initiated && aimIK.solver.initiated;
        }

        private void BuildAimChain(Animator animator, Transform root)
        {
            var spine = new List<Transform>();
            AddBone(spine, animator, HumanBodyBones.Spine);
            AddBone(spine, animator, HumanBodyBones.Chest);
            AddBone(spine, animator, HumanBodyBones.UpperChest);
            var chain = new List<Transform>(spine);

            if (gripMode == GripMode.GunDriven)
            {
                // 총 기준: 총이 손에 붙어 있지 않으므로, 윗가슴에 "정면을 가리키는 막대"를 붙여 그걸 조준점으로 돌린다
                Transform top = spine.Count > 0 ? spine[spine.Count - 1] : root;
                var helper = new GameObject("SpineAimHelper").transform;
                helper.SetParent(top, false);
                helper.SetPositionAndRotation(top.position, Quaternion.LookRotation(transform.forward, transform.up));
                aimIK.solver.transform = helper;
            }
            else if (rightHandBone != null)
            {
                // 손 기준: 척추 + 오른손 (총구가 손 아래에 있어 손목까지 돌린다)
                chain.Add(rightHandBone);
            }

            aimIK.solver.SetChain(chain.ToArray(), root);
            // 척추 세기는 끝(윗가슴)부터 맞춘다 (모델에 UpperChest 가 없으면 앞쪽 값을 버림)
            for (int i = 0; i < spine.Count; i++)
            {
                int w = Mathf.Clamp(spineAimWeights.Length - spine.Count + i, 0, spineAimWeights.Length - 1);
                aimIK.solver.bones[i].weight = spineAimWeights.Length > 0 ? spineAimWeights[w] : 0.5f;
            }
            if (chain.Count > spine.Count) aimIK.solver.bones[chain.Count - 1].weight = handAimWeight;
        }

        /// <summary>
        /// 총 기준 모드 준비: 총을 손에서 떼어 플레이어 아래로 옮기고, 총마다 Hold 핸들과 RightHandGrip 을 만든다.
        /// RightHandGrip 이 없으면 "지금 총과 오른손의 관계"(손에 붙어 있던 상태) 그대로 만든다 → 처음엔 예전 모습과 같다.
        /// </summary>
        private void SetupGunRigs()
        {
            if (weapons == null || rightShoulder == null || rightHandBone == null) return;

            aimFrame = new GameObject("WeaponAimFrame").transform;
            aimFrame.SetParent(transform, false);

            foreach (var item in weapons.Slots)
            {
                if (item == null || rigs.ContainsKey(item)) continue;
                var rig = new GunRig { Item = item, Pose = item.GetComponent<WeaponHandPose>() };

                rig.RightGrip = item.RightHandGrip;
                if (rig.RightGrip == null)
                {
                    rig.RightGrip = new GameObject("RightHandGrip").transform;
                    rig.RightGrip.SetParent(item.transform, false);
                    // 총이 아직 손 아래에 붙어 있는 지금, 손목 위치 = 오른손 자리
                    rig.RightGrip.SetPositionAndRotation(rightHandBone.position, rightHandBone.rotation);
                    item.RightHandGrip = rig.RightGrip;
                }

                rig.Hold = new GameObject("Hold_" + item.name).transform;
                rig.Hold.SetParent(aimFrame, false);
                if (rig.Pose != null && rig.Pose.hasHoldOffset)
                {
                    rig.Hold.localPosition = rig.Pose.holdPosition;
                    rig.Hold.localRotation = rig.Pose.holdRotation;
                    rig.HoldReady = true;
                }

                // 손에서 떼어 플레이어 루트 아래로 (보이는 위치는 그대로)
                item.transform.SetParent(transform, true);
                rigs.Add(item, rig);
            }
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void LateUpdate()
        {
            if (ready == false) return;
            if (gripMode == GripMode.GunDriven) UpdateGunDriven();
            else UpdateHandDriven();
        }

        private void UpdateGunDriven()
        {
            WeaponItem weapon = weapons != null ? weapons.Current : null;
            bool held = weapon != null && weapon.gameObject.activeInHierarchy;
            GunRig rig = weapon != null && rigs.TryGetValue(weapon, out var found) ? found : null;
            WeaponHandPose pose = GetPose(weapon);
            float dt = Time.deltaTime;

            // 손 IK 세기: 재장전 등 손이 다른 일을 하면 0 → 총은 애니메이션 손을 따라간다
            float handTarget = held && rig != null ? anim.HandIKTarget : 0f;
            currentHandWeight = Mathf.MoveTowards(currentHandWeight, handTarget, blendSpeed * dt);
            currentPoseWeight = currentHandWeight;

            // 조준선 쪽을 향하는 정도: 조준 중 1, 평소 hipAim, 달리기·구르기 0
            float hipAim = pose != null ? pose.hipAim : 0.5f;
            float aimTarget = held && anim.AimPostureAllowed ? Mathf.Lerp(hipAim, 1f, anim.AimBlend) * aimWeight : 0f;
            currentAimWeight = Mathf.MoveTowards(currentAimWeight, aimTarget, blendSpeed * dt);

            Vector3 aimPoint = weapons != null ? weapons.AimPoint : transform.position + transform.forward * 50f;

            // ① 상체: 조준할 때만 척추를 조준점 쪽으로
            float spineWeight = currentAimWeight * anim.AimBlend;
            if (spineWeight > 0.001f)
            {
                aimIK.solver.IKPosition = aimPoint;
                aimIK.solver.IKPositionWeight = spineWeight;
                aimIK.solver.Update();
            }

            if (rig == null || held == false) { FinishFrame(pose); return; }

            // ② 총 위치: 어깨 앞 Hold 위치 ↔ 애니메이션 오른손 위치를 IK 세기로 섞는다
            Vector3 up = transform.up;
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, up);
            Quaternion bodyYaw = Quaternion.LookRotation(flatForward.sqrMagnitude > 0.0001f ? flatForward : transform.forward, up);
            Quaternion lowered = bodyYaw * Quaternion.Euler(pose != null ? pose.loweredPitch : 25f, 0f, 0f);
            Vector3 toAim = aimPoint - rightShoulder.position;
            Quaternion aimed = toAim.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toAim, up) : bodyYaw;
            aimFrame.SetPositionAndRotation(rightShoulder.position, Quaternion.Slerp(lowered, aimed, currentAimWeight));

            GunPoseFromHand(rig, out Vector3 handGunPos, out Quaternion handGunRot);
            if (rig.HoldReady == false)
            {
                // 저장된 위치가 없으면 지금 애니메이션이 총을 드는 위치를 Hold 로 삼는다
                rig.Hold.SetPositionAndRotation(handGunPos, handGunRot);
                rig.HoldReady = true;
            }

            float w = currentHandWeight;
            Transform gun = weapon.transform;
            gun.SetPositionAndRotation(Vector3.Lerp(handGunPos, rig.Hold.position, w), Quaternion.Slerp(handGunRot, rig.Hold.rotation, w));

            // 총구가 조준점을 정확히 향하도록 오른손 자리를 중심으로 마지막 보정
            float aimFix = currentAimWeight * w;
            if (aimFix > 0.001f)
            {
                Transform muzzle = weapon.Muzzle;
                Vector3 want = aimPoint - muzzle.position;
                if (want.sqrMagnitude > 0.01f)
                {
                    Quaternion fix = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(muzzle.forward, want), aimFix);
                    Vector3 pivot = rig.RightGrip.position;
                    gun.SetPositionAndRotation(pivot + fix * (gun.position - pivot), fix * gun.rotation);
                }
            }

            // ③ 두 손 + 팔꿈치
            var solver = fbbik.solver;
            float rightW = w * rightHandWeight;
            var rightHand = solver.rightHandEffector;
            rightHand.position = rig.RightGrip.position;
            rightHand.rotation = rig.RightGrip.rotation;
            rightHand.positionWeight = rightW;
            rightHand.rotationWeight = rightW;

            bool leftGrip = weapon.LeftHandGrip != null;
            float leftW = leftGrip ? w * leftHandWeight : 0f;
            var leftHand = solver.leftHandEffector;
            leftHand.positionWeight = leftW;
            leftHand.rotationWeight = matchLeftHandRotation ? leftW : 0f;
            if (leftGrip)
            {
                leftHand.position = weapon.LeftHandGrip.position;
                leftHand.rotation = weapon.LeftHandGrip.rotation;
            }
            SetBendGoals(solver, pose);

            if (rightW > 0.001f || leftW > 0.001f) solver.Update();

            FinishFrame(pose);
        }

        /// <summary>애니메이션 오른손이 RightHandGrip 을 쥐고 있다고 했을 때의 총 위치·회전</summary>
        private void GunPoseFromHand(GunRig rig, out Vector3 position, out Quaternion rotation)
        {
            Transform gun = rig.Item.transform;
            Transform grip = rig.RightGrip;
            rotation = rightHandBone.rotation * Quaternion.Inverse(grip.localRotation);
            position = rightHandBone.position - rotation * Vector3.Scale(grip.localPosition, gun.localScale);
        }

        /// <summary>예전 방식: 총은 오른손에 붙어 있고, 조준은 척추+손목, 왼손만 IK</summary>
        private void UpdateHandDriven()
        {
            WeaponItem weapon = weapons != null ? weapons.Current : null;
            bool held = weapon != null && weapon.gameObject.activeInHierarchy;
            bool hasGrip = held && weapon.LeftHandGrip != null;
            float dt = Time.deltaTime;

            float handTarget = hasGrip ? anim.HandIKTarget * leftHandWeight : 0f;
            float aimTarget = held ? anim.AimIKTarget * aimWeight : 0f;
            currentHandWeight = Mathf.MoveTowards(currentHandWeight, handTarget, blendSpeed * dt);
            currentAimWeight = Mathf.MoveTowards(currentAimWeight, aimTarget, blendSpeed * dt);
            WeaponHandPose pose = GetPose(weapon);
            float poseTarget = pose != null && held ? anim.HandIKTarget : 0f;
            currentPoseWeight = Mathf.MoveTowards(currentPoseWeight, poseTarget, blendSpeed * dt);

            // ① 조준
            if (currentAimWeight > 0.001f && weapon != null)
            {
                aimIK.solver.transform = weapon.Muzzle;
                aimIK.solver.IKPosition = weapons.AimPoint;
                aimIK.solver.IKPositionWeight = currentAimWeight;
                aimIK.solver.Update();
            }

            // ② 왼손 위치 + 팔꿈치 방향
            bool leftGrip = currentHandWeight > 0.001f && weapon != null && weapon.LeftHandGrip != null;
            float rightElbow = ElbowWeight(pose, true);
            var solver = fbbik.solver;

            var leftHand = solver.leftHandEffector;
            leftHand.positionWeight = leftGrip ? currentHandWeight : 0f;
            leftHand.rotationWeight = leftGrip && matchLeftHandRotation ? currentHandWeight : 0f;
            if (leftGrip)
            {
                leftHand.position = weapon.LeftHandGrip.position;
                leftHand.rotation = weapon.LeftHandGrip.rotation;
            }

            // 오른팔: 손(=총)은 지금 자리에 고정하고 팔꿈치만 힌트 쪽으로 돌린다
            var rightHand = solver.rightHandEffector;
            rightHand.positionWeight = rightElbow;
            rightHand.rotationWeight = rightElbow;
            if (rightElbow > 0f)
            {
                rightHand.position = rightHandBone.position;
                rightHand.rotation = rightHandBone.rotation;
            }
            SetBendGoals(solver, pose);

            if (leftGrip || ElbowWeight(pose, false) > 0f || rightElbow > 0f) solver.Update();

            FinishFrame(pose);
        }

        private void SetBendGoals(IKSolverFullBodyBiped solver, WeaponHandPose pose)
        {
            float leftElbow = ElbowWeight(pose, false);
            float rightElbow = ElbowWeight(pose, true);
            solver.leftArmChain.bendConstraint.bendGoal = leftElbow > 0f ? pose.left.elbowHint : null;
            solver.leftArmChain.bendConstraint.weight = leftElbow;
            solver.rightArmChain.bendConstraint.bendGoal = rightElbow > 0f ? pose.right.elbowHint : null;
            solver.rightArmChain.bendConstraint.weight = rightElbow;
        }

        /// <summary>IK 가 팔을 움직인 뒤: 비틀림 뼈 정리 → 손가락</summary>
        private void FinishFrame(WeaponHandPose pose)
        {
            RelaxTwist();
            if (pose != null && currentPoseWeight > 0.001f)
            {
                rightFingers.Apply(pose.right, pose, pose.right.fingerWeight * currentPoseWeight);
                leftFingers.Apply(pose.left, pose, pose.left.fingerWeight * currentPoseWeight);
            }
        }

        private float ElbowWeight(WeaponHandPose pose, bool right)
        {
            if (pose == null) return 0f;
            var hand = pose.Get(right);
            return hand.elbowHint != null ? hand.elbowHintWeight * currentPoseWeight : 0f;
        }

        private WeaponHandPose GetPose(WeaponItem weapon)
        {
            if (weapon != cachedWeapon)
            {
                cachedWeapon = weapon;
                cachedPose = weapon != null ? weapon.GetComponent<WeaponHandPose>() : null;
            }
            return cachedPose;
        }

        private static void AddBone(List<Transform> chain, Animator animator, HumanBodyBones bone)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t != null) chain.Add(t);
        }

        // ───────────────────────── 팔 비틀림 풀기 (실험용) ─────────────────────────
        // Unity 휴머노이드는 팔의 비틀림 보조 뼈(이름에 twist 가 들어간 뼈)를 움직이지 않는다.
        // Final IK 의 TwistSolver 로 보조 뼈를 "앞 뼈와 뒤 뼈 회전의 중간"으로 돌린다. 이 캐릭터에서는 결과가 나빠 기본은 끔.

        private readonly List<TwistSolver> twistSolvers = new List<TwistSolver>();

        private void SetupTwistRelax(Animator animator)
        {
            twistSolvers.Clear();
            foreach (bool right in new[] { true, false })
            {
                Transform shoulder = animator.GetBoneTransform(right ? HumanBodyBones.RightShoulder : HumanBodyBones.LeftShoulder);
                Transform upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                Transform lower = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                Transform hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                if (upper == null || lower == null || hand == null) continue;

                foreach (Transform twist in TwistChildren(lower)) AddTwist(twist, lower, hand);
                foreach (Transform twist in TwistChildren(upper)) AddTwist(twist, shoulder != null ? shoulder : upper.parent, lower);
            }
        }

        private static IEnumerable<Transform> TwistChildren(Transform bone)
        {
            foreach (Transform child in bone)
                if (child.name.IndexOf("twist", System.StringComparison.OrdinalIgnoreCase) >= 0) yield return child;
        }

        private void AddTwist(Transform twist, Transform parent, Transform child)
        {
            if (parent == null || child == null) return;
            var solver = new TwistSolver(twist) { parent = parent, children = new[] { child }, weight = 1f, parentChildCrossfade = 0.5f };
            solver.Initiate();
            twistSolvers.Add(solver);
        }

        private void RelaxTwist()
        {
            if (relaxTwistBones == false) return;
            foreach (var solver in twistSolvers) solver.Relax();
        }
    }
}
