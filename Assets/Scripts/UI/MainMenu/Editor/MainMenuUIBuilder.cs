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
    ///  - 버튼: 셋 다 Btn_MainMenu (GAME START 만 Yellow 프리팹). 시안 버튼은 선택(호버) 스프라이트를 노란색으로 바꿔 "기본 Cyan, 선택 Yellow"
    ///  - 버튼 Navigation 은 위아래 명시 (Automatic 은 화면의 다른 Selectable 로 튈 수 있음)
    /// 배치 수치는 아래 상수에서 조정
    /// </summary>
    public static class MainMenuUIBuilder
    {
        private const string LogoText = "TEAM EVEN";
        private const string HintText = "[Enter] 선택    [Esc] 뒤로";

        private const float MarginX = 120f;
        private static readonly Vector2 ButtonSize = new Vector2(487f, 107f); // 원본 609×134 의 80%
        private const float ButtonSpacing = 24f;
        /// <summary>버튼 묶음 중심 높이 (화면 중앙 기준, 로고와 겹치지 않게 조금 아래)</summary>
        private const float ButtonsCenterY = -20f;
        private const float GradientWidth = 900f;
        private const float GradientAlpha = 0.75f;

        private const string YellowSelectedSprite = "Buttons/Btn_MainMenuYellow_p.png";

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

            var logo = CreateText(canvas, "Logo", LogoText, TitleFont, 56f, Cyan, TextAlignmentOptions.TopLeft);
            Place(logo.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(MarginX, -80f), new Vector2(600f, 80f), new Vector2(0f, 1f));

            // PRESS ANY KEY
            var pressAnyKey = CreateText(canvas, "PressAnyKey", "PRESS ANY KEY", TitleFont, 40f, Cyan, TextAlignmentOptions.Center);
            Place(pressAnyKey.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(800f, 60f));
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
            gradientImage.color = new Color(0f, 0f, 0f, GradientAlpha);
            gradientImage.raycastTarget = false;
            gradient.gameObject.AddComponent<HorizontalFade>();

            var gameStart = CreateButton(menu, "Btn_GameStart", "GAME START", "Btn_MainMenuYellow.prefab", 0, false);
            var options = CreateButton(menu, "Btn_Options", "OPTIONS", "Btn_MainMenuCyan.prefab", 1, true);
            var quit = CreateButton(menu, "Btn_Quit", "QUIT", "Btn_MainMenuCyan.prefab", 2, true);
            LinkVertical(gameStart, options, quit);

            var version = CreateText(menu, "Version", "v0.0.0", TitleFont, 24f, new Color(TextColor.r, TextColor.g, TextColor.b, 0.6f), TextAlignmentOptions.BottomLeft);
            Place(version.gameObject, Vector2.zero, Vector2.zero, new Vector2(MarginX, 40f), new Vector2(400f, 40f), Vector2.zero);

            var hint = CreateText(menu, "Hint", HintText, BodyFont, 24f, new Color(TextColor.r, TextColor.g, TextColor.b, 0.7f), TextAlignmentOptions.BottomRight);
            Place(hint.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 40f), new Vector2(600f, 40f), new Vector2(1f, 0f));

            var popup = (GameObject)PrefabUtility.InstantiatePrefab(popupAsset, canvas);

            var controller = canvas.gameObject.AddComponent<MainMenuController>();
            var so = new SerializedObject(controller);
            Set(so, "pressAnyKeyGroup", pressAnyKeyGroup);
            Set(so, "menuGroup", menuGroup);
            Set(so, "gameStartButton", gameStart);
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

        /// <summary>좌측 세로 버튼 (위에서 index 번째). yellowWhenSelected = 선택 스프라이트를 노란색으로</summary>
        private static Button CreateButton(RectTransform parent, string name, string label, string prefab, int index, bool yellowWhenSelected)
        {
            var go = OptionsPopupBuilder.Instantiate(Prefabs + "Prefabs_Button/" + prefab, parent);
            go.name = name;
            float y = ButtonsCenterY + (1 - index) * (ButtonSize.y + ButtonSpacing); // 3개 중 가운데가 중심
            Place(go, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(MarginX, y), ButtonSize, new Vector2(0f, 0.5f));
            SetButtonText(go, label, TitleFont, 40f);

            var button = go.GetComponent<Button>();
            if (yellowWhenSelected)
            {
                var yellow = LoadSprite(YellowSelectedSprite);
                var sprites = button.spriteState;
                sprites.highlightedSprite = yellow;
                sprites.selectedSprite = yellow;
                sprites.pressedSprite = yellow;
                button.spriteState = sprites;
            }

            // 팩의 마우스 전용 글자색 스크립트는 끄고 MainMenuButtonFx 가 선택(마우스·키보드 공통)에 맞춰 바꾼다
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour.GetType().Name == "TMTextColorControl") behaviour.enabled = false;

            var fx = go.AddComponent<MainMenuButtonFx>();
            var so = new SerializedObject(fx);
            Set(so, "label", go.GetComponentInChildren<TMP_Text>(true));
            so.FindProperty("normalTextColor").colorValue = TextColor;
            so.ApplyModifiedPropertiesWithoutUndo();
            return button;
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
