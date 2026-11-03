using System.Collections.Generic;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 무기 해금 알림 (화면 위쪽 배너). WeaponUnlockedEvent(WeaponUnlockLedger, 스테이지 클리어 때) 를 받아 표시한다.
    /// 보상 화면(UpgradeScreen)·무기 선택 화면(정렬 100) 위에 보이도록 정렬 110. 보상 단계는 timeScale = 0 이라 실제 시간으로 센다.
    /// GameFlowUiBootstrap 이 자동으로 붙인다. 여러 개면 차례로 보여 준다.
    /// 스타일은 UiTheme: 양끝이 흐려지는 띠 + 양옆 가는 선을 단 호박색 제목, 무기 아이콘은 달빛 흰색 선
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnlockToastHud : MonoBehaviour
    {
        private static readonly Vector2 BannerSize = new Vector2(760f, 150f);

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("배너를 띄워 두는 시간(초, 실제 시간)")]
        [SerializeField, Min(0.5f)] private float showTime = 4f;
        [Tooltip("나타나고 사라지는 시간(초, 실제 시간)")]
        [SerializeField, Min(0f)] private float fadeTime = 0.25f;

        private readonly Queue<WeaponDef> pending = new Queue<WeaponDef>();
        private GameFlowUiSkin skin;
        private Canvas canvas;
        private CanvasGroup rootGroup;
        private Image iconImage;
        private TextMeshProUGUI nameText;
        private float shownAt = -1f;

        #region 유니티 메시지
        private void Awake()
        {
            skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
        }

        private void OnEnable() => EventBus<WeaponUnlockedEvent>.Subscribe(OnWeaponUnlocked);

        private void OnDisable()
        {
            EventBus<WeaponUnlockedEvent>.Unsubscribe(OnWeaponUnlocked);
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

        private void OnWeaponUnlocked(WeaponUnlockedEvent e)
        {
            if (e.Weapon != null) pending.Enqueue(e.Weapon);
        }

        private void Show(WeaponDef weapon)
        {
            if (canvas == null) Build();
            Sprite icon = HudFactory.WeaponIcon(weapon.IconKey);
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            nameText.text = weapon.Name;
            canvas.gameObject.SetActive(true);
            rootGroup.alpha = 0f;
            shownAt = Time.unscaledTime;
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("UnlockToastHud", 110, false, out rootGroup);
            var banner = HudFactory.Rect(canvas.transform, "Banner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), BannerSize);
            UiThemeKit.Band(banner, UiThemeKit.BandShape.FadeBoth, 0.85f);

            // 제목: 호박색 + 양옆 가는 선
            var title = HudFactory.Label(banner, "Title", textFont, 20f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(180f, 32f));
            title.text = "새 무기 해금";
            title.color = UiTheme.Ember;
            UiThemeKit.Line(banner, "LineLeft", UiTheme.Steel, new Vector2(0.5f, 1f), new Vector2(1f, 0.5f), new Vector2(-84f, -28f), new Vector2(60f, 1f));
            UiThemeKit.Line(banner, "LineRight", UiTheme.Steel, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(84f, -28f), new Vector2(60f, 1f));

            // 띠 양끝이 흐려지므로 아이콘·글자는 안쪽에
            iconImage = HudFactory.NewImage(HudFactory.Rect(banner, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, -14f), new Vector2(170f, 64f)), null, UiTheme.MoonWhite);
            iconImage.preserveAspect = true;

            const float textX = 344f;
            nameText = HudFactory.Label(banner, "Name", textFont, 32f, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(textX, 0f), new Vector2(BannerSize.x - textX - 150f, 50f));
            nameText.color = UiTheme.MoonWhite;
            var note = HudFactory.Label(banner, "Note", textFont, 16f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(BannerSize.x - 200f, 24f));
            note.text = "다음 스테이지 무기 선택에서 장착할 수 있습니다";
            note.color = UiTheme.Steel;

            canvas.gameObject.SetActive(false);
        }
    }
}
