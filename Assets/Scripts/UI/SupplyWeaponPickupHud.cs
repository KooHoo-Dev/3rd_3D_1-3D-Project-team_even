using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 보급 상자에서 보급 무기를 얻었을 때 화면 우측 중단에 띄우는 카드 (아이콘 · "보급 무기 획득" · 무기명 · 한 줄 활용 설명 · 보급 칸 키).
    /// SupplyCollectedEvent(SupplyCrate 가 발행) 중 Def.IsWeapon 인 것만 받는다 — 디버그 장착·테스트 픽업은 표시하지 않음.
    /// 무기명·아이콘·탄 수는 WeaponTable(WeaponDef), 활용 설명은 SupplyTable.Description (보급 무기 행). 위치·표시 시간은 Inspector.
    /// GameFlowUiBootstrap 이 자동으로 붙인다. 소리는 GameSoundRouter(획득음) 담당
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupplyWeaponPickupHud : MonoBehaviour
    {
        private static readonly Color CardColor = new Color(0.03f, 0.1f, 0.13f, 0.92f);
        /// <summary>무기 상자 빛기둥과 같은 연보라 (SupplyTable.BeaconColor 기본값)</summary>
        private static readonly Color Lavender = new Color(0.784f, 0.635f, 1f, 1f);

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("카드 크기")]
        [SerializeField] private Vector2 cardSize = new Vector2(540f, 184f);
        [Tooltip("화면 오른쪽 가운데 기준 위치 (x 는 음수 = 오른쪽 가장자리에서 안쪽)")]
        [SerializeField] private Vector2 cardPosition = new Vector2(-40f, 40f);
        [Tooltip("카드를 띄워 두는 시간(초, 실제 시간). 활용 설명 두 줄을 읽을 수 있게")]
        [SerializeField, Min(0.5f)] private float showTime = 5f;
        [Tooltip("나타나고 사라지는 시간(초, 실제 시간). 나타날 때 오른쪽에서 밀려 들어온다")]
        [SerializeField, Min(0f)] private float fadeTime = 0.25f;
        [Tooltip("보급 칸 키 안내 문구")]
        [SerializeField] private string slotHint = "[4] 보급 칸";

        private GameFlowUiSkin skin;
        private Canvas canvas;
        private CanvasGroup rootGroup;
        private RectTransform card;
        private Image iconImage;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI descriptionText;
        private TextMeshProUGUI detailText;
        private float shownAt = -1f;

        #region 유니티 메시지
        private void Awake()
        {
            skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
        }

        private void OnEnable() => EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);

        private void OnDisable()
        {
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
            shownAt = -1f;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void Update()
        {
            if (shownAt < 0f) return;

            float t = Time.unscaledTime - shownAt;
            float fadeIn = fadeTime > 0f ? Mathf.Clamp01(t / fadeTime) : 1f;
            float fadeOut = fadeTime > 0f ? Mathf.Clamp01((showTime - t) / fadeTime) : (t < showTime ? 1f : 0f);
            rootGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            card.anchoredPosition = cardPosition + Vector2.right * (1f - fadeIn) * 60f;
            if (t < showTime) return;

            shownAt = -1f;
            canvas.gameObject.SetActive(false);
        }
        #endregion

        /// <summary>보급 무기 상자 획득만. 새로 얻으면 앞 카드를 바로 바꿔 보여 준다 (보급 칸은 하나라 최신 정보만 의미 있음)</summary>
        private void OnSupplyCollected(SupplyCollectedEvent e)
        {
            if (e.Def == null || e.Def.IsWeapon == false || e.ItemAmount <= 0) return; // 같은 무기라 탄창만 받은 경우는 제외
            Show(e.Def);
        }

        private void Show(SupplyDef supply)
        {
            if (canvas == null) Build();
            WeaponDef weapon = DataManager.Weapons.TryGet(supply.WeaponId, out var w) ? w : null;

            Sprite icon = weapon != null ? HudFactory.WeaponIcon(weapon.IconKey) : null;
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            nameText.text = weapon != null ? weapon.Name : supply.DisplayName;
            descriptionText.text = supply.Description;
            detailText.text = slotHint;

            canvas.gameObject.SetActive(true);
            rootGroup.alpha = 0f;
            shownAt = Time.unscaledTime;
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("SupplyWeaponPickupHud", 105, false, out rootGroup);
            card = HudFactory.Rect(canvas.transform, "Card", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), cardPosition, cardSize);
            GameFlowUiSkin.Paint(HudFactory.NewImage(card, null, CardColor), skin.banner, CardColor);
            // 왼쪽 연보라 띠 (무기 상자 빛기둥 색)
            HudFactory.NewImage(HudFactory.Rect(card, "Accent", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, cardSize.y)), null, Lavender);

            var iconRt = HudFactory.Rect(card, "Icon", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -40f), new Vector2(88f, 88f));
            iconImage = HudFactory.NewImage(iconRt, null, Color.white);
            iconImage.preserveAspect = true;

            // 위에서부터: 제목 · 무기명 · 활용 설명(최대 2줄) · 보급 칸 키
            float textLeft = 124f, textWidth = cardSize.x - textLeft - 18f;
            var title = HudFactory.Label(card, "Title", textFont, 18f, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -12f), new Vector2(textWidth, 24f));
            title.text = "보급 무기 획득";
            title.color = Lavender;
            nameText = HudFactory.Label(card, "Name", textFont, 32f, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -36f), new Vector2(textWidth, 40f));
            descriptionText = HudFactory.Label(card, "Description", textFont, 20f, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -80f), new Vector2(textWidth, 56f));
            descriptionText.textWrappingMode = TextWrappingModes.Normal;
            descriptionText.color = Color.white;
            detailText = HudFactory.Label(card, "Detail", textFont, 16f, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -144f), new Vector2(textWidth, 24f));
            detailText.color = new Color(HudFactory.TextColor.r, HudFactory.TextColor.g, HudFactory.TextColor.b, 0.7f);

            canvas.gameObject.SetActive(false);
        }
    }
}
