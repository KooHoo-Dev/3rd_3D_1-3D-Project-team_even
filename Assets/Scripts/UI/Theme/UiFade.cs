using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI.Theme
{
    /// <summary>
    /// 스프라이트 없는 Image 를 가로로 투명해지게 (정점 알파). 띠 바탕·사라지는 밑줄용.
    /// MainMenu.HorizontalFade 와 같은 방식이지만, 코드로 만드는 화면에서 양끝 알파를 정할 수 있게 Set 을 연다
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class UiFade : BaseMeshEffect
    {
        [Tooltip("왼쪽 끝 알파 배율 (Graphic 색 알파에 곱함)")]
        [Range(0f, 1f)] [SerializeField] private float leftAlpha = 1f;
        [Tooltip("오른쪽 끝 알파 배율")]
        [Range(0f, 1f)] [SerializeField] private float rightAlpha;

        /// <summary>양끝 알파 배율을 바꾸고 다시 그린다</summary>
        public void Set(float left, float right)
        {
            leftAlpha = Mathf.Clamp01(left);
            rightAlpha = Mathf.Clamp01(right);
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (IsActive() == false || vh.currentVertCount == 0) return;

            var rect = graphic.rectTransform.rect;
            var vertex = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                float t = Mathf.InverseLerp(rect.xMin, rect.xMax, vertex.position.x);
                vertex.color.a = (byte)(vertex.color.a * Mathf.Lerp(leftAlpha, rightAlpha, t));
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
