using System.IO;
using HybridShooter.Audio;
using HybridShooter.Audio.Editor;
using HybridShooter.Core.Data;
using HybridShooter.UI.MainMenu.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HybridShooter.UI.Options.Editor
{
    /// <summary>
    /// 상단 메뉴 Tools/MainMenu. 옵션 팝업 프리팹과 메인 메뉴 작업 씬을 코드로 만든다 (다시 실행하면 새로 만든다).
    /// SCI-FI UI Pack V2(Cyan) 원본 프리팹은 고치지 않고 중첩 프리팹으로 넣는다.
    ///  - 창 배경: Popup_Settings 프리팹은 데모 내용(Music·Vibration·Language)이 고정 배치라 스프라이트(Popup_Settings.png, 9-slice)만 쓴다
    ///  - 탭: Toggle_PageTag 는 40px 점(페이지 표시)이라 탭으로 쓰기 어려워 Btn_SettingsMenuCyan/Yellow 스프라이트로 만든다
    ///  - 컨트롤: Prefabs_Option 의 Slider·Switch·Dropdown, 하단 버튼은 Btn_Rectangle01Yellow/Cyan, 닫기 X 는 Btn_ClosePage
    /// 한글은 팩 폰트(Aldrich·Play)에 없으므로 본문은 NotoSansKR SDF, 영문 제목만 Aldrich
    /// </summary>
    public static class OptionsPopupBuilder
    {
        private const string Pack = "Assets/ExternalAssets/SCI-FI UI Pack Pro/";
        internal const string Prefabs = Pack + "SCI-FI UI Pack_V2 Pro(Cyan)/Prefabs/";
        internal const string Textures = Pack + "SCI-FI UI Pack_V2 Pro(Cyan)/Textures/DemoScene_Cyan/";
        private const string TitleFontPath = Pack + "Common/Fonts/Aldrich-Regular SDF.asset";
        // TODO: StudySample 은 공부용 폴더 → 정식 폰트 폴더로 옮기면 경로만 바꾼다
        private const string KoreanFontPath = "Assets/StudySample/99. External Assets/Font/Noto_Sans_KR/static/NotoSansKR-Regular SDF.asset";

        public const string PopupPath = "Assets/Prefabs/UI/OptionsPopup.prefab";
        /// <summary>메인 메뉴 씬 (메인 메뉴 UI·디오라마 빌더 공용). 씬을 옮기면 여기만 바꾼다</summary>
        public const string TestScenePath = "Assets/Scenes/TestScenes/Test_MainMenu.unity";

        internal static readonly Color Cyan = new Color(0.2f, 0.9f, 1f);
        internal static readonly Color TextColor = new Color(0.85f, 0.95f, 1f);

        private static readonly Vector2 WindowSize = new Vector2(1300f, 860f);
        private const float RowHeight = 60f;
        private const float LabelWidth = 300f;
        /// <summary>컨트롤 시작 x (라벨 + 간격)</summary>
        private const float ControlX = LabelWidth + 20f;
        private const float SliderWidth = 560f;

        private static TMP_FontAsset titleFont;
        internal static TMP_FontAsset TitleFont => titleFont;
        private static TMP_FontAsset bodyFont;
        internal static TMP_FontAsset BodyFont => bodyFont;

        // ───────────────────────── 메뉴 ─────────────────────────

        [MenuItem("Tools/MainMenu/Build Options Popup")]
        public static void BuildPopupPrefab()
        {
            if (LoadFonts() == false) return;

            var root = new GameObject("OptionsPopup", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(OptionsPopup));
            try
            {
                BuildPopup(root);
                EnsureFolder(Path.GetDirectoryName(PopupPath));
                PrefabUtility.SaveAsPrefabAsset(root, PopupPath, out bool saved);
                if (saved) Debug.Log($"[OptionsPopupBuilder] {PopupPath} 를 만들었습니다");
                else Debug.LogError($"[OptionsPopupBuilder] {PopupPath} 저장 실패");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 메인 메뉴 작업 씬 (개인 작업 씬 규칙: 완성 전에는 Test_MainMenu):
        /// 카메라 + EventSystem(InputSystemUIInputModule) + SoundService(믹서, Router 없음)
        /// + 대치 장면 (MainMenuDioramaBuilder) + 메뉴 UI·옵션 팝업 (MainMenuUIBuilder)
        /// </summary>
        [MenuItem("Tools/MainMenu/Create Test Scene (Test_MainMenu)")]
        public static void CreateTestScene()
        {
            if (Application.isBatchMode == false)
            {
                if (File.Exists(TestScenePath)
                    && EditorUtility.DisplayDialog("Test_MainMenu", $"{TestScenePath} 를 새로 만듭니다. 기존 씬은 덮어씁니다.", "덮어쓰기", "취소") == false)
                    return;
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            }
            if (LoadFonts() == false) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PopupPath) == null) BuildPopupPrefab();
            var popupAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PopupPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // 메뉴 씬에는 GameSoundRouter 를 두지 않는다 (디오라마 몬스터에 이미터가 자동으로 붙지 않게)
            var sound = new GameObject("SoundService", typeof(SoundService));
            SoundServiceMenu.AssignMixer(sound.GetComponent<SoundService>());

            MainMenuDioramaBuilder.BuildInScene(camera);
            MainMenuUIBuilder.BuildInScene(popupAsset); // 디오라마 카메라(CM_Menu)를 연결하므로 디오라마 다음

            EnsureFolder(Path.GetDirectoryName(TestScenePath));
            EditorSceneManager.SaveScene(scene, TestScenePath);
            Debug.Log($"[OptionsPopupBuilder] {TestScenePath} 를 만들었습니다");
        }

        /// <summary>batchmode 용 (-executeMethod HybridShooter.UI.Options.Editor.OptionsPopupBuilder.BuildAll)</summary>
        public static void BuildAll()
        {
            BuildPopupPrefab();
            CreateTestScene();
        }

        // ───────────────────────── 팝업 ─────────────────────────

        private static void BuildPopup(GameObject root)
        {
            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f); // 뒤 화면 어둡게 + 클릭 막기
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var window = NewRect("Window", rootRect);
            Place(window.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, WindowSize);
            var windowImage = window.gameObject.AddComponent<Image>();
            windowImage.sprite = LoadSprite("PopupsAndPanels/Popup_Settings.png");
            windowImage.type = Image.Type.Sliced;

            var title = CreateText(window, "Title", "OPTIONS", titleFont, 46f, Cyan, TextAlignmentOptions.Center);
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(600f, 70f));

            var closeIcon = Instantiate(Prefabs + "Prefabs_Button/Btn_ClosePage.prefab", window);
            Place(closeIcon, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-70f, -80f), new Vector2(67f, 59f));

            // 탭
            string[] tabNames = { "사운드", "조작", "그래픽" };
            var tabGroup = window.gameObject.AddComponent<ToggleGroup>();
            tabGroup.allowSwitchOff = false;
            var tabs = new Toggle[tabNames.Length];
            var pages = new GameObject[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabs[i] = CreateTab(window, tabNames[i], new Vector2((i - 1) * 250f, -170f), tabGroup);
                pages[i] = CreatePage(window, tabNames[i]).gameObject;
                pages[i].SetActive(i == 0);
            }
            tabs[0].isOn = true;

            // 사운드
            var soundPage = (RectTransform)pages[0].transform;
            var (master, masterValue) = SliderRow(soundPage, "전체 음량");
            var (bgm, bgmValue) = SliderRow(soundPage, "배경음악");
            var (sfx, sfxValue) = SliderRow(soundPage, "효과음");
            var (monster, monsterValue) = SliderRow(soundPage, "몬스터");
            var (ui, uiValue) = SliderRow(soundPage, "UI");

            // 조작
            var controlPage = (RectTransform)pages[1].transform;
            var (firstPerson, firstPersonValue) = SliderRow(controlPage, "마우스 감도 (1인칭)");
            var (thirdPerson, thirdPersonValue) = SliderRow(controlPage, "마우스 감도 (3인칭)");
            var invertY = SwitchRow(controlPage, "Y축 반전");
            CreateKeyGuide(controlPage);

            // 그래픽
            var graphicPage = (RectTransform)pages[2].transform;
            var resolution = DropdownRow(graphicPage, "해상도");
            var screenMode = DropdownRow(graphicPage, "화면 모드");
            var quality = DropdownRow(graphicPage, "품질");
            var vSync = SwitchRow(graphicPage, "수직 동기화 (V-Sync)");
            var frameRate = DropdownRow(graphicPage, "프레임 제한");

            // 하단 버튼
            var reset = BottomButton(window, "Prefabs_Button/Btn_Rectangle01Cyan.prefab", "기본값 복원", -300f);
            var apply = BottomButton(window, "Prefabs_Button/Btn_Rectangle01Yellow.prefab", "적용", 0f);
            var close = BottomButton(window, "Prefabs_Button/Btn_Rectangle01Cyan.prefab", "닫기", 300f);

            var so = new SerializedObject(root.GetComponent<OptionsPopup>());
            Set(so, "group", group);
            SetArray(so, "tabs", tabs);
            SetArray(so, "pages", pages);
            Set(so, "masterSlider", master);
            Set(so, "masterValue", masterValue);
            var rows = so.FindProperty("volumeRows");
            var volumeRows = new (SoundCategory, Slider, TMP_Text)[]
            {
                (SoundCategory.Bgm, bgm, bgmValue), (SoundCategory.Sfx, sfx, sfxValue),
                (SoundCategory.Monster, monster, monsterValue), (SoundCategory.Ui, ui, uiValue),
            };
            rows.arraySize = volumeRows.Length;
            for (int i = 0; i < volumeRows.Length; i++)
            {
                var row = rows.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("Category").enumValueIndex = (int)volumeRows[i].Item1;
                row.FindPropertyRelative("Slider").objectReferenceValue = volumeRows[i].Item2;
                row.FindPropertyRelative("Value").objectReferenceValue = volumeRows[i].Item3;
            }
            Set(so, "firstPersonSensitivity", firstPerson);
            Set(so, "firstPersonValue", firstPersonValue);
            Set(so, "thirdPersonSensitivity", thirdPerson);
            Set(so, "thirdPersonValue", thirdPersonValue);
            Set(so, "invertY", invertY);
            Set(so, "resolution", resolution);
            Set(so, "screenMode", screenMode);
            Set(so, "quality", quality);
            Set(so, "vSync", vSync);
            Set(so, "frameRate", frameRate);
            Set(so, "applyButton", apply);
            Set(so, "resetButton", reset);
            Set(so, "closeButton", close);
            Set(so, "closeIconButton", closeIcon.GetComponent<Button>());
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(false); // OptionsPopup.Open() 이 켠다
        }

        /// <summary>시안 탭 배경 + 선택 시 노란 배경(Toggle.graphic)</summary>
        private static Toggle CreateTab(RectTransform parent, string label, Vector2 position, ToggleGroup group)
        {
            var rect = NewRect("Tab_" + label, parent);
            Place(rect.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position, new Vector2(230f, 108f));
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = LoadSprite("Buttons/Btn_SettingsMenuCyan_n.png");

            var on = NewRect("Selected", rect);
            Stretch(on);
            var onImage = on.gameObject.AddComponent<Image>();
            onImage.sprite = LoadSprite("Buttons/Btn_SettingsMenuYellow_n.png");
            onImage.raycastTarget = false;

            var text = CreateText(rect, "Label", label, bodyFont, 32f, Color.white, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = onImage;
            toggle.group = group;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.isOn = false;
            return toggle;
        }

        /// <summary>탭 아래 ~ 하단 버튼 위 영역. 줄은 위에서부터 쌓는다</summary>
        private static RectTransform CreatePage(RectTransform window, string label)
        {
            var page = NewRect("Page_" + label, window);
            page.anchorMin = Vector2.zero;
            page.anchorMax = Vector2.one;
            page.offsetMin = new Vector2(100f, 150f);
            page.offsetMax = new Vector2(-100f, -240f);

            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return page;
        }

        private static RectTransform Row(RectTransform page, string label, float height = RowHeight)
        {
            var row = NewRect(label, page);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;

            var text = CreateText(row, "Label", label, bodyFont, 30f, TextColor, TextAlignmentOptions.MidlineLeft);
            Place(text.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(LabelWidth, 0f), new Vector2(0f, 0.5f));
            return row;
        }

        private static (Slider, TMP_Text) SliderRow(RectTransform page, string label)
        {
            var row = Row(page, label);
            var slider = Instantiate(Prefabs + "Prefabs_Option/Slider.prefab", row);
            Place(slider, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ControlX, 0f), new Vector2(SliderWidth, 26f), new Vector2(0f, 0.5f));

            var value = CreateText(row, "Value", "100%", bodyFont, 30f, Cyan, TextAlignmentOptions.MidlineRight);
            Place(value.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(ControlX + SliderWidth + 20f, 0f), new Vector2(130f, 0f), new Vector2(0f, 0.5f));
            return (slider.GetComponent<Slider>(), value);
        }

        private static Toggle SwitchRow(RectTransform page, string label)
        {
            var row = Row(page, label);
            var toggle = Instantiate(Prefabs + "Prefabs_Option/Switch.prefab", row);
            Place(toggle, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ControlX, 0f), new Vector2(122f, 54f), new Vector2(0f, 0.5f));
            foreach (var legacy in toggle.GetComponentsInChildren<Text>(true)) legacy.text = ""; // 데모 문구 제거
            return toggle.GetComponent<Toggle>();
        }

        private static TMP_Dropdown DropdownRow(RectTransform page, string label)
        {
            var row = Row(page, label);
            var dropdown = Instantiate(Prefabs + "Prefabs_Option/Dropdown.prefab", row);
            Place(dropdown, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ControlX, 0f), new Vector2(460f, 56f), new Vector2(0f, 0.5f));
            foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = bodyFont;
                text.fontSize = 26f;
            }
            return dropdown.GetComponent<TMP_Dropdown>();
        }

        /// <summary>조작 탭 아래쪽: 제목 + 두 줄 그리드 스크롤 목록 (KeyGuideList 가 실행 시 채움)</summary>
        private static void CreateKeyGuide(RectTransform page)
        {
            var header = CreateText(page, "KeyGuideTitle", "키 안내", bodyFont, 26f, Cyan, TextAlignmentOptions.BottomLeft);
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = headerElement.preferredHeight = 36f;

            var area = NewRect("KeyGuide", page);
            var areaElement = area.gameObject.AddComponent<LayoutElement>();
            areaElement.minHeight = 120f;
            areaElement.flexibleHeight = 1f;
            area.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);

            var viewport = NewRect("Viewport", area);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(16, 16, 10, 10);
            grid.cellSize = new Vector2(510f, 40f);
            grid.spacing = new Vector2(36f, 4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = viewport;
            scroll.content = content;

            var list = area.gameObject.AddComponent<KeyGuideList>();
            var so = new SerializedObject(list);
            Set(so, "content", content);
            Set(so, "font", bodyFont);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button BottomButton(RectTransform window, string prefab, string label, float x)
        {
            var go = Instantiate(Prefabs + prefab, window);
            go.name = "Btn_" + label;
            Place(go, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 85f), new Vector2(250f, 90f));
            SetButtonText(go, label, bodyFont, 32f);
            return go.GetComponent<Button>();
        }

        // ───────────────────────── 공통 ─────────────────────────

        internal static bool LoadFonts()
        {
            titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
            if (titleFont != null && bodyFont != null) return true;
            Debug.LogError($"[OptionsPopupBuilder] 폰트가 없습니다: {(titleFont == null ? TitleFontPath : KoreanFontPath)}");
            return false;
        }

        internal static RectTransform CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        /// <summary>팩 프리팹을 연결된 채로(중첩 프리팹) 넣는다</summary>
        internal static GameObject Instantiate(string path, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new FileNotFoundException("[OptionsPopupBuilder] 프리팹이 없습니다", path);
            return (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        }

        internal static Sprite LoadSprite(string relativePath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Textures + relativePath);
            if (sprite == null) Debug.LogWarning($"[OptionsPopupBuilder] 스프라이트가 없습니다: {relativePath}");
            return sprite;
        }

        internal static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static TextMeshProUGUI CreateText(Transform parent, string name, string value, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        internal static void SetButtonText(GameObject button, string label, TMP_FontAsset font, float size)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text == null) return;
            text.font = font;
            text.text = label;
            text.enableAutoSizing = false;
            text.fontSize = size;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        internal static void Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        internal static void Set(SerializedObject so, string property, Object value)
        {
            var p = so.FindProperty(property);
            if (p == null) Debug.LogError($"[OptionsPopupBuilder] {so.targetObject.GetType().Name}.{property} 가 없습니다");
            else p.objectReferenceValue = value;
        }

        private static void SetArray(SerializedObject so, string property, Object[] values)
        {
            var p = so.FindProperty(property);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        internal static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
