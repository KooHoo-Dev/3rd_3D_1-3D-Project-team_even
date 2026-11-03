using System.Collections.Generic;
using HybridShooter.Core.Events;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 보스 클리어 후 다음 라운드 시작 지급 알림 (화면 위쪽 배너). RoundSupplyGrantedEvent(RoundStartSupply) 를 받아 실제 지급량을 표시한다.
    /// 해금 알림(UnlockToastHud) 배너 아래에 놓여 겹치지 않는다. 라운드 시작 직전은 timeScale = 0 일 수 있어 실제 시간으로 센다.
    /// GameFlowUiBootstrap 이 자동으로 붙인다. 소리는 GameSoundRouter 담당.
    /// 스타일은 UiTheme: 양끝이 흐려지는 띠 + 양옆 가는 선을 단 호박색 제목 (글자만, 상자 스프라이트 없음)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundSupplyToastHud : MonoBehaviour
    {
        private static readonly Vector2 BannerSize = new Vector2(640f, 110f);

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("배너를 띄워 두는 시간(초, 실제 시간)")]
        [SerializeField, Min(0.5f)] private float showTime = 3.5f;
        [Tooltip("나타나고 사라지는 시간(초, 실제 시간)")]
        [SerializeField, Min(0f)] private float fadeTime = 0.25f;

        private readonly Queue<RoundSupplyGrantedEvent> pending = new Queue<RoundSupplyGrantedEvent>();
        private GameFlowUiSkin skin;
        private Canvas canvas;
        private CanvasGroup rootGroup;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI detailText;
        private float shownAt = -1f;

        #region 유니티 메시지
        private void Awake()
        {
            skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
        }

        private void OnEnable() => EventBus<RoundSupplyGrantedEvent>.Subscribe(OnRoundSupplyGranted);

        private void OnDisable()
        {
            EventBus<RoundSupplyGrantedEvent>.Unsubscribe(OnRoundSupplyGranted);
            pending.Clear();
            shownAt = -1f;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void Update()
        {
            if (shownAt < 0f)
            {
                if (pending.Count > 0) Show(pending.Dequeue());
                return;
            }

            float t = Time.unscaledTime - shownAt;
            float fadeIn = fadeTime > 0f ? Mathf.Clamp01(t / fadeTime) : 1f;
            float fadeOut = fadeTime > 0f ? Mathf.Clamp01((showTime - t) / fadeTime) : (t < showTime ? 1f : 0f);
            rootGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            if (t < showTime) return;

            shownAt = -1f;
            canvas.gameObject.SetActive(false);
        }
        #endregion

        private void OnRoundSupplyGranted(RoundSupplyGrantedEvent e) => pending.Enqueue(e);

        private void Show(RoundSupplyGrantedEvent e)
        {
            if (canvas == null) Build();
            titleText.text = $"라운드 {e.Round} 보급";

            var parts = new List<string>(3);
            if (e.Grenades > 0) parts.Add($"수류탄 +{e.Grenades}");
            if (e.HealPacks > 0) parts.Add($"회복 팩 +{e.HealPacks}");
            if (e.MagazinesAdded > 0) parts.Add($"예비 탄창 +{e.MagazinesAdded}");
            detailText.text = string.Join("   ·   ", parts);

            canvas.gameObject.SetActive(true);
            rootGroup.alpha = 0f;
            shownAt = Time.unscaledTime;
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("RoundSupplyToastHud", 110, false, out rootGroup);
            // 해금 알림 배너(위 60, 높이 150) 아래
            var banner = HudFactory.Rect(canvas.transform, "Banner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -240f), BannerSize);
            UiThemeKit.Band(banner, UiThemeKit.BandShape.FadeBoth, 0.85f);

            // 제목: 호박색 + 양옆 가는 선
            titleText = HudFactory.Label(banner, "Title", textFont, 20f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(180f, 32f));
            titleText.color = UiTheme.Ember;
            UiThemeKit.Line(banner, "LineLeft", UiTheme.Steel, new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(-96f, -28f), new Vector2(60f, 1f));
            UiThemeKit.Line(banner, "LineRight", UiTheme.Steel, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(96f, -28f), new Vector2(60f, 1f));

            detailText = HudFactory.Label(banner, "Detail", textFont, 26f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(BannerSize.x - 80f, 44f));
            detailText.color = UiTheme.MoonWhite;

            canvas.gameObject.SetActive(false);
        }
    }
}
