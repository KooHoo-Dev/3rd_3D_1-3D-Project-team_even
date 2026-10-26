using HybridShooter.Core.Events;
using HybridShooter.Weapon;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 사격 반동 (총 기준). Player 루트에 붙인다.
    /// Final IK Recoil 은 손 뼈 축 기준으로 손을 돌려서, 이 캐릭터에서는 총구가 내려가고 개머리판이 올라갔다 → 총의 축으로 직접 정한다.
    ///  - 손잡이 지점(RightHandGrip, 없으면 오른손)을 축으로 총구가 위로 튀고(pitch), 총 뒤쪽으로 밀리고(back), 좌우·기울기가 조금 흔들린다 (yaw·roll 무작위)
    ///  - 쏠 때마다 임계 감쇠 스프링에 충격 → kickTime 에 가장 크게 튀고 부드럽게 돌아온다. 연사하면 쌓이되 한 발의 maxStack 배까지
    ///  - 값: WeaponGripTable 총별(customRecoil), 없으면 총 종류별 기본값. × 프로필 recoilMagnitude
    ///  - 3인칭: FBBIK 가 풀리기 직전 두 손 효과기에 위치 오프셋(+오른어깨), 풀린 직후 두 손을 같은 만큼 돌린다 (애니메이션·AimIK·발 IK 위에 더해진다)
    ///  - 1인칭: PlayerFirstPersonIK 가 눈 기준 총 자리에 ApplyFirstPerson 으로 더한다 (조준 중에는 adsScale 배)
    /// 화면(카메라) 반동은 총 시스템(Gun → GunSlot.AddRecoil)이 따로 한다. 1인칭 총은 눈을 따라가므로 같이 올라간다
    /// 실행 순서: Update(스프링) … PlayerFirstPersonIK(9998) → FullBodyBipedIK(9999, 앞뒤 콜백) → 이 스크립트 LateUpdate(10000, 총구 궤적 기록)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public class PlayerGunRecoil : MonoBehaviour
    {
        private const int Pitch = 0, Back = 1, Yaw = 2, Roll = 3;
        private const int TrailSize = 48;

        [SerializeField] private PlayerAnimationDriver driver;
        [SerializeField] private PlayerBodyIK bodyIK;
        [SerializeField] private PlayerFirstPersonIK firstPersonIK;
        [Tooltip("몸(Body)의 FullBodyBipedIK. 비워 두면 자식에서 찾는다")]
        [SerializeField] private FullBodyBipedIK fbbik;
        [Tooltip("3인칭: 뒤로 밀리는 거리 중 오른어깨가 같이 밀리는 비율")]
        [SerializeField, Range(0f, 1f)] private float shoulderShare = 0.4f;
        [Tooltip("1인칭 조준 중 총이 튀는 정도 배수 (조준기가 화면 중앙에서 덜 벗어나게)")]
        [SerializeField, Range(0f, 1f)] private float adsScale = 0.5f;
        [Tooltip("한 발마다 세기 무작위 폭 (0.1 = ±10%)")]
        [SerializeField, Range(0f, 0.5f)] private float magnitudeRandom = 0.1f;

        private readonly float[] x = new float[4];      // 지금 반동 (pitch 도, back m, yaw 도, roll 도)
        private readonly float[] v = new float[4];
        private readonly float[] limit = new float[4];
        private float omega = 20f;
        private float tpWeight;
        private Quaternion gunDelta = Quaternion.identity;
        private Quaternion rightBefore = Quaternion.identity, leftBefore = Quaternion.identity;
        private readonly Vector3[] trail = new Vector3[TrailSize];
        private int trailCount;

        /// <summary>반동이 남아 있는지</summary>
        public bool Active { get; private set; }
        /// <summary>지금 총구가 튄 각도(도)·밀린 거리(m)</summary>
        public float CurrentPitch => x[Pitch];
        public float CurrentBack => x[Back];
        /// <summary>이번 반동(연사 포함) 동안 가장 크게 튄 값 (IK Pose Lab 표시)</summary>
        public float PeakPitch { get; private set; }
        public float PeakBack { get; private set; }
        public float AdsScale => adsScale;
        /// <summary>이번 반동 동안의 총구 위치 (프레임 끝 기준, IK Pose Lab 씬 뷰 표시)</summary>
        public int TrailCount => trailCount;
        public Vector3 TrailPoint(int i) => trail[i];

        private void Awake()
        {
            if (driver == null) driver = GetComponent<PlayerAnimationDriver>();
            if (bodyIK == null) bodyIK = GetComponent<PlayerBodyIK>();
            if (firstPersonIK == null) firstPersonIK = GetComponent<PlayerFirstPersonIK>();
            if (fbbik == null) fbbik = GetComponentInChildren<FullBodyBipedIK>();
            // 예전 Final IK Recoil 이 남아 있으면 끈다 (손 뼈 축 기준이라 방향이 틀림)
            foreach (Recoil old in GetComponentsInChildren<Recoil>(true)) old.enabled = false;
        }

        private void OnEnable()
        {
            EventBus<WeaponFiredEvent>.Subscribe(OnWeaponFired);
            if (fbbik != null)
            {
                fbbik.solver.OnPreUpdate += BeforeFBBIK;
                fbbik.solver.OnPostUpdate += AfterFBBIK;
            }
        }

        private void OnDisable()
        {
            EventBus<WeaponFiredEvent>.Unsubscribe(OnWeaponFired);
            if (fbbik != null)
            {
                fbbik.solver.OnPreUpdate -= BeforeFBBIK;
                fbbik.solver.OnPostUpdate -= AfterFBBIK;
            }
        }

        /// <summary>상체 자세가 꺼져 있으면(구르기·사망) 걸지 않는다</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (driver == null || driver.UpperPoseWeight < 0.5f) return;
            Fire();
        }

        /// <summary>한 발 (scale = 추가 배수)</summary>
        public void Fire(float scale = 1f)
        {
            Gun gun = bodyIK != null ? bodyIK.GripGun : null;
            WeaponGripTable table = bodyIK != null ? bodyIK.GripTable : null;
            WeaponGripTable.RecoilSettings s = table != null ? table.RecoilFor(gun) : WeaponGripTable.DefaultRecoil(gun != null ? WeaponGripTable.Key(gun) : null);
            float profileScale = driver != null && driver.Profile != null ? driver.Profile.recoilMagnitude : 1f;
            float m = scale * profileScale * (1f + Random.Range(-magnitudeRandom, magnitudeRandom));
            if (Active == false)
            {
                trailCount = 0;
                PeakPitch = PeakBack = 0f;
            }

            omega = 1f / Mathf.Max(0.01f, s.kickTime);
            float stack = Mathf.Max(1f, s.maxStack);
            Kick(Pitch, s.pitch * m, Mathf.Abs(s.pitch * m) * stack);
            Kick(Back, s.back * m, Mathf.Abs(s.back * m) * stack);
            Kick(Yaw, Random.Range(-1f, 1f) * s.yaw * m, Mathf.Abs(s.yaw * m) * stack);
            Kick(Roll, Random.Range(-1f, 1f) * s.roll * m, Mathf.Abs(s.roll * m) * stack);
            Active = true;
        }

        /// <summary>임계 감쇠 스프링 x(t) = v0·t·e^(-ωt) 의 꼭짓점(t = 1/ω)이 amount 가 되도록 속도를 더한다</summary>
        private void Kick(int i, float amount, float max)
        {
            v[i] += amount * omega * (float)System.Math.E;
            limit[i] = max;
        }

        private void Update()
        {
            if (Active == false) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float decay = Mathf.Exp(-omega * dt);
            bool active = false;
            for (int i = 0; i < 4; i++)
            {
                // 임계 감쇠 스프링 (목표 0) 정확한 적분
                float a = v[i] + omega * x[i];
                x[i] = (x[i] + a * dt) * decay;
                v[i] = (v[i] - omega * a * dt) * decay;
                if (Mathf.Abs(x[i]) > limit[i])
                {
                    x[i] = Mathf.Sign(x[i]) * limit[i];
                    if (v[i] * x[i] > 0f) v[i] = 0f;
                }
                if (Mathf.Abs(x[i]) > 1e-4f || Mathf.Abs(v[i]) > 1e-3f) active = true;
            }
            PeakPitch = Mathf.Max(PeakPitch, x[Pitch]);
            PeakBack = Mathf.Max(PeakBack, x[Back]);
            if (active) return;
            Active = false;
            for (int i = 0; i < 4; i++) x[i] = v[i] = 0f;
        }

        /// <summary>총 자리(위치·회전)에 지금 반동을 더한다. pivotInGun = 회전 축 (총 기준, 크기 무시 m)</summary>
        public void ApplyToGun(ref Vector3 position, ref Quaternion rotation, Vector3 pivotInGun, float scale)
        {
            if (Active == false || scale <= 0f) return;
            // 총 기준: x 축(오른쪽)으로 음수 회전 = 총구가 위로
            Quaternion delta = Quaternion.Euler(-x[Pitch] * scale, x[Yaw] * scale, x[Roll] * scale);
            Vector3 pivot = position + rotation * pivotInGun;
            Quaternion recoiled = rotation * delta;
            position = pivot - recoiled * pivotInGun + rotation * new Vector3(0f, 0f, -x[Back] * scale);
            rotation = recoiled;
        }

        /// <summary>1인칭: 눈 기준으로 정한 총 자리에 반동을 더한다 (조준 정도만큼 adsScale 배)</summary>
        public void ApplyFirstPerson(ref Vector3 position, ref Quaternion rotation, Gun gun, float aimWeight)
        {
            if (Active == false || gun == null) return;
            ApplyToGun(ref position, ref rotation, PivotInGun(gun), Mathf.Lerp(1f, adsScale, aimWeight));
        }

        /// <summary>회전 축: 손잡이 지점(있으면) 또는 오른손 (총 기준, 크기 무시 m)</summary>
        public Vector3 PivotInGun(Gun gun)
        {
            Transform g = gun.transform;
            Transform pivot = bodyIK.RightHandGrip != null ? bodyIK.RightHandGrip : bodyIK.RightHandBone;
            return pivot != null ? Quaternion.Inverse(g.rotation) * (pivot.position - g.position) : Vector3.zero;
        }

        /// <summary>3인칭: 반동을 더한 총 자리에 맞춰 두 손을 옮긴다 (총과 손의 지금 관계 그대로). 1인칭 몫은 PlayerFirstPersonIK 가 목표에 넣는다</summary>
        private void BeforeFBBIK()
        {
            tpWeight = 0f;
            Gun gun = bodyIK != null ? bodyIK.GripGun : null;
            Transform right = bodyIK != null ? bodyIK.RightHandBone : null;
            Transform left = bodyIK != null ? bodyIK.LeftHandBone : null;
            if (Active == false || gun == null || right == null || left == null || driver == null) return;
            float firstPerson = firstPersonIK != null ? firstPersonIK.Weight : 0f;
            tpWeight = (1f - firstPerson) * driver.UpperPoseWeight * driver.ActionBlend;
            if (tpWeight <= 0f) return;

            Transform g = gun.transform;
            Vector3 position = g.position;
            Quaternion rotation = g.rotation;
            ApplyToGun(ref position, ref rotation, PivotInGun(gun), 1f);
            Quaternion toGun = Quaternion.Inverse(g.rotation);
            gunDelta = rotation * toGun;

            IKSolverFullBodyBiped solver = fbbik.solver;
            Vector3 newRight = position + rotation * (toGun * (right.position - g.position));
            Vector3 newLeft = position + rotation * (toGun * (left.position - g.position));
            solver.rightHandEffector.positionOffset += (newRight - right.position) * tpWeight;
            solver.leftHandEffector.positionOffset += (newLeft - left.position) * tpWeight;
            solver.rightShoulderEffector.positionOffset += -g.forward * (x[Back] * shoulderShare * tpWeight);
            rightBefore = right.rotation;
            leftBefore = left.rotation;
        }

        /// <summary>3인칭: FBBIK 는 손 위치만 옮기므로 회전은 풀린 뒤 총과 같은 만큼 돌린다</summary>
        private void AfterFBBIK()
        {
            if (tpWeight <= 0f) return;
            Transform right = bodyIK.RightHandBone, left = bodyIK.LeftHandBone;
            right.rotation = Quaternion.Slerp(right.rotation, gunDelta * rightBefore, tpWeight);
            left.rotation = Quaternion.Slerp(left.rotation, gunDelta * leftBefore, tpWeight);
        }

        /// <summary>프레임 끝 총구 자리 기록 (IK Pose Lab 의 총구 궤적)</summary>
        private void LateUpdate()
        {
            Gun gun = bodyIK != null ? bodyIK.GripGun : null;
            if (Active == false || gun == null || gun.Muzzle == null) return;
            if (trailCount < TrailSize) trail[trailCount++] = gun.Muzzle.position;
        }
    }
}
