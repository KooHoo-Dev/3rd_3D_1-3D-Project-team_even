using HybridShooter.UI;
using TMPro;
using UnityEngine;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 튜토리얼 목적지 화면 표식. TutorialDirector 가 Show / Hide 로 직접 부른다 (전역 이벤트 없음).
    /// 겉모양은 메인 메뉴와 같은 말 (TutorialHud 색): 상자 없이 얇은 선
    ///  - 화면 안: 속이 빈 얇은 마름모(달빛 흰색) + 가운데 붉은 점, 위에 이름(청회색), 아래로 목적지를 가리키는 가는 선과 거리 "032 M"(영문 글꼴)
    ///  - 화면 밖·등 뒤: 화면 가장자리(edgeMargin)에 붙고, 이름·선은 숨기고 붉은 꺾쇠(›)가 목적지 방향을 가리킨다
    ///  - fadeDistance 안으로 들어오면 흐려진다 (바닥 고리·빛기둥이 그 자리를 보여 준다)
    /// 목표 패널(110) 위에 그린다. 입력은 막지 않는다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialWaypointHud : MonoBehaviour
    {
        private const int SortingOrder = 115;
        private const float DiamondSize = 22f, Stroke = 2f;
        private const float StemLength = 22f;

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("거리 숫자용 (Aldrich SDF). 비워 두면 GameFlowUiSkin 의 숫자 글꼴")]
        [SerializeField] private TMP_FontAsset titleFont;
        [Tooltip("목적지 지점에서 표식을 띄우는 높이(m)")]
        [SerializeField, Min(0f)] private float heightOffset = 2.5f;
        [Tooltip("화면 밖일 때 가장자리에서 띄우는 간격 (1920×1080 기준 px)")]
        [SerializeField, Min(0f)] private float edgeMargin = 90f;
        [Tooltip("이 거리(m) 안이면 흐려지기 시작해 절반 거리에서 사라진다")]
        [SerializeField, Min(0.5f)] private float fadeDistance = 6f;

        private Canvas canvas;
        private RectTransform canvasRect, root, chevron, stem;
        private CanvasGroup group;
        private TextMeshProUGUI nameText, distanceText;
        private Transform target;

        public bool IsShown => target != null;

        private void Awake()
        {
            var skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
            if (titleFont == null) titleFont = skin.numberFont != null ? skin.numberFont : textFont;
            Build();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        /// <summary>목적지를 표시한다. label = 표식 이름 (예: "전초기지 게이트")</summary>
        public void Show(Transform destination, string label)
        {
            target = destination;
            nameText.text = label;
            canvas.gameObject.SetActive(destination != null);
        }

        public void Hide()
        {
            target = null;
            canvas.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Camera cam = Camera.main;
            if (cam == null)
            {
                group.alpha = 0f;
                return;
            }

            Vector3 world = target.position + Vector3.up * heightOffset;
            float distance = Vector3.Distance(cam.transform.position, target.position);
            distanceText.text = $"{Mathf.RoundToInt(distance):000} M";

            Vector3 screen = cam.WorldToScreenPoint(world);
            bool behind = screen.z < 0f;
            if (behind) screen = -screen; // 등 뒤면 화면 반대쪽으로 뒤집어 가장자리 방향을 맞춘다
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);

            Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(edgeMargin, edgeMargin);
            bool outside = behind || Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y;
            if (outside)
            {
                // 화면 가운데 → 목적지 방향의 직선이 가장자리 사각형과 만나는 점
                Vector2 dir = local.sqrMagnitude > 0.01f ? local.normalized : Vector2.up;
                float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(dir.x), 0.0001f), half.y / Mathf.Max(Mathf.Abs(dir.y), 0.0001f));
                local = dir * scale;
                chevron.anchoredPosition = dir * (DiamondSize + 10f);
                chevron.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }
            chevron.gameObject.SetActive(outside);
            nameText.gameObject.SetActive(outside == false); // 가장자리에서는 거리만 (화면을 덜 가린다)
            stem.gameObject.SetActive(outside == false);
            distanceText.rectTransform.anchoredPosition = new Vector2(0f, outside ? -DiamondSize * 0.5f - 6f : -DiamondSize * 0.5f - StemLength - 6f);
            root.anchoredPosition = local;

            group.alpha = outside ? 1f : Mathf.Clamp01((distance - fadeDistance * 0.5f) / (fadeDistance * 0.5f));
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("TutorialWaypointHud", SortingOrder, false, out group);
            canvasRect = (RectTransform)canvas.transform;
            root = HudFactory.Rect(canvas.transform, "Waypoint", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(DiamondSize, DiamondSize));

            // 속이 빈 마름모 = 45° 돌린 사각형 테두리 (얇은 선 4개) + 가운데 붉은 점
            var diamond = HudFactory.Rect(root, "Diamond", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(DiamondSize, DiamondSize) * 0.72f);
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            float side = DiamondSize * 0.72f;
            Bar(diamond, "Top", new Vector2(0.5f, 1f), new Vector2(side, Stroke), TutorialHud.MoonWhite);
            Bar(diamond, "Bottom", new Vector2(0.5f, 0f), new Vector2(side, Stroke), TutorialHud.MoonWhite);
            Bar(diamond, "Left", new Vector2(0f, 0.5f), new Vector2(Stroke, side), TutorialHud.MoonWhite);
            Bar(diamond, "Right", new Vector2(1f, 0.5f), new Vector2(Stroke, side), TutorialHud.MoonWhite);
            Bar(diamond, "Core", new Vector2(0.5f, 0.5f), new Vector2(5f, 5f), TutorialHud.Accent);

            // 아래로 목적지를 가리키는 가는 선
            stem = Bar(root, "Stem", new Vector2(0.5f, 0.5f), new Vector2(1.5f, StemLength), new Color(TutorialHud.Steel.r, TutorialHud.Steel.g, TutorialHud.Steel.b, 0.7f));
            stem.pivot = new Vector2(0.5f, 1f);
            stem.anchoredPosition = new Vector2(0f, -DiamondSize * 0.5f - 2f);

            // 화면 밖 방향 꺾쇠 › (+x 를 가리키는 두 선. 회전으로 방향을 맞춘다)
            chevron = HudFactory.Rect(root, "Chevron", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
            foreach (int sign in new[] { 1, -1 })
            {
                var arm = Bar(chevron, sign > 0 ? "ArmUp" : "ArmDown", new Vector2(0.5f, 0.5f), new Vector2(12f, 2.5f), TutorialHud.Accent);
                arm.pivot = new Vector2(1f, 0.5f);
                arm.anchoredPosition = new Vector2(4f, 0f);
                arm.localRotation = Quaternion.Euler(0f, 0f, sign * 40f);
            }
            chevron.gameObject.SetActive(false);

            nameText = HudFactory.Label(root, "Name", textFont, 18f, TextAlignmentOptions.Bottom,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(360f, 26f));
            nameText.color = TutorialHud.Steel;
            nameText.characterSpacing = 2f;
            nameText.outlineWidth = 0.15f;
            nameText.outlineColor = Color.black;
            distanceText = HudFactory.Label(root, "Distance", titleFont, 16f, TextAlignmentOptions.Top,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(160f, 22f));
            distanceText.color = TutorialHud.MoonWhite;
            distanceText.characterSpacing = 6f;
            distanceText.outlineWidth = 0.15f;
            distanceText.outlineColor = Color.black;

            canvas.gameObject.SetActive(false);
        }

        private static RectTransform Bar(RectTransform parent, string name, Vector2 anchor, Vector2 size, Color color)
        {
            var rect = HudFactory.Rect(parent, name, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            HudFactory.NewImage(rect, null, color);
            return rect;
        }
    }
}
