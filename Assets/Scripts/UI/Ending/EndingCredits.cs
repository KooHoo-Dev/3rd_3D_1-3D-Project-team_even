using TMPro;
using UnityEngine;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 엔딩 크레딧: 글이 화면 아래에서 위로 흘러가고(크롤), 끝나면 로고와 'THANK YOU FOR PLAYING' 이 떠오른다.
    /// 화면 상태는 시각 t 하나로 계산한다(Evaluate) → 건너뛰기·Timeline 미리보기에서도 같은 화면.
    /// 크롤 속도는 글 길이에 맞춰 자동: 마지막 줄이 EndingTimeline.Logo 에 화면 위로 빠져나간다
    /// </summary>
    public sealed class EndingCredits : MonoBehaviour
    {
        [SerializeField] private EndingCreditsText content;

        [Header("크롤")]
        [Tooltip("크롤이 보이는 영역 (RectMask2D 로 위아래 끝을 부드럽게 자른다)")]
        [SerializeField] private RectTransform viewport;
        [Tooltip("viewport 아래 끝 기준, 위쪽 가운데 피벗")]
        [SerializeField] private RectTransform roll;
        [SerializeField] private TMP_Text rollText;
        [SerializeField] private CanvasGroup rollGroup;
        [Tooltip("크레딧 시작 뒤 글이 올라오기 시작할 때까지(초)")]
        [SerializeField] private float startDelay = 0.6f;

        [Header("로고")]
        [SerializeField] private CanvasGroup logoGroup;
        [SerializeField] private TMP_Text logoText;
        [SerializeField] private TMP_Text thanksText;
        [SerializeField] private float logoFade = 1.6f;

        private bool prepared;
        private float rollHeight;

        private void Awake() => Prepare();

        /// <summary>문구·높이 준비. 에디터 빌더에서도 부른다</summary>
        public void Prepare()
        {
            if (prepared) return;
            prepared = true;
            rollText.text = content.BuildCredits();
            logoText.text = content.logo;
            thanksText.text = content.thanks;
            rollText.ForceMeshUpdate();
            rollHeight = rollText.preferredHeight;
            roll.sizeDelta = new Vector2(roll.sizeDelta.x, rollHeight);
        }

        /// <summary>시각 t 의 화면 (EndingTimeline 기준 초)</summary>
        public void Evaluate(float t)
        {
            Prepare();
            float from = EndingTimeline.Credits + startDelay;
            float k = Mathf.Clamp01((t - from) / (EndingTimeline.Logo - from));
            // 글 위 끝: 화면 아래 끝(0) → 글 아래 끝이 화면 위 끝을 지날 때(viewport 높이 + 글 높이)
            roll.anchoredPosition = new Vector2(roll.anchoredPosition.x, k * (viewport.rect.height + rollHeight));
            rollGroup.alpha = t >= EndingTimeline.Credits && t < EndingTimeline.Logo + 0.5f ? 1f : 0f;

            logoGroup.alpha = Mathf.SmoothStep(0f, 1f, (t - EndingTimeline.Logo) / logoFade);
            // 로고는 조금씩 다가온다 (마지막 으뜸화음까지)
            float grow = Mathf.Clamp01((t - EndingTimeline.Logo) / (EndingTimeline.End - EndingTimeline.Logo));
            logoGroup.transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.04f, grow);
        }
    }
}
