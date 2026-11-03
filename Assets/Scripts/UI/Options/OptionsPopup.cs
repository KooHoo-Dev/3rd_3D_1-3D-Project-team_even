using System;
using System.Collections.Generic;
using DG.Tweening;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Services;
using HybridShooter.Core.Settings;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HybridShooter.UI.Options
{
    /// <summary>
    /// 옵션 팝업 (사운드 / 조작 / 그래픽 탭). 메인 메뉴·일시정지 메뉴 어디서든 Open() 으로 연다.
    /// 레이아웃은 Tools > MainMenu > Build Options Popup 이 만든 프리팹(Assets/Prefabs/UI/OptionsPopup.prefab)에 있고, 이 컴포넌트는 값 연결만 한다.
    ///
    /// 열 때 GameSettings.Current 의 사본을 만들어 고친다.
    ///  - 사운드: 슬라이더를 움직이면 바로 미리듣기 (사운드 서비스에 사본 값을 넣음)
    ///  - [적용]: 사본을 저장(GameSettings.Save) + 그래픽 적용
    ///  - [닫기]·Esc: 적용하지 않은 변경은 버리고 사운드도 저장값으로 되돌린다
    ///  - [기본값 복원]: 사본만 기본값으로 (적용해야 저장)
    /// </summary>
    public sealed class OptionsPopup : MonoBehaviour
    {
        /// <summary>카테고리 볼륨 슬라이더 한 줄</summary>
        [Serializable]
        private sealed class VolumeRow
        {
            public SoundCategory Category;
            public Slider Slider;
            public TMP_Text Value;
        }

        [Header("창")]
        [Tooltip("페이드용. 비우면 페이드 없이 켜고 끈다")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("열기·닫기 페이드 시간(초, unscaled)")]
        [SerializeField] private float fadeTime = 0.15f;
        [Tooltip("Esc / 패드 B (UI Cancel) 로 닫기")]
        [SerializeField] private bool closeOnCancel = true;

        [Header("탭 (같은 순서로 페이지)")]
        [SerializeField] private Toggle[] tabs;
        [SerializeField] private GameObject[] pages;

        [Header("사운드")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private TMP_Text masterValue;
        [SerializeField] private VolumeRow[] volumeRows;

        [Header("조작")]
        [SerializeField] private Slider firstPersonSensitivity;
        [SerializeField] private TMP_Text firstPersonValue;
        [SerializeField] private Slider thirdPersonSensitivity;
        [SerializeField] private TMP_Text thirdPersonValue;
        [SerializeField] private Toggle invertY;

        [Header("그래픽")]
        [SerializeField] private TMP_Dropdown resolution;
        [SerializeField] private TMP_Dropdown screenMode;
        [SerializeField] private TMP_Dropdown quality;
        [SerializeField] private Toggle vSync;
        [SerializeField] private TMP_Dropdown frameRate;

        [Header("하단 버튼")]
        [SerializeField] private Button applyButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button closeButton;
        [Tooltip("우상단 X (선택)")]
        [SerializeField] private Button closeIconButton;

        [Header("SoundTable 키 (Ui)")]
        [SerializeField] private string clickSoundKey = "ui_menu_select";
        [SerializeField] private string tabSoundKey = "ui_menu_cursor";

        /// <summary>화면 모드 드롭다운 순서</summary>
        private static readonly FullScreenMode[] ScreenModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        private static readonly string[] ScreenModeNames = { "전체 화면", "테두리 없는 창", "창 모드" };
        /// <summary>프레임 제한 드롭다운 순서 (0 = 무제한)</summary>
        private static readonly int[] FrameRates = { 60, 120, 144, 0 };

        private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
        private GameSettings pending;
        private bool wired;
        private bool refreshing;

        public bool IsOpen { get; private set; }
        /// <summary>마지막으로 닫힌 프레임. 같은 Esc 로 뒤 화면(종료 팝업 등)이 열리지 않게 여는 쪽에서 확인</summary>
        public int LastClosedFrame { get; private set; } = -1;
        public event Action Closed;

        // ───────────────────────── 열기·닫기 ─────────────────────────

        public void Open()
        {
            if (IsOpen) return;
            Wire();
            IsOpen = true;
            gameObject.SetActive(true);

            pending = GameSettings.Current.Clone();
            BuildGraphicOptions();
            Refresh();
            SelectTab(0);
            if (tabs != null && tabs.Length > 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(tabs[0].gameObject);

            Fade(1f, null);
        }

        /// <summary>적용하지 않은 변경은 버리고 닫는다</summary>
        public void Close()
        {
            if (IsOpen == false) return;
            IsOpen = false;
            LastClosedFrame = Time.frameCount;
            PreviewSound(GameSettings.Current);
            Fade(0f, () => gameObject.SetActive(false));
            Closed?.Invoke();
        }

        private void Fade(float to, Action done)
        {
            DOTween.Kill(this);
            if (group == null || fadeTime <= 0f)
            {
                if (group != null) group.alpha = to;
                done?.Invoke();
                return;
            }
            group.interactable = to > 0f;
            group.blocksRaycasts = to > 0f;
            DOTween.To(() => group.alpha, a => group.alpha = a, to, fadeTime)
                .SetUpdate(true) // 일시정지(timeScale 0)에서도
                .SetTarget(this)
                .OnComplete(() => done?.Invoke());
        }

        private void OnDisable() => DOTween.Kill(this);

        /// <summary>Esc / 패드 B. 드롭다운 목록이 펼쳐져 있으면 그 목록만 닫히도록 무시</summary>
        private void Update()
        {
            if (IsOpen == false || closeOnCancel == false || CancelPressed() == false) return;
            if (IsExpanded(resolution) || IsExpanded(screenMode) || IsExpanded(quality) || IsExpanded(frameRate)) return;
            PlayUi(clickSoundKey);
            Close();
        }

        private static bool IsExpanded(TMP_Dropdown dropdown) => dropdown != null && dropdown.IsExpanded;

        /// <summary>EventSystem 의 UI Cancel 액션 (키보드 Esc·패드 B). 모듈이 없으면 Esc 키. 메인 메뉴도 같은 판정을 쓴다</summary>
        public static bool CancelPressed()
        {
            if (EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule module
                && module.cancel != null && module.cancel.action != null)
                return module.cancel.action.WasPressedThisFrame();
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        }

        // ───────────────────────── 연결 ─────────────────────────

        /// <summary>컨트롤 범위·리스너 연결 (처음 열 때 한 번)</summary>
        private void Wire()
        {
            if (wired) return;
            wired = true;

            for (int i = 0; tabs != null && i < tabs.Length; i++)
            {
                int index = i;
                tabs[i].onValueChanged.AddListener(on =>
                {
                    if (on == false || refreshing) return;
                    PlayUi(tabSoundKey);
                    SelectTab(index);
                });
            }

            SetupSlider(masterSlider, 0f, 1f, v => { pending.MasterVolume = v; OnVolumeChanged(); });
            foreach (var row in volumeRows)
            {
                var category = row.Category;
                SetupSlider(row.Slider, 0f, 1f, v => { pending.SetVolume(category, v); OnVolumeChanged(); });
            }

            SetupSlider(firstPersonSensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, v => { pending.FirstPersonSensitivity = v; RefreshLabels(); });
            SetupSlider(thirdPersonSensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity, v => { pending.ThirdPersonSensitivity = v; RefreshLabels(); });
            SetupToggle(invertY, v => pending.InvertY = v);

            SetupDropdown(resolution, i =>
            {
                if (i < 0 || i >= resolutions.Count) return;
                pending.ResolutionWidth = resolutions[i].x;
                pending.ResolutionHeight = resolutions[i].y;
            });
            SetupDropdown(screenMode, i => pending.ScreenMode = ScreenModes[Mathf.Clamp(i, 0, ScreenModes.Length - 1)]);
            SetupDropdown(quality, i => pending.QualityLevel = i);
            SetupToggle(vSync, v => pending.VSync = v);
            SetupDropdown(frameRate, i => pending.FrameRateLimit = FrameRates[Mathf.Clamp(i, 0, FrameRates.Length - 1)]);

            AddClick(applyButton, Apply);
            AddClick(resetButton, ResetToDefault);
            AddClick(closeButton, Close);
            AddClick(closeIconButton, Close);
        }

        private void SetupSlider(Slider slider, float min, float max, Action<float> changed)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(v => { if (refreshing == false) changed(v); });
        }

        private void SetupToggle(Toggle toggle, Action<bool> changed)
        {
            if (toggle == null) return;
            toggle.onValueChanged.AddListener(v =>
            {
                if (refreshing) return;
                PlayUi(tabSoundKey);
                changed(v);
            });
        }

        private void SetupDropdown(TMP_Dropdown dropdown, Action<int> changed)
        {
            if (dropdown == null) return;
            dropdown.onValueChanged.AddListener(i =>
            {
                if (refreshing) return;
                PlayUi(tabSoundKey);
                changed(i);
            });
        }

        private void AddClick(Button button, Action click)
        {
            if (button == null) return;
            button.onClick.AddListener(() =>
            {
                PlayUi(clickSoundKey);
                click();
            });
        }

        // ───────────────────────── 동작 ─────────────────────────

        private void Apply()
        {
            GameSettings.Save(pending);
            pending.ApplyGraphics();
            pending = GameSettings.Current.Clone();
            Refresh();
        }

        private void ResetToDefault()
        {
            pending = GameSettings.CreateDefault();
            Refresh();
            PreviewSound(pending);
        }

        private void OnVolumeChanged()
        {
            RefreshLabels();
            PreviewSound(pending);
        }

        private static void PreviewSound(GameSettings settings)
        {
            if (ServiceLocator.TryGet<ISoundService>(out var sound)) settings.ApplySound(sound);
        }

        private void SelectTab(int index)
        {
            refreshing = true;
            for (int i = 0; tabs != null && i < tabs.Length; i++)
            {
                tabs[i].SetIsOnWithoutNotify(i == index);
                // 글자 탭(빌더가 글자를 targetGraphic 으로 둔 경우): 선택된 탭만 달빛 흰색
                if (tabs[i].targetGraphic is TMP_Text label) label.color = i == index ? UiTheme.MoonWhite : UiTheme.Steel;
            }
            refreshing = false;
            for (int i = 0; pages != null && i < pages.Length; i++)
                if (pages[i] != null) pages[i].SetActive(i == index);
        }

        private void PlayUi(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }

        // ───────────────────────── 표시 ─────────────────────────

        /// <summary>해상도(주사율 무시, 중복 제거)·화면 모드·품질·프레임 제한 목록</summary>
        private void BuildGraphicOptions()
        {
            resolutions.Clear();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (resolutions.Contains(size) == false) resolutions.Add(size);
            }
            var now = new Vector2Int(Screen.width, Screen.height);
            if (resolutions.Contains(now) == false) resolutions.Add(now); // 에디터 Game 뷰·창 모드 크기
            resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            var labels = new List<string>();
            foreach (var r in resolutions) labels.Add($"{r.x} × {r.y}");
            SetOptions(resolution, labels);
            SetOptions(screenMode, new List<string>(ScreenModeNames));
            SetOptions(quality, new List<string>(QualitySettings.names));
            var rates = new List<string>();
            foreach (int rate in FrameRates) rates.Add(rate > 0 ? $"{rate} FPS" : "무제한");
            SetOptions(frameRate, rates);
        }

        private static void SetOptions(TMP_Dropdown dropdown, List<string> labels)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
        }

        /// <summary>pending 값을 컨트롤에 (알림 없이)</summary>
        private void Refresh()
        {
            refreshing = true;

            SetSlider(masterSlider, pending.MasterVolume);
            foreach (var row in volumeRows) SetSlider(row.Slider, pending.GetVolume(row.Category));
            SetSlider(firstPersonSensitivity, pending.FirstPersonSensitivity);
            SetSlider(thirdPersonSensitivity, pending.ThirdPersonSensitivity);
            SetToggle(invertY, pending.InvertY);

            var size = pending.ResolutionWidth > 0
                ? new Vector2Int(pending.ResolutionWidth, pending.ResolutionHeight)
                : new Vector2Int(Screen.width, Screen.height);
            SetDropdown(resolution, resolutions.IndexOf(size));
            SetDropdown(screenMode, Array.IndexOf(ScreenModes, pending.ScreenMode));
            SetDropdown(quality, pending.QualityLevel);
            SetToggle(vSync, pending.VSync);
            SetDropdown(frameRate, Array.IndexOf(FrameRates, pending.FrameRateLimit));

            refreshing = false;
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            SetText(masterValue, Percent(pending.MasterVolume));
            foreach (var row in volumeRows) SetText(row.Value, Percent(pending.GetVolume(row.Category)));
            SetText(firstPersonValue, $"x{pending.FirstPersonSensitivity:0.00}");
            SetText(thirdPersonValue, $"x{pending.ThirdPersonSensitivity:0.00}");
        }

        private static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private static void SetSlider(Slider slider, float value)
        {
            if (slider != null) slider.SetValueWithoutNotify(value);
        }

        private static void SetToggle(Toggle toggle, bool value)
        {
            if (toggle != null) toggle.SetIsOnWithoutNotify(value);
        }

        /// <summary>목록에 없는 값(-1 등)은 첫 항목</summary>
        private static void SetDropdown(TMP_Dropdown dropdown, int index)
        {
            if (dropdown != null) dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, Mathf.Max(0, dropdown.options.Count - 1)));
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
