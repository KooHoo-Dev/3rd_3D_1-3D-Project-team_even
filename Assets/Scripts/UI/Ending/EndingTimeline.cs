namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 엔딩 연출 시각 (초). BGM(tools/audio/ending_bgm.py, 80BPM · 1마디 3초)의 마디 경계에 맞춘 값을 한 곳에 모았다.
    /// EndingSceneBuilder 가 이 값으로 Timeline 클립을 놓고, EndingDirector 가 환경 전환·크레딧·암전을 맞춘다.
    /// 곡 구성을 바꾸면 여기와 ending_bgm.py 머리말의 마디 표를 함께 고친다
    ///   마디 0 탑승 · 4 점화 · 6 이륙 · 8 우주 · 10 궤도 돌파 · 12 도약 · 13 크레딧 · 17 행성 균열 · 21 행성 폭발 · 25 로고(종지 시작) · 26 으뜸화음 · 28 암전 끝
    /// </summary>
    public static class EndingTimeline
    {
        public const float Bpm = 80f;
        public const float Bar = 4f * 60f / Bpm;

        /// <summary>새벽 착륙장. 플레이어가 구조선으로 걸어간다</summary>
        public const float Board = 0f;
        /// <summary>플레이어가 해치 안으로 사라진다</summary>
        public const float BoardEnd = 3.6f * Bar;
        /// <summary>엔진 점화 — 먼지·흔들림</summary>
        public const float Ignite = 4 * Bar;
        /// <summary>수직 이륙 → 기수를 들고 하늘로</summary>
        public const float Liftoff = 6 * Bar;
        /// <summary>우주 컷: 행성 대기권을 뚫고 올라온다 (하늘·조명이 우주로 바뀐다)</summary>
        public const float Space = 8 * Bar;
        /// <summary>행성 너머로 해가 떠오르며 궤도를 벗어난다 (곡의 클라이맥스)</summary>
        public const float Breakaway = 10 * Bar;
        /// <summary>도약 — 별 사이로 사라지는 섬광</summary>
        public const float Jump = 12 * Bar;
        /// <summary>크레딧 시작 (검은 띠가 걷힌다)</summary>
        public const float Credits = 13 * Bar;
        /// <summary>행성에 금이 가기 시작한다 (크레딧 B) — 금에서 금빛이 새고 핏줄 빛이 꺼져 간다</summary>
        public const float CrackStart = 17 * Bar;
        /// <summary>행성이 터진다 (크레딧 C 총주 첫 박) — 섬광·파편·충격파 고리</summary>
        public const float Shatter = 21 * Bar;
        /// <summary>크레딧 끝 · 로고 (종지 시작)</summary>
        public const float Logo = 25 * Bar;
        /// <summary>마지막 으뜸화음</summary>
        public const float Final = 26 * Bar;
        /// <summary>암전이 끝나고 메인 메뉴를 로드한다</summary>
        public const float End = 28 * Bar;
    }
}
