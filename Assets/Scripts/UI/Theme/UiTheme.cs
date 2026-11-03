using TMPro;
using UnityEngine;

namespace HybridShooter.UI.Theme
{
    /// <summary>
    /// UI 리디자인 "Moonlit Night" 의 공용 색·글꼴·크기. 메인 메뉴(MainMenuUIBuilder)의 차가운 청회색 + 붉은 포인트를 게임 전체에 쓴다.
    ///  - 색: 선택·핵심 숫자 MoonWhite / 평소 글자 Steel / 빈 칸·비활성 SteelDim / 선택 표식·위험 Red / 경고·재화 Ember
    ///  - 붉은색은 화면마다 "지금 봐야 할 것" 하나에만. 시안(#1AE0FF) 강조는 쓰지 않는다
    ///  - 글꼴: 숫자·영문 = Aldrich, 한글 = Noto Sans KR (GameFlowUiSkin 이 가리키는 글꼴 그대로)
    /// 각 화면은 자기 색 상수 대신 여기를 본다. 설계: Docs/Design/UiRedesign_Plan.md
    /// </summary>
    public static class UiTheme
    {
        #region 색
        /// <summary>달빛 흰색 #F0F5FF (선택된 글자·핵심 숫자·로고)</summary>
        public static readonly Color MoonWhite = new Color(0.94f, 0.96f, 1f);
        /// <summary>차가운 청회색 #99ADC7 (평소 글자·라벨·부가 정보)</summary>
        public static readonly Color Steel = new Color(0.6f, 0.68f, 0.78f);
        /// <summary>어두운 청회색 #4A5869 (빈 칸·비활성·게이지 바탕)</summary>
        public static readonly Color SteelDim = new Color(0.29f, 0.345f, 0.41f);
        /// <summary>달 눈동자의 붉은색 #D9423B (선택 표식·밑줄·HP 위급). 선·면에 쓴다</summary>
        public static readonly Color Red = new Color(0.85f, 0.26f, 0.23f);
        /// <summary>붉은 글자 #E0574D (키 이름 등 어두운 바탕 위 글자. Red 보다 조금 밝아 읽기 쉽다)</summary>
        public static readonly Color RedText = new Color(0.88f, 0.34f, 0.3f);
        /// <summary>호박색 #D9A24B (탄약 부족 같은 주의 · 재화 · 보상 숫자)</summary>
        public static readonly Color Ember = new Color(0.85f, 0.635f, 0.294f);
        /// <summary>청색 #7FB2E5 (강화 등급 '희귀' 전용)</summary>
        public static readonly Color Rare = new Color(0.5f, 0.7f, 0.9f);
        /// <summary>띠·패널 바탕 #03040A (알파는 Band 가 정한다)</summary>
        public static readonly Color Night = new Color(0.012f, 0.016f, 0.04f, 0.82f);
        /// <summary>게이지 바탕 (SteelDim 60%)</summary>
        public static readonly Color GaugeBack = new Color(0.29f, 0.345f, 0.41f, 0.6f);
        /// <summary>화면 전체를 어둡게 덮는 막 (보상·게임 오버·옵션 뒤)</summary>
        public static readonly Color ScreenDim = new Color(0f, 0f, 0f, 0.55f);
        #endregion

        #region 크기 (1920×1080 기준)
        /// <summary>HUD 가장자리 여백</summary>
        public const float HudMargin = 64f;
        /// <summary>메뉴·전체 화면 패널의 좌우 여백 (메인 메뉴와 같음)</summary>
        public const float ScreenMargin = 120f;
        /// <summary>HP 같은 주 게이지 두께</summary>
        public const float GaugeHeight = 6f;
        /// <summary>스태미나·재장전 같은 보조 막대 두께</summary>
        public const float ThinBarHeight = 3f;
        /// <summary>선택 표식(세로 막대) 폭</summary>
        public const float MarkerWidth = 4f;
        /// <summary>밑줄·구분선 두께</summary>
        public const float LineWidth = 2f;
        /// <summary>섹션 머리 아래 붉은 선 길이</summary>
        public const float HeaderLineLength = 56f;
        #endregion

        #region 글자
        /// <summary>영문 라벨 자간 (TMP characterSpacing)</summary>
        public const float LabelSpacing = 10f;
        /// <summary>영문 제목 자간</summary>
        public const float TitleSpacing = 10f;

        public const float NumberSize = 64f;
        public const float TitleSize = 44f;
        public const float BodySize = 24f;
        public const float CaptionSize = 19f;
        public const float LabelSize = 17f;

        /// <summary>한글이 들어가는 글자용 (Noto Sans KR SDF)</summary>
        public static TMP_FontAsset TextFont => GameFlowUiSkin.Current.textFont;
        /// <summary>숫자·영문 라벨용 (Aldrich SDF)</summary>
        public static TMP_FontAsset NumberFont => GameFlowUiSkin.Current.numberFont;
        #endregion
    }
}
