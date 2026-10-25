using HybridShooter.Weapon;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 1인칭 무기 자리 (Final IK FullBodyBipedIK 손 효과기). Player 루트에 붙인다.
    /// 1인칭 카메라는 눈 높이에 고정이라, 총이 애니메이션(걷기·숨쉬기)대로 흔들리면 화면 속 총이 출렁이고 조준기가 중앙에 오지 않는다.
    /// 그래서 1인칭에서는 총이 있어야 할 자리를 눈 기준으로 정하고, 두 손을 FBBIK 로 그 자리에 끌어온다 (손이 안 닿으면 상체가 따라온다)
    ///  - 평소: 조준기가 눈 기준 hipSight 자리 (화면 오른쪽 아래)
    ///  - 조준: 조준기가 눈 바로 앞 화면 정중앙 (adsDistance 앞). 그 사이는 드라이버의 조준 정도(Aim)로 보간
    ///    어깨 견착 총(로켓·그레네이드 런처)은 조준기가 어깨 위 옆자리(shoulderSight), 저격총은 조준이 끝나면 스코프 화면(PlayerAimView)이 덮는다
    ///  - 총 방향 = 눈 정면. 벽이 총 길이보다 가까우면 총을 몸 쪽으로 당기고 총구를 내린다
    ///  - 사격 반동(PlayerGunRecoil)은 이 총 자리에 더한다 (조준 중에는 줄여서)
    ///  - 오른손 = 총과 손의 지금 관계 그대로 (총은 오른손 아래 WeaponSocket 에 붙어 있다), 왼손 = 총의 LeftHandGrip
    ///  - 총별 값은 WeaponGripTable(fpHipSight, adsDistance), 없으면 여기 기본값. IK Pose Lab 에서 1인칭으로 보며 고친다
    ///  - 가중치 = 1인칭 정도(시점 전환 중 부드럽게) × 상체 자세 레이어 × 팔 동작 없음 (재장전·꺼내기는 애니메이션 그대로)
    /// 실행 순서: AimIK(9997, 척추가 시선을 따라감) → 이 스크립트(9998, 손 목표) → FullBodyBipedIK(9999)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(9998)]
    public class PlayerFirstPersonIK : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimationDriver driver;
        [SerializeField] private PlayerBodyIK bodyIK;
        [Tooltip("몸(Body)의 FullBodyBipedIK. 비워 두면 자식에서 찾는다")]
        [SerializeField] private FullBodyBipedIK fbbik;
        [Tooltip("사격 반동 (총 기준). 1인칭에서는 눈 기준 총 자리에 반동을 더한다")]
        [SerializeField] private PlayerGunRecoil recoil;

        [Header("기본값 (총별 값이 없을 때)")]
        [Tooltip("평소: 조준기가 올 자리 (1인칭 눈 기준 m, x 오른쪽 y 위 z 앞)")]
        [SerializeField] private Vector3 hipSight = new Vector3(0.13f, -0.14f, 0.32f);
        [Tooltip("조준: 눈과 조준기 사이 거리(m)")]
        [SerializeField] private float adsDistance = 0.15f;
        [Tooltip("어깨 견착 조준: 조준기가 올 자리 (1인칭 눈 기준 m). 총이 화면 오른쪽 아래로 비켜 있고 중앙은 조준선 UI")]
        [SerializeField] private Vector3 shoulderSight = new Vector3(0.17f, -0.12f, 0.3f);

        [Header("벽 가까이")]
        [Tooltip("총이 벽에 닿는지 검사할 레이어")]
        [SerializeField] private LayerMask wallMask = ~0;
        [SerializeField] private float wallProbeRadius = 0.05f;
        [Tooltip("총을 몸 쪽으로 당기는 최대 거리(m)")]
        [SerializeField] private float maxWallPull = 0.35f;
        [Tooltip("최대로 당겼을 때 총구를 내리는 각도(도)")]
        [SerializeField] private float wallPitch = 40f;
        [SerializeField] private float wallSmoothTime = 0.08f;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private float wallPull;
        private float wallPullVelocity;

        /// <summary>지금 1인칭 손 IK 가중치</summary>
        public float Weight { get; private set; }
        /// <summary>벽 때문에 당긴 거리(m)</summary>
        public float WallPull => wallPull;
        public Vector3 DefaultHipSight => hipSight;
        public float DefaultAdsDistance => adsDistance;
        public Vector3 DefaultShoulderSight => shoulderSight;
        /// <summary>디버그: FBBIK 가 풀린 직후 오른손이 목표에서 벗어난 거리(cm)·각도(도), 총 정면과 시선 사이 각도(도)</summary>
        public float HandPositionError { get; private set; }
        public float HandRotationError { get; private set; }
        public float GunAngleError { get; private set; }
        /// <summary>디버그: FBBIK 가 지금까지 풀린 횟수 (늘지 않으면 FBBIK 가 돌지 않는 것)</summary>
        public int FbbikSolveCount { get; private set; }
        public FullBodyBipedIK FullBodyIK => fbbik;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (driver == null) driver = GetComponent<PlayerAnimationDriver>();
            if (bodyIK == null) bodyIK = GetComponent<PlayerBodyIK>();
            if (fbbik == null) fbbik = GetComponentInChildren<FullBodyBipedIK>();
            if (recoil == null) recoil = GetComponent<PlayerGunRecoil>();
        }

        private void OnEnable()
        {
            if (fbbik != null) fbbik.solver.OnPostUpdate += MeasureAfterSolve;
        }

        private void OnDisable()
        {
            if (fbbik != null) fbbik.solver.OnPostUpdate -= MeasureAfterSolve;
        }

        private void LateUpdate()
        {
            if (fbbik == null || player == null || driver == null || bodyIK == null) return;
            IKEffector right = fbbik.solver.rightHandEffector;
            IKEffector left = fbbik.solver.leftHandEffector;

            Gun gun = bodyIK.GripGun;
            Transform hand = bodyIK.RightHandBone;
            Weight = player.FirstPersonBlend * driver.UpperPoseWeight * driver.ActionBlend;
            if (Weight <= 0f || gun == null || hand == null || bodyIK.Sight == null)
            {
                SetWeights(right, 0f);
                SetWeights(left, 0f);
                wallPull = 0f;
                return;
            }

            // 총 기준(크기 무시, m) 지점들. 모두 총에 붙어 있어 관계는 매 프레임 같다
            Transform g = gun.transform;
            Quaternion toGun = Quaternion.Inverse(g.rotation);
            Vector3 sightInGun = toGun * (bodyIK.Sight.position - g.position);
            Vector3 muzzleInGun = toGun * (gun.Muzzle.position - g.position);

            // 조준기가 올 자리 (눈 기준): 평소 ↔ 조준
            WeaponGripTable table = bodyIK.GripTable;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            Vector3 hip = entry != null && entry.fpHipSight != Vector3.zero ? entry.fpHipSight : hipSight;
            float ads = entry != null && entry.adsDistance > 0f ? entry.adsDistance : adsDistance;
            Vector3 aimSight = new Vector3(0f, 0f, ads);
            if (table != null && table.AimModeFor(gun) == WeaponGripTable.AimMode.Shoulder)
                aimSight = entry != null && entry.fpShoulderSight != Vector3.zero ? entry.fpShoulderSight : shoulderSight;
            Vector3 sightLocal = Vector3.Lerp(hip, aimSight, driver.AimWeight);

            Pose eye = player.FirstPersonEyePose;
            float reach = sightLocal.z + (muzzleInGun.z - sightInGun.z) + wallProbeRadius;
            float targetPull = Mathf.Min(maxWallPull, Mathf.Max(0f, reach - WallDistance(eye, reach)));
            wallPull = Mathf.SmoothDamp(wallPull, targetPull, ref wallPullVelocity, wallSmoothTime);
            sightLocal.z -= wallPull;

            Quaternion gunRotation = eye.rotation * Quaternion.Euler(maxWallPull > 0f ? wallPitch * wallPull / maxWallPull : 0f, 0f, 0f);
            Vector3 gunPosition = eye.position + eye.rotation * sightLocal - gunRotation * sightInGun;
            if (recoil != null) recoil.ApplyFirstPerson(ref gunPosition, ref gunRotation, gun, driver.AimWeight);

            // 총이 그 자리에 오려면 손이 어디 있어야 하는지 (총과 손의 관계 그대로)
            right.position = gunPosition + gunRotation * (toGun * (hand.position - g.position));
            right.rotation = gunRotation * (toGun * hand.rotation);
            SetWeights(right, Weight);

            Transform grip = bodyIK.LeftHandGrip;
            if (grip != null)
            {
                Vector3 leftPosition = gunPosition + gunRotation * (toGun * (grip.position - g.position));
                Quaternion leftRotation = gunRotation * (toGun * grip.rotation);
                bodyIK.ApplyLeftHandOverride(ref leftPosition, ref leftRotation); // 아이템 사용 중이면 그 자리로
                left.position = leftPosition;
                left.rotation = leftRotation;
                SetWeights(left, Weight * bodyIK.LeftHandWeight);
            }
            else SetWeights(left, 0f);
        }

        private void MeasureAfterSolve()
        {
            FbbikSolveCount++;
            Gun gun = bodyIK != null ? bodyIK.GripGun : null;
            Transform hand = bodyIK != null ? bodyIK.RightHandBone : null;
            if (Weight <= 0f || gun == null || hand == null)
            {
                HandPositionError = HandRotationError = GunAngleError = 0f;
                return;
            }
            IKEffector right = fbbik.solver.rightHandEffector;
            HandPositionError = Vector3.Distance(hand.position, right.position) * 100f;
            HandRotationError = Quaternion.Angle(hand.rotation, right.rotation);
            GunAngleError = Vector3.Angle(gun.transform.forward, player.FirstPersonEyePose.rotation * Vector3.forward);
        }

        /// <summary>눈 정면으로 벽까지 거리 (reach 안에 없으면 reach)</summary>
        private float WallDistance(Pose eye, float reach)
        {
            Vector3 forward = eye.rotation * Vector3.forward;
            int count = Physics.SphereCastNonAlloc(eye.position, wallProbeRadius, forward, hits, reach, wallMask, QueryTriggerInteraction.Ignore);
            float distance = reach;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(player.transform)) continue;
                if (hits[i].distance < distance) distance = hits[i].distance; // 0 = 눈이 이미 벽에 붙어 있음
            }
            return distance;
        }

        private static void SetWeights(IKEffector effector, float weight)
        {
            effector.positionWeight = weight;
            effector.rotationWeight = weight;
        }
    }
}
