using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HybridShooter.UI.Theme.Editor
{
    /// <summary>
    /// UiTheme·UiThemeKit 부품과 무기 아이콘을 한 화면에 늘어놓아 확인한다. 상단 메뉴 Tools/UI Theme/Open Preview Scene.
    /// 저장하지 않는 새 씬에 만든다 (씬 파일이 생기지 않아 다른 작업과 충돌하지 않음). Play 를 누르면 글자 버튼의 선택 효과·소리까지 확인할 수 있다
    /// </summary>
    public static class UiThemePreview
    {
        private static readonly string[] WeaponIconKeys =
            { "pistol", "assault_rifle", "rifle", "smg", "shotgun", "sniper", "rocket_launcher", "grenade_launcher" };
        private static readonly string[] WeaponNames = { "권총", "돌격소총", "라이플", "기관단총", "산탄총", "저격소총", "로켓 발사기", "유탄 발사기" };

        [MenuItem("Tools/UI Theme/Open Preview Scene")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (UiTheme.TextFont == null || UiTheme.NumberFont == null)
                Debug.LogWarning("[UiThemePreview] GameFlowUiSkin 에 글꼴이 비어 있습니다 (Resources/UI/GameFlowUiSkin)");

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.043f, 0.059f, 0.09f);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvas = CreateCanvas();
            BuildColors(canvas);
            BuildBands(canvas);
            BuildControls(canvas, eventSystem.GetComponent<EventSystem>());
            BuildWeaponIcons(canvas);

            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("[UiThemePreview] 미리보기 씬을 만들었습니다 (저장 안 됨). Game 뷰를 1920×1080 으로 보세요");
        }

        /// <summary>좌상단: 섹션 머리 + 색 견본</summary>
        private static void BuildColors(RectTransform canvas)
        {
            UiThemeKit.SectionHeader(canvas, "01  COLOR", TopLeft, TopLeft, new Vector2(120f, -80f), 400f);
            (string name, Color color)[] swatches =
            {
                ("Moon White", UiTheme.MoonWhite), ("Steel", UiTheme.Steel), ("Steel Dim", UiTheme.SteelDim),
                ("Red", UiTheme.Red), ("Ember", UiTheme.Ember), ("Rare", UiTheme.Rare),
            };
            for (int i = 0; i < swatches.Length; i++)
            {
                float y = -130f - i * 62f;
                UiThemeKit.Line(canvas, "Swatch", swatches[i].color, TopLeft, TopLeft, new Vector2(120f, y), new Vector2(48f, 48f));
                UiThemeKit.KrLabel(canvas, "SwatchName", swatches[i].name, UiTheme.CaptionSize, UiTheme.MoonWhite, TextAlignmentOptions.MidlineLeft,
                    TopLeft, TopLeft, new Vector2(186f, y), new Vector2(240f, 48f));
            }
        }

        /// <summary>위쪽 가운데·오른쪽: 띠 세 종류</summary>
        private static void BuildBands(RectTransform canvas)
        {
            UiThemeKit.SectionHeader(canvas, "02  BAND", TopLeft, TopLeft, new Vector2(560f, -80f), 400f);

            var right = Rect(canvas, "Band_FadeRight", TopLeft, TopLeft, new Vector2(560f, -130f), new Vector2(560f, 80f));
            UiThemeKit.Band(right, UiThemeKit.BandShape.FadeRight);
            UiThemeKit.Marker(right, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, 40f, 3f);
            UiThemeKit.KrLabel(right, "Text", "FadeRight · 왼쪽 HUD", UiTheme.BodySize, UiTheme.MoonWhite, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(500f, 40f));

            var left = Rect(canvas, "Band_FadeLeft", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -130f), new Vector2(600f, 80f));
            UiThemeKit.Band(left, UiThemeKit.BandShape.FadeLeft);
            UiThemeKit.KrLabel(left, "Text", "FadeLeft · 오른쪽 HUD", UiTheme.BodySize, UiTheme.MoonWhite, TextAlignmentOptions.MidlineRight,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(500f, 40f));

            var both = Rect(canvas, "Band_FadeBoth", TopLeft, TopLeft, new Vector2(560f, -240f), new Vector2(760f, 110f));
            UiThemeKit.Band(both, UiThemeKit.BandShape.FadeBoth);
            UiThemeKit.EnLabel(both, "Title", "ROUND 03 SUPPLY", 16f, UiTheme.Ember, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(600f, 26f), 8f);
            UiThemeKit.KrLabel(both, "Detail", "수류탄 +1   ·   회복 팩 +1   ·   예비 탄창 +2", 26f, UiTheme.MoonWhite, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(700f, 40f));
        }

        /// <summary>가운데: 키 상자 · 게이지 · 글자 버튼</summary>
        private static void BuildControls(RectTransform canvas, EventSystem eventSystem)
        {
            UiThemeKit.SectionHeader(canvas, "03  CONTROLS", TopLeft, TopLeft, new Vector2(560f, -400f), 400f);

            string[] keys = { "F", "ESC", "SPACE" };
            float x = 560f;
            foreach (string key in keys)
            {
                var cap = UiThemeKit.KeyCap(canvas, "Key_" + key, key, TopLeft, TopLeft, new Vector2(x, -450f));
                x += ((RectTransform)cap.rectTransform.parent).sizeDelta.x + 24f;
            }

            var hp = UiThemeKit.Gauge(canvas, "Gauge_Normal", TopLeft, TopLeft, new Vector2(560f, -520f), new Vector2(400f, UiTheme.GaugeHeight));
            hp.SetValue(0.87f);
            var critical = UiThemeKit.Gauge(canvas, "Gauge_Critical", TopLeft, TopLeft, new Vector2(560f, -550f), new Vector2(400f, UiTheme.GaugeHeight));
            critical.SetValue(0.22f);
            critical.SetColor(UiTheme.Red);
            var thin = UiThemeKit.Gauge(canvas, "Gauge_Thin", TopLeft, TopLeft, new Vector2(560f, -580f), new Vector2(400f, UiTheme.ThinBarHeight));
            thin.SetValue(0.55f);
            thin.SetColor(UiTheme.Steel);

            var retry = UiThemeKit.TextButton(canvas, "Btn_Retry", "RETRY", 46f, TopLeft, new Vector2(560f, -660f), new Vector2(560f, 84f));
            var menu = UiThemeKit.TextButton(canvas, "Btn_MainMenu", "MAIN MENU", 40f, TopLeft, new Vector2(560f, -754f), new Vector2(560f, 84f));
            retry.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = menu, selectOnDown = menu };
            menu.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = retry, selectOnDown = retry };
            eventSystem.firstSelectedGameObject = retry.gameObject;
        }

        /// <summary>오른쪽: 무기 아이콘 8종 (위 줄 Moon White = 선택, 아래 줄 Steel = 평소)</summary>
        private static void BuildWeaponIcons(RectTransform canvas)
        {
            UiThemeKit.SectionHeader(canvas, "04  WEAPON ICONS", TopLeft, TopLeft, new Vector2(1240f, -400f), 400f);
            for (int i = 0; i < WeaponIconKeys.Length; i++)
            {
                var sprite = Resources.Load<Sprite>("UI/WeaponIcons/" + WeaponIconKeys[i]);
                var position = new Vector2(1240f + (i % 2) * 300f, -450f - (i / 2) * 140f);
                var icon = Rect(canvas, "Icon_" + WeaponIconKeys[i], TopLeft, TopLeft, position, new Vector2(256f, 96f));
                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.color = i % 4 < 2 ? UiTheme.MoonWhite : UiTheme.Steel;
                UiThemeKit.KrLabel(canvas, "IconName", sprite != null ? WeaponNames[i] : WeaponNames[i] + " (아이콘 없음)", 16f, UiTheme.Steel,
                    TextAlignmentOptions.TopLeft, TopLeft, TopLeft, position + new Vector2(0f, -100f), new Vector2(256f, 24f));
            }
        }

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        private static RectTransform CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return (RectTransform)go.transform;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }
    }
}
