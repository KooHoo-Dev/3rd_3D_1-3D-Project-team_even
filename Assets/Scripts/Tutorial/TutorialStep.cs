using System.Collections;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 튜토리얼 과목 하나. TutorialDirector 가 차례로 Run 을 돌리고, 끝나거나 건너뛰면 Cleanup 을 부른다.
    /// 플레이어·HUD·소환 같은 공용 기능은 Director 를 통해 쓴다 (D)
    /// </summary>
    public abstract class TutorialStep
    {
        protected TutorialDirector D { get; private set; }
        protected TutorialScript Script => D.Script;

        /// <summary>목표 패널 제목 (예: "과목 1 · 기동")</summary>
        public abstract string Title { get; }

        internal void Bind(TutorialDirector director) => D = director;

        /// <summary>과목 진행. 끝나면 반환한다 (통과 대사·차단막 열기까지 포함)</summary>
        public abstract IEnumerator Run();

        /// <summary>구독 해제 등. 정상 종료·건너뛰기 모두 한 번 불린다</summary>
        public virtual void Cleanup() { }

        protected void Objective(string id, int progress = 0, int goal = 0) => D.Hud.SetObjective(Title, Script.ObjectiveText(id, progress, goal));
    }
}
