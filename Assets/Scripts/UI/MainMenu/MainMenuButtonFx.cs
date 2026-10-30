using DG.Tweening;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 메인 메뉴 버튼 효과. 호버와 키보드/패드 선택을 같은 것으로 다룬다 (마우스가 올라오면 선택을 옮김).
    ///  - 선택: 커서음 + 글자색 바꿈 + 살짝 커짐. 있으면 왼쪽 표식(marker) 켜기·글자 오른쪽으로 밀기·밑줄(underline) 그어짐
    ///  - 클릭·Submit: 선택음 (버튼이 여는 화면(옵션 팝업 등)은 자기 소리를 내지 않으므로 여는 소리는 여기서 낸다)
    /// 팩 버튼의 TMTextColorControl 은 마우스에만 반응해 키보드 선택과 어긋나므로 빌더가 끄고 이 컴포넌트가 대신한다.
    /// 배경 반응(MenuBackdropDirector)은 선택된 버튼을 보는 MainMenuController 가 넘긴다
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class MainMenuButtonFx : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
    {
        [Header("SoundTable 키 (Ui)")]
        [SerializeField] private string hoverSoundKey = "ui_menu_cursor";
        [SerializeField] private string clickSoundKey = "ui_menu_select";

        [Header("선택 표시")]
        [Tooltip("비우면 글자색을 바꾸지 않는다")]
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color normalTextColor = new Color(0.85f, 0.95f, 1f);
        [SerializeField] private Color selectedTextColor = Color.white;
        [SerializeField] private float selectedScale = 1.04f;
        [Tooltip("선택 때 켜지는 표식 (알파 0→1). 비우면 없음")]
        [SerializeField] private Graphic marker;
        [Tooltip("선택 때 왼쪽부터 그어지는 밑줄 (가로 크기 0→1, 피벗 왼쪽). 비우면 없음")]
        [SerializeField] private RectTransform underline;
        [Tooltip("선택 때 글자를 오른쪽으로 미는 거리(px)")]
        [SerializeField] private float labelShift;
        [Tooltip("색·크기 보간 시간(초, unscaled)")]
        [SerializeField] private float transitionTime = 0.12f;

        private Button button;
        private Vector2 labelPosition;

        private void Awake()
        {
            button = GetComponent<Button>();
            if (label != null) labelPosition = label.rectTransform.anchoredPosition;
            button.onClick.AddListener(() => Play(clickSoundKey));
            Show(false, true);
        }

        private void OnDisable()
        {
            DOTween.Kill(this);
            Show(false, true);
        }

        /// <summary>마우스가 올라오면 선택도 옮겨서 키보드 선택과 호버가 한 버튼을 가리키게 (소리는 OnSelect 에서 한 번)</summary>
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
            float scale = selected ? selectedScale : 1f;
            var color = selected ? selectedTextColor : normalTextColor;
            float on = selected ? 1f : 0f;
            var labelTarget = labelPosition + new Vector2(labelShift * on, 0f);
            if (immediate || transitionTime <= 0f)
            {
                transform.localScale = Vector3.one * scale;
                if (label != null)
                {
                    label.color = color;
                    label.rectTransform.anchoredPosition = labelTarget;
                }
                if (marker != null) marker.canvasRenderer.SetAlpha(on);
                if (underline != null) underline.localScale = new Vector3(on, 1f, 1f);
                return;
            }
            transform.DOScale(scale, transitionTime).SetUpdate(true).SetTarget(this);
            if (label != null)
            {
                label.DOColor(color, transitionTime).SetUpdate(true).SetTarget(this);
                label.rectTransform.DOAnchorPos(labelTarget, transitionTime).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
            }
            if (marker != null) marker.CrossFadeAlpha(on, transitionTime, true);
            // 밑줄은 조금 늦게·길게 그어 "선택이 확정되는" 느낌
            if (underline != null) underline.DOScaleX(on, transitionTime * 2f).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
        }

        private static void Play(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }
    }
}
