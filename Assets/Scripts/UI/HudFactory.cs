using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 코드로 HUD 를 만드는 공용 도우미 (WeaponSlotHud · InteractPromptHud · LoadoutPanel).
    /// 배치 규칙은 PlayerHud 와 같다: 1920×1080 기준, 높이 맞춤, 위치 = anchor 기준 pivot 자리
    /// </summary>
    internal static class HudFactory
    {
        public const string NumberFontGuid = "4d0bb7337f597634b94b9005947e8e28";   // Aldrich-Regular SDF
        /// <summary>한글이 들어가는 글자용. SCI-FI 팩 글꼴(Play-Bold 등)에는 한글이 없어 네모로 깨진다</summary>
        public const string TextFontGuid = "659ceb8947c683b4a9e1216df0a67515";     // NotoSansKR-Regular SDF

        public static readonly Color TextColor = new Color(0.85f, 0.97f, 1f, 1f);
        public static readonly Color Amber = new Color(1f, 0.72f, 0.2f, 1f);
        public static readonly Color PanelColor = new Color(0.02f, 0.06f, 0.09f, 0.72f);

        /// <summary>화면 전체를 덮는 Screen Space Overlay 캔버스. 클릭을 받지 않는다 (interactive = true 면 받는다)</summary>
        public static Canvas CreateCanvas(string name, int sortingOrder, bool interactive, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            if (interactive) go.AddComponent<GraphicRaycaster>();
            group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = interactive;
            group.interactable = interactive;
            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>부모를 꽉 채우는 RectTransform</summary>
        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMax = Vector2.one;
            return rt;
        }

        public static Image NewImage(RectTransform rt, Sprite sprite, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment,
                                            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 box)
        {
            var rt = Rect(parent, name, anchor, pivot, position, box);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.color = TextColor;
            return text;
        }

        /// <summary>Resources/UI/WeaponIcons/{iconKey} 스프라이트. 키가 비었거나 없으면 null</summary>
        public static Sprite WeaponIcon(string iconKey) =>
            string.IsNullOrEmpty(iconKey) ? null : Resources.Load<Sprite>("UI/WeaponIcons/" + iconKey);

        /// <summary>키 표시 이름 (키보드 배치 반영)</summary>
        public static string KeyName(UnityEngine.InputSystem.Key key)
        {
            if (key == UnityEngine.InputSystem.Key.None) return "-";
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null ? keyboard[key].displayName : key.ToString();
        }

#if UNITY_EDITOR
        public static T LoadAsset<T>(string guid) where T : Object
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
        }
#endif
    }
}
