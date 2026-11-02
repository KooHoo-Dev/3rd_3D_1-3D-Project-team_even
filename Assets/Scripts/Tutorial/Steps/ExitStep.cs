using System.Collections;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>⑥ 전장 입장. 전 과목 통과 안내 → 전초기지 게이트 트리거에 들어가면 끝 (Director 가 완료 기록 · 게임 씬 전환)</summary>
    public sealed class ExitStep : TutorialStep
    {
        public override string Title => "전장 진입";
        public override bool IsTraining => false;

        public override IEnumerator Run()
        {
            Objective("obj_exit");
            D.ShowDestination(D.ExitZone, "wp_exit");
            yield return D.Say("vo_tut_06_allclear");
            D.SayNow("vo_tut_06_gate");
            if (D.ExitZone != null) yield return D.WaitUntil(() => D.ExitZone.Reached, "vo_tut_06_gate");
            D.HideDestination();
        }
    }
}
