using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using HybridShooter.Round;
using TMPro;
using UnityEngine;

namespace HybridShooter.UI
{
    /// <summary>
    /// 라운드 목표 HUD (화면 위쪽 가운데 한 블록): 라운드 번호·유형 / 한 줄 목표 / 핵심 수치.
    /// 섬멸전 = 큰 "처치 37 / N" + 작은 증원 남은 시간 · 생존전 = 큰 남은 시간 + 얇은 진행 막대 · 보스 라운드 = 목표 문구만.
    /// RoundSequencer 의 공개 값(CurrentRoundDef·RoundNumber·Phase·KillCount·ElapsedSeconds)을 매 프레임 읽어 그린다.
    /// 군중전 동안만 보이고(보스전은 보스 체력 막대가 위쪽을 쓴다), 라운드 시작 직후 잠깐 크게 보여 목표를 알린다.
    /// GameFlowUiBootstrap 이 자동으로 붙인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundObjectiveHud : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.02f, 0.06f, 0.09f, 0.6f);
        private static readonly Color BarBackColor = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Vector2 PanelSize = new Vector2(520f, 150f);

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("라운드 시작 뒤 크게 보여 주는 시간(초, 라운드 시간)")]
        [SerializeField, Min(0f)] private float introTime = 3f;
        [Tooltip("시작 안내 때 블록 크기 배율")]
        [SerializeField, Min(1f)] private float introScale = 1.3f;

        private RoundSequencer roundSequencer;
        private Canvas canvas;
        private RectTransform panel;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI objectiveText;
        private TextMeshProUGUI valueText;
        private TextMeshProUGUI detailText;
        private RectTransform timeBar;
        private RectTransform timeBarFill;  // 오른쪽 끝(anchorMax.x) = 남은 시간 비율

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
                    timeBar.gameObject.SetActive(false);
                    break;
                case RoundType.Annihilation:
                    float reinforcementLeft = def.PhaseTime - elapsedSeconds;
                    titleText.text = $"라운드 {roundSequencer.RoundNumber} · 섬멸전";
                    objectiveText.text = $"적 {def.KillTarget}마리를 처치하세요";
                    valueText.text = $"처치 {roundSequencer.KillCount} / {def.KillTarget}";
                    detailText.text = reinforcementLeft > 0f ? $"증원 종료까지 {FormatTime(reinforcementLeft)}" : "증원 종료";
                    timeBar.gameObject.SetActive(false);
                    break;
                case RoundType.Survival:
                    float survivalLeft = Mathf.Max(0f, def.PhaseTime - elapsedSeconds);
                    titleText.text = $"라운드 {roundSequencer.RoundNumber} · 생존전";
                    objectiveText.text = "시간이 끝날 때까지 생존하세요";
                    valueText.text = FormatTime(survivalLeft);
                    detailText.text = "";
                    timeBar.gameObject.SetActive(true);
                    timeBarFill.anchorMax = new Vector2(survivalLeft / def.PhaseTime, 1f);
                    break;
            }
        }

        /// <summary>라운드 시작 뒤 introTime 동안 크게, 마지막 0.5초에 원래 크기로 줄인다</summary>
        private void UpdateIntroScale()
        {
            float shrinkProgress = Mathf.Clamp01((roundSequencer.ElapsedSeconds - (introTime - 0.5f)) / 0.5f);
            panel.localScale = Vector3.one * Mathf.Lerp(introScale, 1f, shrinkProgress);
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
            panel = HudFactory.Rect(canvas.transform, "Panel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), PanelSize);
            HudFactory.NewImage(panel, null, PanelColor);

            titleText = HudFactory.Label(panel, "Title", textFont, 20f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(PanelSize.x - 20f, 28f));
            titleText.color = HudFactory.Amber;
            objectiveText = HudFactory.Label(panel, "Objective", textFont, 22f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(PanelSize.x - 20f, 30f));
            valueText = HudFactory.Label(panel, "Value", textFont, 40f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(PanelSize.x - 20f, 50f));
            detailText = HudFactory.Label(panel, "Detail", textFont, 18f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(PanelSize.x - 20f, 24f));

            // 생존전 남은 시간 막대 (패널 아래 가장자리)
            timeBar = HudFactory.Rect(panel, "TimeBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(PanelSize.x - 60f, 6f));
            HudFactory.NewImage(timeBar, null, BarBackColor);
            timeBarFill = HudFactory.Stretch(timeBar, "Fill");
            HudFactory.NewImage(timeBarFill, null, HudFactory.TextColor);
        }
    }
}
