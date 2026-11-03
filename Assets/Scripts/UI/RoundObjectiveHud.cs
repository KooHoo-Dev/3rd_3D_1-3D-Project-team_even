using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using HybridShooter.Round;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;

namespace HybridShooter.UI
{
    /// <summary>
    /// 라운드 목표 HUD (화면 좌측 상단 한 블록): 라운드 번호·유형 / 한 줄 목표 / 핵심 수치.
    /// 섬멸전(섬멸 후 보스 포함) = 처치 비율 막대 + 작은 "처치 37 / N" + 작은 증원 남은 시간 ·생존전 = 큰 남은 시간 + 얇은 진행 막대 · 보스 라운드 = 목표 문구만.
    /// RoundSequencer 의 공개 값(CurrentRoundDef·RoundNumber·Phase·KillCount·KillTarget·ElapsedSeconds)을 매 프레임 읽어 그린다.
    /// 군중전 동안만 보이고(보스전은 보스 체력 막대가 위쪽을 쓴다), 라운드 시작 직후 잠깐 크게 보여 목표를 알린다.
    /// GameFlowUiBootstrap 이 자동으로 붙인다.
    /// 스타일은 UiTheme (시안 12): 왼쪽 가장자리부터 오른쪽으로 흐려지는 띠 + 짧은 붉은 표식, 왼쪽 정렬 글자, 얇은 단색 막대
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundObjectiveHud : MonoBehaviour
    {
        private static readonly Color BarBackColor = UiTheme.GaugeBack;
        private static readonly Vector2 PanelSize = new Vector2(520f, 180f);
        /// <summary>글자 시작 x (왼쪽 표식 자리)</summary>
        private const float TextX = 64f;
        // 라벨 상자 높이는 글자 크기 × 1.45 이상 (NotoSansKR 줄 높이). 작으면 Ellipsis 가 줄을 통째로 숨긴다

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("라운드 시작 뒤 크게 보여 주는 시간(초, 라운드 시간)")]
        [SerializeField, Min(0f)] private float introTime = 3f;
        [Tooltip("시작 안내 때 블록 크기 배율")]
        [SerializeField, Min(1f)] private float introScale = 1.3f;
        [Tooltip("평소 블록 크기 배율 (1 = 원래 크기)")]
        [SerializeField, Min(0.1f)] private float hudScale = 0.8f;

        private RoundSequencer roundSequencer;
        private Canvas canvas;
        private RectTransform panel;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI objectiveText;
        private TextMeshProUGUI valueText;
        private TextMeshProUGUI detailText;
        private RectTransform timeBar;
        private RectTransform timeBarFill;  // 오른쪽 끝(anchorMax.x) = 남은 시간 비율
        private TextMeshProUGUI killCountText;
        private RectTransform killBar;
        private RectTransform killBarFill;  // 오른쪽 끝(anchorMax.x) = 처치 비율

