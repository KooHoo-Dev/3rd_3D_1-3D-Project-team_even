using UnityEngine;

namespace HybridShooter.Atmosphere
{
    /// <summary>
    /// 주변 연기 파티클 발생기를 메인 카메라 아래로 따라 옮긴다.
    /// 파티클은 월드 공간에서 시뮬레이션되므로 이미 생긴 연기는 제자리에 남고, 새 연기만 카메라 주변에 생긴다.
    /// → 어디로 가든 일정 거리 바깥이 연기 벽처럼 보인다.
    /// </summary>
    public sealed class FollowCameraGround : MonoBehaviour
    {
        [Tooltip("카메라 높이 기준 발생기 높이 오프셋 (음수 = 카메라 아래)")]
        [SerializeField] private float heightOffset = -2f;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.position = cam.transform.position + Vector3.up * heightOffset;
        }
    }
}
