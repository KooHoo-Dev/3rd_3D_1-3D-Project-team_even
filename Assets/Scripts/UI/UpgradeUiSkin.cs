using UnityEngine;

namespace HybridShooter.UI
{
    /// <summary>
    /// 라운드 보상·강화 선택 화면(UpgradeScreen)의 글꼴·스프라이트 묶음. SCI-FI UI Pack V2 Pro(Cyan) 에셋을 가리킨다.
    /// 에셋: Resources/UI/UpgradeUiSkin.asset. 강화 화면이 여러 씬에 있어도 씬마다 스프라이트를 연결하지 않도록 한 곳에 둔다.
    /// UpgradeScreen 의 Inspector 칸이 비어 있으면 이 스킨 값을 쓰고, 스킨도 비어 있으면 단색으로 대신 그린다
    /// </summary>
    [CreateAssetMenu(fileName = "UpgradeUiSkin", menuName = "HybridShooter/UI/Upgrade UI Skin")]
    public sealed class UpgradeUiSkin : ScriptableObject
    {
        public const string ResourcePath = "UI/UpgradeUiSkin";

        [Header("글꼴 (비우면 LegacyRuntime)")]
        public Font textFont;
        [Tooltip("숫자·영문용 (Play-Bold)")]
        public Font numberFont;

        [Header("A. Reward")]
        [Tooltip("배경 띠 (Victory1_bg)")]
        public Sprite rewardRibbon;
        public Sprite victoryTitle;

        [Header("B. Upgrade")]
        public Sprite titleBar;
        public Sprite coinBar;
        public Sprite cardPanel;
        public Sprite buyNormal;
        public Sprite buyDisabled;
        public Sprite ownedButton;
        public Sprite starOn;
        public Sprite starOff;

        [Header("공통 버튼·아이콘")]
        public Sprite mainButton;
        public Sprite coinIcon;
        [Tooltip("UpgradeType 순서: ReloadSpeed, MagazineSize, GrenadeCapacity")]
        public Sprite[] upgradeIcons = new Sprite[3];

        private static UpgradeUiSkin cached;

        /// <summary>Resources 의 스킨. 없으면 빈 스킨 (화면이 단색으로 그린다)</summary>
        public static UpgradeUiSkin Current
        {
            get
            {
                if (cached == null) cached = Resources.Load<UpgradeUiSkin>(ResourcePath);
                if (cached == null) cached = CreateInstance<UpgradeUiSkin>();
                return cached;
            }
        }
    }
}
