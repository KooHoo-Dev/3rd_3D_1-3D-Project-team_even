using UnityEngine;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 진행용 차단막 (피해 없음). 과목을 통과하기 전까지 길을 막고, Open 하면 openTime 동안 빛이 사그라들며 콜라이더를 끈다.
    /// 막는 콜라이더와 보이는 판(visual)은 Tools > Tutorial > Setup Tutorial Scene 이 만든다
    /// </summary>
    public sealed class TutorialGate : MonoBehaviour
    {
        [Tooltip("길을 막는 콜라이더 (트리거 아님)")]
        [SerializeField] private Collider blocker;
        [Tooltip("보이는 차단막. 열릴 때 위아래로 접히며 사라진다")]
        [SerializeField] private Transform visual;
        [Tooltip("열리는 시간(초)")]
        [SerializeField, Min(0f)] private float openTime = 0.6f;

        public bool IsOpen { get; private set; }

        private float openedAt = -1f;
        private Vector3 visualScale;

        private void Awake()
        {
            if (visual != null) visualScale = visual.localScale;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            openedAt = Time.time;
            if (blocker != null) blocker.enabled = false;
            if (visual == null || openTime <= 0f) Hide();
        }

        private void Update()
        {
            if (openedAt < 0f || visual == null) return;
            float t = Mathf.Clamp01((Time.time - openedAt) / openTime);
            visual.localScale = new Vector3(visualScale.x, visualScale.y * (1f - t), visualScale.z);
            if (t >= 1f) Hide();
        }

        private void Hide()
        {
            openedAt = -1f;
            if (visual != null) visual.gameObject.SetActive(false);
            enabled = false;
        }
    }
}
