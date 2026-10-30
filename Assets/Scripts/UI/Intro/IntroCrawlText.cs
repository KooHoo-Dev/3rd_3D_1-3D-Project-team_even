using UnityEngine;

namespace HybridShooter.UI.Intro
{
    /// <summary>
    /// 인트로 크롤 문구. 코드를 고치지 않고 에셋에서 바로 바꿀 수 있게 분리했다 (Docs 의 '게임 네러티브 설정 문서' 기준).
    /// 크롤 속도는 글 길이에 맞춰 자동으로 정해지므로(IntroCrawlDirector) 문단을 늘리면 읽을 시간이 줄어든다 — 지금 분량 ±20% 안을 권장
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
            "은하 곳곳으로 번지는 생체 재앙 '하이브'. 수많은 행성이 침묵했고, 인류 연합군은 마침내 그 근원으로 추정되는 외계 행성을 찾아냈다.",
            "연합군 최강의 전투 사이보그인 당신은 행성 섬멸 작전의 선봉으로 투입되었다. 그러나 대기권에 진입하는 순간, 지표에서 솟구친 생체 대공 포화에 격추되어 행성 표면에 불시착하고 만다.",
            "추락의 충격으로 주 동력로가 파손되었다. 무장과 신체 기능 대부분이 잠긴 지금, 남은 것은 궤도 위 모함이 보내는 보급 포드, 그리고 괴물에게서 뽑아낸 생체 자원뿐이다.",
            "행성은 이미 침입자를 알아차렸다. 괴물들은 끝없이 몰려오고, 쓰러뜨릴수록 더 강해진다. 구조선은 행성을 감싼 생체 전자기장을 피해 먼 우회 항로로 다가오고 있다.",
        };

        [Header("맺음 (가운데 정렬, 조금 크게)")]
        [TextArea(2, 4)]
        public string closing = "당신이 쓰러지면, 작전도 인류의 반격도 여기서 끝난다.\n\n사냥하라. 무장을 되살려라.\n구조선이 닿을 때까지 — 살아남아라.";

        /// <summary>크롤 TMP 에 넣을 리치 텍스트 (머리말·맺음은 가운데, 본문은 양쪽 정렬)</summary>
        public string BuildCrawl()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<align=center><size=115%><cspace=0.12em>").Append(episode).Append("</cspace></size>\n");
            sb.Append("<size=85%>").Append(episodeSubtitle).Append("</size></align>\n\n");
            sb.Append("<align=justified>");
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0) sb.Append("\n\n");
                sb.Append(paragraphs[i]);
            }
            sb.Append("</align>\n\n");
            sb.Append("<align=center><size=110%>").Append(closing).Append("</size></align>");
            return sb.ToString();
        }

        /// <summary>폰트 아틀라스에 미리 넣을 글자 전부</summary>
        public string AllCharacters() => openingLine + logo + episode + episodeSubtitle + string.Concat(paragraphs) + closing;
    }
}