        #region 유니티 메시지
        private void Awake()
        {
            if (textFont == null) textFont = GameFlowUiSkin.Current.textFont;
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        /// <summary>군중전이면 라운드 유형에 맞는 목표를 그리고, 아니면 숨긴다</summary>
        private void Update()
        {
            if (roundSequencer == null) roundSequencer = FindAnyObjectByType<RoundSequencer>();
            bool isCrowdBattle = roundSequencer != null && roundSequencer.IsRunning && roundSequencer.Phase == GameState.CrowdBattle;
            if (isCrowdBattle == false)
            {
                if (canvas != null) canvas.gameObject.SetActive(false);
                return;
            }

            if (canvas == null) Build();
            canvas.gameObject.SetActive(true);
            ShowObjective(roundSequencer.CurrentRoundDef);
            FitHeight(roundSequencer.CurrentRoundDef.Type);
            UpdateIntroScale();
        }
        #endregion

        /// <summary>라운드 유형별 문구·수치를 채운다. 문구는 기획 UI 안을 그대로 쓴다</summary>
        /// <param name="def">진행 중인 라운드 데이터</param>
        private void ShowObjective(RoundDef def)
        {
            float elapsedSeconds = roundSequencer.ElapsedSeconds;
            switch (def.Type)
            {
                case RoundType.CrowdToBoss:
                    titleText.text = $"라운드 {roundSequencer.RoundNumber} · 보스전";
                    objectiveText.text = "적의 공세를 버티고 보스를 처치하세요";
                    valueText.text = "";
                    detailText.text = "";
                    killCountText.text = "";
                    killBar.gameObject.SetActive(false);
                    timeBar.gameObject.SetActive(false);
                    break;
                case RoundType.Annihilation:
                case RoundType.AnnihilationToBoss:
                    float reinforcementLeft = def.PhaseTime - elapsedSeconds;
                    int killTarget = roundSequencer.KillTarget;
                    titleText.text = def.HasBoss ? $"라운드 {roundSequencer.RoundNumber} · 섬멸전 → 보스전" : $"라운드 {roundSequencer.RoundNumber} · 섬멸전";
                    objectiveText.text = def.HasBoss ? $"적 {killTarget}마리를 처치해 보스를 끌어내세요" : $"적 {killTarget}마리를 처치하세요";
                    valueText.text = "";
                    detailText.text = reinforcementLeft > 0f ? $"증원 종료까지 {FormatTime(reinforcementLeft)}" : "증원 종료";
                    killCountText.text = $"처치 {roundSequencer.KillCount} / {killTarget}";
                    killBar.gameObject.SetActive(true);
                    killBarFill.anchorMax = new Vector2(Mathf.Clamp01(roundSequencer.KillCount / (float)killTarget), 1f);
                    timeBar.gameObject.SetActive(false);
                    break;
                case RoundType.Survival:
                    float survivalLeft = Mathf.Max(0f, def.PhaseTime - elapsedSeconds);
                    titleText.text = $"라운드 {roundSequencer.RoundNumber} · 생존전";
                    objectiveText.text = "시간이 끝날 때까지 생존하세요";
                    valueText.text = FormatTime(survivalLeft);
                    detailText.text = "";
                    killCountText.text = "";
                    killBar.gameObject.SetActive(false);
                    timeBar.gameObject.SetActive(true);
                    timeBarFill.anchorMax = new Vector2(survivalLeft / def.PhaseTime, 1f);
                    break;
            }
        }

        /// <summary>띠 높이를 보이는 줄에 맞춘다 (보스 라운드는 두 줄뿐이라 빈 공간이 남지 않게). 위쪽 모서리 기준이라 아래만 줄어든다</summary>
        private void FitHeight(RoundType type)
        {
            float height = type == RoundType.CrowdToBoss ? 88f   // 제목 + 목표
                : type == RoundType.Survival ? 150f              // + 큰 남은 시간 + 막대
                : 142f;                                          // + 처치 막대 + 증원 시간
            if (Mathf.Approximately(panel.sizeDelta.y, height) == false) panel.sizeDelta = new Vector2(PanelSize.x, height);
        }

        /// <summary>라운드 시작 뒤 introTime 동안 크게, 마지막 0.5초에 평소 크기(hudScale)로 줄인다</summary>
        private void UpdateIntroScale()
        {
            float shrinkProgress = Mathf.Clamp01((roundSequencer.ElapsedSeconds - (introTime - 0.5f)) / 0.5f);
            panel.localScale = Vector3.one * hudScale * Mathf.Lerp(introScale, 1f, shrinkProgress);
        }

        /// <summary>초를 "02:14" 처럼 분:초로 바꾼다 (올림 — 0초가 되는 순간 끝남)</summary>
        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.CeilToInt(seconds);
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("RoundObjectiveHud", 50, false, out _);
            // 화면 왼쪽 끝에 붙인다 (시작 안내 때 커져도 왼쪽 위 모서리 기준)
            panel = HudFactory.Rect(canvas.transform, "Panel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -28f), PanelSize);
            UiThemeKit.Band(panel, UiThemeKit.BandShape.FadeRight, 0.8f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            HudFactory.NewImage(HudFactory.Rect(panel, "Marker", topLeft, topLeft, new Vector2(24f, -16f), new Vector2(3f, 22f)), null, UiTheme.Red);

            float width = PanelSize.x - TextX - 16f;
            titleText = HudFactory.Label(panel, "Title", textFont, 18f, TextAlignmentOptions.MidlineLeft,
                topLeft, topLeft, new Vector2(TextX, -10f), new Vector2(width, 30f));
            titleText.color = UiTheme.Steel;
            objectiveText = HudFactory.Label(panel, "Objective", textFont, 22f, TextAlignmentOptions.MidlineLeft,
                topLeft, topLeft, new Vector2(TextX, -40f), new Vector2(width, 34f));
            objectiveText.color = UiTheme.MoonWhite;
            valueText = HudFactory.Label(panel, "Value", UiTheme.NumberFont != null ? UiTheme.NumberFont : textFont, 44f, TextAlignmentOptions.MidlineLeft,
                topLeft, topLeft, new Vector2(TextX, -74f), new Vector2(width, 56f));
            valueText.color = UiTheme.MoonWhite;
            detailText = HudFactory.Label(panel, "Detail", textFont, 16f, TextAlignmentOptions.MidlineLeft,
                topLeft, topLeft, new Vector2(TextX, -104f), new Vector2(width, 26f));
            detailText.color = UiTheme.Steel;

            // 생존전 남은 시간 막대 (큰 시간 아래)
            timeBar = HudFactory.Rect(panel, "TimeBar", topLeft, topLeft, new Vector2(TextX, -134f), new Vector2(280f, UiTheme.ThinBarHeight));
            HudFactory.NewImage(timeBar, null, BarBackColor);
            timeBarFill = HudFactory.Stretch(timeBar, "Fill");
            HudFactory.NewImage(timeBarFill, null, UiTheme.MoonWhite);

            // 섬멸전 처치 막대 + 오른쪽 작은 처치 수
            killBar = HudFactory.Rect(panel, "KillBar", topLeft, topLeft, new Vector2(TextX, -88f), new Vector2(280f, 4f));
            HudFactory.NewImage(killBar, null, BarBackColor);
            killBarFill = HudFactory.Stretch(killBar, "Fill");
            HudFactory.NewImage(killBarFill, null, UiTheme.MoonWhite);
            killCountText = HudFactory.Label(panel, "KillCount", textFont, 16f, TextAlignmentOptions.MidlineLeft,
                topLeft, topLeft, new Vector2(TextX + 296f, -78f), new Vector2(160f, 24f));
            killCountText.color = UiTheme.Steel;
        }
    }
}
