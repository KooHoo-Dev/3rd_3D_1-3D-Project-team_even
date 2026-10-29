using UnityEngine;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 메뉴 카메라(CinemachineCamera)의 느린 좌우 흔들림. 기준 위치·회전 둘레로 오른쪽 이동 + 약간의 좌우 회전.
    /// 숨쉬기 수준의 떨림은 같은 오브젝트의 CinemachineBasicMultiChannelPerlin(Noise) 이 맡는다.
    /// 메뉴 진입처럼 기준 자체를 옮길 때는 SetBase, 호버 연출처럼 잠깐 비켜 서는 이동은 Offset (카메라 기준 로컬) — 둘이 서로 덮어쓰지 않는다.
    /// 흔들림은 어느 쪽이든 계속 얹힌다
    /// </summary>
    public sealed class MenuCameraSway : MonoBehaviour
    {
        [Tooltip("좌우 이동 폭(m, 한쪽)")]
        [SerializeField] private float distance = 0.35f;
        [Tooltip("좌우 회전 폭(도, 한쪽)")]
        [SerializeField] private float yaw = 1.2f;
        [Tooltip("한 번 왕복하는 시간(초)")]
        [SerializeField] private float period = 12f;

        private Vector3 basePosition;
        private Quaternion baseRotation;

        public Vector3 BasePosition => basePosition;
        public Quaternion BaseRotation => baseRotation;
        /// <summary>기준 위치에 더하는 이동 (기준 회전 기준 로컬, m). MenuBackdropDirector 의 호버 연출이 쓴다</summary>
        public Vector3 Offset { get; set; }

        private void Awake()
        {
            basePosition = transform.position;
            baseRotation = transform.rotation;
        }

        public void SetBase(Vector3 position, Quaternion rotation)
        {
            basePosition = position;
            baseRotation = rotation;
        }

        private void LateUpdate()
        {
            float s = period > 0f ? Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / period) : 0f;
            transform.SetPositionAndRotation(
                basePosition + baseRotation * (Offset + Vector3.right * (s * distance)),
                baseRotation * Quaternion.Euler(0f, s * yaw, 0f));
        }
    }
}
