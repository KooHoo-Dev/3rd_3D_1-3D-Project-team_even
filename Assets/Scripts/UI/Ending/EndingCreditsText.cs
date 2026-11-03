using System;
using System.Text;
using UnityEngine;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 엔딩 크레딧 문구. 코드를 고치지 않고 에셋(Assets/Prefabs/Ending/EndingCreditsText.asset)에서 바로 바꾼다.
    /// 크롤 속도는 글 길이에 맞춰 자동(EndingCredits) → 줄을 늘려도 로고 시각(EndingTimeline.Logo)에 맞춰 끝난다.
    /// 글자를 새로 넣었으면 Tools > Ending > Rebuild Ending Scene 을 다시 실행해 폰트 아틀라스에 굽는다 (빌드 첫 프레임 끊김 방지)
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Ending/Credits Text", fileName = "EndingCreditsText")]
    public sealed class EndingCreditsText : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("맡은 일 (왼쪽, 작은 글씨)")] public string Role;
            [Tooltip("이름 (오른쪽). 여러 명은 줄바꿈으로")]
            [TextArea(1, 4)] public string Names;
        }

        [Serializable]
        public struct Section
        {
            public string Heading;
            public Entry[] Entries;
        }

        [Header("크롤 머리말")]
        public string title = "OPERATION : HIVE PURGE";
        public string subtitle = "작전 완료 — 하이브 근원 행성 소멸";

        [Header("크레딧 (구역마다 제목 + 맡은 일·이름)")]
        public Section[] sections =
        {
            new Section
            {
                Heading = "TEAM EVEN",
                Entries = new[]
                {
                    new Entry { Role = "기획", Names = "(이름)" },
                    new Entry { Role = "프로그래밍", Names = "(이름)" },
                    new Entry { Role = "보스·연출", Names = "(이름)" },
                    new Entry { Role = "맵·사운드", Names = "(이름)" },
                    new Entry { Role = "UI", Names = "(이름)" },
                },
            },
            new Section
            {
                Heading = "사용한 에셋",
                Entries = new[]
                {
                    new Entry { Role = "몬스터", Names = "Monsters Full Pack Vol 1" },
                    new Entry { Role = "캐릭터", Names = "G2 Cyborg Characters" },
                    new Entry { Role = "무기", Names = "Sci-Fi Weapons" },
                    new Entry { Role = "환경", Names = "3D Scifi Kit Vol 2\nSci-Fi Base" },
                    new Entry { Role = "이펙트", Names = "QFX Sci-Fi VFX" },
                    new Entry { Role = "UI", Names = "SCI-FI UI Pack Pro" },
                    new Entry { Role = "글꼴", Names = "Orbit · Aldrich · Noto Sans KR" },
                },
            },
            new Section
            {
                Heading = "도구",
                Entries = new[]
                {
                    new Entry { Role = "엔진", Names = "Unity 6" },
                    new Entry { Role = "연출", Names = "Cinemachine · Timeline" },
                    new Entry { Role = "라이브러리", Names = "DOTween · UniTask" },
                },
            },
        };

        [Header("맺음 (크레딧 끝에 가운데)")]
        [Tooltip("맺음 로고 = 게임 이름. 팀 이름(TEAM EVEN)은 로고 아래 작은 줄로 엔딩 씬 빌더가 넣는다")]
        public string logo = "HIVE PURGE";
        public string thanks = "THANK YOU FOR PLAYING";

        /// <summary>크레딧 TMP 에 넣을 리치 텍스트 (가운데 정렬: 구역 제목(달빛 흰색) → 맡은 일(작게, 청회색) → 이름). 색은 UiTheme 과 같은 값</summary>
        public string BuildCredits()
        {
            var sb = new StringBuilder();
            sb.Append("<size=120%><cspace=0.12em>").Append(title).Append("</cspace></size>\n");
            sb.Append("<size=80%><color=#99ADC7>").Append(subtitle).Append("</color></size>\n\n\n\n");
            foreach (var section in sections)
            {
                sb.Append("<size=110%><color=#F0F5FF><cspace=0.1em>").Append(section.Heading).Append("</cspace></color></size>\n\n");
                foreach (var entry in section.Entries ?? Array.Empty<Entry>())
                {
                    sb.Append("<size=70%><color=#99ADC7><cspace=0.08em>").Append(entry.Role).Append("</cspace></color></size>\n");
                    sb.Append((entry.Names ?? "").Trim()).Append("\n\n");
                }
                sb.Append("\n\n");
            }
            return sb.ToString();
        }

        /// <summary>폰트 아틀라스에 미리 넣을 글자 전부</summary>
        public string AllCharacters()
        {
            var sb = new StringBuilder(title + subtitle + logo + thanks);
            foreach (var section in sections)
            {
                sb.Append(section.Heading);
                foreach (var entry in section.Entries ?? Array.Empty<Entry>()) sb.Append(entry.Role).Append(entry.Names);
            }
            return sb.ToString();
        }
    }
}
