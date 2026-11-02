using System.IO;
using HybridShooter.UI.Options;
using HybridShooter.UI.Options.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static HybridShooter.UI.Options.Editor.OptionsPopupBuilder;

namespace HybridShooter.UI.MainMenu.Editor
{
    /// <summary>
    /// 메인 메뉴 UI 생성. 상단 메뉴 Tools/MainMenu/Rebuild Menu UI (Test_MainMenu 의 Canvas 만 다시 만든다).
    /// Canvas(1920×1080, Match 0.5) 순서: 로고(좌상단, 항상) → PRESS ANY KEY(하단 중앙) → Menu(그라데이션·버튼·버전·조작 힌트) → 옵션 팝업
    /// 디자인: 외계 행성의 밤(차가운 달빛·청회색 안개, 붉은 눈동자 같은 달)에 맞춘 차가운 청회색 + 붉은 포인트
    ///  - 버튼: 팩 버튼 상자 대신 글자 중심의 얇은 메뉴. 평소 청회색 글자, 선택하면 달빛 흰색 + 왼쪽 붉은 표식 + 오른쪽으로 밀림 + 붉은 밑줄 (MainMenuButtonFx)
    ///  - 로고: 넓은 자간의 달빛 흰색 + 붉은 선, PRESS ANY KEY: 넓은 자간 + 양옆 가는 선, 조작 힌트의 키 이름만 붉게
    ///  - 버튼 Navigation 은 위아래 명시 (Automatic 은 화면의 다른 Selectable 로 튈 수 있음)
    /// 배치 수치는 아래 상수에서 조정
    /// </summary>
    public static class MainMenuUIBuilder
    {
        private const string LogoText = "TEAM EVEN";
        private const string HintText = "<color=#E0574D>ENTER</color>  선택      <color=#E0574D>ESC</color>  뒤로";

        /// <summary>달빛 흰색 (로고·선택된 글자)</summary>
        private static readonly Color MoonWhite = new Color(0.94f, 0.96f, 1f);
        /// <summary>차가운 청회색 (평소 글자·부가 정보)</summary>
        private static readonly Color Steel = new Color(0.6f, 0.68f, 0.78f);
        /// <summary>달 눈동자의 붉은색 (선택 표식·밑줄·강조)</summary>
        private static readonly Color Accent = new Color(0.85f, 0.26f, 0.23f);

        private const float MarginX = 120f;
        private static readonly Vector2 ButtonSize = new Vector2(560f, 84f);
        private const float ButtonSpacing = 10f;
        /// <summary>버튼 묶음 중심 높이 (화면 중앙 기준, 로고와 겹치지 않게 조금 아래)</summary>
        private const float ButtonsCenterY = -40f;
        /// <summary>글자 시작 x (왼쪽 붉은 표식 자리)</summary>
        private const float LabelX = 34f;
        private const float GradientWidth = 1000f;
        private static readonly Color GradientColor = new Color(0.01f, 0.015f, 0.03f, 0.85f);

