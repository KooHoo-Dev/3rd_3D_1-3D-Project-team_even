using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HybridShooter.UI
{
    /// <summary>
    /// 화면 우측 중단의 보급 정보 카드. 카드 하나를 두 용도로 쓴다.
    ///  ① 획득 카드 (우선): 보급 상자에서 보급 무기를 얻으면 showTime 초 — 아이콘 · "보급 무기 획득" · 무기명 · 활용 설명 · 보급 칸 키.
    ///     SupplyCollectedEvent 중 Def.IsWeapon 이고 ItemAmount 1(장착함)인 것만. 무기명·아이콘 = WeaponTable, 설명 = SupplyTable.Description
    ///  ② 조준 카드: 화면 중앙 조준선이 aimRange 안의 착지한 보급 상자(IProximityInteractable)에 닿아 있는 동안 — "보급 상자" · 상자 이름 · 설명 · 거리.
    ///     내용은 계약 GetPrompt(플레이어) 로 받는다 (SupplyCrate 를 직접 참조하지 않음).
    ///     가까이 가서 상호작용 프롬프트가 뜨면(InteractFocusChangedEvent) 숨긴다 — 근처 안내는 InteractPromptHud 담당
    ///     사격 중(WeaponFiredEvent 뒤 hideAfterFire 초)에도 숨긴다
    /// 카드 높이는 설명 길이에 맞춰 늘어난다 (글자가 잘리지 않게). 위치·크기·시간·사거리는 Inspector.
    /// GameFlowUiBootstrap 이 자동으로 붙인다. 소리는 GameSoundRouter(획득음) 담당
    /// 스타일은 UiTheme: 상자 스프라이트 대신 왼쪽 끝이 흐려지는 띠, 제목 앞 짧은 표식 (획득 = 호박색, 조준 = 청회색)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupplyInfoCardHud : MonoBehaviour
    {
        private enum Mode { None, Pickup, Aim }

        /// <summary>획득 카드 강조색 (보급 무기 = 보상과 같은 호박색)</summary>
        private static readonly Color PickupAccent = UiTheme.Ember;
        /// <summary>조준 카드 강조색</summary>
        private static readonly Color AimAccent = UiTheme.Steel;
        private static readonly Color WarningColor = UiTheme.Ember;
        /// <summary>띠가 카드 왼쪽 밖으로 더 나가는 길이 (흐려지는 구간이 글자 밖에 오게)</summary>
        private const float BandOverhang = 120f;
        /// <summary>Unity 기본 Ignore Raycast 레이어 (상자 근접 영역). 조준 판정에서 뺀다</summary>
        private const int IgnoreRaycastLayer = 2;
        private const int MaxHits = 16;

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("카드 폭")]
        [SerializeField] private float cardWidth = 580f;
        [Tooltip("카드 최소 높이 (설명이 길면 그만큼 늘어난다)")]
        [SerializeField] private float minCardHeight = 150f;
        [Tooltip("카드 배경 불투명도 (0 = 투명, 1 = 불투명). 뒤의 전장이 비쳐 보이게")]
        [SerializeField, Range(0f, 1f)] private float cardAlpha = 0.65f;
        [Tooltip("화면 오른쪽 가운데 기준 위치 (x 는 음수 = 오른쪽 가장자리에서 안쪽)")]
        [SerializeField] private Vector2 cardPosition = new Vector2(-40f, 40f);
        [Tooltip("획득 카드를 띄워 두는 시간(초, 실제 시간). 활용 설명을 읽을 수 있게")]
        [SerializeField, Min(0.5f)] private float showTime = 5f;
        [Tooltip("나타나고 사라지는 시간(초, 실제 시간). 나타날 때 오른쪽에서 밀려 들어온다")]
        [SerializeField, Min(0f)] private float fadeTime = 0.25f;
        [Tooltip("보급 칸 키 안내 문구 (획득 카드)")]
        [SerializeField] private string slotHint = "[4] 보급 칸";
        [Tooltip("조준 카드를 보여 주는 거리(m, 플레이어 ↔ 상자)")]
        [SerializeField, Min(1f)] private float aimRange = 30f;
        [Tooltip("마지막 사격 뒤 이 시간(초, 실제 시간) 동안은 조준 카드를 띄우지 않는다 (사격 중 시야 방해 방지)")]
        [SerializeField, Min(0f)] private float hideAfterFire = 0.6f;

        private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        private GameFlowUiSkin skin;
        private Canvas canvas;
        private CanvasGroup rootGroup;
        private RectTransform card;
        private Image accentImage;
        private Image iconImage;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI distanceText;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI descriptionText;
        private TextMeshProUGUI detailText;

        private Mode mode;
        private float shownAt = -1f;
        private bool promptFocused;
        private float lastFiredAt = float.NegativeInfinity;
        private IInteractable aimTarget;
        private InteractPrompt aimPrompt;

        #region 유니티 메시지
        private void Awake()
        {
            skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
        }

        private void OnEnable()
        {
            EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);
            EventBus<InteractFocusChangedEvent>.Subscribe(OnInteractFocusChanged);
            EventBus<WeaponFiredEvent>.Subscribe(OnWeaponFired);
        }

        private void OnDisable()
        {
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
            EventBus<InteractFocusChangedEvent>.Unsubscribe(OnInteractFocusChanged);
            EventBus<WeaponFiredEvent>.Unsubscribe(OnWeaponFired);
            Hide();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void Update()
        {
            if (mode == Mode.Pickup)
            {
                UpdatePickup();
                return;
            }
            UpdateAim();
        }
        #endregion

        // ── ① 획득 카드 ──

        /// <summary>보급 무기를 장착한 획득만. 새로 얻으면 앞 카드를 바로 바꿔 보여 준다 (보급 칸은 하나라 최신 정보만 의미 있음)</summary>
        private void OnSupplyCollected(SupplyCollectedEvent e)
        {
            if (e.Def == null || e.Def.IsWeapon == false || e.ItemAmount <= 0) return; // 같은 무기라 탄창만 받은 경우는 제외
            ShowPickup(e.Def);
        }

        private void ShowPickup(SupplyDef supply)
        {
            if (canvas == null) Build();
            WeaponDef weapon = DataManager.Weapons.TryGet(supply.WeaponId, out var w) ? w : null;
            Sprite icon = weapon != null ? HudFactory.WeaponIcon(weapon.IconKey) : null;

            SetContent("보급 무기 획득", PickupAccent, icon, "", weapon != null ? weapon.Name : supply.DisplayName, supply.Description, slotHint, false);
            mode = Mode.Pickup;
            aimTarget = null;
            canvas.gameObject.SetActive(true);
            rootGroup.alpha = 0f;
            shownAt = Time.unscaledTime;
        }

        private void UpdatePickup()
        {
            float t = Time.unscaledTime - shownAt;
            float fadeIn = fadeTime > 0f ? Mathf.Clamp01(t / fadeTime) : 1f;
            float fadeOut = fadeTime > 0f ? Mathf.Clamp01((showTime - t) / fadeTime) : (t < showTime ? 1f : 0f);
            rootGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            card.anchoredPosition = cardPosition + Vector2.right * (1f - fadeIn) * 60f;
            if (t >= showTime) Hide();
        }

        // ── ② 조준 카드 ──

        private void OnInteractFocusChanged(InteractFocusChangedEvent e) => promptFocused = e.HasTarget;

        /// <summary>플레이어가 든 총이 쏠 때만 발행된다 (Gun.IsCurrent). 사격 중에는 조준 카드를 숨긴다</summary>
        private void OnWeaponFired(WeaponFiredEvent e) => lastFiredAt = Time.unscaledTime;

        private bool IsFiring => Time.unscaledTime - lastFiredAt < hideAfterFire;

        /// <summary>조준 대상이 있으면 내용을 갱신하며 보이고, 없으면 빠르게 사라진다</summary>
        private void UpdateAim()
        {
            IInteractable target = null;
            float distance = 0f;
            GameObject user = null;
            if (promptFocused == false && IsFiring == false && TryGetPlayer(out user)) target = FindAimTarget(user, out distance);

            if (target == null)
            {
                if (mode != Mode.Aim) return;
                if (IsFiring)
                {
                    Hide(); // 사격하면 바로 숨김
                    return;
                }
                rootGroup.alpha = fadeTime > 0f ? Mathf.MoveTowards(rootGroup.alpha, 0f, Time.unscaledDeltaTime / fadeTime) : 0f;
                if (rootGroup.alpha <= 0f) Hide();
                return;
            }

            if (canvas == null) Build();
            InteractPrompt prompt = target.GetPrompt(user);
            if (mode != Mode.Aim || ReferenceEquals(target, aimTarget) == false || prompt.Equals(aimPrompt) == false)
            {
                bool hasWarning = string.IsNullOrEmpty(prompt.Warning) == false;
                SetContent("보급 상자", AimAccent, null, "", prompt.Title, prompt.Description, hasWarning ? prompt.Warning : "", hasWarning);
                if (mode != Mode.Aim) rootGroup.alpha = 0f;
                mode = Mode.Aim;
                aimTarget = target;
                aimPrompt = prompt;
                canvas.gameObject.SetActive(true);
                card.anchoredPosition = cardPosition;
            }
            distanceText.SetText("{0:0}m", distance);
            rootGroup.alpha = fadeTime > 0f ? Mathf.MoveTowards(rootGroup.alpha, 1f, Time.unscaledDeltaTime / fadeTime) : 1f;
        }

        /// <summary>
        /// 화면 중앙 조준선 레이 (트리거 포함, 근접 영역 레이어 제외, 가까운 순). 첫 비트리거 콜라이더(벽·지면)에서 멈춘다.
        /// 플레이어에서 aimRange 안의, 상호작용 가능한(착지한) IProximityInteractable 만
        /// </summary>
        private IInteractable FindAimTarget(GameObject user, out float distance)
        {
            distance = 0f;
            var cam = Camera.main;
            if (cam == null) return null;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float length = aimRange + Vector3.Distance(cam.transform.position, user.transform.position);
            int count = Physics.RaycastNonAlloc(ray, hits, length, ~(1 << IgnoreRaycastLayer), QueryTriggerInteraction.Collide);
            if (count == 0) return null;
            Array.Sort(hits, 0, count, Comparer.Instance);

            for (int i = 0; i < count; i++)
            {
                Collider col = hits[i].collider;
                if (col == null || col.transform.IsChildOf(user.transform)) continue;

                var target = col.GetComponentInParent<IProximityInteractable>();
                if (target != null && IsAlive(target) && target.CanInteract(user))
                {
                    Transform point = target.InteractTransform != null ? target.InteractTransform : col.transform;
                    distance = Vector3.Distance(user.transform.position, point.position);
                    return distance <= aimRange ? target : null;
                }
                if (col.isTrigger == false) return null; // 벽·지면 등에 막힘
            }
            return null;
        }

        private static bool TryGetPlayer(out GameObject user)
        {
            user = ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null ? player.Root.gameObject : null;
            return user != null;
        }

        /// <summary>파괴된 MonoBehaviour 도 인터페이스로는 null 이 아니라서 따로 확인</summary>
        private static bool IsAlive(IInteractable target) => target is Object obj ? obj != null : target != null;

        // ── 공통 ──

        private void Hide()
        {
            mode = Mode.None;
            shownAt = -1f;
            aimTarget = null;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        /// <summary>내용을 채우고, 아이콘 유무·설명 길이에 맞춰 배치와 카드 높이를 다시 잡는다</summary>
        private void SetContent(string title, Color accent, Sprite icon, string distance, string itemName, string description, string footer, bool footerIsWarning)
        {
            accentImage.color = accent;
            titleText.text = title;
            titleText.color = accent;
            distanceText.text = distance;
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            nameText.text = itemName;
            descriptionText.text = description;
            detailText.text = footer;
            detailText.color = footerIsWarning ? WarningColor : UiTheme.Steel;
            Layout(icon != null);
        }

        /// <summary>
        /// 위에서부터 제목(+거리) · 이름 · 설명(줄 수 제한 없음) · 아래 줄을 쌓는다.
        /// 각 줄 높이는 TMP 선호 크기로 잰다 (칸이 글꼴 줄 높이보다 낮으면 TMP 가 그 줄을 통째로 숨기기 때문)
        /// </summary>
        private void Layout(bool hasIcon)
        {
            float left = hasIcon ? 124f : 26f;
            float width = cardWidth - left - 20f;
            float y = 12f;

            float titleHeight = LineHeight(titleText, width);
            Place(titleText.rectTransform, left, y, width, titleHeight);
            Place(distanceText.rectTransform, left, y, width, titleHeight);
            // 제목 앞 짧은 세로 표식
            Place(accentImage.rectTransform, left - 14f, y + titleHeight * 0.2f, 3f, titleHeight * 0.6f);
            y += titleHeight + 2f;

            y = Stack(nameText, left, y, width, 4f);
            y = Stack(descriptionText, left, y, width, 6f);
            y = Stack(detailText, left, y, width, 0f);

            float height = Mathf.Max(minCardHeight, y + 14f);
            card.sizeDelta = new Vector2(cardWidth, height);
        }

        /// <summary>글자가 있으면 선호 높이로 배치하고 다음 줄 위치를 돌려준다 (비었으면 자리 없음)</summary>
        private static float Stack(TextMeshProUGUI text, float left, float y, float width, float gap)
        {
            if (string.IsNullOrEmpty(text.text))
            {
                Place(text.rectTransform, left, y, width, 0f);
                return y;
            }
            float height = LineHeight(text, width);
            Place(text.rectTransform, left, y, width, height);
            return y + height + gap;
        }

        private static float LineHeight(TextMeshProUGUI text, float width) =>
            Mathf.Ceil(text.GetPreferredValues(string.IsNullOrEmpty(text.text) ? " " : text.text, width, 0f).y);

        private static void Place(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("SupplyInfoCardHud", 105, false, out rootGroup);
            card = HudFactory.Rect(canvas.transform, "Card", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), cardPosition, new Vector2(cardWidth, minCardHeight));
            // 바탕: 카드보다 왼쪽으로 조금 더 나간 띠. 왼쪽 끝만 흐려 글자 뒤는 진하다 (카드 높이를 따라 늘어난다)
            var bandArea = HudFactory.Stretch(card, "BandArea");
            bandArea.offsetMin = new Vector2(-BandOverhang, 0f);
            UiThemeKit.Band(bandArea, UiThemeKit.BandShape.FadeLeft, Mathf.Clamp01(cardAlpha + 0.15f), 0.3f);
            // 제목 앞 표식 (획득 카드 = 호박색, 조준 카드 = 청회색). 위치는 Layout 이 잡는다
            accentImage = HudFactory.NewImage(HudFactory.Rect(card, "Accent", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 20f)), null, PickupAccent);

            var iconRt = HudFactory.Rect(card, "Icon", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -40f), new Vector2(88f, 88f));
            iconImage = HudFactory.NewImage(iconRt, null, UiTheme.MoonWhite);
            iconImage.preserveAspect = true;

            Vector2 topLeft = new Vector2(0f, 1f);
            titleText = HudFactory.Label(card, "Title", textFont, 18f, TextAlignmentOptions.Left, topLeft, topLeft, Vector2.zero, Vector2.zero);
            distanceText = HudFactory.Label(card, "Distance", UiTheme.NumberFont != null ? UiTheme.NumberFont : textFont, 18f, TextAlignmentOptions.Right, topLeft, topLeft, Vector2.zero, Vector2.zero);
            distanceText.color = UiTheme.MoonWhite;
            nameText = HudFactory.Label(card, "Name", textFont, 32f, TextAlignmentOptions.Left, topLeft, topLeft, Vector2.zero, Vector2.zero);
            nameText.color = UiTheme.MoonWhite;
            descriptionText = HudFactory.Label(card, "Description", textFont, 20f, TextAlignmentOptions.TopLeft, topLeft, topLeft, Vector2.zero, Vector2.zero);
            descriptionText.textWrappingMode = TextWrappingModes.Normal;
            descriptionText.overflowMode = TextOverflowModes.Overflow;
            // 한 줄 칸들은 높이를 선호 크기로 잡으니 말줄임 대신 넘침 허용 (칸이 줄 높이보다 낮아 통째로 숨는 일 방지)
            titleText.overflowMode = TextOverflowModes.Overflow;
            nameText.overflowMode = TextOverflowModes.Overflow;
            nameText.fontStyle = FontStyles.Bold;
            descriptionText.color = UiTheme.Steel;
            detailText = HudFactory.Label(card, "Detail", textFont, 16f, TextAlignmentOptions.Left, topLeft, topLeft, Vector2.zero, Vector2.zero);
            detailText.overflowMode = TextOverflowModes.Overflow;
            detailText.textWrappingMode = TextWrappingModes.Normal;

            canvas.gameObject.SetActive(false);
        }

        private sealed class Comparer : IComparer<RaycastHit>
        {
            public static readonly Comparer Instance = new Comparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
