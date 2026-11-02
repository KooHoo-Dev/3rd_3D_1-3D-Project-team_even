using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Services;
using HybridShooter.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 튜토리얼 화면 안내. TutorialDirector 가 직접 부른다 (전역 이벤트 없음).
    ///  - 오른쪽 위: 목표 패널 (과목 이름 + 목표 문구, 키 배지는 문구 안의 [키])
    ///  - 아래 가운데: 오퍼레이터 자막 (타자기 효과). 대사 Id 가 SoundTable 에 있으면 그 음성을 2D 로 같이 재생, 없으면 자막만
    ///  - 목표 패널 아래: "+N 생체 자원" (처치 보상)
    ///  - 오른쪽 아래: 건너뛰기 게이지 (Esc 길게) · 화면 전체: 끝날 때 페이드아웃
    /// 겉모양은 HudFactory + GameFlowUiSkin(글꼴) 을 쓴다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialHud : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.02f, 0.07f, 0.1f, 0.82f);
        private static readonly Color Cyan = new Color(0.45f, 0.9f, 1f, 1f);
        private static readonly Color Muted = new Color(0.6f, 0.7f, 0.75f, 1f);
        private static readonly Vector2 ObjectiveSize = new Vector2(520f, 118f);
        private static readonly Vector2 SubtitleSize = new Vector2(1180f, 120f);
        private const float SubtitleBottomY = 150f;
        /// <summary>강화 화면(100)·무기 선택 화면(100) 위. 입력은 막지 않는다</summary>
        private const int SortingOrder = 110;

        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;
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
        private RectTransform subtitleRect;
        private CanvasGroup objectiveGroup, subtitleGroup, rewardGroup, skipGroup;
        private TextMeshProUGUI objectiveTitle, objectiveText, subtitleText, rewardText;
        private Image skipFill, fade;
        private string fullSubtitle = string.Empty;
        private float subtitleStart = -1f, subtitleEnd = -1f, rewardAt = -1f;

        #region 유니티 메시지
        private void Awake()
        {
            if (textFont == null) textFont = GameFlowUiSkin.Current.textFont;
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
            subtitleText.text = string.Empty;
            float duration = Mathf.Max(minSubtitleTime, fullSubtitle.Length * readSecondsPerChar);
            subtitleStart = Time.unscaledTime;
            subtitleEnd = subtitleStart + duration + subtitleLinger;
            subtitleGroup.alpha = 1f;
            PlayVoice(line.Id);
            return duration;
        }

        public void SetObjective(string title, string text)
        {
            objectiveTitle.text = title;
            objectiveText.text = text;
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
            rewardText.text = $"<color=#7CFFB2>+{delta} 생체 자원</color>   <size=80%><color=#9AB0B8>보유 {balance}</color></size>";
            rewardAt = Time.unscaledTime;
            rewardGroup.alpha = 1f;
        }

        /// <summary>건너뛰기 게이지. 0 이하면 안내만 흐리게, 1 이면 가득</summary>
        public void SetSkipProgress(float ratio)
        {
            skipFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(ratio), 1f, 1f); // 피벗이 왼쪽이라 왼쪽부터 찬다
            skipGroup.alpha = ratio > 0f ? 1f : 0.45f;
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

            // 목표 패널 (오른쪽 위)
            var objective = HudFactory.Rect(canvas.transform, "Objective", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), ObjectiveSize);
            objectiveGroup = objective.gameObject.AddComponent<CanvasGroup>();
            HudFactory.NewImage(objective, null, PanelColor);
            HudFactory.NewImage(HudFactory.Rect(objective, "Accent", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(5f, ObjectiveSize.y)), null, Cyan);
            objectiveTitle = HudFactory.Label(objective, "Title", textFont, 20f, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(ObjectiveSize.x - 40f, 28f));
            objectiveTitle.color = Cyan;
            objectiveText = HudFactory.Label(objective, "Text", textFont, 26f, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -48f), new Vector2(ObjectiveSize.x - 40f, 60f));
            objectiveText.textWrappingMode = TextWrappingModes.Normal;
            objectiveGroup.alpha = 0f;

            // 보상 (목표 패널 아래)
            var reward = HudFactory.Rect(canvas.transform, "Reward", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f - ObjectiveSize.y - 12f), new Vector2(ObjectiveSize.x, 44f));
            rewardGroup = reward.gameObject.AddComponent<CanvasGroup>();
            rewardText = HudFactory.Label(reward, "Text", textFont, 28f, TextAlignmentOptions.MidlineRight,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ObjectiveSize.x, 44f));
            rewardText.richText = true;
            rewardGroup.alpha = 0f;

            // 자막 (아래 가운데)
            var subtitle = HudFactory.Rect(canvas.transform, "Subtitle", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, SubtitleBottomY), SubtitleSize);
            subtitleRect = subtitle;
            subtitleGroup = subtitle.gameObject.AddComponent<CanvasGroup>();
            HudFactory.NewImage(subtitle, null, PanelColor);
            var speaker = HudFactory.Label(subtitle, "Speaker", textFont, 20f, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -12f), new Vector2(300f, 26f));
            speaker.text = "오퍼레이터";
            speaker.color = Cyan;
            subtitleText = HudFactory.Label(subtitle, "Text", textFont, 28f, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -42f), new Vector2(SubtitleSize.x - 56f, SubtitleSize.y - 50f));
            subtitleText.textWrappingMode = TextWrappingModes.Normal;
            subtitleGroup.alpha = 0f;

            // 건너뛰기 (오른쪽 아래)
            var skip = HudFactory.Rect(canvas.transform, "Skip", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(320f, 34f));
            skipGroup = skip.gameObject.AddComponent<CanvasGroup>();
            HudFactory.NewImage(skip, null, PanelColor);
            skipFill = HudFactory.NewImage(HudFactory.Rect(skip, "Fill", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(320f, 4f)), null, Cyan);
            var skipText = HudFactory.Label(skip, "Text", textFont, 18f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 34f));
            skipText.text = "[Esc] 길게 누르기 — 튜토리얼 건너뛰기";
            skipText.color = Muted;
            SetSkipProgress(0f);

            // 페이드
            fade = HudFactory.NewImage(HudFactory.Stretch(canvas.transform, "Fade"), null, Color.clear);
            fade.gameObject.SetActive(false);
        }
        #endregion
    }
}
