using System.Collections.Generic;
using HybridShooter.Core.Data;
using HybridShooter.Reward;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 라운드 보상·강화 선택 화면 (BL-030, BL-031, BL-032). 디자인: UI 리디자인 시안 04·05 (Docs/Design/UiRedesign_Plan.md)
    ///  - A. Reward  : MISSION ACCOMPLISHED · 이번 라운드 획득 재화(군중전 / 보스전 / 합계)와 보유 재화, [강화 선택]
    ///  - B. Upgrade : 무작위 강화 카드(등급 색·효과·비용·레벨, 산 카드는 SOLD), 보유 재화, 보유 강화, [리롤] · [다음 라운드] / [건너뛰기].
    ///                재화가 남아 있으면 카드를 계속 살 수 있다
    /// 표시만 담당한다. 상태와 규칙은 UpgradeManager 가 가지고, 이 화면은 Manager.Changed 를 받아 다시 그린다.
    /// 레이아웃은 실행 시 코드로 만든다 (1920×1080 기준). 스타일은 UiTheme: 상자 스프라이트 대신 선·띠, 글자는 TMP(Aldrich · Noto Sans KR),
    /// 버튼은 메인 메뉴식 글자 버튼. 강화 아이콘만 UpgradeUiSkin(또는 Inspector)의 스프라이트를 쓴다
    /// </summary>
    public sealed class UpgradeScreen : MonoBehaviour
    {
        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private UpgradeManager manager;

        [Header("이전 스킨 칸 (리디자인 뒤에는 강화 아이콘만 쓴다. 기존 씬·프리팹 호환용으로 남겨 둠)")]
        [SerializeField] private Font textFont;
        [SerializeField] private Font numberFont;

        [Header("A. Reward")]
        [SerializeField] private Sprite rewardRibbon;
        [SerializeField] private Sprite victoryTitle;

        [Header("B. Upgrade")]
        [SerializeField] private Sprite titleBar;
        [SerializeField] private Sprite coinBar;
        [SerializeField] private Sprite cardPanel;
        [SerializeField] private Sprite buyNormal;
        [SerializeField] private Sprite buyDisabled;
        [SerializeField] private Sprite ownedButton;
        [SerializeField] private Sprite starOn;
        [SerializeField] private Sprite starOff;

        [Header("공통 버튼·아이콘")]
        [SerializeField] private Sprite mainButton;
        [SerializeField] private Sprite coinIcon;
        [Tooltip("UpgradeType 순서 (enum 값 = 칸 번호). 비어 있는 칸은 아이콘 없이 그린다")]
        [SerializeField] private Sprite[] upgradeIcons = new Sprite[3];

        /// <summary>배치(1920×1080 기준, 위 40 ~ 아래 1040) 축소 비율. 0.9 면 위아래로 약 60씩 여백</summary>
        private const float PanelScale = 0.9f;
        private const float MarginX = UiTheme.ScreenMargin;
        private const float CardTop = 300f, CardHeight = 560f, CardGap = 40f;
        /// <summary>카드 안쪽 여백</summary>
        private const float Pad = 34f;
        /// <summary>누를 수 없는 글자 버튼 투명도</summary>
        private const float DisabledAlpha = 0.35f;
        /// <summary>
        /// 다음으로 넘어가는 버튼 끝 표시. ▶ 는 Noto Sans KR 폰트 에셋에 없어 네모(□)로 나오므로 › 를 붉게 쓴다.
        /// 한글 글꼴(Aldrich 에는 한글이 없다)로 그리는 글자에만 붙인다
        /// </summary>
        private const string NextMark = "  <color=#E0574D>›</color>";

        private static Sprite white;

        /// <summary>강화 카드 한 장의 화면 요소</summary>
        private sealed class CardView
        {
            public GameObject Root;
            public CanvasGroup Group;
            /// <summary>살 수 있을 때 켜지는 왼쪽 붉은 표식</summary>
            public Image Highlight;
            public Image TopLine, Icon;
            public TextMeshProUGUI Grade, Name, Level, Description, Effect, Cost, BuyLabel, Shortage;
            public readonly List<Image> Stars = new List<Image>();
            public RectTransform StarRow;
            public Button Buy;
            public Image BuyCoin, BuyUnderline;
        }

        private GameObject root, rewardPanel, upgradePanel;
        private TextMeshProUGUI crowdText, bossText, totalText, rewardBalanceText;
        private TextMeshProUGUI balanceText, subtitleText, finishLabel, rerollLabel, rerollCostText, ownedText;
        private Button selectButton, finishButton, rerollButton;
        private readonly List<CardView> cards = new List<CardView>();
        private readonly System.Text.StringBuilder ownedBuilder = new System.Text.StringBuilder();

        private void Awake()
        {
            if (manager == null) manager = FindAnyObjectByType<UpgradeManager>();
            if (manager == null)
            {
                Debug.LogError("[UpgradeScreen] 씬에 UpgradeManager 가 없습니다.", this);
                enabled = false;
                return;
            }
            ApplySkin(UpgradeUiSkin.Current);

            EnsureEventSystem();
            BuildLayout();
            root.SetActive(false);
        }

        /// <summary>Inspector 에서 비워 둔 칸을 스킨 값으로 채운다 (씬마다 연결하지 않아도 같은 겉모양)</summary>
        private void ApplySkin(UpgradeUiSkin skin)
        {
            if (textFont == null) textFont = skin.textFont;
            if (numberFont == null) numberFont = skin.numberFont;
            if (rewardRibbon == null) rewardRibbon = skin.rewardRibbon;
            if (victoryTitle == null) victoryTitle = skin.victoryTitle;
            if (titleBar == null) titleBar = skin.titleBar;
            if (coinBar == null) coinBar = skin.coinBar;
            if (cardPanel == null) cardPanel = skin.cardPanel;
            if (buyNormal == null) buyNormal = skin.buyNormal;
            if (buyDisabled == null) buyDisabled = skin.buyDisabled;
            if (ownedButton == null) ownedButton = skin.ownedButton;
            if (starOn == null) starOn = skin.starOn;
            if (starOff == null) starOff = skin.starOff;
            if (mainButton == null) mainButton = skin.mainButton;
            if (coinIcon == null) coinIcon = skin.coinIcon;

            if (upgradeIcons == null || upgradeIcons.Length < skin.upgradeIcons.Length) System.Array.Resize(ref upgradeIcons, skin.upgradeIcons.Length);
            for (int i = 0; i < skin.upgradeIcons.Length; i++)
                if (upgradeIcons[i] == null) upgradeIcons[i] = skin.upgradeIcons[i];
        }

        private void OnEnable()
        {
            if (manager != null) manager.Changed += Refresh;
        }

        private void OnDisable()
        {
            if (manager != null) manager.Changed -= Refresh;
        }

        // ── 표시 ──

        /// <summary>Manager 상태로 전체를 다시 그린다. 열릴 때는 A(Reward)부터 보여 준다</summary>
        private void Refresh()
        {
            if (manager.IsOpen == false)
            {
                root.SetActive(false);
                return;
            }
            if (root.activeSelf == false)
            {
                root.SetActive(true);
                ShowUpgrade(false);
            }

            crowdText.text = $"+ {manager.CrowdEarned}";
            bossText.text = $"+ {manager.BossEarned}";
            totalText.text = $"+ {manager.RoundEarned}";
            rewardBalanceText.text = manager.Balance.ToString();
            balanceText.text = manager.Balance.ToString();

            for (int i = 0; i < cards.Count; i++) RefreshCard(cards[i], manager.GetOption(i));
            RefreshOwned();

            bool anyPurchasable = manager.AnyPurchasable;
            SetInteractable(finishButton, manager.CanFinish);
            SetInteractable(rerollButton, manager.CanReroll);
            rerollCostText.text = manager.RerollCost.ToString();
            finishLabel.text = manager.HasPurchased ? "다음 라운드" + NextMark : "건너뛰기" + NextMark;
            subtitleText.text = anyPurchasable
                ? (manager.HasPurchased ? $"강화 {manager.PurchaseCount}회 · 재화가 남아 있으면 계속 강화할 수 있습니다"
                                        : "재화가 있는 만큼 강화할 수 있습니다 · 다음 라운드까지 유지됩니다")
                : (manager.HasPurchased ? $"강화 {manager.PurchaseCount}회 완료 · 다음 라운드를 시작하세요"
                                        : "구매할 수 있는 강화가 없습니다");
        }

        private void RefreshCard(CardView card, UpgradeManager.UpgradeOption option)
        {
            card.Root.SetActive(option.HasOffer);
            if (option.IsEmpty) return;

            Color gradeColor = option.GradeDef != null ? option.GradeDef.Color : Color.white;
            var icon = (int)option.Type < upgradeIcons.Length ? upgradeIcons[(int)option.Type] : null;
            card.Icon.sprite = icon;
            card.Icon.enabled = icon != null;

            card.TopLine.color = gradeColor;
            card.Grade.text = option.GradeDef != null ? option.GradeDef.Name : option.Grade.ToString();
            card.Grade.color = gradeColor;
            card.Name.text = option.Next != null ? option.Next.Name : option.Type.ToString();
            card.Description.text = option.Next != null ? option.Next.Description.Replace("{0}", FormatValue(option.Type, option.GradeValue)) : "-";
            card.Level.text = $"LV {option.Level} → {option.Level + 1} / {option.MaxLevel}";
            card.Effect.text = $"<color=#99ADC7>{FormatValue(option.Type, option.CurrentValue)}</color>  →  {FormatValue(option.Type, option.NextValue)}";
            SetStars(card, option.Type, option.IsSold ? option.Level + 1 : option.Level, option.MaxLevel);

            card.Group.alpha = option.IsSold ? 0.5f : (option.CanBuy ? 1f : 0.7f);
            card.Highlight.enabled = option.CanBuy;
            card.Buy.interactable = option.CanBuy;
            card.Shortage.gameObject.SetActive(option.IsSold == false && option.CanBuy == false);
            card.BuyLabel.gameObject.SetActive(option.CanBuy);
            card.BuyUnderline.enabled = option.CanBuy;

            if (option.IsSold)
            {
                card.BuyCoin.gameObject.SetActive(false);
                card.Cost.text = "SOLD";
                card.Cost.characterSpacing = 10f;
                card.Cost.color = UiTheme.Steel;
                card.Cost.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            }
            else
            {
                bool canBuy = option.CanBuy;
                card.BuyCoin.gameObject.SetActive(true);
                card.BuyCoin.color = canBuy ? UiTheme.Ember : UiTheme.SteelDim;
                card.Cost.text = option.Next.Cost.ToString();
                card.Cost.characterSpacing = 0f;
                card.Cost.color = canBuy ? UiTheme.Ember : UiTheme.Steel;
                card.Cost.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            }
        }

        /// <summary>보유 강화 목록: 이름 + 레벨마다 산 등급 색 점 (리롤 판단용)</summary>
        private void RefreshOwned()
        {
            ownedBuilder.Clear();
            foreach (var type in manager.Types)
            {
                int level = manager.GetLevel(type);
                if (level <= 0 || DataManager.UpgradeTracks.TryGet(type, out var track) == false) continue;
                string name = track.Levels[0].Name.TrimEnd(' ', 'I');
                ownedBuilder.Append(name).Append("  ");
                for (int lv = 1; lv <= level; lv++)
                    ownedBuilder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(GradeColor(manager.GetGrade(type, lv)))).Append(">●</color>");
                ownedBuilder.Append('\n');
            }
            ownedText.text = ownedBuilder.Length > 0 ? ownedBuilder.ToString() : "<color=#99ADC7>아직 없음</color>";
        }

        private static Color GradeColor(UpgradeGrade? grade)
            => grade.HasValue && DataManager.UpgradeGrades.TryGet(grade.Value, out var def) ? def.Color : Color.white;

        /// <summary>강화 값을 종류에 맞는 단위로 표기 (UpgradeDef.GradeValues 의 단위)</summary>
        private static string FormatValue(UpgradeType type, float value)
        {
            switch (type)
            {
                case UpgradeType.ReloadSpeed: return $"-{value * 100f:0}%";
                case UpgradeType.MagazineSize: return $"+{value:0}발";
                case UpgradeType.GrenadeCapacity:
                case UpgradeType.HealCapacity: return $"+{value:0}개";
                case UpgradeType.MaxHealth:
                case UpgradeType.MaxStamina: return $"+{value:0}";
                case UpgradeType.GrenadeRadius:
                case UpgradeType.StaminaRegen:
                case UpgradeType.LuckyGirl: return $"+{value * 100f:0}%";
                default: return value.ToString("0.##");
            }
        }

        /// <summary>최대 레벨 수만큼 마름모를 만들고, 산 레벨까지 그 레벨 등급 색으로 켠다</summary>
        private void SetStars(CardView card, UpgradeType type, int level, int maxLevel)
        {
            while (card.Stars.Count < maxLevel)
            {
                var star = CreateImage("Star", card.StarRow, UiTheme.SteelDim);
                star.rectTransform.sizeDelta = new Vector2(11f, 11f);
                star.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                card.Stars.Add(star);
            }
            for (int i = 0; i < card.Stars.Count; i++)
            {
                card.Stars[i].gameObject.SetActive(i < maxLevel);
                bool on = i < level;
                card.Stars[i].color = on ? GradeColor(manager.GetGrade(type, i + 1)) : UiTheme.SteelDim;
            }
        }

        private void ShowUpgrade(bool upgrade)
        {
            rewardPanel.SetActive(upgrade == false);
            upgradePanel.SetActive(upgrade);
            // 키보드·패드로 바로 고를 수 있게 그 화면의 주 버튼을 선택해 둔다
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(upgrade ? finishButton.gameObject : selectButton.gameObject);
        }

        /// <summary>글자 버튼은 색 전환이 없으므로 누를 수 없을 때 흐리게</summary>
        private static void SetInteractable(Button button, bool interactable)
        {
            button.interactable = interactable;
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = interactable ? 1f : DisabledAlpha;
        }

        // ── 기본 레이아웃 (1920×1080) ──

        private void BuildLayout()
        {
            var canvasGo = new GameObject("UpgradeScreenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Expand: 화면 비율과 상관없이 기준 해상도(1920×1080) 영역 전체가 항상 화면 안에 들어온다 (가로로 넓은 창에서 위아래 잘림 방지)
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root = canvasGo;

            // 게임 화면을 어둡게 가리고 뒤쪽 클릭을 막는다
            var mask = CreateImage("Mask", canvasGo.transform, new Color(0f, 0f, 0f, 0.6f));
            mask.raycastTarget = true;
            Stretch(mask.rectTransform);

            BuildRewardPanel(canvasGo.transform);
            BuildUpgradePanel(canvasGo.transform);
        }

        /// <summary>A. 보상: 가운데 띠 위에 MISSION ACCOMPLISHED + 재화 줄 + [강화 선택]</summary>
        private void BuildRewardPanel(Transform parent)
        {
            rewardPanel = CreatePanel("RewardPanel", parent);
            var p = rewardPanel.transform;

            var band = new GameObject("BandArea", typeof(RectTransform)).GetComponent<RectTransform>();
            band.SetParent(p, false);
            Place(band, 0f, 180f, 1920f, 720f);
            UiThemeKit.Band(band, UiThemeKit.BandShape.FadeBoth, 0.88f, 0.25f);

            var header = CreateText("Header", p, UiTheme.NumberFont, UiTheme.LabelSize, TextAlignmentOptions.Center, UiTheme.Steel, "ROUND CLEAR");
            header.characterSpacing = UiTheme.LabelSpacing;
            Place(header.rectTransform, 0f, 244f, 1200f, 28f);
            var title = CreateText("Title", p, UiTheme.NumberFont, 76f, TextAlignmentOptions.Center, UiTheme.MoonWhite, "MISSION ACCOMPLISHED");
            title.characterSpacing = 16f;
            Place(title.rectTransform, 0f, 284f, 1800f, 96f);
            Place(CreateImage("Line", p, UiTheme.Red).rectTransform, 0f, 392f, 140f, 3f);

            crowdText = CreateRow(p, "군중전 획득", 432f);
            bossText = CreateRow(p, "보스전 획득", 494f);
            totalText = CreateTotalRow(p, 566f);
            rewardBalanceText = CreateRow(p, "보유 재화 (이전 라운드 잔액 포함)", 656f, false);

            selectButton = CreateTextButton(p, "SelectUpgradeButton", "강화 선택" + NextMark, 34f, UiTheme.TextFont, out _);
            Place((RectTransform)selectButton.transform, 0f, 776f, 340f, 76f);
            selectButton.onClick.AddListener(() => ShowUpgrade(true));
        }

        /// <summary>B. 강화: 왼쪽 위 제목 · 오른쪽 위 보유 강화·재화 · 가운데 카드 · 아래 [리롤] [다음 라운드]</summary>
        private void BuildUpgradePanel(Transform parent)
        {
            upgradePanel = CreatePanel("UpgradePanel", parent);
            var p = upgradePanel.transform;

            var header = CreateText("Header", p, UiTheme.NumberFont, UiTheme.LabelSize, TextAlignmentOptions.TopLeft, UiTheme.Steel, "UPGRADE");
            header.characterSpacing = UiTheme.LabelSpacing;
            PlaceLeft(header.rectTransform, MarginX, 72f, 600f, 26f);
            PlaceLeft(CreateText("Title", p, UiTheme.TextFont, UiTheme.TitleSize, TextAlignmentOptions.TopLeft, UiTheme.MoonWhite, "무기 강화").rectTransform, MarginX, 104f, 600f, 60f);
            PlaceLeft(CreateImage("Line", p, UiTheme.Red).rectTransform, MarginX, 172f, 140f, 3f);
            subtitleText = CreateText("Subtitle", p, UiTheme.TextFont, 20f, TextAlignmentOptions.TopLeft, UiTheme.Steel, string.Empty);
            PlaceLeft(subtitleText.rectTransform, MarginX, 190f, 1100f, 32f);

            // 오른쪽 위: 보유 강화 · 보유 재화
            var ownedLabel = CreateText("OwnedLabel", p, UiTheme.NumberFont, 15f, TextAlignmentOptions.TopLeft, UiTheme.Steel, "OWNED");
            ownedLabel.characterSpacing = 8f;
            PlaceLeft(ownedLabel.rectTransform, 1920f - MarginX - 640f, 72f, 300f, 24f);
            ownedText = CreateText("Owned", p, UiTheme.TextFont, 19f, TextAlignmentOptions.TopLeft, UiTheme.MoonWhite, string.Empty);
            ownedText.lineSpacing = 10f;
            PlaceLeft(ownedText.rectTransform, 1920f - MarginX - 640f, 102f, 330f, 180f);

            var creditLabel = CreateText("CreditLabel", p, UiTheme.NumberFont, 15f, TextAlignmentOptions.TopRight, UiTheme.Steel, "CREDIT");
            creditLabel.characterSpacing = 8f;
            PlaceLeft(creditLabel.rectTransform, 1920f - MarginX - 260f, 72f, 260f, 24f);
            balanceText = CreateText("Balance", p, UiTheme.NumberFont, 48f, TextAlignmentOptions.TopRight, UiTheme.Ember, "0");
            PlaceLeft(balanceText.rectTransform, 1920f - MarginX - 260f, 100f, 260f, 60f);

            int count = manager.OfferCount;
            float cardWidth = count > 0 ? (1920f - MarginX * 2f - CardGap * (count - 1)) / count : 0f;
            for (int i = 0; i < count; i++)
                cards.Add(BuildCard(p, MarginX + i * (cardWidth + CardGap), cardWidth));

            rerollButton = CreateTextButton(p, "RerollButton", "리롤", 28f, UiTheme.TextFont, out rerollLabel);
            PlaceLeft((RectTransform)rerollButton.transform, MarginX - 34f, 916f, 360f, 70f);
            // "리롤" 뒤에 재화 마름모 + 비용 (글자 자식이라 선택 때 같이 밀린다)
            float rerollTextWidth = rerollLabel.GetPreferredValues("리롤").x;
            var rerollCoin = CreateImage("CoinIcon", rerollLabel.transform, UiTheme.Ember);
            rerollCoin.rectTransform.anchorMin = rerollCoin.rectTransform.anchorMax = rerollCoin.rectTransform.pivot = new Vector2(0f, 0.5f);
            rerollCoin.rectTransform.anchoredPosition = new Vector2(rerollTextWidth + 22f, 0f);
            rerollCoin.rectTransform.sizeDelta = new Vector2(10f, 10f);
            rerollCoin.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            rerollCostText = CreateText("Cost", rerollLabel.transform, UiTheme.NumberFont, 28f, TextAlignmentOptions.MidlineLeft, UiTheme.Ember, string.Empty);
            rerollCostText.rectTransform.anchorMin = new Vector2(0f, 0f);
            rerollCostText.rectTransform.anchorMax = new Vector2(0f, 1f);
            rerollCostText.rectTransform.pivot = new Vector2(0f, 0.5f);
            rerollCostText.rectTransform.anchoredPosition = new Vector2(rerollTextWidth + 40f, 0f);
            rerollCostText.rectTransform.sizeDelta = new Vector2(120f, 0f);
            rerollButton.onClick.AddListener(() => manager.TryReroll());

            finishButton = CreateTextButton(p, "FinishButton", string.Empty, 32f, UiTheme.TextFont, out finishLabel);
            PlaceLeft((RectTransform)finishButton.transform, 1920f - MarginX - 330f, 916f, 360f, 70f);
            finishButton.onClick.AddListener(manager.Finish);

            rerollButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = finishButton, selectOnRight = finishButton };
            finishButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = rerollButton, selectOnRight = rerollButton };
        }

        /// <summary>카드 한 장 (left = 패널 왼쪽에서의 위치). 아이콘·등급·내용은 Refresh 때 카드에 뽑힌 강화로 채운다</summary>
        private CardView BuildCard(Transform parent, float left, float width)
        {
            var card = new CardView();
            var bg = CreateImage("Card", parent, new Color(UiTheme.MoonWhite.r, UiTheme.MoonWhite.g, UiTheme.MoonWhite.b, 0.05f));
            PlaceLeft(bg.rectTransform, left, CardTop, width, CardHeight);
            card.Root = bg.gameObject;
            card.Group = bg.gameObject.AddComponent<CanvasGroup>();
            var c = bg.transform;
            float inner = width - Pad * 2f;

            card.TopLine = CreateImage("TopLine", c, UiTheme.SteelDim);
            PlaceLeft(card.TopLine.rectTransform, 0f, 0f, width, 2f);
            card.Highlight = CreateImage("Marker", c, UiTheme.Red);
            PlaceLeft(card.Highlight.rectTransform, 0f, 28f, 5f, 34f);

            card.Grade = CreateText("Grade", c, UiTheme.TextFont, 18f, TextAlignmentOptions.MidlineLeft, Color.white, string.Empty);
            card.Grade.fontStyle = FontStyles.Bold;
            card.Grade.characterSpacing = 4f;
            PlaceLeft(card.Grade.rectTransform, Pad, 30f, inner - 160f, 30f);

            var stars = new GameObject("Stars", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            stars.transform.SetParent(c, false);
            var layout = stars.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            card.StarRow = (RectTransform)stars.transform;
            PlaceLeft(card.StarRow, width - Pad - 160f, 30f, 160f, 30f);

            card.Icon = CreateImage("Icon", c, Color.white);
            card.Icon.preserveAspect = true;
            PlaceLeft(card.Icon.rectTransform, Pad, 76f, 64f, 64f);

            card.Name = CreateText("Name", c, UiTheme.TextFont, 32f, TextAlignmentOptions.MidlineLeft, UiTheme.MoonWhite, string.Empty);
            PlaceLeft(card.Name.rectTransform, Pad, 152f, inner, 48f);
            card.Level = CreateText("Level", c, UiTheme.NumberFont, 18f, TextAlignmentOptions.MidlineLeft, UiTheme.Steel, string.Empty);
            card.Level.characterSpacing = 3f;
            PlaceLeft(card.Level.rectTransform, Pad, 202f, inner, 26f);

            // 효과: 위아래 가는 선 사이에 설명 + "지금 → 다음"
            var lineColor = new Color(UiTheme.SteelDim.r, UiTheme.SteelDim.g, UiTheme.SteelDim.b, 0.6f);
            PlaceLeft(CreateImage("EffectTop", c, lineColor).rectTransform, Pad, 246f, inner, 1f);
            card.Description = CreateText("Description", c, UiTheme.TextFont, 21f, TextAlignmentOptions.TopLeft, UiTheme.Steel, string.Empty);
            card.Description.textWrappingMode = TextWrappingModes.Normal;
            PlaceLeft(card.Description.rectTransform, Pad, 262f, inner, 64f);
            // 값에 "개"·"발" 같은 한글 단위가 붙으므로 한글 글꼴 (Aldrich 에는 한글이 없어 네모로 나온다)
            card.Effect = CreateText("Effect", c, UiTheme.TextFont, 26f, TextAlignmentOptions.MidlineLeft, UiTheme.MoonWhite, string.Empty);
            PlaceLeft(card.Effect.rectTransform, Pad, 330f, inner, 38f);
            PlaceLeft(CreateImage("EffectBottom", c, lineColor).rectTransform, Pad, 384f, inner, 1f);

            // 구매 줄: 재화 마름모 + 비용 ······ 구매 ›  (살 수 있을 때 붉은 밑줄)
            var buyRect = new GameObject("BuyButton", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            buyRect.SetParent(c, false);
            PlaceLeft(buyRect, Pad, 466f, inner, 64f);
            buyRect.GetComponent<Image>().color = Color.clear;
            card.Buy = buyRect.GetComponent<Button>();
            card.Buy.transition = Selectable.Transition.None;
            card.BuyCoin = CreateImage("CoinIcon", buyRect, UiTheme.Ember);
            card.BuyCoin.rectTransform.anchorMin = card.BuyCoin.rectTransform.anchorMax = card.BuyCoin.rectTransform.pivot = new Vector2(0f, 0.5f);
            card.BuyCoin.rectTransform.anchoredPosition = new Vector2(4f, 0f);
            card.BuyCoin.rectTransform.sizeDelta = new Vector2(11f, 11f);
            card.BuyCoin.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            card.Cost = CreateText("Cost", buyRect, UiTheme.NumberFont, 34f, TextAlignmentOptions.MidlineLeft, UiTheme.Ember, string.Empty);
            Stretch(card.Cost.rectTransform);
            card.BuyLabel = CreateText("BuyLabel", buyRect, UiTheme.TextFont, 24f, TextAlignmentOptions.MidlineRight, UiTheme.MoonWhite, "구매 <color=#E0574D>›</color>");
            Stretch(card.BuyLabel.rectTransform);
            card.BuyUnderline = UiThemeKit.FadeLine(buyRect, "Underline", UiTheme.Red, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(inner, UiTheme.LineWidth));
            int index = cards.Count;
            card.Buy.onClick.AddListener(() => manager.TryPurchase(index));

            card.Shortage = CreateText("Shortage", buyRect, UiTheme.TextFont, 20f, TextAlignmentOptions.MidlineRight, UiTheme.RedText, "재화 부족");
            Stretch(card.Shortage.rectTransform);
            return card;
        }

        /// <summary>A 화면의 한 줄: 왼쪽 항목명(청회색), 오른쪽 값(Aldrich), 아래 가는 선. 값 글자를 돌려준다</summary>
        private TextMeshProUGUI CreateRow(Transform parent, string label, float top, bool line = true)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            Place((RectTransform)row.transform, 0f, top, 760f, 60f);
            var left = CreateText("Label", row.transform, UiTheme.TextFont, line ? 24f : 20f, TextAlignmentOptions.MidlineLeft, UiTheme.Steel, label);
            PlaceLeft(left.rectTransform, 0f, 0f, 520f, 60f);
            var value = CreateText("Value", row.transform, UiTheme.NumberFont, line ? 30f : 28f, TextAlignmentOptions.MidlineRight, UiTheme.MoonWhite, "0");
            PlaceLeft(value.rectTransform, 440f, 0f, 320f, 60f);
            if (line)
            {
                var sep = CreateImage("Line", row.transform, new Color(UiTheme.SteelDim.r, UiTheme.SteelDim.g, UiTheme.SteelDim.b, 0.6f));
                PlaceLeft(sep.rectTransform, 0f, 59f, 760f, 1f);
            }
            return value;
        }

        /// <summary>A 화면의 합계 줄: 호박색 옅은 띠 + 왼쪽 호박색 표식 + 큰 호박색 숫자</summary>
        private TextMeshProUGUI CreateTotalRow(Transform parent, float top)
        {
            var row = CreateImage("TotalRow", parent, new Color(UiTheme.Ember.r, UiTheme.Ember.g, UiTheme.Ember.b, 0.14f));
            Place(row.rectTransform, 0f, top, 760f, 80f);
            row.gameObject.AddComponent<UiFade>().Set(1f, 0f);
            PlaceLeft(CreateImage("Marker", row.transform, UiTheme.Ember).rectTransform, 0f, 0f, 3f, 80f);
            var left = CreateText("Label", row.transform, UiTheme.TextFont, 26f, TextAlignmentOptions.MidlineLeft, UiTheme.MoonWhite, "이번 라운드 합계");
            PlaceLeft(left.rectTransform, 24f, 0f, 480f, 80f);
            var value = CreateText("Value", row.transform, UiTheme.NumberFont, 52f, TextAlignmentOptions.MidlineRight, UiTheme.Ember, "0");
            PlaceLeft(value.rectTransform, 400f, 0f, 360f, 80f);
            return value;
        }

        // ── 생성 도우미 ──

        /// <summary>마우스 클릭을 받으려면 EventSystem 이 필요하다. 씬에 없으면 Input System 용으로 만든다</summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// <summary>
        /// 1920×1080 기준 배치를 담는 패널. 화면 가운데에 두고 PanelScale 로 줄여 위아래 여백을 남긴다
        /// (배치 좌표는 기준 해상도 그대로 두고 크기만 한 곳에서 조절)
        /// </summary>
        private static GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localScale = Vector3.one * PanelScale;
            return go;
        }

        /// <summary>단색 면·선 (흰 스프라이트에 색만)</summary>
        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            if (white == null) white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            image.sprite = white;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Color color, string text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = alignment;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>메인 메뉴식 글자 버튼 (UiThemeKit.TextButton). 배치는 부른 쪽이 Place 로 다시 잡는다</summary>
        private static Button CreateTextButton(Transform parent, string name, string label, float fontSize, TMP_FontAsset font, out TextMeshProUGUI labelText)
        {
            var button = UiThemeKit.TextButton(parent, name, label, fontSize, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(340f, 70f), font);
            labelText = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            return button;
        }

        /// <summary>화면 상단 가운데 기준 배치. x = 가운데에서의 가로 오프셋, top = 위에서의 거리</summary>
        private static void Place(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>부모의 좌상단 기준 배치</summary>
        private static void PlaceLeft(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
