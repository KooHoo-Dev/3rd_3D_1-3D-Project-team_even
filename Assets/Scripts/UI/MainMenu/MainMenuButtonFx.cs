using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 메인 메뉴 버튼 효과. 지금은 소리만: 호버·키보드/패드 선택 = 커서음, 클릭·Submit = 선택음 (Ui 카테고리).
    /// 버튼이 여는 화면(옵션 팝업 등)은 자기 소리를 내지 않으므로 여는 소리는 여기서 낸다.
    /// TODO(4·5단계): 호버 색·스케일, 배경 반응 이벤트 발행
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class MainMenuButtonFx : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        [Header("SoundTable 키 (Ui)")]
        [SerializeField] private string hoverSoundKey = "ui_menu_cursor";
        [SerializeField] private string clickSoundKey = "ui_menu_select";

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(() => Play(clickSoundKey));
        }

        /// <summary>마우스가 올라오면 선택도 옮겨서 키보드 선택과 호버가 한 버튼을 가리키게 (소리는 OnSelect 에서 한 번)</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button.interactable == false || EventSystem.current == null) return;
            if (EventSystem.current.currentSelectedGameObject == gameObject) return;
            EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (button.interactable) Play(hoverSoundKey);
        }

        private static void Play(string key)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlaySfx(key);
        }
    }
}
