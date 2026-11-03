using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Player;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 상호작용 프롬프트 (조준점 아래). Player 루트에 붙인다. InteractFocusChangedEvent(PlayerInteractor) 를 받아 표시한다.
    ///  [F] 획득 / 대상 이름 / 설명 / 경고 (예: "현재 ○○ 은(는) 버려집니다")
    /// 키 이름은 PlayerController.InputPlayerSettings.interactKey 에서 읽는다. 강화 선택·게임 오버·일시정지 때는 숨긴다
    /// 스타일은 UiTheme: 테두리 키 상자 + 붉은 키 이름, 설명은 청회색, 경고는 호박색, 뒤에 양끝이 사라지는 옅은 띠
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractPromptHud : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset numberFont;
        [Tooltip("한글 글자용 (NotoSansKR SDF)")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("조준점(화면 가운데) 기준 위치 (1920×1080 기준)")]
        [SerializeField] private Vector2 position = new Vector2(0f, -70f);
        [Tooltip("나타나고 사라지는 시간(초)")]
        [SerializeField, Min(0f)] private float fadeTime = 0.08f;

        private Canvas canvas;
        private CanvasGroup rootGroup;
        private TextMeshProUGUI keyText, actionText, titleText, descriptionText, warningText;
        private Image iconImage;
        private bool hasTarget;
        private bool stateHidden;
        private float alpha;

        private void Reset() => AssignAssets();

        private void AssignAssets()
        {
#if UNITY_EDITOR
            if (numberFont == null) numberFont = HudFactory.LoadAsset<TMP_FontAsset>(HudFactory.NumberFontGuid);
            if (textFont == null) textFont = HudFactory.LoadAsset<TMP_FontAsset>(HudFactory.TextFontGuid);
#endif
        }

        #region 유니티 메시지
        private void Awake()
        {
            AssignAssets();
            Build();
        }

        private void OnEnable()
        {
            EventBus<InteractFocusChangedEvent>.Subscribe(OnFocusChanged);
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            if (canvas != null) canvas.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            EventBus<InteractFocusChangedEvent>.Unsubscribe(OnFocusChanged);
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            hasTarget = false;
            alpha = 0f;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void Update()
        {
            float target = hasTarget && stateHidden == false ? 1f : 0f;
            alpha = fadeTime > 0f ? Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeTime) : target;
            rootGroup.alpha = alpha;
        }
        #endregion

        #region 갱신
        private void OnGameStateChanged(GameStateChangedEvent e)
            => stateHidden = e.Current == GameState.Reward || e.Current == GameState.GameOver || e.Current == GameState.Paused;

        private void OnFocusChanged(InteractFocusChangedEvent e)
        {
            hasTarget = e.HasTarget;
            if (e.HasTarget == false) return;

            InteractPrompt p = e.Prompt;
            actionText.text = string.IsNullOrEmpty(p.ActionLabel) ? "사용" : p.ActionLabel;
            titleText.text = p.Title;
            descriptionText.text = p.Description;
            descriptionText.gameObject.SetActive(string.IsNullOrEmpty(p.Description) == false);
            warningText.text = p.Warning;
            warningText.gameObject.SetActive(string.IsNullOrEmpty(p.Warning) == false);

            Sprite icon = HudFactory.WeaponIcon(p.IconKey);
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
        #endregion

        #region 화면 만들기
        private void Build()
        {
            canvas = HudFactory.CreateCanvas("InteractPromptHud", 55, false, out rootGroup);
            rootGroup.alpha = 0f;
            var root = HudFactory.Rect(canvas.transform, "Prompt", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), position, new Vector2(520f, 150f));
            var column = root.gameObject.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperCenter;
            column.spacing = 2f;
            column.childControlWidth = column.childControlHeight = false;
            column.childForceExpandWidth = column.childForceExpandHeight = false;

            // 글자 뒤 옅은 띠 (양끝이 사라짐, 줄 배치에서 빠진다)
            UiThemeKit.Band(root, UiThemeKit.BandShape.FadeBoth, 0.5f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // [F] 획득 (테두리 상자 + 붉은 키 이름)
            var actionRow = HudFactory.Rect(root, "Action", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 42f));
            keyText = UiThemeKit.KeyCap(actionRow, "KeyBox", HudFactory.KeyName(new PlayerController.InputPlayerSettings().interactKey),
                new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f));
            if (numberFont != null) keyText.font = numberFont;
            actionText = HudFactory.Label(actionRow, "Label", textFont, 24f, TextAlignmentOptions.MidlineLeft,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(180f, 40f));
            actionText.color = UiTheme.MoonWhite;

            iconImage = HudFactory.NewImage(HudFactory.Rect(root, "Icon", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 44f)), null, UiTheme.MoonWhite);
            iconImage.preserveAspect = true;
            iconImage.gameObject.SetActive(false);

            titleText = HudFactory.Label(root, "Title", textFont, 22f, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 30f));
            titleText.color = UiTheme.MoonWhite;
            descriptionText = HudFactory.Label(root, "Description", textFont, 18f, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 26f));
            descriptionText.color = UiTheme.Steel;
            warningText = HudFactory.Label(root, "Warning", textFont, 18f, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 26f));
            warningText.color = UiTheme.Ember;
            warningText.gameObject.SetActive(false);
        }
        #endregion
    }
}
