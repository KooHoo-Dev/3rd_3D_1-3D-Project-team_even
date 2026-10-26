using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 조준 화면 HUD (PlayerAimView 가 시작할 때 만든다, 화면 맨 위 Canvas).
    ///  - 스코프: 화면 높이 92% 원 밖은 검정(가장자리로 갈수록 어두워지는 렌즈 테두리), SCI-FI UI 팩 원형 테두리,
    ///    듀플렉스 조준선(바깥 굵게·안쪽 가늘게, 가운데 비움) + 가운데 점 + 눈금, 오른쪽 아래 거리 표시
    ///  - 견착 조준선: 가운데 점, SCI-FI UI 팩 열린 원, 좌우 선, 아래로 거리 눈금 3개
    ///  - 전환 암전: 스코프로 들어가고 나올 때 잠깐 검게
    /// 색은 SCI-FI 무기·UI 팩에 맞춘 청록. 텍스처(원 마스크, 흰 점)는 실행 중에 만든다
    /// </summary>
    public sealed class AimReticleHud : MonoBehaviour
    {
        public static readonly Color Cyan = new Color(0.1f, 0.88f, 1f, 1f);
        private const float ScopeHeight = 0.92f; // 스코프 원 지름 (화면 높이 비율)
        private const float RefHeight = 1080f;

        private CanvasGroup scope;
        private CanvasGroup launcher;
        private Image flash;
        private Text range;
        private Sprite white;

        /// <summary>scopeRing: 스코프 원 테두리, launcherRing: 견착 조준선 원 (없으면 생략)</summary>
        public static AimReticleHud Create(Sprite scopeRing, Sprite launcherRing)
        {
            var go = new GameObject("AimReticleHud", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, RefHeight);
            scaler.matchWidthOrHeight = 1f; // 높이 기준 → 원 크기가 화면 높이에 맞는다
            var hud = go.AddComponent<AimReticleHud>();
            hud.Build(scopeRing, launcherRing);
            return hud;
        }

        /// <summary>scope: 스코프 화면 (0~1), launcherAlpha: 견착 조준선, flashAlpha: 전환 암전, rangeMeters: 조준점 거리 (음수 = 모름)</summary>
        public void Set(float scopeAlpha, float launcherAlpha, float flashAlpha, float rangeMeters)
        {
            scope.alpha = scopeAlpha;
            scope.gameObject.SetActive(scopeAlpha > 0f);
            launcher.alpha = launcherAlpha;
            launcher.gameObject.SetActive(launcherAlpha > 0f);
            flash.color = new Color(0f, 0f, 0f, flashAlpha);
            flash.enabled = flashAlpha > 0f;
            if (scopeAlpha > 0f) range.text = rangeMeters >= 0f ? $"RNG {rangeMeters,4:0} m" : "RNG ---- m";
        }

        private void Build(Sprite scopeRing, Sprite launcherRing)
        {
            white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            RectTransform root = (RectTransform)transform;

            // 스코프
            scope = Group(root, "Scope", stretch: true);
            float d = RefHeight * ScopeHeight;
            var mask = Square(scope.transform, "Mask", d, MaskSprite());
            mask.color = Color.black;
            // 원 사각형 바깥 네 방향은 검정 판으로 (화면 비율·해상도와 상관없이 덮이게)
            Side(scope.transform, "Left", Vector2.left, d);
            Side(scope.transform, "Right", Vector2.right, d);
            Side(scope.transform, "Top", Vector2.up, d);
            Side(scope.transform, "Bottom", Vector2.down, d);
            if (scopeRing != null)
            {
                Image ring = Square(scope.transform, "Ring", d * 1.04f, scopeRing);
                ring.color = new Color(1f, 1f, 1f, 0.85f);
            }
            float r = d * 0.5f;
            float gap = 22f, thinLength = r * 0.45f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 dir = i == 0 ? Vector2.right : i == 1 ? Vector2.left : i == 2 ? Vector2.up : Vector2.down;
                // 안쪽 가는 선 (가운데 비움) + 바깥 굵은 선
                Line(scope.transform, dir * (gap + thinLength * 0.5f), dir, thinLength, 2f, Cyan);
                float thickStart = gap + thinLength;
                Line(scope.transform, dir * ((thickStart + r) * 0.5f), dir, r - thickStart, 5f, Cyan);
            }
            Dot(scope.transform, Vector2.zero, 4f, Cyan);
            // 눈금: 가로·아래 세로 선 위 (거리 보정 표시)
            for (int i = 1; i <= 4; i++)
            {
                float step = gap + thinLength * i / 4.6f;
                Line(scope.transform, new Vector2(step, 0f), Vector2.up, i == 4 ? 14f : 8f, 2f, Cyan);
                Line(scope.transform, new Vector2(-step, 0f), Vector2.up, i == 4 ? 14f : 8f, 2f, Cyan);
                Line(scope.transform, new Vector2(0f, -step), Vector2.right, i == 4 ? 14f : 8f, 2f, Cyan);
            }
            range = Label(scope.transform, new Vector2(r * 0.42f, -r * 0.62f), 22);

            // 견착 조준선
            launcher = Group(root, "Launcher", stretch: false);
            if (launcherRing != null)
            {
                Image ring = Square(launcher.transform, "Ring", 120f, launcherRing);
                ring.color = new Color(1f, 1f, 1f, 0.9f);
            }
            Dot(launcher.transform, Vector2.zero, 5f, Cyan);
            Line(launcher.transform, new Vector2(-95f, 0f), Vector2.right, 50f, 2f, Cyan);
            Line(launcher.transform, new Vector2(95f, 0f), Vector2.right, 50f, 2f, Cyan);
            for (int i = 1; i <= 3; i++)
                Line(launcher.transform, new Vector2(0f, -40f - i * 22f), Vector2.right, 34f - i * 7f, 2f, Cyan);

            // 전환 암전
            flash = new GameObject("Flash", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            Stretch(flash.rectTransform, root);
            flash.sprite = white;
            flash.raycastTarget = false;

            Set(0f, 0f, 0f, -1f);
        }

        private static CanvasGroup Group(Transform parent, string name, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rt = (RectTransform)go.transform;
            if (stretch) Stretch(rt, parent);
            else
            {
                rt.SetParent(parent, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
            }
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            return group;
        }

        private static void Stretch(RectTransform rt, Transform parent)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private Image Square(Transform parent, string name, float size, Sprite sprite)
        {
            Image image = NewImage(parent, name, sprite);
            image.rectTransform.sizeDelta = new Vector2(size, size);
            image.preserveAspect = true;
            return image;
        }

        /// <summary>
        /// 화면 가운데 d 크기 사각형(원 마스크)의 한쪽 바깥을 화면 끝까지 덮는 검정 판.
        /// 좌우는 화면 높이 전체, 위아래는 화면 너비 전체 (겹쳐도 같은 검정이라 상관없다). 경계는 1px 겹친다
        /// </summary>
        private void Side(Transform parent, string name, Vector2 side, float d)
        {
            Image image = NewImage(parent, name, white);
            image.color = Color.black;
            RectTransform rt = image.rectTransform;
            float h = d * 0.5f - 1f;
            if (side.x != 0f)
            {
                rt.anchorMin = new Vector2(side.x < 0f ? 0f : 0.5f, 0f);
                rt.anchorMax = new Vector2(side.x < 0f ? 0.5f : 1f, 1f);
                rt.offsetMin = new Vector2(side.x < 0f ? 0f : h, 0f);
                rt.offsetMax = new Vector2(side.x < 0f ? -h : 0f, 0f);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, side.y < 0f ? 0f : 0.5f);
                rt.anchorMax = new Vector2(1f, side.y < 0f ? 0.5f : 1f);
                rt.offsetMin = new Vector2(0f, side.y < 0f ? 0f : h);
                rt.offsetMax = new Vector2(0f, side.y < 0f ? -h : 0f);
            }
        }

        private void Line(Transform parent, Vector2 center, Vector2 along, float length, float thickness, Color color)
        {
            Image image = NewImage(parent, "Line", white);
            image.color = color;
            image.rectTransform.anchoredPosition = center;
            image.rectTransform.sizeDelta = Mathf.Abs(along.x) > 0.5f ? new Vector2(length, thickness) : new Vector2(thickness, length);
        }

        private void Dot(Transform parent, Vector2 center, float size, Color color)
        {
            Image image = NewImage(parent, "Dot", white);
            image.color = color;
            image.rectTransform.anchoredPosition = center;
            image.rectTransform.sizeDelta = new Vector2(size, size);
        }

        private static Text Label(Transform parent, Vector2 position, int size)
        {
            var text = new GameObject("Range", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = new Vector2(240f, 40f);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Cyan;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private static Image NewImage(Transform parent, string name, Sprite sprite)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>가운데 원은 투명, 테두리 쪽으로 갈수록 검게 (렌즈 가장자리), 원 밖은 완전 검정</summary>
        private static Sprite MaskSprite()
        {
            const int Size = 512;
            var texture = new Texture2D(Size, Size, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp, name = "ScopeMask" };
            var pixels = new Color32[Size * Size];
            float half = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half; // 0 가운데 ~ 1 원 테두리
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, r));
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f));
        }
    }
}
