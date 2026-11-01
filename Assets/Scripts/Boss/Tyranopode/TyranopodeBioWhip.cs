using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 늘어난 촉수 끝에서 이어지는 빛나는 생체 채찍 (촉수 폭풍). 꿈틀대는 띠(LineRenderer), 끝으로 갈수록 가늘다.
    /// TyranopodeTentacleSweep 이 매 프레임 Draw(촉수 끝, 판정 끝)로 그린다. 굵기·물결은 보스 크기를 따른다
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TyranopodeBioWhip : MonoBehaviour
    {
        private const int Points = 24;

        [SerializeField] private LineRenderer line;
        [Tooltip("뿌리 쪽 굵기(m, 원본 크기 기준)")]
        [SerializeField, Min(0.01f)] private float width = 0.7f;
        [Tooltip("옆으로 꿈틀대는 폭(m, 원본 크기 기준)")]
        [SerializeField, Min(0f)] private float wobble = 0.35f;
        [SerializeField, ColorUsage(true, true)] private Color color = new Color(2.4f, 1.1f, 0.3f, 1f);

        private readonly Vector3[] positions = new Vector3[Points];

        public bool HasReferences => line != null && line.sharedMaterial != null;

        private void Awake()
        {
            line.positionCount = Points;
            line.useWorldSpace = true;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 0.55f), new Keyframe(1f, 0.15f));
            line.enabled = false;
        }

        /// <summary>from(촉수 끝) → to(판정 끝). alpha 0~1 = 나타난 정도</summary>
        public void Draw(Vector3 from, Vector3 to, float alpha)
        {
            float size = transform.lossyScale.x;
            Vector3 along = to - from;
            Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
            for (int i = 0; i < Points; i++)
            {
                float t = (float)i / (Points - 1);
                // 뿌리·끝은 고정, 가운데가 물결친다
                float wave = Mathf.Sin(t * 9f - Time.time * 14f) * Mathf.Sin(t * Mathf.PI) * wobble * size;
                positions[i] = from + along * t + side * wave;
            }
            line.SetPositions(positions);
            line.widthMultiplier = width * size * alpha;
            Color c = color;
            c.a *= alpha;
            line.startColor = c;
            line.endColor = c;
            line.enabled = true;
        }

        public void Hide() => line.enabled = false;
    }
}
