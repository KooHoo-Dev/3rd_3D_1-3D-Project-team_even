using UnityEngine;

namespace HybridShooter.UI.Intro
{
    /// <summary>
    /// 인트로 크롤 문구. 코드를 고치지 않고 에셋에서 바로 바꿀 수 있게 분리했다 (Docs 의 '게임 네러티브 설정 문서' 기준).
    /// 크롤 속도는 글 길이에 맞춰 자동으로 정해지므로(IntroCrawlDirector) 문단을 늘리면 읽을 시간이 줄어든다 — 지금 분량 ±20% 안을 권장.
    /// 본문 문단 + 맺음이 화면의 '작전 기록 n / N' 한 칸씩이 된다
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Intro/Crawl Text", fileName = "IntroCrawlText")]
    public sealed class IntroCrawlText : ScriptableObject
    {
        [Tooltip("검은 화면에 먼저 뜨는 푸른 도입 문구")]
        [TextArea(2, 4)] public string openingLine = "먼 미래,\n하이브의 근원으로 향하는 궤도 위에서……";

        [Tooltip("BGM 첫 타격(6.4초)과 함께 나타나 멀어지는 큰 로고")]
        public string logo = "TEAM EVEN";

        [Header("크롤 머리말")]
        public string episode = "OPERATION : HIVE PURGE";
        public string episodeSubtitle = "하이브 근원 행성 섬멸 작전";

        [Header("크롤 본문 (문단마다 한 칸, 양쪽 정렬)")]
        [TextArea(3, 6)]
        public string[] paragraphs =
        {
            "은하로 번지는 생체 재앙 '하이브'. 연합군 최강의 전투 사이보그인 당신은 그 근원 행성을 섬멸하러 투입되었지만, 대기권 진입 중 격추되어 지표에 불시착했다.",
            "주 동력로는 파손되었고 무장 대부분이 잠겼다. 남은 것은 궤도 모함의 보급 포드와, 괴물에게서 뽑아낸 생체 자원뿐.",
            "행성은 침입자를 알아차렸다. 쓰러뜨릴수록 괴물은 더 강해지고, 구조선은 아직 먼 우회 항로 위에 있다.",
        };

        [Header("맺음 (가운데 정렬, 조금 크게)")]
        [TextArea(2, 4)]
        public string closing = "사냥하라. 무장을 되살려라.\n구조선이 닿을 때까지 — 살아남아라.";

        /// <summary>TMP 링크 ID — 본문 문단·맺음을 하나씩 감싸 '작전 기록' 카운터가 문단 첫 글자를 찾는다 (textInfo.linkInfo)</summary>
        public const string RecordLink = "record";

        /// <summary>크롤 TMP 에 넣을 리치 텍스트 (머리말·맺음은 가운데, 본문은 양쪽 정렬). 본문 문단·맺음은 RecordLink 로 감싼다</summary>
        public string BuildCrawl()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<align=center><size=115%><cspace=0.12em>").Append(episode).Append("</cspace></size>\n");
            sb.Append("<size=85%>").Append(episodeSubtitle).Append("</size></align>\n\n");
            sb.Append("<align=justified>");
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0) sb.Append("\n\n");
                AppendRecord(sb, paragraphs[i]);
            }
            sb.Append("</align>\n\n");
            sb.Append("<align=center><size=110%>");
            AppendRecord(sb, closing);
            sb.Append("</size></align>");
            return sb.ToString();
        }

        private static void AppendRecord(System.Text.StringBuilder sb, string text) =>
            sb.Append("<link=\"").Append(RecordLink).Append("\">").Append(text).Append("</link>");

        /// <summary>폰트 아틀라스에 미리 넣을 글자 전부</summary>
        public string AllCharacters() => openingLine + logo + episode + episodeSubtitle + string.Concat(paragraphs) + closing;
    }
}
