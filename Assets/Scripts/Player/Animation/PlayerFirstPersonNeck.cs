using System.Collections;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 1인칭 목 숨김. Player 루트에 붙인다 (없으면 PlayerController 가 자동으로 붙인다).
    /// 목은 몸 메시의 일부라 머리 렌더러를 숨겨도 남는다. 아래를 보면 척추(AimIK)·상체(FBBIK)가 굽어 목 단면이 고정된 1인칭 카메라 앞에 들어온다.
    /// 그래서 머리가 숨겨진 동안(PlayerController.IsHeadHidden) 모든 IK 가 끝난 뒤 목 뼈를 거의 0 크기로 접어 그리고, 프레임 끝에 원래 크기로 되돌린다
    ///  - 다음 프레임의 물리·애니메이션·IK 는 항상 원래 크기 뼈를 본다 (렌더링에만 영향)
    ///  - 머리 그림자도 같이 사라진다 (머리 메시가 목 뼈 아래)
    /// 실행 순서: AimIK(9997) → PlayerFirstPersonIK(9998) → FBBIK(9999) → PlayerGunRecoil(10000) → AnimJitterProbe(10001) → 이 스크립트(10002)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10002)]
    public class PlayerFirstPersonNeck : MonoBehaviour
    {
        private const float CollapsedScale = 0.001f; // 0 이면 행렬이 퇴화하므로 아주 작게

        [SerializeField] private PlayerController player;
        [Tooltip("접을 뼈. 비워 두면 몸 Animator 의 Neck (없으면 Head)")]
        [SerializeField] private Transform neckBone;

        private Vector3 neckHomeScale;
        private bool collapsed;
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

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
        }

        private void OnEnable() => StartCoroutine(RestoreAtEndOfFrame());

        private void OnDisable() => Restore();

        private void LateUpdate()
        {
            if (neckBone == null || player == null) return;
            player.ReportNeckPosition(neckBone.position); // 접기 전 위치 → 다음 프레임 눈이 목보다 앞에 오게 (목 보호)
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
