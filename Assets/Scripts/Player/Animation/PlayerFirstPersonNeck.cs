using System.Collections;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 1인칭 목 숨김. Player 루트에 붙인다 (없으면 PlayerController 가 자동으로 붙인다).
    /// 목은 몸 메시의 일부라 머리 렌더러를 숨겨도 남는다. 아래를 보면 척추(AimIK)·상체(FBBIK)가 굽어 목 단면이 고정된 1인칭 카메라 앞에 들어온다.
    /// 그래서 머리가 숨겨진 동안(PlayerController.IsHeadHidden) 모든 IK 가 끝난 뒤 목 뼈를 거의 0 크기로 접어 그리고, 프레임 끝에 원래 크기로 되돌린다
    ///  - 다음 프레임의 물리·애니메이션·IK 는 항상 원래 크기 뼈를 본다 (렌더링에만 영향)
    ///  - 머리 그림자도 같이 사라진다 (머리 메시가 목 뼈 아래)
    ///  - 목 보호(PlayerController 가 눈을 목보다 앞으로 미는 것)에는 FBBIK 직전 목 위치(애니메이션 + AimIK)를 넘긴다.
    ///    FBBIK 뒤 위치를 넘기면: 눈을 밀면 → 눈 기준 총 자리도 밀리고 → 손 IK 가 어깨·목을 앞으로 끌고 → 다시 눈을 미는 순환이 생겨
    ///    정면·위를 볼 때 눈이 40~65cm 밀리고 손이 총 자리에 못 닿았다
    /// 실행 순서: AimIK(9997) → PlayerFirstPersonIK(9998) → FBBIK(9999) → PlayerGunRecoil(10000) → 이 스크립트(10002)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10002)]
    public class PlayerFirstPersonNeck : MonoBehaviour
    {
        private const float CollapsedScale = 0.001f; // 0 이면 행렬이 퇴화하므로 아주 작게

        [SerializeField] private PlayerController player;
        [Tooltip("접을 뼈. 비워 두면 몸 Animator 의 Neck (없으면 Head)")]
        [SerializeField] private Transform neckBone;
        [Tooltip("목 보호에 넘길 목 위치: 0 = 모든 IK 뒤, 1 = FBBIK 직전 (손 IK 가 목을 끌어낸 몫을 빼고 본다. 0 쪽은 손 IK 와 서로 미는 순환이 생긴다)")]
        [SerializeField, Range(0f, 1f)] private float beforeFullBodyIKWeight = 1f;

        private Vector3 neckHomeScale;
        private bool collapsed;
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        private FullBodyBipedIK fbbik;

        /// <summary>이번 프레임 FBBIK 가 풀리기 직전 목 뼈 월드 위치 (애니메이션 + AimIK 까지)</summary>
        public Vector3 NeckBeforeFullBodyIK { get; private set; }

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (neckBone == null)
            {
                Animator animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
                    if (neckBone == null) neckBone = animator.GetBoneTransform(HumanBodyBones.Head);
                }
            }
            if (neckBone != null) neckHomeScale = neckBone.localScale;
            fbbik = GetComponentInChildren<FullBodyBipedIK>();
        }

        private void OnEnable()
        {
            StartCoroutine(RestoreAtEndOfFrame());
            if (fbbik != null) fbbik.solver.OnPreUpdate += RecordBeforeFullBodyIK;
        }

        private void OnDisable()
        {
            if (fbbik != null) fbbik.solver.OnPreUpdate -= RecordBeforeFullBodyIK;
            Restore();
        }

        private void RecordBeforeFullBodyIK()
        {
            if (neckBone != null) NeckBeforeFullBodyIK = neckBone.position;
        }

        private void LateUpdate()
        {
            if (neckBone == null || player == null) return;
            if (fbbik == null) NeckBeforeFullBodyIK = neckBone.position;
            // 접기 전 위치 → 다음 프레임 눈이 목보다 앞에 오게 (목 보호)
            player.ReportNeckPosition(Vector3.Lerp(neckBone.position, NeckBeforeFullBodyIK, beforeFullBodyIKWeight));
            if (player.IsHeadHidden == false) return;
            neckBone.localScale = neckHomeScale * CollapsedScale;
            collapsed = true;
        }

        /// <summary>렌더링이 끝난 뒤 원래 크기로 (다음 프레임 물리·애니메이션 전에)</summary>
        private IEnumerator RestoreAtEndOfFrame()
        {
            while (true)
            {
                yield return endOfFrame;
                Restore();
            }
        }

        private void Restore()
        {
            if (collapsed == false) return;
            neckBone.localScale = neckHomeScale;
            collapsed = false;
        }
    }
}
