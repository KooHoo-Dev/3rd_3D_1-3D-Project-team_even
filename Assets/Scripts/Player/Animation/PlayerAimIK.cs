using HybridShooter.Core.Events;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 조준·반동 IK 연결 (Final IK AimIK · Recoil). 매 프레임 조준점과 가중치를 AimIK 에 넘기고, 쏠 때마다 Recoil 을 건다 (Player 루트에 붙인다).
    ///  - 조준점: 지금 화면 카메라에서 정면으로 쏜 광선이 처음 닿는 곳 (없으면 maxAimDistance 앞). 자기 콜라이더는 무시
    ///  - 가중치: 드라이버의 조준 정도(Aim) × 상체 자세 레이어 가중치 × 팔 동작 없음(ActionBlend)
    ///    → 들고 다닐 때 0, 조준·사격할 때 1, 재장전·꺼내기·구르기·사망 중 0
    ///  - 반동: WeaponFiredEvent 마다 Recoil.Fire(프로필 recoilMagnitude)
    /// AimIK(실행 순서 9997)는 이 스크립트(100)와 PlayerController 의 카메라 갱신(0) 다음에 풀린다
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class PlayerAimIK : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimationDriver driver;
        [Tooltip("몸(Body)의 AimIK. 빌더 메뉴가 붙이고 연결한다")]
        [SerializeField] private AimIK aimIK;
        [Tooltip("몸(Body)의 Final IK Recoil (FullBodyBipedIK 필요). 빌더 메뉴가 붙이고 연결한다")]
        [SerializeField] private Recoil recoil;
        [Tooltip("조준점을 찾을 때 광선이 닿는 레이어")]
        [SerializeField] private LayerMask aimMask = ~0;
        [SerializeField] private float maxAimDistance = 100f;
        [Tooltip("조준점이 이보다 가까우면(벽에 붙음) 이 거리로 민다. 너무 가까우면 척추가 크게 꺾인다")]
        [SerializeField] private float minAimDistance = 2f;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>마지막으로 계산한 조준점 (디버그용)</summary>
        public Vector3 AimPoint { get; private set; }

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (driver == null) driver = GetComponent<PlayerAnimationDriver>();
            if (aimIK == null) aimIK = GetComponentInChildren<AimIK>();
            if (recoil == null) recoil = GetComponentInChildren<Recoil>();
        }

        private void OnEnable() => EventBus<WeaponFiredEvent>.Subscribe(OnWeaponFired);

        private void OnDisable() => EventBus<WeaponFiredEvent>.Unsubscribe(OnWeaponFired);

        /// <summary>쏠 때마다 반동. 상체 자세가 꺼져 있으면(구르기·사망) 걸지 않는다</summary>
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            if (recoil == null || driver == null || driver.Profile == null || driver.UpperPoseWeight < 0.5f) return;
            recoil.Fire(driver.Profile.recoilMagnitude);
        }

        private void LateUpdate()
        {
            if (aimIK == null || player == null || driver == null) return;

            Transform cam = player.CurrentCameraTransform;
            float weight = driver.AimWeight * driver.UpperPoseWeight * driver.ActionBlend;
            if (cam == null || weight <= 0f)
            {
                aimIK.solver.IKPositionWeight = 0f;
                return;
            }

            AimPoint = FindAimPoint(cam.position, cam.forward);
            aimIK.solver.IKPosition = AimPoint;
            aimIK.solver.IKPositionWeight = weight;
        }

        private Vector3 FindAimPoint(Vector3 origin, Vector3 direction)
        {
            float distance = maxAimDistance;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(player.transform)) continue;
                if (hits[i].distance < distance) distance = hits[i].distance;
            }
            return origin + direction * Mathf.Max(distance, minAimDistance);
        }
    }
}
