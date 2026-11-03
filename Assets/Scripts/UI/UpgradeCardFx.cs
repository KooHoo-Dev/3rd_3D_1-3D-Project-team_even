using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 강화 카드 한 장의 "지금 고른 카드" 표시 (UpgradeScreen 이 카드를 만들 때 붙이고 Setup 으로 채운다).
    ///  - 마우스가 카드 위에 올라오면 그 카드의 구매 버튼을 EventSystem 선택으로 옮긴다 → 마우스·키보드·패드 선택을 같은 것으로 다룬다
    ///  - 고른 카드: 위로 살짝 들림 + 바탕이 밝아짐 + 달빛 흰색 테두리 + 등급 윗선이 굵어짐 + 커서음
    ///  - 고른 카드가 살 수 있으면 구매 줄이 붉은 면 버튼으로 바뀐다 (비용·글자는 달빛 흰색). 살 수 없으면 카드 강조만
    /// 상태(살 수 있는지)는 UpgradeScreen.RefreshCard 가 SetBuyable 로 알려 준다. 보상 단계는 timeScale = 0 이라 unscaled 시간으로 움직인다
    /// </summary>
    public sealed class UpgradeCardFx : MonoBehaviour, IPointerEnterHandler
    {
        [Header("SoundTable 키 (Ui)")]
        [SerializeField] private string hoverSoundKey = "ui_menu_cursor";

        [Header("고른 카드 모양")]
        [Tooltip("위로 들리는 거리(px)")]
        [SerializeField] private float lift = 8f;
        [SerializeField] private float normalBackAlpha = 0.05f;
        [SerializeField] private float focusedBackAlpha = 0.11f;
        [SerializeField] private float normalTopLine = 2f;
        [SerializeField] private float focusedTopLine = 4f;
        [Tooltip("보간 시간(초, unscaled)")]
        [SerializeField] private float transitionTime = 0.12f;

        private const string BuyText = "구매 <color=#E0574D>›</color>";
        private const string BuyTextFilled = "구매  ›";

        private Button buy;
        private Image back, topLine, buyFill, buyCoin, buyUnderline;
        private CanvasGroup frame;
        private TextMeshProUGUI cost, buyLabel;
        private RectTransform rect;
        private Vector2 home;
        private bool buyable, focused;
        /// <summary>고른 정도 0~1, 붉은 구매 면 0~1</summary>
        private float focus, fill;
        /// <summary>RefreshCard 가 정한 비용·마름모 색 (붉은 면 위에서는 달빛 흰색으로 섞는다)</summary>
        private Color costColor, coinColor;

        public void Setup(Button buy, Image back, Image topLine, CanvasGroup frame, Image buyFill, Image buyCoin, Image buyUnderline,
                          TextMeshProUGUI cost, TextMeshProUGUI buyLabel)
        {
            this.buy = buy;
            this.back = back;
            this.topLine = topLine;
            this.frame = frame;
            this.buyFill = buyFill;
            this.buyCoin = buyCoin;
            this.buyUnderline = buyUnderline;
            this.cost = cost;
            this.buyLabel = buyLabel;
            rect = (RectTransform)transform;
            home = rect.anchoredPosition;
            costColor = cost.color;
            coinColor = buyCoin.color;
            focus = fill = 0f;
            Apply();
        }

        /// <summary>RefreshCard 가 카드 내용을 다시 칠한 뒤 부른다 (비용·마름모 색은 그때 칠한 값을 기준으로 삼는다)</summary>
        public void SetBuyable(bool value)
        {
            buyable = value;
            costColor = cost.color;
            coinColor = buyCoin.color;
            Apply();
        }

        private void OnDisable()
        {
            focused = false;
            focus = fill = 0f;
            if (rect != null) Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (buy == null || EventSystem.current == null) return;
            if (EventSystem.current.currentSelectedGameObject == buy.gameObject) return;
            EventSystem.current.SetSelectedGameObject(buy.gameObject);
        }

        private void LateUpdate()
        {
            if (buy == null) return;
            bool now = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == buy.gameObject;
            if (now && focused == false) Play(hoverSoundKey);
            focused = now;

            float step = transitionTime > 0f ? Time.unscaledDeltaTime / transitionTime : 1f;
            float nextFocus = Mathf.MoveTowards(focus, focused ? 1f : 0f, step);
            float nextFill = Mathf.MoveTowards(fill, focused && buyable ? 1f : 0f, step);
            if (Mathf.Approximately(nextFocus, focus) && Mathf.Approximately(nextFill, fill)) return;
            focus = nextFocus;
            fill = nextFill;
            Apply();
        }

        private void Apply()
        {
            float f = Mathf.SmoothStep(0f, 1f, focus);
            rect.anchoredPosition = home + new Vector2(0f, lift * f);
            var color = back.color;
            color.a = Mathf.Lerp(normalBackAlpha, focusedBackAlpha, f);
            back.color = color;
            frame.alpha = f;
            topLine.rectTransform.sizeDelta = new Vector2(topLine.rectTransform.sizeDelta.x, Mathf.Lerp(normalTopLine, focusedTopLine, f));

            var fillColor = buyFill.color;
            fillColor.a = fill;
            buyFill.color = fillColor;
            buyFill.enabled = fill > 0f;
            buyUnderline.canvasRenderer.SetAlpha(1f - fill);
            cost.color = Color.Lerp(costColor, UiTheme.MoonWhite, fill);
            buyCoin.color = Color.Lerp(coinColor, UiTheme.MoonWhite, fill);
            // 붉은 › 는 붉은 면 위에서 안 보이므로 면이 반 넘게 차면 흰 › 로
            buyLabel.text = fill > 0.5f ? BuyTextFilled : BuyText;
        }

        private static void Play(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }
    }
}
