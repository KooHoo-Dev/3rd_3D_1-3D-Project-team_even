using System.Collections.Generic;
using HybridShooter.Core.Data;
using HybridShooter.Reward;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 라운드 보상·강화 선택 화면 (BL-030, BL-031, BL-032). 디자인: Docs/Design/UpgradeUI_Mockup.html
    ///  - A. Reward  : 이번 라운드 획득 재화(군중전 / 보스전 / 합계)와 보유 재화, [강화 선택]
    ///  - B. Upgrade : 강화 카드 3장(효과·비용·레벨), 보유 재화, [다음 라운드] / [건너뛰기]
    /// 표시만 담당한다. 상태와 규칙은 UpgradeManager 가 가지고, 이 화면은 Manager.Changed 를 받아 다시 그린다.
    /// 레이아웃은 실행 시 코드로 만든다 (1920×1080 기준). 스프라이트는 Inspector 에 연결하고, 비어 있으면 단색으로 대신한다.
    /// </summary>
    public sealed class UpgradeScreen : MonoBehaviour
    {
        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private UpgradeManager manager;

        [Header("글꼴 (비우면 LegacyRuntime. 숫자·영문용은 한글이 없는 글꼴도 가능)")]
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
        [Tooltip("UpgradeType 순서: ReloadSpeed, MagazineSize, GrenadeCapacity")]
        [SerializeField] private Sprite[] upgradeIcons = new Sprite[3];

        private static readonly Color Cyan = new Color(0.2f, 0.9f, 1f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
        private static readonly Color Dim = new Color(0.5f, 0.65f, 0.69f);
        private static readonly Color Warning = new Color(1f, 0.42f, 0.35f);

        /// <summary>강화 카드 한 장의 화면 요소</summary>
        private sealed class CardView
        {
            public CanvasGroup Group;
            public Outline Highlight;
            public Text Name, Level, Description, Effect, Cost, Shortage;
            public readonly List<Image> Stars = new List<Image>();
            public RectTransform StarRow;
            public Button Buy;
            public Image BuyImage, BuyCoin;
        }

        private GameObject root, rewardPanel, upgradePanel;
        private Text crowdText, bossText, totalText, rewardBalanceText;
        private Text balanceText, subtitleText, finishLabel;
        private Button finishButton;
        private readonly List<CardView> cards = new List<CardView>();

        private void Awake()
        {
            if (manager == null) manager = FindAnyObjectByType<UpgradeManager>();
            if (manager == null)
            {
                Debug.LogError("[UpgradeScreen] 씬에 UpgradeManager 가 없습니다.", this);
                enabled = false;
                return;
            }
            if (textFont == null) textFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (numberFont == null) numberFont = textFont;

            EnsureEventSystem();
            BuildLayout();
            root.SetActive(false);
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

            for (int i = 0; i < cards.Count; i++) RefreshCard(cards[i], manager.GetOption(manager.Types[i]));

            bool anyPurchasable = manager.AnyPurchasable;
            bool skip = manager.HasPurchased == false && anyPurchasable == false;
            finishButton.interactable = manager.CanFinish;
            finishLabel.text = skip ? "건너뛰기 ▶" : "다음 라운드 ▶";
            subtitleText.text = manager.HasPurchased ? "강화 완료 · 다음 라운드를 시작하세요"
                              : skip ? "구매할 수 있는 강화가 없습니다"
                              : "하나를 선택하세요 · 다음 라운드까지 유지됩니다";
        }

        private void RefreshCard(CardView card, UpgradeManager.UpgradeOption option)
        {
            bool purchasedThis = manager.HasPurchased && manager.PurchasedType == option.Type;
            bool locked = manager.HasPurchased && purchasedThis == false;
            var shown = option.Next ?? option.Current;

            card.Name.text = shown != null ? shown.Name : option.Type.ToString();
            card.Description.text = shown != null ? shown.Description : "-";
            card.Level.text = option.IsMax ? $"Lv {option.Level} / {option.MaxLevel}"
                                           : $"Lv {option.Level} → {option.Level + 1} / {option.MaxLevel}";
            card.Effect.text = purchasedThis ? $"{FormatValue(option.Type, option.CurrentValue)} 적용"
                             : option.IsMax ? $"{FormatValue(option.Type, option.CurrentValue)} (최대)"
                             : $"<color=#7FA6B0>{FormatValue(option.Type, option.CurrentValue)}</color>  →  {FormatValue(option.Type, option.NextValue)}";
            SetStars(card, option.Level, option.MaxLevel);

            card.Group.alpha = locked ? 0.45f : 1f;
            card.Highlight.enabled = purchasedThis;
            card.Buy.interactable = option.CanBuy;
            card.Shortage.gameObject.SetActive(locked == false && option.IsMax == false && option.CanBuy == false && manager.HasPurchased == false);

            if (purchasedThis || option.IsMax)
            {
                SetSprite(card.BuyImage, ownedButton, new Color(0.05f, 0.4f, 0.45f));
                card.BuyCoin.gameObject.SetActive(false);
                card.Cost.text = purchasedThis ? "구매 완료" : "MAX";
                card.Cost.font = purchasedThis ? textFont : numberFont;
                card.Cost.color = Color.white;
            }
            else
            {
                bool canBuy = option.CanBuy;
                SetSprite(card.BuyImage, canBuy ? buyNormal : buyDisabled, canBuy ? new Color(0.95f, 0.65f, 0.1f) : new Color(0.45f, 0.32f, 0.1f));
                card.BuyCoin.gameObject.SetActive(true);
                card.BuyCoin.color = canBuy ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                card.Cost.text = option.Next.Cost.ToString();
                card.Cost.font = numberFont;
                card.Cost.color = canBuy ? new Color(0.23f, 0.12f, 0f) : new Color(0.79f, 0.69f, 0.52f, 0.5f);
            }
        }

        /// <summary>누적 강화 값을 종류에 맞게 표기. ReloadSpeed=감소율, MagazineSize=+발, GrenadeCapacity=+개</summary>
        private static string FormatValue(UpgradeType type, float value)
        {
            switch (type)
            {
                case UpgradeType.ReloadSpeed: return $"-{value * 100f:0}%";
                case UpgradeType.MagazineSize: return $"+{value:0}발";
                case UpgradeType.GrenadeCapacity: return $"+{value:0}개";
                default: return value.ToString("0.##");
            }
        }

        /// <summary>최대 레벨 수만큼 별을 만들고, 현재 레벨까지 켠다</summary>
        private void SetStars(CardView card, int level, int maxLevel)
        {
            while (card.Stars.Count < maxLevel)
            {
                var star = CreateImage("Star", card.StarRow, starOff, Dim);
                star.rectTransform.sizeDelta = new Vector2(34f, 33f);
                card.Stars.Add(star);
            }
            for (int i = 0; i < card.Stars.Count; i++)
            {
                card.Stars[i].gameObject.SetActive(i < maxLevel);
                SetSprite(card.Stars[i], i < level ? starOn : starOff, i < level ? Gold : Dim);
            }
        }

        private void ShowUpgrade(bool upgrade)
        {
            rewardPanel.SetActive(upgrade == false);
            upgradePanel.SetActive(upgrade);
        }

        // ── 기본 레이아웃 (1920×1080, 목업 좌표 그대로) ──

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
            scaler.matchWidthOrHeight = 0.5f;
            root = canvasGo;

            // 게임 화면을 어둡게 가리고 뒤쪽 클릭을 막는다
            var mask = CreateImage("Mask", canvasGo.transform, null, new Color(0f, 0f, 0f, 0.6f));
            Stretch(mask.rectTransform);

            BuildRewardPanel(canvasGo.transform);
            BuildUpgradePanel(canvasGo.transform);
        }

        private void BuildRewardPanel(Transform parent)
        {
            rewardPanel = CreatePanel("RewardPanel", parent);
            var p = rewardPanel.transform;

            Place(CreateImage("Ribbon", p, rewardRibbon, new Color(0.15f, 0.3f, 0.36f, 0.85f)).rectTransform, 0f, 210f, 1704f, 578f);
            var title = CreateImage("VictoryTitle", p, victoryTitle, Color.clear);
            Place(title.rectTransform, 0f, 140f, 535f, 118f);
            if (victoryTitle == null) Place(CreateText("VictoryText", p, numberFont, 96, TextAnchor.MiddleCenter, Cyan, "VICTORY!").rectTransform, 0f, 140f, 800f, 118f);

            crowdText = CreateRow(p, "군중전 획득", 330f, 30, Color.white, false);
            bossText = CreateRow(p, "보스전 획득", 394f, 30, Color.white, false);
            var totalBg = CreateImage("TotalBg", p, null, new Color(0.2f, 0.9f, 1f, 0.15f));
            Place(totalBg.rectTransform, 0f, 468f, 760f, 64f);
            totalText = CreateRow(p, "이번 라운드 합계", 468f, 34, Gold, true);
            rewardBalanceText = CreateRow(p, "보유 재화 (이전 라운드 잔액 포함)", 532f, 26, Dim, false, false);

            var next = CreateButton("SelectUpgradeButton", p, mainButton, new Color(0.15f, 0.55f, 0.75f), "강화 선택 ▶", out _);
            Place((RectTransform)next.transform, 0f, 830f, 342f, 124f);
            next.onClick.AddListener(() => ShowUpgrade(true));
        }

        private void BuildUpgradePanel(Transform parent)
        {
            upgradePanel = CreatePanel("UpgradePanel", parent);
            var p = upgradePanel.transform;

            Place(CreateImage("TitleBar", p, titleBar, new Color(0.15f, 0.35f, 0.4f, 0.8f)).rectTransform, 0f, 40f, 1109f, 254f);
            var title = CreateText("Title", p, textFont, 52, TextAnchor.MiddleCenter, Color.white, "무기 강화");
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, 0f, 118f, 800f, 80f);
            subtitleText = CreateText("Subtitle", p, textFont, 26, TextAnchor.MiddleCenter, Dim, string.Empty);
            Place(subtitleText.rectTransform, 0f, 300f, 1200f, 36f);

            // 보유 재화 (우측 상단)
            var bar = CreateImage("CoinBar", p, coinBar, new Color(0.25f, 0.33f, 0.36f));
            Place(bar.rectTransform, 739f, 40f, 322f, 80f);
            var icon = CreateImage("CoinIcon", bar.transform, coinIcon, Gold);
            PlaceLeft(icon.rectTransform, 18f, 16f, 48f, 48f);
            PlaceLeft(CreateText("Label", bar.transform, textFont, 18, TextAnchor.UpperLeft, Dim, "보유 재화").rectTransform, 76f, 8f, 220f, 26f);
            balanceText = CreateText("Value", bar.transform, numberFont, 34, TextAnchor.UpperLeft, Gold, "0");
            PlaceLeft(balanceText.rectTransform, 76f, 30f, 220f, 44f);

            var types = manager.Types;
            for (int i = 0; i < types.Count; i++)
                cards.Add(BuildCard(p, i < upgradeIcons.Length ? upgradeIcons[i] : null, (i - (types.Count - 1) * 0.5f) * 508f));

            finishButton = CreateButton("FinishButton", p, mainButton, new Color(0.15f, 0.55f, 0.75f), string.Empty, out finishLabel);
            Place((RectTransform)finishButton.transform, 0f, 916f, 342f, 124f);
            finishButton.onClick.AddListener(manager.Finish);
            var colors = finishButton.colors;
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.8f);
            finishButton.colors = colors;
        }

        private CardView BuildCard(Transform parent, Sprite iconSprite, float x)
        {
            var card = new CardView();
            var bg = CreateImage("Card", parent, cardPanel, new Color(0.02f, 0.15f, 0.18f, 0.95f));
            Place(bg.rectTransform, x, 335f, 460f, 540f);
            card.Group = bg.gameObject.AddComponent<CanvasGroup>();
            card.Highlight = bg.gameObject.AddComponent<Outline>();
            card.Highlight.effectColor = new Color(0.2f, 0.9f, 1f, 0.9f);
            card.Highlight.effectDistance = new Vector2(4f, -4f);
            var c = bg.transform;

            var glow = CreateImage("IconGlow", c, null, new Color(0.2f, 0.9f, 1f, 0.12f));
            Place(glow.rectTransform, 0f, 34f, 132f, 132f);
            Place(CreateImage("Icon", c, iconSprite, Color.white).rectTransform, 0f, 54f, 92f, 92f);

            card.Name = CreateText("Name", c, textFont, 34, TextAnchor.MiddleCenter, Color.white, string.Empty);
            card.Name.fontStyle = FontStyle.Bold;
            Place(card.Name.rectTransform, 0f, 162f, 440f, 50f);

            var stars = new GameObject("Stars", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            stars.transform.SetParent(c, false);
            var layout = stars.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            card.StarRow = (RectTransform)stars.transform;
            Place(card.StarRow, 0f, 216f, 300f, 33f);

            card.Level = CreateText("Level", c, numberFont, 22, TextAnchor.MiddleCenter, Dim, string.Empty);
            Place(card.Level.rectTransform, 0f, 252f, 400f, 30f);

            var effectBox = CreateImage("EffectBox", c, null, new Color(0f, 0f, 0f, 0.35f));
            Place(effectBox.rectTransform, 0f, 288f, 388f, 84f);
            var boxLine = effectBox.gameObject.AddComponent<Outline>();
            boxLine.effectColor = new Color(0.2f, 0.9f, 1f, 0.35f);
            boxLine.effectDistance = new Vector2(1f, -1f);
            card.Description = CreateText("Description", effectBox.transform, textFont, 24, TextAnchor.MiddleCenter, Color.white, string.Empty);
            PlaceLeft(card.Description.rectTransform, 0f, 6f, 388f, 36f);
            card.Effect = CreateText("Effect", effectBox.transform, textFont, 26, TextAnchor.MiddleCenter, Cyan, string.Empty);
            card.Effect.supportRichText = true;
            PlaceLeft(card.Effect.rectTransform, 0f, 42f, 388f, 36f);

            card.Buy = CreateButton("BuyButton", c, buyNormal, Gold, string.Empty, out card.Cost);
            card.BuyImage = card.Buy.GetComponent<Image>();
            Place((RectTransform)card.Buy.transform, 0f, 392f, 258f, 110f);
            card.Cost.font = numberFont;
            card.Cost.fontSize = 34;
            card.Cost.fontStyle = FontStyle.Bold;
            card.Cost.rectTransform.offsetMin = new Vector2(44f, 0f);
            card.BuyCoin = CreateImage("CoinIcon", card.Buy.transform, coinIcon, Color.white);
            PlaceLeft(card.BuyCoin.rectTransform, 70f, 37f, 36f, 36f);
            var colors = card.Buy.colors;
            colors.disabledColor = Color.white;   // 비활성 모양은 스프라이트로 표현
            card.Buy.colors = colors;
            var type = manager.Types[cards.Count];
            card.Buy.onClick.AddListener(() => manager.TryPurchase(type));

            card.Shortage = CreateText("Shortage", c, textFont, 22, TextAnchor.MiddleCenter, Warning, "재화 부족");
            card.Shortage.fontStyle = FontStyle.Bold;
            Place(card.Shortage.rectTransform, 0f, 500f, 300f, 28f);
            return card;
        }

        /// <summary>A 화면의 한 줄: 왼쪽 항목명, 오른쪽 값. 값 Text 를 돌려준다</summary>
        private Text CreateRow(Transform parent, string label, float top, int size, Color valueColor, bool emphasize, bool line = true)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            Place((RectTransform)row.transform, 0f, top, 760f, 64f);
            var left = CreateText("Label", row.transform, textFont, size, TextAnchor.MiddleLeft, emphasize ? Cyan : (line ? new Color(0.81f, 0.91f, 0.93f) : Dim), label);
            PlaceLeft(left.rectTransform, 28f, 0f, 480f, 64f);
            var value = CreateText("Value", row.transform, numberFont, emphasize ? 48 : size + 6, TextAnchor.MiddleRight, valueColor, "0");
            value.fontStyle = FontStyle.Bold;
            PlaceLeft(value.rectTransform, 412f, 0f, 320f, 64f);
            if (line)
            {
                var sep = CreateImage("Line", row.transform, null, new Color(1f, 1f, 1f, 0.13f));
                PlaceLeft(sep.rectTransform, 0f, 63f, 760f, 1f);
            }
            return value;
        }

        // ── 생성 도우미 ──

        /// <summary>마우스 클릭을 받으려면 EventSystem 이 필요하다. 씬에 없으면 Input System 용으로 만든다</summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            return go;
        }

        /// <summary>스프라이트가 없으면 단색으로 대신한다</summary>
        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color fallback)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            SetSprite(image, sprite, fallback);
            return image;
        }

        private static void SetSprite(Image image, Sprite sprite, Color fallback)
        {
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : fallback;
        }

        private static Text CreateText(string name, Transform parent, Font font, int size, TextAnchor anchor, Color color, string text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private Button CreateButton(string name, Transform parent, Sprite sprite, Color fallback, string label, out Text labelText)
        {
            var image = CreateImage(name, parent, sprite, fallback);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            labelText = CreateText("Label", image.transform, textFont, 34, TextAnchor.MiddleCenter, Color.white, label);
            labelText.fontStyle = FontStyle.Bold;
            Stretch(labelText.rectTransform);
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
