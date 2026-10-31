using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 게임 흐름 화면(무기 선택 · 해금 알림 · 게임 오버)의 글꼴·스프라이트 묶음. SCI-FI UI Pack V2 Pro(Cyan) 에셋을 가리킨다.
    /// 에셋: Resources/UI/GameFlowUiSkin.asset (팩 원본은 건드리지 않고 참조만). 비어 있는 칸은 각 화면이 단색으로 대신 그린다
    /// </summary>
    [CreateAssetMenu(fileName = "GameFlowUiSkin", menuName = "HybridShooter/UI/Game Flow UI Skin")]
    public sealed class GameFlowUiSkin : ScriptableObject
    {
        public const string ResourcePath = "UI/GameFlowUiSkin";

        [Header("글꼴")]
        [Tooltip("한글 글자용 (NotoSansKR SDF)")]
        public TMP_FontAsset textFont;
        [Tooltip("숫자·키 표시용 (Aldrich SDF)")]
        public TMP_FontAsset numberFont;

        [Header("창")]
        [Tooltip("무기 선택 화면 뒤 창 (Panel_StoreWeaponNormal)")]
        public Sprite window;
        [Tooltip("해금 알림 배너 (DialogBox)")]
        public Sprite banner;
        [Tooltip("해금 알림 제목 띠 (DialogBoxTitle)")]
        public Sprite bannerTitle;

        [Header("무기 칸·후보")]
        [Tooltip("보통 칸 (Btn_EquipmentNull_n)")]
        public Sprite slot;
        [Tooltip("선택된 칸 (Btn_EquipmentCyan_n)")]
        public Sprite slotSelected;
        [Tooltip("누르는 중 (Btn_EquipmentCyan_p)")]
        public Sprite slotPressed;
        [Tooltip("잠긴 무기 카드 (Btn_LevelLocked)")]
        public Sprite locked;
        [Tooltip("자물쇠 아이콘 (Icon_Lock0)")]
        public Sprite lockIcon;

        [Header("버튼")]
        [Tooltip("주 버튼 (Btn_Rectangle01Cyan_n / _p)")]
        public Sprite primaryButton;
        public Sprite primaryButtonPressed;
        [Tooltip("누를 수 없는 버튼 (Btn_Rectangle01Gray_n)")]
        public Sprite disabledButton;
        [Tooltip("메인 메뉴 버튼 (Btn_MainMenuCyan_n / _P)")]
        public Sprite menuButton;
        public Sprite menuButtonPressed;

        [Header("게임 오버")]
        [Tooltip("배경 띠 (GameOver_bg)")]
        public Sprite gameOverBackground;
        [Tooltip("GAME OVER 글자 (Title_GAMEOVER)")]
        public Sprite gameOverTitle;

        private static GameFlowUiSkin cached;

        /// <summary>Resources 의 스킨. 없으면 빈 스킨 (모든 화면이 단색으로 그린다)</summary>
        public static GameFlowUiSkin Current
        {
            get
            {
                if (cached == null) cached = Resources.Load<GameFlowUiSkin>(ResourcePath);
                if (cached == null) cached = CreateInstance<GameFlowUiSkin>();
                return cached;
            }
        }

        /// <summary>sprite 가 있으면 그 스프라이트(9분할이면 Sliced), 없으면 fallback 단색</summary>
        public static void Paint(Image image, Sprite sprite, Color fallback)
        {
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : fallback;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }

        /// <summary>버튼에 눌림·비활성 스프라이트를 건다 (스프라이트가 없으면 색 전환 그대로)</summary>
        public static void SetStates(Button button, Sprite pressed, Sprite disabled)
        {
            if (pressed == null && disabled == null) return;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { highlightedSprite = null, pressedSprite = pressed, selectedSprite = null, disabledSprite = disabled };
        }
    }
}
