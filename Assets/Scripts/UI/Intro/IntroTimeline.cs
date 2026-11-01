namespace HybridShooter.UI.Intro
{
    /// <summary>
    /// 인트로 연출 시각 (초). BGM(tools/audio/intro_bgm.py, 75BPM · 1마디 3.2초)의 마디 경계에 맞춘 값을 한 곳에 모았다.
    /// 곡 구성을 바꾸면 여기와 intro_bgm.py 머리말의 마디 표를 함께 고친다
    ///   마디 2  로고 타격 · 마디 6~13 크롤 · 마디 14~15 빌드업(카메라 숙임) · 마디 16 행성·우주선 진입 · 마디 18 격추 · 마디 20 종지
    /// </summary>
    public static class IntroTimeline
    {
        public const float Bpm = 75f;
        public const float Bar = 4f * 60f / Bpm;

        /// <summary>검은 화면이 별하늘로 바뀌고 로고가 나온다</summary>
        public const float Logo = 2 * Bar;
        /// <summary>크롤 마지막 줄이 사라지기 시작하고 카메라가 숙여지기 시작한다</summary>
        public const float CrawlEnd = 14 * Bar;
        /// <summary>카메라가 다 숙여져 행성이 보이고 우주선이 들어온다</summary>
        public const float ShipEnter = 16 * Bar;
        public const float Hit = 18 * Bar;
        /// <summary>종지 화음 — 암전이 끝나고 게임 씬을 로드한다</summary>
        public const float End = 20 * Bar;
    }
}
