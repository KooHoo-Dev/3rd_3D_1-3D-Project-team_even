using System.Collections;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Services;
using HybridShooter.UI;
using HybridShooter.UI.MainMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 튜토리얼 화면 안내. TutorialDirector 가 직접 부른다 (전역 이벤트 없음).
    /// 겉모양은 메인 메뉴(MainMenuUIBuilder)와 같은 말: 상자 없이 글자와 얇은 선, 옆으로 옅어지는 어두운 띠,
    /// 달빛 흰색(강조) · 청회색(평소) · 붉은 포인트(표식·키 이름), 넓은 자간의 영문(Aldrich = GameFlowUiSkin.numberFont)
    ///  - 오른쪽 위 구석: "ESC 길게 눌러 건너뛰기" + 차오르는 붉은 선
    ///  - 그 아래: 목표 — "TRAINING 02 / 05" · 붉은 짧은 선 · 과목 이름 · 목표 문구(키 이름은 문구 안 붉은 색)
    ///  - 그 아래: 처치 보상 "● +1 생체 자원 · 보유 3"
    ///  - 아래 가운데: "OPERATOR" + 양옆 가는 선, 자막(타자기 효과). 대사 Id 가 SoundTable 에 있으면 음성을 2D 로 같이 재생
    ///  - 화면 전체: 끝날 때 페이드아웃
    /// 글이 길면 판 높이가 늘어난다 (FitHeight — 줄임표로 자르지 않음)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialHud : MonoBehaviour
    {
        // ── 메인 메뉴와 같은 색 (MainMenuUIBuilder) ──
        internal static readonly Color MoonWhite = new Color(0.94f, 0.96f, 1f);
        internal static readonly Color Steel = new Color(0.6f, 0.68f, 0.78f);
        internal static readonly Color Accent = new Color(0.85f, 0.26f, 0.23f);
        /// <summary>글자 뒤 가독성용 어두운 띠 (메뉴 그라데이션과 같은 색)</summary>
        private static readonly Color BandColor = new Color(0.01f, 0.015f, 0.03f, 0.8f);

        private const float ObjectiveWidth = 560f, ObjectiveBandWidth = 760f;
        private const float ObjectiveTextTop = 104f, ObjectiveMinHeight = 150f;
        private const float SubtitleWidth = 1240f, SubtitleBottomY = 120f;
        private const float SubtitleTextTop = 46f, SubtitleMinHeight = 110f;
        private const float BottomPadding = 18f;
        private const float EdgeX = 60f;
        /// <summary>오른쪽 위 구석: 건너뛰기(맨 위) → 그 아래 목표 → 보상</summary>
        private const float SkipTop = 30f, SkipHeight = 36f;
        private const float ObjectiveTop = SkipTop + SkipHeight + 16f;
        /// <summary>강화 화면(100)·무기 선택 화면(100) 위. 입력은 막지 않는다</summary>
        private const int SortingOrder = 110;

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
        [Tooltip("영문·숫자 머리말용 (Aldrich SDF, 메인 메뉴와 같은 글꼴). 비워 두면 GameFlowUiSkin 의 숫자 글꼴")]
        [SerializeField] private TMP_FontAsset titleFont;
        [Tooltip("자막 글자가 찍히는 속도 (글자/초)")]
        [SerializeField, Min(1f)] private float typeSpeed = 32f;
        [Tooltip("자막을 읽는 데 주는 시간 = 글자 수 × 이 값 (초). 최소 minSubtitleTime")]
        [SerializeField, Min(0.01f)] private float readSecondsPerChar = 0.075f;
        [SerializeField, Min(0.5f)] private float minSubtitleTime = 2.2f;
        [Tooltip("대사가 끝난 뒤 자막을 남겨 두는 시간(초)")]
        [SerializeField, Min(0f)] private float subtitleLinger = 2.5f;
        [Tooltip("보상 표시 시간(초)")]
        [SerializeField, Min(0.5f)] private float rewardShowTime = 2.5f;

        private Canvas canvas;
        private RectTransform subtitleRect, objectiveRect, rewardRect, rewardDot, skipFill;
        private CanvasGroup objectiveGroup, subtitleGroup, rewardGroup, skipGroup;
        private TextMeshProUGUI objectiveHeader, objectiveName, objectiveText, subtitleText, rewardAmount, rewardNote;
        private Image fade;
        private string fullSubtitle = string.Empty;
        private float subtitleStart = -1f, subtitleEnd = -1f, rewardAt = -1f;

        #region 유니티 메시지
        private void Awake()
        {
            var skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
            if (titleFont == null) titleFont = skin.numberFont != null ? skin.numberFont : textFont;
            Build();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void LateUpdate()
        {
            UpdateSubtitle();
            UpdateReward();
        }
        #endregion

        #region 외부 API
        /// <summary>대사 한 줄을 띄우고(음성이 있으면 재생) 읽는 데 걸리는 시간(초)을 돌려준다. 다음 대사가 오면 바로 바뀐다</summary>
        public float Say(TutorialScript.Line line)
        {
            if (line == null) return 0f;
            fullSubtitle = line.Subtitle ?? string.Empty;
            // 타자기로 글자가 늘어나도 띠가 흔들리지 않게, 다 찍힌 문장 기준으로 높이를 먼저 맞춘다
            FitHeight(subtitleRect, subtitleText, fullSubtitle, SubtitleMinHeight, SubtitleTextTop);
            subtitleText.text = string.Empty;
            float duration = Mathf.Max(minSubtitleTime, fullSubtitle.Length * readSecondsPerChar);
            subtitleStart = Time.unscaledTime;
            subtitleEnd = subtitleStart + duration + subtitleLinger;
            subtitleGroup.alpha = 1f;
            PlayVoice(line.Id);
            return duration;
        }

        /// <summary>header = 영문 머리말 (예: "TRAINING 02 / 05"), name = 과목 이름, text = 목표 문구 (리치 텍스트)</summary>
        public void SetObjective(string header, string name, string text)
        {
            objectiveHeader.text = header;
            objectiveName.text = name;
            objectiveText.text = text;
            float height = FitHeight(objectiveRect, objectiveText, text, ObjectiveMinHeight, ObjectiveTextTop);
            rewardRect.anchoredPosition = new Vector2(rewardRect.anchoredPosition.x, -ObjectiveTop - height - 4f); // 보상은 목표 바로 아래
            objectiveGroup.alpha = 1f;
        }

        public void ClearObjective() => objectiveGroup.alpha = 0f;

        /// <summary>자막을 화면 위쪽으로 (강화 화면처럼 아래쪽 버튼을 쓰는 화면이 열려 있는 동안). false = 원래 아래</summary>
        public void SetSubtitleAtTop(bool top)
        {
            var anchor = top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            subtitleRect.anchorMin = subtitleRect.anchorMax = subtitleRect.pivot = anchor;
            subtitleRect.anchoredPosition = top ? new Vector2(0f, -24f) : new Vector2(0f, SubtitleBottomY);
        }

        public void ShowReward(int delta, int balance)
        {
            rewardNote.text = $"생체 자원 · 보유 {balance}";
            rewardAmount.text = $"+{delta}";
            // 숫자(영문 글꼴)는 설명 글 바로 왼쪽에 붙인다
            float noteWidth = rewardNote.GetPreferredValues(rewardNote.text).x;
            rewardAmount.rectTransform.anchoredPosition = new Vector2(-noteWidth - 12f, 0f);
            float amountWidth = rewardAmount.GetPreferredValues(rewardAmount.text).x;
            rewardDot.anchoredPosition = new Vector2(-amountWidth - 12f, 0f); // 붉은 점은 숫자 바로 왼쪽
            rewardAt = Time.unscaledTime;
            rewardGroup.alpha = 1f;
        }

        /// <summary>건너뛰기 게이지 (0~1). 0 이면 안내만 흐리게</summary>
        public void SetSkipProgress(float ratio)
        {
            skipFill.localScale = new Vector3(Mathf.Clamp01(ratio), 1f, 1f); // 피벗이 오른쪽이라 오른쪽부터 찬다
            skipGroup.alpha = ratio > 0f ? 1f : 0.55f;
        }

        /// <summary>화면을 검게 덮는다 (실제 시간)</summary>
        public IEnumerator FadeOut(float time)
        {
            fade.gameObject.SetActive(true);
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                fade.color = new Color(0f, 0f, 0f, t / time);
                yield return null;
            }
            fade.color = Color.black;
        }
        #endregion

        #region 내부
        /// <summary>
        /// 문장이 다 들어가도록 글자 칸과 판 높이를 늘린다 (최소 minHeight). 반환: 판 높이.
        /// 글자 칸이 고정 높이면 두 줄째부터 줄임표로 잘린다 (NotoSansKR 줄 높이 ≈ 글자 크기 × 1.45)
        /// </summary>
        private static float FitHeight(RectTransform panel, TextMeshProUGUI label, string text, float minHeight, float textTop)
        {
            float width = label.rectTransform.sizeDelta.x;
            float textHeight = Mathf.Ceil(label.GetPreferredValues(text, width, 0f).y);
            float panelHeight = Mathf.Max(minHeight, textTop + textHeight + BottomPadding);
            label.rectTransform.sizeDelta = new Vector2(width, panelHeight - textTop - BottomPadding);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, panelHeight);
            return panelHeight;
        }

        private static void PlayVoice(string id)
        {
            if (string.IsNullOrEmpty(id) || DataManager.Sounds.TryGet(id, out var sound) == false) return; // 음성 파일이 아직 없으면 자막만
            if (ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(sound);
        }

        private void UpdateSubtitle()
        {
            if (subtitleStart < 0f) return;
            float now = Time.unscaledTime;
            int shown = Mathf.Min(fullSubtitle.Length, Mathf.FloorToInt((now - subtitleStart) * typeSpeed));
            if (subtitleText.text.Length != shown) subtitleText.text = fullSubtitle.Substring(0, shown);
            if (now < subtitleEnd) return;
            subtitleStart = -1f;
            subtitleGroup.alpha = 0f;
        }

        private void UpdateReward()
        {
            if (rewardAt < 0f) return;
            float t = Time.unscaledTime - rewardAt;
            rewardGroup.alpha = Mathf.Clamp01((rewardShowTime - t) / 0.4f);
            if (t >= rewardShowTime) rewardAt = -1f;
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("TutorialHud", SortingOrder, false, out _);
            BuildObjective();
            BuildReward();
            BuildSubtitle();
            BuildSkip();
            fade = HudFactory.NewImage(HudFactory.Stretch(canvas.transform, "Fade"), null, Color.clear);
            fade.gameObject.SetActive(false);
        }

        /// <summary>오른쪽 위 목표: 오른쪽 정렬, 오른쪽이 진하고 왼쪽으로 옅어지는 띠 + 오른쪽 끝 붉은 표식</summary>
        private void BuildObjective()
        {
            objectiveRect = HudFactory.Rect(canvas.transform, "Objective", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-EdgeX, -ObjectiveTop), new Vector2(ObjectiveWidth, ObjectiveMinHeight));
            objectiveGroup = objectiveRect.gameObject.AddComponent<CanvasGroup>();

            var band = HudFactory.Rect(objectiveRect, "Band", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-ObjectiveBandWidth * 0.5f + EdgeX, 0f), new Vector2(ObjectiveBandWidth, 0f));
            band.anchorMin = new Vector2(1f, 0f);
            band.anchorMax = new Vector2(1f, 1f);
            FadeBand(band, toLeft: true);

            var marker = HudFactory.Rect(objectiveRect, "Marker", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(18f, -16f), new Vector2(3f, 74f));
            HudFactory.NewImage(marker, null, Accent);

            objectiveHeader = HudFactory.Label(objectiveRect, "Header", titleFont, 17f, TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -14f), new Vector2(ObjectiveWidth, 24f));
            objectiveHeader.characterSpacing = 10f;
            objectiveHeader.color = Steel;
            Line(objectiveRect, "Line", Accent, new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(56f, 2f), new Vector2(1f, 0.5f));
            objectiveName = HudFactory.Label(objectiveRect, "Name", textFont, 30f, TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -54f), new Vector2(ObjectiveWidth, 44f));
            objectiveName.color = MoonWhite;
            objectiveText = HudFactory.Label(objectiveRect, "Text", textFont, 22f, TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -ObjectiveTextTop), new Vector2(ObjectiveWidth, ObjectiveMinHeight - ObjectiveTextTop - BottomPadding));
            objectiveText.color = Steel;
            objectiveText.richText = true;
            objectiveText.textWrappingMode = TextWrappingModes.Normal;
            objectiveText.overflowMode = TextOverflowModes.Overflow; // 높이는 FitHeight 가 맞춘다
            objectiveGroup.alpha = 0f;
        }

        /// <summary>목표 아래 보상: "● +1  생체 자원 · 보유 3" (오른쪽 정렬)</summary>
        private void BuildReward()
        {
            rewardRect = HudFactory.Rect(canvas.transform, "Reward", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-EdgeX, -ObjectiveTop - ObjectiveMinHeight - 4f), new Vector2(ObjectiveWidth, 40f));
            rewardGroup = rewardRect.gameObject.AddComponent<CanvasGroup>();
            rewardNote = HudFactory.Label(rewardRect, "Note", textFont, 20f, TextAlignmentOptions.MidlineRight,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(ObjectiveWidth, 40f));
            rewardNote.color = Steel;
            rewardAmount = HudFactory.Label(rewardRect, "Amount", titleFont, 28f, TextAlignmentOptions.MidlineRight,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(160f, 40f));
            rewardAmount.color = MoonWhite;
            rewardAmount.characterSpacing = 4f;
            rewardDot = HudFactory.Rect(rewardAmount.rectTransform, "Dot", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));
            rewardDot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            HudFactory.NewImage(rewardDot, null, Accent);
            rewardGroup.alpha = 0f;
        }

        /// <summary>아래 가운데 자막: 가운데가 진하고 양옆으로 옅어지는 띠 + "OPERATOR" 와 양옆 가는 선</summary>
        private void BuildSubtitle()
        {
            subtitleRect = HudFactory.Rect(canvas.transform, "Subtitle", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, SubtitleBottomY), new Vector2(SubtitleWidth, SubtitleMinHeight));
            subtitleGroup = subtitleRect.gameObject.AddComponent<CanvasGroup>();

            foreach (bool left in new[] { true, false })
            {
                var half = HudFactory.Rect(subtitleRect, left ? "BandLeft" : "BandRight", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((left ? -1f : 1f) * SubtitleWidth * 0.25f, 0f), new Vector2(SubtitleWidth * 0.5f, 0f));
                half.anchorMin = new Vector2(0.5f, 0f);
                half.anchorMax = new Vector2(0.5f, 1f);
                FadeBand(half, toLeft: left);
            }

            var speaker = HudFactory.Label(subtitleRect, "Speaker", titleFont, 17f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(220f, 24f));
            speaker.text = "OPERATOR";
            speaker.characterSpacing = 18f;
            speaker.color = Accent;
            Line(speaker.rectTransform, "LineLeft", Steel, new Vector2(0f, 0.5f), new Vector2(-14f, 0f), new Vector2(110f, 1.5f), new Vector2(1f, 0.5f));
            Line(speaker.rectTransform, "LineRight", Steel, new Vector2(1f, 0.5f), new Vector2(14f, 0f), new Vector2(110f, 1.5f), new Vector2(0f, 0.5f));

            subtitleText = HudFactory.Label(subtitleRect, "Text", textFont, 28f, TextAlignmentOptions.Top,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -SubtitleTextTop), new Vector2(SubtitleWidth - 160f, SubtitleMinHeight - SubtitleTextTop - BottomPadding));
            subtitleText.color = MoonWhite;
            subtitleText.textWrappingMode = TextWrappingModes.Normal;
            subtitleText.overflowMode = TextOverflowModes.Overflow; // 높이는 FitHeight 가 맞춘다
            subtitleGroup.alpha = 0f;
        }

        /// <summary>오른쪽 위 구석(목표 위) 건너뛰기: 메뉴 조작 힌트처럼 키 이름만 붉게 + 아래 얇은 선이 오른쪽부터 붉게 찬다</summary>
        private void BuildSkip()
        {
            const float width = 300f;
            var skip = HudFactory.Rect(canvas.transform, "Skip", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-EdgeX, -SkipTop), new Vector2(width, SkipHeight));
            skipGroup = skip.gameObject.AddComponent<CanvasGroup>();
            var text = HudFactory.Label(skip, "Text", textFont, 19f, TextAlignmentOptions.BottomRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(width, 28f));
            text.richText = true;
            text.text = "<color=#D94238>ESC</color>  길게 눌러 건너뛰기";
            text.color = Steel;
            // 왼쪽이 진한 HorizontalFade 를 가운데 기준으로 뒤집어 오른쪽이 진하게 (오른쪽 정렬 줄)
            var track = Line(skip, "Track", new Color(Steel.r, Steel.g, Steel.b, 0.35f), new Vector2(1f, 0f), new Vector2(-width * 0.5f, 0.75f), new Vector2(width, 1.5f), new Vector2(0.5f, 0.5f));
            track.gameObject.AddComponent<HorizontalFade>();
            track.localRotation = Quaternion.Euler(0f, 0f, 180f);
            skipFill = Line(skip, "Fill", Accent, new Vector2(1f, 0f), Vector2.zero, new Vector2(width, 2.5f), new Vector2(1f, 0f));
            SetSkipProgress(0f);
        }

        /// <summary>
        /// 글자 뒤 어두운 띠 (메뉴 그라데이션과 같은 HorizontalFade: 왼쪽 진함 → 오른쪽 투명).
        /// toLeft = true 면 오른쪽이 진하고 왼쪽으로 옅어지게 뒤집는다
        /// </summary>
        private static void FadeBand(RectTransform rect, bool toLeft)
        {
            var image = HudFactory.NewImage(rect, null, BandColor);
            image.gameObject.AddComponent<HorizontalFade>();
            if (toLeft) rect.localRotation = Quaternion.Euler(0f, 0f, 180f);
        }

        private static RectTransform Line(RectTransform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var rect = HudFactory.Rect(parent, name, anchor, pivot, position, size);
            HudFactory.NewImage(rect, null, color);
            return rect;
        }
        #endregion
    }
}
