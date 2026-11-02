using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 스프라이트 없는 Image 를 왼쪽→오른쪽으로 투명해지게 (정점 알파). 메뉴 버튼 뒤 가독성용 어두운 패널.
    /// 텍스처를 따로 만들지 않으려고 Image(Simple, 정점 4개)의 오른쪽 두 정점 알파만 바꾼다
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class HorizontalFade : BaseMeshEffect
    {
        [Tooltip("왼쪽 끝 알파 배율 (Image 색 알파에 곱함)")]
        [Range(0f, 1f)] [SerializeField] private float leftAlpha = 1f;
        [Tooltip("오른쪽 끝 알파 배율")]
        [Range(0f, 1f)] [SerializeField] private float rightAlpha;

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
