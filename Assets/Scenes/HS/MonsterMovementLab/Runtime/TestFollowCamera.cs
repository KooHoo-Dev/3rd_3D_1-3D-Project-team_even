using UnityEngine;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>
    /// 이동 실험실의 추적 카메라. TestPlayer 뒤쪽 위에서 따라가며 내려다본다.
    /// TestPlayer 가 우클릭 드래그로 정한 시점(ViewYaw/ViewPitch)대로 플레이어 주위를 돈다 (보급 테스트용 TesterFollowCamera 와 같은 방식).
    /// TestPlayer 의 View Camera 로 지정하면 몬스터의 '시야 밖' 판정도 이 카메라 기준이 된다.
    /// </summary>
    public sealed class TestFollowCamera : MonoBehaviour
    {
        [Tooltip("따라갈 플레이어. 필수")]
        [SerializeField] private TestPlayer target;
        [Tooltip("플레이어와의 거리를 정하는 값 (m). 방향은 무시하고 길이만 쓴다")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 18f, -16f);
        [Tooltip("목표 위치를 따라가는 빠르기 (1/s). 클수록 바로 붙는다")]
        [SerializeField] private float followSharpness = 10f;

        /// <summary>
        /// 플레이어가 이번 프레임 움직인 뒤, 시점 각도 방향으로 offset 길이만큼 떨어진 위치로 부드럽게 옮기고 플레이어를 바라본다
        /// </summary>
        private void LateUpdate()
        {
            Vector3 targetPosition = target.transform.position;
            Quaternion viewRotation = Quaternion.Euler(target.ViewPitch, target.ViewYaw, 0f);
            Vector3 desired = targetPosition + viewRotation * new Vector3(0f, 0f, -offset.magnitude);

            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(targetPosition - transform.position, Vector3.up);
        }
    }
}
