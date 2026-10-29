using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    /// <summary>
    /// 등장 공격 예고 그림자(Shadow 프리팹 루트)의 연출.
    /// EntranceAttack 은 이 오브젝트의 스케일(지름)만 바꾸므로, 여기서는 생성 후 경과 시간으로 진행도를 잡는다.
    /// 그림자가 점점 진해지고, 테두리 경고선은 갈수록 빠르게 깜빡이며, 다 커진 뒤(낙하 중)에는 가장 급하게 깜빡인다.
    /// </summary>
    public class EntranceShadowTelegraph : MonoBehaviour
    {
        private static readonly int DarknessId = Shader.PropertyToID("_Darkness");
        private static readonly int UrgencyId = Shader.PropertyToID("_Urgency");
        private static readonly int BlinkPhaseId = Shader.PropertyToID("_BlinkPhase");

        [SerializeField] private Renderer projector;
        [Tooltip("ClypeosaurusTable 의 EntranceTelegraphTime 과 맞춘다 (연출 진행도 계산용)")]
        [SerializeField, Min(0.01f)] private float expectedTelegraphTime = 3f;
        [SerializeField, Range(0f, 1f)] private float startDarkness = 0.25f;
        [Tooltip("경고선 깜빡임 속도 (라디안/초): 시작 → 끝")]
        [SerializeField] private Vector2 blinkSpeed = new Vector2(5f, 22f);
        [SerializeField, Min(0f)] private float fallingBlinkSpeed = 30f;

        private MaterialPropertyBlock block;
        private float elapsed;
        private float phase;
        private float lastScale;
        private int stableFrames;

        private void OnEnable()
        {
            elapsed = 0f;
            phase = 0f;
            lastScale = -1f;
            stableFrames = 0;
            Apply(startDarkness, 0f);
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            elapsed += deltaTime;

            // 스케일이 더 이상 변하지 않으면 다 커진 것(낙하 단계)으로 본다
            float scale = transform.localScale.x;
            stableFrames = (scale > 0f && Mathf.Approximately(scale, lastScale)) ? stableFrames + 1 : 0;
            lastScale = scale;
            bool falling = stableFrames >= 2 && elapsed > 0.2f;

            float progress = falling ? 1f : Mathf.Clamp01(elapsed / expectedTelegraphTime);
            float speed = falling ? fallingBlinkSpeed : Mathf.Lerp(blinkSpeed.x, blinkSpeed.y, progress * progress);
            phase = Mathf.Repeat(phase + speed * deltaTime, Mathf.PI * 2f * 100f);

            Apply(Mathf.Lerp(startDarkness, 1f, progress), progress);
        }

        private void Apply(float darkness, float urgency)
        {
            if (projector == null) return;

            block ??= new MaterialPropertyBlock();
            projector.GetPropertyBlock(block);
            block.SetFloat(DarknessId, darkness);
            block.SetFloat(UrgencyId, urgency);
            block.SetFloat(BlinkPhaseId, phase);
            projector.SetPropertyBlock(block);
        }
    }
}
