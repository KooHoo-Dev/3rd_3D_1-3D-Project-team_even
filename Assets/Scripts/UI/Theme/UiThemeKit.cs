using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI.Theme
{
    /// <summary>
    /// UiTheme 스타일의 공통 부품을 코드로 만든다. 상자 스프라이트 대신 선·표식·사라지는 띠로 그린다.
    /// 배치 규칙은 HudFactory 와 같다 (1920×1080 기준, 위치 = anchor 기준 pivot 자리). 캔버스·Rect·Label 은 HudFactory 를 그대로 쓴다
    /// </summary>
    public static class UiThemeKit
    {
        public enum BandShape
        {
            /// <summary>왼쪽이 진하고 오른쪽으로 사라짐 (왼쪽에 붙는 HUD·패널)</summary>
            FadeRight,
            /// <summary>오른쪽이 진하고 왼쪽으로 사라짐 (오른쪽에 붙는 HUD·패널)</summary>
            FadeLeft,
            /// <summary>가운데가 진하고 양끝이 사라짐 (위쪽 가운데 알림·보상 띠)</summary>
            FadeBoth,
        }

        /// <summary>FadeBoth 에서 양끝 사라지는 구간 비율</summary>
        private const float EdgeRatio = 0.22f;

        #region 띠·선
        /// <summary>
        /// target 을 꽉 채우는 어두운 띠를 맨 뒤 자식으로 깐다.
        /// fadeRatio = 흐려지는 구간 비율 (FadeRight·FadeLeft 기본 1 = 전체가 서서히, 0.3 = 끝 30% 만 흐리고 나머지는 진하게.
        /// FadeBoth 는 양끝 각각, 기본 0.22)
        /// </summary>
        public static RectTransform Band(RectTransform target, BandShape shape, float alpha = 0.82f, float fadeRatio = -1f)
        {
            var band = HudFactory.Stretch(target, "Band");
            band.SetAsFirstSibling();
            var color = new Color(UiTheme.Night.r, UiTheme.Night.g, UiTheme.Night.b, alpha);
            float fade = Mathf.Clamp01(fadeRatio < 0f ? (shape == BandShape.FadeBoth ? EdgeRatio : 1f) : fadeRatio);
            // 정점 4개짜리 Image 는 양끝 알파만 정할 수 있으므로 흐린 조각 · 진한 조각으로 나눈다
            switch (shape)
            {
                case BandShape.FadeRight:
                    if (fade < 1f) BandPiece(band, "Solid", 0f, 1f - fade, color);
                    Fade(BandPiece(band, "Fade", 1f - fade, 1f, color), 1f, 0f);
                    break;
                case BandShape.FadeLeft:
                    Fade(BandPiece(band, "Fade", 0f, fade, color), 0f, 1f);
                    if (fade < 1f) BandPiece(band, "Solid", fade, 1f, color);
                    break;
                default:
                    fade = Mathf.Min(fade, 0.5f);
                    Fade(BandPiece(band, "Left", 0f, fade, color), 0f, 1f);
                    BandPiece(band, "Middle", fade, 1f - fade, color);
                    Fade(BandPiece(band, "Right", 1f - fade, 1f, color), 1f, 0f);
                    break;
            }
            return band;
        }

        /// <summary>단색 선·막대 (클릭 안 받음)</summary>
        public static Image Line(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) =>
            HudFactory.NewImage(HudFactory.Rect(parent, name, anchor, pivot, position, size), null, color);

        /// <summary>오른쪽으로 사라지는 선 (선택 밑줄). 피벗을 왼쪽에 두면 localScale.x 로 그어지는 연출을 할 수 있다</summary>
        public static Image FadeLine(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var line = Line(parent, name, color, anchor, pivot, position, size);
            Fade(line, 1f, 0f);
            return line;
        }

        /// <summary>붉은 선택 표식 (세로 막대)</summary>
        public static Image Marker(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, float height, float width = UiTheme.MarkerWidth) =>
            Line(parent, "Marker", UiTheme.Red, anchor, pivot, position, new Vector2(width, height));
        #endregion

        #region 글자
        /// <summary>영문·숫자 라벨 (Aldrich, 넓은 자간)</summary>
        public static TextMeshProUGUI EnLabel(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions alignment,
                                              Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 box, float spacing = UiTheme.LabelSpacing)
        {
            var label = HudFactory.Label(parent, name, UiTheme.NumberFont, size, alignment, anchor, pivot, position, box);
            label.text = text;
            label.color = color;
            label.characterSpacing = spacing;
            return label;
        }

        /// <summary>한글 라벨 (Noto Sans KR)</summary>
        public static TextMeshProUGUI KrLabel(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions alignment,
                                              Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 box)
        {
            var label = HudFactory.Label(parent, name, UiTheme.TextFont, size, alignment, anchor, pivot, position, box);
            label.text = text;
            label.color = color;
            return label;
        }

        /// <summary>
        /// 섹션 머리: 넓은 자간 영문 라벨 + 아래 짧은 붉은 선 (튜토리얼 목표·메뉴 로고와 같은 모양).
        /// 선은 라벨의 자식이라 라벨을 옮기면 같이 움직인다
        /// </summary>
        public static TextMeshProUGUI SectionHeader(Transform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 position, float width)
        {
            var header = EnLabel(parent, "Header", text, UiTheme.LabelSize, UiTheme.Steel, TextAlignmentOptions.BottomLeft,
                anchor, pivot, position, new Vector2(width, UiTheme.LabelSize * 1.5f));
            Line(header.rectTransform, "Line", UiTheme.Red, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, -6f),
                new Vector2(UiTheme.HeaderLineLength, UiTheme.LineWidth));
            return header;
        }

        /// <summary>
        /// 키 표시: 1px 청회색 테두리 상자 + 붉은 키 이름 (Aldrich). 돌려주는 글자의 부모가 상자다.
        /// 키 이름을 바꾼 뒤에는 FitKeyCap 으로 상자 폭을 맞춘다
        /// </summary>
        public static TextMeshProUGUI KeyCap(Transform parent, string name, string key, Vector2 anchor, Vector2 pivot, Vector2 position, float height = 36f)
        {
            var box = HudFactory.Rect(parent, name, anchor, pivot, position, new Vector2(height, height));
            var frame = new Color(UiTheme.Steel.r, UiTheme.Steel.g, UiTheme.Steel.b, 0.8f);
            FrameSide(box, "Top", frame, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f));
            FrameSide(box, "Bottom", frame, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1f));
            FrameSide(box, "Left", frame, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1f, 0f));
            FrameSide(box, "Right", frame, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(1f, 0f));

            var label = HudFactory.Label(box, "Key", UiTheme.NumberFont, height * 0.55f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.color = UiTheme.RedText;
            label.text = key;
            FitKeyCap(label);
            return label;
        }

        /// <summary>키 이름 길이에 맞춰 상자 폭을 늘린다 (정사각형보다 작아지지는 않음)</summary>
        public static void FitKeyCap(TextMeshProUGUI keyLabel)
        {
            var box = (RectTransform)keyLabel.rectTransform.parent;
            float height = box.sizeDelta.y;
            float textWidth = keyLabel.GetPreferredValues(keyLabel.text).x;
            box.sizeDelta = new Vector2(Mathf.Max(height, textWidth + height * 0.45f), height);
        }
        #endregion

        #region 게이지·버튼
        /// <summary>얇은 게이지 (바탕 + 왼쪽부터 차는 막대). 값은 UiGauge.SetValue 로</summary>
        public static UiGauge Gauge(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var root = HudFactory.Rect(parent, name, anchor, pivot, position, size);
            HudFactory.NewImage(root, null, UiTheme.GaugeBack);
            var fill = HudFactory.Stretch(root, "Fill");
            var fillImage = HudFactory.NewImage(fill, null, UiTheme.MoonWhite);
            var gauge = new UiGauge(root, fill, fillImage);
            gauge.SetValue(1f);
            return gauge;
        }

        /// <summary>
        /// 메인 메뉴와 같은 글자 버튼: 투명한 클릭 영역 + 왼쪽 붉은 표식 + 영문 글자 + 오른쪽으로 사라지는 붉은 밑줄.
        /// 피벗·기준점은 왼쪽 가운데. 색·위치 변화는 UiTextButtonFx 가 맡는다 (Button Transition 없음).
        /// font 를 주면 그 글꼴(한글 버튼은 UiTheme.TextFont)과 좁은 자간, 비우면 Aldrich·넓은 자간. 글자는 이름 "Label" 자식
        /// </summary>
        public static Button TextButton(Transform parent, string name, string text, float fontSize, Vector2 anchor, Vector2 position, Vector2 size,
                                        TMP_FontAsset font = null)
        {
            const float labelX = 34f;
            var rect = HudFactory.Rect(parent, name, anchor, new Vector2(0f, 0.5f), position, size);
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var marker = Marker(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, fontSize * 0.95f, 5f);
            var label = EnLabel(rect, "Label", text, fontSize, UiTheme.Steel, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(labelX, 0f), new Vector2(size.x - labelX, size.y));
            if (font != null)
            {
                label.font = font;
                label.characterSpacing = 2f;
            }
            var underline = FadeLine(rect, "Underline", UiTheme.Red, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(labelX, -fontSize * 0.62f), new Vector2(Mathf.Min(380f, size.x - labelX), UiTheme.LineWidth));

            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            rect.gameObject.AddComponent<UiTextButtonFx>().Setup(label, marker, underline.rectTransform, UiTheme.Steel, UiTheme.MoonWhite);
            return button;
        }
        #endregion

        private static Image BandPiece(RectTransform band, string name, float fromX, float toX, Color color)
        {
            var piece = HudFactory.Stretch(band, name);
            piece.anchorMin = new Vector2(fromX, 0f);
            piece.anchorMax = new Vector2(toX, 1f);
            return HudFactory.NewImage(piece, null, color);
        }

        /// <summary>상자 한 변 (anchorMin~anchorMax 로 늘어나는 1px 선)</summary>
        private static void FrameSide(RectTransform box, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            var side = HudFactory.Rect(box, name, anchorMin, pivot, Vector2.zero, size);
            side.anchorMax = anchorMax;
            HudFactory.NewImage(side, null, color);
        }

        private static void Fade(Graphic graphic, float left, float right) => graphic.gameObject.AddComponent<UiFade>().Set(left, right);
    }

    /// <summary>UiThemeKit.Gauge 가 만든 게이지. 값 0~1 은 막대의 오른쪽 끝(anchorMax.x)</summary>
    public readonly struct UiGauge
    {
        public readonly RectTransform Root;
        public readonly RectTransform Fill;
        public readonly Image FillImage;

        public UiGauge(RectTransform root, RectTransform fill, Image fillImage)
        {
            Root = root;
            Fill = fill;
            FillImage = fillImage;
        }

        public void SetValue(float value) => Fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);

        public void SetColor(Color color) => FillImage.color = color;
    }
}