        [MenuItem("Tools/MainMenu/Rebuild Menu UI")]
        public static void Rebuild()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            if (File.Exists(TestScenePath) == false)
            {
                CreateTestScene(); // 씬 생성 중에 메뉴 UI 도 만든다
                return;
            }
            if (LoadFonts() == false) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PopupPath) == null) BuildPopupPrefab();

            var scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
            BuildInScene(AssetDatabase.LoadAssetAtPath<GameObject>(PopupPath));
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MainMenuUIBuilder] 메뉴 UI 를 다시 만들었습니다");
        }

        /// <summary>batchmode 용</summary>
        public static void RebuildBatch() => Rebuild();

        /// <summary>열린 씬에 Canvas 를 (있으면 지우고) 만든다. 폰트는 LoadFonts 로 읽어 둔 상태여야 한다</summary>
        public static void BuildInScene(GameObject popupAsset)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Canvas") Object.DestroyImmediate(root);

            var canvas = CreateCanvas("Canvas");

            // 로고: 넓은 자간 + 아래 붉은 선
            var logo = CreateText(canvas, "Logo", LogoText, TitleFont, 72f, MoonWhite, TextAlignmentOptions.TopLeft);
            logo.characterSpacing = 18f;
            Place(logo.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(MarginX, -80f), new Vector2(900f, 90f), new Vector2(0f, 1f));
            Line(logo.rectTransform, "Line", Accent, new Vector2(0f, 0f), new Vector2(0f, -6f), new Vector2(140f, 3f), new Vector2(0f, 1f));

            // PRESS ANY KEY: 넓은 자간 + 양옆 가는 선 (같이 깜빡인다)
            var pressAnyKey = CreateText(canvas, "PressAnyKey", "PRESS ANY KEY", TitleFont, 30f, Steel, TextAlignmentOptions.Center);
            pressAnyKey.characterSpacing = 24f;
            Place(pressAnyKey.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(420f, 50f)); // 글자 폭에 맞춰야 양옆 선이 붙어 보인다
            Line(pressAnyKey.rectTransform, "LineLeft", Steel, new Vector2(0f, 0.5f), new Vector2(-20f, 0f), new Vector2(140f, 1.5f), new Vector2(1f, 0.5f));
            Line(pressAnyKey.rectTransform, "LineRight", Steel, new Vector2(1f, 0.5f), new Vector2(20f, 0f), new Vector2(140f, 1.5f), new Vector2(0f, 0.5f));
            var pressAnyKeyGroup = pressAnyKey.gameObject.AddComponent<CanvasGroup>();
            pressAnyKeyGroup.interactable = false;
            pressAnyKeyGroup.blocksRaycasts = false;

            // 메뉴
            var menu = NewRect("Menu", canvas);
            Stretch(menu);
            var menuGroup = menu.gameObject.AddComponent<CanvasGroup>();

            var gradient = NewRect("Gradient", menu);
            Place(gradient.gameObject, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(GradientWidth, 0f), new Vector2(0f, 0.5f));
            var gradientImage = gradient.gameObject.AddComponent<Image>();
            gradientImage.color = GradientColor;
            gradientImage.raycastTarget = false;
            gradient.gameObject.AddComponent<HorizontalFade>();

            var gameStart = CreateButton(menu, "Btn_GameStart", "GAME START", 0, 50f, MoonWhite);
            var tutorial = CreateButton(menu, "Btn_Tutorial", "TUTORIAL", 1, 40f, Steel);
            var options = CreateButton(menu, "Btn_Options", "OPTIONS", 2, 40f, Steel);
            var quit = CreateButton(menu, "Btn_Quit", "QUIT", 3, 40f, Steel);
            LinkVertical(gameStart, tutorial, options, quit);

            // 화면 모서리는 비네트로 어두워지므로 부가 정보도 너무 옅지 않게
            var version = CreateText(menu, "Version", "v0.0.0", TitleFont, 20f, new Color(Steel.r, Steel.g, Steel.b, 0.8f), TextAlignmentOptions.BottomLeft);
            version.characterSpacing = 6f;
            Place(version.gameObject, Vector2.zero, Vector2.zero, new Vector2(MarginX, 40f), new Vector2(400f, 40f), Vector2.zero);

            var hint = CreateText(menu, "Hint", HintText, BodyFont, 22f, Steel, TextAlignmentOptions.BottomRight);
            hint.richText = true;
            Place(hint.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 40f), new Vector2(700f, 40f), new Vector2(1f, 0f));

            // 메뉴의 어두운 그라데이션이 로고를 덮지 않게 맨 뒤로 (로고·PRESS ANY KEY 는 버튼과 겹치지 않는다)
            menu.SetAsFirstSibling();

            var popup = (GameObject)PrefabUtility.InstantiatePrefab(popupAsset, canvas);

            var controller = canvas.gameObject.AddComponent<MainMenuController>();
            var so = new SerializedObject(controller);
            Set(so, "pressAnyKeyGroup", pressAnyKeyGroup);
            Set(so, "menuGroup", menuGroup);
            Set(so, "gameStartButton", gameStart);
            Set(so, "tutorialButton", tutorial);
            Set(so, "optionsButton", options);
            Set(so, "quitButton", quit);
            Set(so, "optionsPopup", popup.GetComponent<OptionsPopup>());
            Set(so, "versionText", version);
            Set(so, "menuCamera", Object.FindAnyObjectByType<MenuCameraSway>());
            Set(so, "backdrop", Object.FindAnyObjectByType<MenuBackdropDirector>());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>디오라마를 다시 만들어 CM_Menu·배경 연출이 바뀌었을 때 메뉴 컨트롤러에 다시 연결</summary>
        public static void LinkDiorama(MenuCameraSway menuCamera, MenuBackdropDirector backdrop)
        {
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            if (controller == null) return;
            var so = new SerializedObject(controller);
            Set(so, "menuCamera", menuCamera);
            Set(so, "backdrop", backdrop);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 좌측 세로 버튼 (위에서 index 번째). 투명한 판(클릭 영역) + 글자 + 왼쪽 붉은 표식 + 붉은 밑줄.
        /// 색·위치 변화는 Button Transition 대신 MainMenuButtonFx 가 보간한다 (마우스·키보드 공통)
        /// </summary>
        private static Button CreateButton(RectTransform parent, string name, string label, int index, float fontSize, Color normalColor)
        {
            var rect = NewRect(name, parent);
            float y = ButtonsCenterY + (1.5f - index) * (ButtonSize.y + ButtonSpacing); // 4개의 가운데가 중심
            Place(rect.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(MarginX, y), ButtonSize, new Vector2(0f, 0.5f));
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear; // 보이지 않는 클릭 영역

            var marker = Line(rect, "Marker", Accent, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, fontSize * 0.95f), new Vector2(0f, 0.5f));

            var text = CreateText(rect, "Label", label, TitleFont, fontSize, normalColor, TextAlignmentOptions.MidlineLeft);
            text.characterSpacing = 10f;
            Place(text.gameObject, Vector2.zero, new Vector2(1f, 1f), new Vector2(LabelX, 0f), new Vector2(-LabelX, 0f), new Vector2(0f, 0.5f));

            var underline = Line(rect, "Underline", Accent, new Vector2(0f, 0.5f), new Vector2(LabelX, -fontSize * 0.62f), new Vector2(380f, 2f), new Vector2(0f, 0.5f));
            underline.gameObject.AddComponent<HorizontalFade>(); // 오른쪽으로 갈수록 사라지는 선

            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;

            var fx = rect.gameObject.AddComponent<MainMenuButtonFx>();
            var so = new SerializedObject(fx);
            Set(so, "label", text);
            Set(so, "marker", marker.GetComponent<Image>());
            Set(so, "underline", underline);
            so.FindProperty("normalTextColor").colorValue = normalColor;
            so.FindProperty("selectedTextColor").colorValue = MoonWhite;
            so.FindProperty("selectedScale").floatValue = 1f;
            so.FindProperty("labelShift").floatValue = 16f;
            so.FindProperty("transitionTime").floatValue = 0.15f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        /// <summary>단색 선·막대 (Image, 클릭 안 받음)</summary>
        private static RectTransform Line(RectTransform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var rect = NewRect(name, parent);
            Place(rect.gameObject, anchor, anchor, position, size, pivot);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static void LinkVertical(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i - 1 + buttons.Length) % buttons.Length], // 끝에서 처음으로 돈다
                    selectOnDown = buttons[(i + 1) % buttons.Length],
                };
            }
        }
    }
}
