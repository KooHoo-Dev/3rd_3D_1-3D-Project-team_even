using DG.Tweening;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HybridShooter.UI.Theme
{
    /// <summary>
    /// 글자 버튼 효과 (게임 오버·보상·무기 선택 등 코드로 만드는 화면용). 모양과 동작은 메인 메뉴 버튼(MainMenuButtonFx)과 같다.
    ///  - 선택: 커서음 + 글자 Steel → MoonWhite + 왼쪽 붉은 표식 + 글자 오른쪽으로 밀림 + 붉은 밑줄 그어짐
    ///  - 호버와 키보드/패드 선택을 같은 것으로 다룬다 (마우스가 올라오면 선택을 옮김)
    /// MainMenuButtonFx 는 빌더가 직렬화 필드를 채우는 방식이라, 실행 중에 만드는 화면은 Setup 으로 채우는 이 컴포넌트를 쓴다
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class UiTextButtonFx : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
    {
        [Header("SoundTable 키 (Ui)")]
        [SerializeField] private string hoverSoundKey = "ui_menu_cursor";
        [SerializeField] private string clickSoundKey = "ui_menu_select";

        [Header("선택 표시")]
        [SerializeField] private TMP_Text label;
        [SerializeField] private Graphic marker;
        [SerializeField] private RectTransform underline;
        [SerializeField] private Color normalTextColor = new Color(0.6f, 0.68f, 0.78f);
        [SerializeField] private Color selectedTextColor = new Color(0.94f, 0.96f, 1f);
        [Tooltip("선택 때 글자를 오른쪽으로 미는 거리(px)")]
        [SerializeField] private float labelShift = 16f;
        [Tooltip("색·위치 보간 시간(초, unscaled)")]
        [SerializeField] private float transitionTime = 0.15f;

        private Button button;
        private Vector2 labelPosition;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(() => Play(clickSoundKey));
        }

        private void OnDisable()
        {
            DOTween.Kill(this);
            Show(false, true);
        }

        /// <summary>코드로 만든 부품을 연결하고 평소 모양으로 맞춘다 (UiThemeKit.TextButton 이 부른다)</summary>
        public void Setup(TMP_Text label, Graphic marker, RectTransform underline, Color normalTextColor, Color selectedTextColor)
        {
            this.label = label;
            this.marker = marker;
            this.underline = underline;
            this.normalTextColor = normalTextColor;
            this.selectedTextColor = selectedTextColor;
            if (label != null) labelPosition = label.rectTransform.anchoredPosition;
            Show(false, true);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button.IsInteractable() == false || EventSystem.current == null) return;
            if (EventSystem.current.currentSelectedGameObject == gameObject) return;
            EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (button.IsInteractable() == false) return;
            Play(hoverSoundKey);
            Show(true, false);
        }

        public void OnDeselect(BaseEventData eventData) => Show(false, false);

        private void Show(bool selected, bool immediate)
        {
            DOTween.Kill(this);
            var color = selected ? selectedTextColor : normalTextColor;
            float on = selected ? 1f : 0f;
            var labelTarget = labelPosition + new Vector2(labelShift * on, 0f);
            if (immediate || transitionTime <= 0f)
            {
                if (label != null)
                {
                    label.color = color;
                    label.rectTransform.anchoredPosition = labelTarget;
                }
                if (marker != null) marker.canvasRenderer.SetAlpha(on);
                if (underline != null) underline.localScale = new Vector3(on, 1f, 1f);
                return;
            }
            if (label != null)
            {
                label.DOColor(color, transitionTime).SetUpdate(true).SetTarget(this);
                label.rectTransform.DOAnchorPos(labelTarget, transitionTime).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
            }
            if (marker != null) marker.CrossFadeAlpha(on, transitionTime, true);
            // 밑줄은 조금 늦게·길게 그어 "선택이 확정되는" 느낌 (메인 메뉴와 같음)
            if (underline != null) underline.DOScaleX(on, transitionTime * 2f).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
        }

        private static void Play(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }
    }
}
