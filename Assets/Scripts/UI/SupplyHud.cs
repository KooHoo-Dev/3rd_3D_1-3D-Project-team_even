using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 보급 확인용 HUD (BL-019).
    ///  - 수류탄 / 회복 팩 보유 수량 (GrenadeCountChangedEvent, HealPackCountChangedEvent)
    ///  - 보급 투하·획득 알림 (SupplyDroppedEvent, SupplyCollectedEvent)
    /// 이벤트만 구독하므로 플레이어·보급 구현을 직접 참조하지 않는다.
    /// Text 를 연결하지 않으면 실행 시 기본 레이아웃을 코드로 만든다.
    /// </summary>
    public sealed class SupplyHud : MonoBehaviour
    {
        [SerializeField] private Text grenadeText;
        [SerializeField] private Text healPackText;
        [SerializeField] private Text messageText;
        [SerializeField] private Text helpText;

        [TextArea]
        [SerializeField] private string help = "[1] 보급 투하   [W A S D] 이동   [Shift] 달리기   [G] 수류탄 사용   [V] 회복 팩 사용";
        [SerializeField] private float messageDuration = 4f;
        [SerializeField] private int maxMessages = 5;
        [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private float highlightDuration = 0.6f;

        private struct Message
        {
            public string Text;
            public float ExpireTime;
        }

        private readonly List<Message> messages = new List<Message>();
        private Color normalColor = Color.white;
        private float grenadeHighlightEnd, healHighlightEnd;
        private int lastGrenades = -1, lastHealPacks = -1;

        private void Awake()
        {
            if (grenadeText == null || healPackText == null || messageText == null) BuildDefaultLayout();
            normalColor = grenadeText.color;
            if (helpText != null) helpText.text = help;
            SetGrenades(0, 0);
            SetHealPacks(0, 0);
            messageText.text = string.Empty;
        }

        private void OnEnable()
        {
            EventBus<GrenadeCountChangedEvent>.Subscribe(OnGrenadeCountChanged);
            EventBus<HealPackCountChangedEvent>.Subscribe(OnHealPackCountChanged);
            EventBus<SupplyDroppedEvent>.Subscribe(OnSupplyDropped);
            EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);
        }

        private void OnDisable()
        {
            EventBus<GrenadeCountChangedEvent>.Unsubscribe(OnGrenadeCountChanged);
            EventBus<HealPackCountChangedEvent>.Unsubscribe(OnHealPackCountChanged);
            EventBus<SupplyDroppedEvent>.Unsubscribe(OnSupplyDropped);
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
        }

        private void Update()
        {
            grenadeText.color = Time.time < grenadeHighlightEnd ? highlightColor : normalColor;
            healPackText.color = Time.time < healHighlightEnd ? highlightColor : normalColor;

            if (messages.Count > 0 && messages[0].ExpireTime <= Time.time)
            {
                messages.RemoveAll(m => m.ExpireTime <= Time.time);
                RefreshMessages();
            }
        }

        // ── 이벤트 ──

        private void OnGrenadeCountChanged(GrenadeCountChangedEvent e)
        {
            if (lastGrenades >= 0 && e.Current > lastGrenades) grenadeHighlightEnd = Time.time + highlightDuration;
            lastGrenades = e.Current;
            SetGrenades(e.Current, e.Max);
        }

        private void OnHealPackCountChanged(HealPackCountChangedEvent e)
        {
            if (lastHealPacks >= 0 && e.Current > lastHealPacks) healHighlightEnd = Time.time + highlightDuration;
            lastHealPacks = e.Current;
            SetHealPacks(e.Current, e.Max);
        }

        private void OnSupplyDropped(SupplyDroppedEvent e)
        {
            string distance = string.Empty;
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null)
            {
                Vector3 offset = e.Position - player.Root.position;
                offset.y = 0f;
                distance = $" — {offset.magnitude:0}m";
            }
            PushMessage($"보급 투하: {GetSupplyName(e.Def.SupplyType)}{distance}");
        }

        private void OnSupplyCollected(SupplyCollectedEvent e)
            => PushMessage($"{GetSupplyName(e.Def.SupplyType)} +{e.Def.Amount} 획득!");

        // ── 표시 ──

        private void SetGrenades(int current, int max) => grenadeText.text = $"수류탄   {current} / {max}";
        private void SetHealPacks(int current, int max) => healPackText.text = $"회복 팩   {current} / {max}";

        private static string GetSupplyName(SupplyType type) => type == SupplyType.Grenade ? "수류탄" : "회복 팩";

        private void PushMessage(string text)
        {
            messages.Add(new Message { Text = text, ExpireTime = Time.time + messageDuration });
            while (messages.Count > maxMessages) messages.RemoveAt(0);
            RefreshMessages();
        }

        private void RefreshMessages()
        {
            var lines = new string[messages.Count];
            for (int i = 0; i < messages.Count; i++) lines[i] = messages[i].Text;
            messageText.text = string.Join("\n", lines);
        }

        // ── 기본 레이아웃 (씬에서 Text 를 연결하지 않은 경우) ──

        private void BuildDefaultLayout()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("SupplyHudCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 좌상단 수량 패널
            var panel = new GameObject("CountPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(800f, -30f);
            panelRect.sizeDelta = new Vector2(360f, 130f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            grenadeText = CreateText("GrenadeText", panel.transform, font, 40, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(24f, 0f), new Vector2(-16f, 0f));
            healPackText = CreateText("HealPackText", panel.transform, font, 40, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(24f, 0f), new Vector2(-16f, 0f));

            // 상단 중앙 알림
            messageText = CreateText("MessageText", canvasGo.transform, font, 34, TextAnchor.UpperCenter,
                new Vector2(0.25f, 0.6f), new Vector2(0.75f, 1f), new Vector2(0f, 0f), new Vector2(0f, -30f));

            // 하단 조작 안내
            helpText = CreateText("HelpText", canvasGo.transform, font, 28, TextAnchor.LowerCenter,
                new Vector2(0f, 0f), new Vector2(1f, 0.1f), new Vector2(0f, 20f), new Vector2(0f, 0f));
        }

        private static Text CreateText(string name, Transform parent, Font font, int size, TextAnchor anchor,
                                       Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return text;
        }
    }
}
