using System.IO;
using HybridShooter.Audio;
using HybridShooter.Audio.Editor;
using HybridShooter.Core.Data;
using HybridShooter.UI.MainMenu;
using HybridShooter.UI.MainMenu.Editor;
using HybridShooter.UI.Theme;
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
    /// 스타일은 UiTheme (UI 리디자인 시안 07): 화면을 어둡게 덮고 오른쪽 전체 높이 패널(왼쪽 끝이 흐려지는 띠)에 그린다.
    ///  - 제목: OPTIONS (Aldrich, 넓은 자간) + 붉은 선 / 탭: 글자만, 선택된 탭은 달빛 흰색 + 붉은 밑줄 (Toggle.graphic)
    ///  - 컨트롤: 슬라이더·체크 상자는 단색으로 직접 만들고, 드롭다운만 팩 Prefabs_Option/Dropdown 을 중첩 프리팹으로 넣어 색·스프라이트를 덮어쓴다
    ///    (TMP_Dropdown 의 목록 템플릿까지 새로 만들기는 번거로워서)
    ///  - 하단: 기본값 복원 · 적용 · 닫기 는 메인 메뉴식 글자 버튼(MainMenuButtonFx), 오른쪽 아래 "ESC 닫기" 는 닫기 아이콘 버튼 자리
    /// 한글은 팩 폰트(Aldrich·Play)에 없으므로 본문은 NotoSansKR SDF, 영문 제목·숫자만 Aldrich
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

        /// <summary>오른쪽 패널 폭 (화면 오른쪽 끝에 붙고 위아래로 꽉 찬다)</summary>
        private const float PanelWidth = 1180f;
        /// <summary>패널 안 글자 시작 x (왼쪽 흐려지는 구간 밖) / 오른쪽 여백</summary>
        private const float ContentLeft = 260f;
        private const float ContentRight = 120f;
        private const float RowHeight = 64f;
        private const float LabelWidth = 240f;
        /// <summary>컨트롤 시작 x (라벨 + 간격)</summary>
        private const float ControlX = LabelWidth + 20f;
        private const float SliderWidth = 400f;
        private const float ValueWidth = 120f;

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
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f); // 뒤 화면 어둡게 + 클릭 막기
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            // 오른쪽 전체 높이 패널. 왼쪽 끝 25% 만 흐려 글자 뒤는 진하다 (메뉴 화면이 왼쪽에 비쳐 보인다)
            var window = NewRect("Window", rootRect);
            Place(window.gameObject, new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(PanelWidth, 0f), new Vector2(1f, 0.5f));
            UiThemeKit.Band(window, UiThemeKit.BandShape.FadeLeft, 0.95f, 0.25f);

            var title = CreateText(window, "Title", "OPTIONS", titleFont, 52f, UiTheme.MoonWhite, TextAlignmentOptions.TopLeft);
            title.characterSpacing = 14f;
            Place(title.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ContentLeft, -84f), new Vector2(700f, 70f), new Vector2(0f, 1f));
            Line(window, "TitleLine", UiTheme.Red, new Vector2(0f, 1f), new Vector2(ContentLeft, -156f), new Vector2(140f, 3f), new Vector2(0f, 1f));

            // 탭: 글자만 + 선택 밑줄. 탭 줄 아래 가는 선
            string[] tabNames = { "사운드", "조작", "그래픽" };
            var tabGroup = window.gameObject.AddComponent<ToggleGroup>();
            tabGroup.allowSwitchOff = false;
            var tabs = new Toggle[tabNames.Length];
            var pages = new GameObject[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabs[i] = CreateTab(window, tabNames[i], new Vector2(ContentLeft + i * TabSpacing, -196f), tabGroup);
                pages[i] = CreatePage(window, tabNames[i]).gameObject;
                pages[i].SetActive(i == 0);
            }
            tabs[0].isOn = true;
            var tabLine = Line(window, "TabLine", WithAlpha(UiTheme.SteelDim, 0.7f), new Vector2(0f, 1f), new Vector2(ContentLeft, -250f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            tabLine.anchorMax = new Vector2(1f, 1f);
            tabLine.offsetMax = new Vector2(-ContentRight, tabLine.offsetMax.y);
            tabLine.SetSiblingIndex(1); // 탭 밑줄이 이 선 위에 그려지게 (0 = 띠)

            // 오른쪽 아래 "ESC 닫기" (닫기 아이콘 버튼 자리. 눌러도 닫힌다)
            var closeIcon = HintButton(window, "Btn_CloseHint", "<color=#E0574D>ESC</color>  닫기");

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

            // 하단 버튼 (메인 메뉴식 글자 버튼. 적용만 평소에도 밝게)
            var reset = BottomButton(window, "기본값 복원", ContentLeft, UiTheme.Steel);
            var apply = BottomButton(window, "적용", ContentLeft + 240f, UiTheme.MoonWhite);
            var close = BottomButton(window, "닫기", ContentLeft + 400f, UiTheme.Steel);
            LinkHorizontal(reset, apply, close);

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
            Set(so, "closeIconButton", closeIcon);
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(false); // OptionsPopup.Open() 이 켠다
        }

        /// <summary>탭 사이 간격 (왼쪽 끝 기준)</summary>
        private const float TabSpacing = 160f;

        /// <summary>
        /// 글자 탭: 투명한 클릭 영역 + 글자 + 선택 시 붉은 밑줄(Toggle.graphic).
        /// 글자 색(선택 = 달빛 흰색, 평소 = 청회색)은 OptionsPopup.SelectTab 이 바꾸고, 키보드 포커스는 글자 밝기(ColorTint)로 보인다
        /// </summary>
        private static Toggle CreateTab(RectTransform parent, string label, Vector2 position, ToggleGroup group)
        {
            var rect = NewRect("Tab_" + label, parent);
            Place(rect.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(130f, 54f), new Vector2(0f, 1f));
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var text = CreateText(rect, "Label", label, bodyFont, 26f, UiTheme.Steel, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            var on = NewRect("Selected", rect);
            Place(on.gameObject, Vector2.zero, new Vector2(0f, 0f), Vector2.zero, new Vector2(text.GetPreferredValues(label).x, 2f), Vector2.zero);
            var onImage = on.gameObject.AddComponent<Image>();
            onImage.color = UiTheme.Red;
            onImage.raycastTarget = false;

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = text;
            toggle.colors = FocusColors();
            toggle.graphic = onImage;
            toggle.group = group;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.isOn = false;
            return toggle;
        }

        /// <summary>포커스·호버 때만 원래 밝기, 평소에는 조금 어둡게 (글자·핸들에 쓰는 ColorTint)</summary>
        private static ColorBlock FocusColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.fadeDuration = 0.08f;
            return colors;
        }

        /// <summary>탭 아래 ~ 하단 버튼 위 영역. 줄은 위에서부터 쌓는다</summary>
        private static RectTransform CreatePage(RectTransform window, string label)
        {
            var page = NewRect("Page_" + label, window);
            page.anchorMin = Vector2.zero;
            page.anchorMax = Vector2.one;
            page.offsetMin = new Vector2(ContentLeft, 160f);
            page.offsetMax = new Vector2(-ContentRight, -282f);

            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
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

            var text = CreateText(row, "Label", label, bodyFont, 24f, UiTheme.Steel, TextAlignmentOptions.MidlineLeft);
            Place(text.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(LabelWidth, 0f), new Vector2(0f, 0.5f));
            return row;
        }

        /// <summary>
        /// 얇은 단색 슬라이더: 3px 바탕선 + 달빛 흰색 채움 + 3×19 핸들. 줄 높이 전체가 클릭 영역 (핸들이 가늘어도 잡기 쉽게).
        /// 이름(Background · Fill Area/Fill · Handle Slide Area/Handle)은 Unity 기본 슬라이더와 같다
        /// </summary>
        private static (Slider, TMP_Text) SliderRow(RectTransform page, string label)
        {
            var row = Row(page, label);
            var rect = NewRect("Slider", row);
            Place(rect.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ControlX, 0f), new Vector2(SliderWidth, 40f), new Vector2(0f, 0.5f));
            rect.gameObject.AddComponent<Image>().color = Color.clear; // 클릭 영역

            var track = Line(rect, "Background", WithAlpha(UiTheme.SteelDim, 0.8f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, UiTheme.ThinBarHeight), new Vector2(0f, 0.5f));
            track.anchorMax = new Vector2(1f, 0.5f);

            var fillArea = NewRect("Fill Area", rect);
            Stretch(fillArea);
            var fill = Line(fillArea, "Fill", UiTheme.MoonWhite, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, UiTheme.ThinBarHeight), new Vector2(0f, 0.5f));

            var handleArea = NewRect("Handle Slide Area", rect);
            Stretch(handleArea);
            var handle = Line(handleArea, "Handle", UiTheme.MoonWhite, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 19f), new Vector2(0.5f, 0.5f));
            handle.GetComponent<Image>().raycastTarget = true;

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.colors = FocusColors();

            var value = CreateText(row, "Value", "100%", titleFont, 24f, UiTheme.MoonWhite, TextAlignmentOptions.MidlineRight);
            Place(value.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(ControlX + SliderWidth + 20f, 0f), new Vector2(ValueWidth, 0f), new Vector2(0f, 0.5f));
            return (slider, value);
        }

        /// <summary>체크 상자: 어두운 칸 + 가는 청회색 테두리, 켜지면 안쪽 달빛 흰색 네모 (Toggle.graphic)</summary>
        private static Toggle SwitchRow(RectTransform page, string label)
        {
            var row = Row(page, label);
            var rect = NewRect("Switch", row);
            Place(rect.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ControlX, 0f), new Vector2(34f, 34f), new Vector2(0f, 0.5f));
            var background = rect.gameObject.AddComponent<Image>();
            background.color = UiTheme.Night;
            Frame(rect, WithAlpha(UiTheme.Steel, 0.8f));

            var check = Line(rect, "Checkmark", UiTheme.MoonWhite, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f), new Vector2(0.5f, 0.5f));

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.colors = FocusColors();
            toggle.graphic = check.GetComponent<Image>();
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            return toggle;
        }

        /// <summary>
        /// 팩 드롭다운(중첩 프리팹)의 겉만 바꾼다: 상자는 어두운 단색 + 가는 테두리, 화살표 청회색, 펼친 목록은 어두운 판 + 붉은 체크.
        /// 목록 템플릿·스크롤 구성은 팩 그대로 쓴다
        /// </summary>
        private static TMP_Dropdown DropdownRow(RectTransform page, string label)
        {
            var row = Row(page, label);
            var dropdown = Instantiate(Prefabs + "Prefabs_Option/Dropdown.prefab", row);
            Place(dropdown, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(ControlX, 0f), new Vector2(SliderWidth, 48f), new Vector2(0f, 0.5f));
            var component = dropdown.GetComponent<TMP_Dropdown>();

            foreach (var image in dropdown.GetComponentsInChildren<Image>(true))
            {
                string name = image.gameObject.name;
                if (image.gameObject == dropdown)
                {
                    image.sprite = null;
                    image.color = UiTheme.Night;
                }
                else if (name.Contains("Arrow")) image.color = UiTheme.Steel;
                else if (name.Contains("Checkmark")) image.color = UiTheme.Red;
                else if (name.Contains("Item Background"))
                {
                    image.sprite = null;
                    image.color = WithAlpha(UiTheme.MoonWhite, 0.06f);
                }
                else if (name.Contains("Handle")) { image.sprite = null; image.color = UiTheme.Steel; }
                else if (name.Contains("Scrollbar")) { image.sprite = null; image.color = WithAlpha(UiTheme.SteelDim, 0.6f); }
                else if (component.template != null && image.transform == component.template)
                {
                    image.sprite = null;
                    image.color = new Color(UiTheme.Night.r, UiTheme.Night.g, UiTheme.Night.b, 0.97f);
                }
            }
            component.colors = FocusColors();
            Frame((RectTransform)dropdown.transform, WithAlpha(UiTheme.Steel, 0.6f));
            if (component.template != null) Frame(component.template, WithAlpha(UiTheme.SteelDim, 0.8f));

            foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = bodyFont;
                text.fontSize = 22f;
                text.color = text.gameObject.name.Contains("Item") ? UiTheme.MoonWhite : UiTheme.Steel;
            }
            if (component.captionText != null) component.captionText.color = UiTheme.MoonWhite;
            return component;
        }

        /// <summary>조작 탭 아래쪽: 제목 + 두 줄 그리드 스크롤 목록 (KeyGuideList 가 실행 시 채움)</summary>
        private static void CreateKeyGuide(RectTransform page)
        {
            var header = CreateText(page, "KeyGuideTitle", "키 안내", bodyFont, 20f, UiTheme.Steel, TextAlignmentOptions.BottomLeft);
            var headerElement = header.gameObject.AddComponent<LayoutElement>();
            headerElement.minHeight = headerElement.preferredHeight = 40f;

            var area = NewRect("KeyGuide", page);
            var areaElement = area.gameObject.AddComponent<LayoutElement>();
            areaElement.minHeight = 120f;
            areaElement.flexibleHeight = 1f;
            area.gameObject.AddComponent<Image>().color = WithAlpha(UiTheme.Night, 0.5f);
            Line(area, "TopLine", WithAlpha(UiTheme.SteelDim, 0.7f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 1f))
                .anchorMax = Vector2.one;

            var viewport = NewRect("Viewport", area);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            // 패널 안쪽 폭(800) 에 두 칸: 12 + 372 + 32 + 372 + 12
            grid.padding = new RectOffset(12, 12, 10, 10);
            grid.cellSize = new Vector2(372f, 38f);
            grid.spacing = new Vector2(32f, 4f);
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
            so.FindProperty("fontSize").floatValue = 21f;
            so.FindProperty("labelColor").colorValue = UiTheme.Steel;
            so.FindProperty("keyColor").colorValue = UiTheme.RedText;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 하단 글자 버튼 (메인 메뉴 버튼과 같은 모양·효과: 선택하면 왼쪽 붉은 표식 + 글자 밀림 + 붉은 밑줄).
        /// 클릭음은 OptionsPopup 이 내므로 MainMenuButtonFx 의 클릭음은 끈다 (두 번 울리지 않게)
        /// </summary>
        private static Button BottomButton(RectTransform window, string label, float x, Color normalColor)
        {
            const float labelX = 34f, fontSize = 26f;
            var rect = NewRect("Btn_" + label, window);
            Place(rect.gameObject, Vector2.zero, Vector2.zero, new Vector2(x - labelX, 70f), new Vector2(220f, 56f), new Vector2(0f, 0.5f));
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var marker = Line(rect, "Marker", UiTheme.Red, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, fontSize * 0.95f), new Vector2(0f, 0.5f));
            var text = CreateText(rect, "Label", label, bodyFont, fontSize, normalColor, TextAlignmentOptions.MidlineLeft);
            Place(text.gameObject, Vector2.zero, Vector2.one, new Vector2(labelX, 0f), new Vector2(-labelX, 0f), new Vector2(0f, 0.5f));
            var underline = Line(rect, "Underline", UiTheme.Red, new Vector2(0f, 0.5f), new Vector2(labelX, -fontSize * 0.7f),
                new Vector2(text.GetPreferredValues(label).x + 20f, 2f), new Vector2(0f, 0.5f));
            underline.gameObject.AddComponent<UiFade>().Set(1f, 0f);

            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;

            var fx = rect.gameObject.AddComponent<MainMenuButtonFx>();
            var so = new SerializedObject(fx);
            Set(so, "label", text);
            Set(so, "marker", marker.GetComponent<Image>());
            Set(so, "underline", underline);
            so.FindProperty("clickSoundKey").stringValue = "";
            so.FindProperty("normalTextColor").colorValue = normalColor;
            so.FindProperty("selectedTextColor").colorValue = UiTheme.MoonWhite;
            so.FindProperty("selectedScale").floatValue = 1f;
            so.FindProperty("labelShift").floatValue = 12f;
            so.FindProperty("transitionTime").floatValue = 0.15f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        /// <summary>오른쪽 아래 키 안내 겸 닫기 버튼 ("ESC  닫기"). 눌러도 닫힌다</summary>
        private static Button HintButton(RectTransform window, string name, string text)
        {
            var rect = NewRect(name, window);
            Place(rect.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-ContentRight, 70f), new Vector2(240f, 44f), new Vector2(1f, 0.5f));
            var label = CreateText(rect, "Label", text, bodyFont, 20f, UiTheme.Steel, TextAlignmentOptions.MidlineRight);
            label.richText = true;
            label.raycastTarget = true;
            Stretch(label.rectTransform);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            button.colors = FocusColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // 키보드 이동 대상은 아님 (Esc 로 닫는다)
            return button;
        }

        /// <summary>하단 버튼 좌우 이동 (끝에서 처음으로 돈다)</summary>
        private static void LinkHorizontal(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = buttons[(i - 1 + buttons.Length) % buttons.Length],
                    selectOnRight = buttons[(i + 1) % buttons.Length],
                };
            }
        }

        /// <summary>단색 선·면 (Image, 클릭 안 받음)</summary>
        private static RectTransform Line(RectTransform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var rect = NewRect(name, parent);
            Place(rect.gameObject, anchor, anchor, position, size, pivot);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>rect 둘레에 1px 테두리 (네 변이 rect 크기를 따라 늘어난다)</summary>
        private static void Frame(RectTransform rect, Color color)
        {
            FrameSide(rect, "FrameTop", color, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, 1f));
            FrameSide(rect, "FrameBottom", color, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1f));
            FrameSide(rect, "FrameLeft", color, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1f, 0f));
            FrameSide(rect, "FrameRight", color, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(1f, 0f));
        }

        private static void FrameSide(RectTransform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            var side = NewRect(name, parent);
            Place(side.gameObject, anchorMin, anchorMax, Vector2.zero, size, pivot);
            var image = side.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
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
