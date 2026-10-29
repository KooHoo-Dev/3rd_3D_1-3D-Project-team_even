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
    ///  - 선택: 커서음 + 글자색 바꿈 + 살짝 커짐 (배경 노란색은 Button 의 Sprite Swap 이 맡는다)
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
        [Tooltip("색·크기 보간 시간(초, unscaled)")]
        [SerializeField] private float transitionTime = 0.12f;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
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
            if (immediate || transitionTime <= 0f)
            {
                transform.localScale = Vector3.one * scale;
                if (label != null) label.color = color;
                return;
            }
            transform.DOScale(scale, transitionTime).SetUpdate(true).SetTarget(this);
            if (label != null) label.DOColor(color, transitionTime).SetUpdate(true).SetTarget(this);
        }

        private static void Play(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }
    }
}
