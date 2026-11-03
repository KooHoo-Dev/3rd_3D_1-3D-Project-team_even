using System.Collections;
using HybridShooter.Core.Events;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ③ 수류탄. 휴면 개체 GrenadeGroupCount 마리를 한곳에 모아 세우고, 수류탄 폭발 1회 + 무리 전멸로 통과 (남은 개체는 총으로 처리해도 된다).
    /// 수류탄이 없고 아직 한 번도 터뜨리지 않았으면 GrenadeGrant 개를 채워 준다
    /// </summary>
    public sealed class GrenadeStep : TutorialStep
    {
        public override string Title => "투척";

        private bool exploded;

        public override IEnumerator Run()
        {
            EventBus<GrenadeExplodedEvent>.Subscribe(OnGrenadeExploded);
            var group = D.SpawnDormantGroup(D.GrenadeGroupCenter, Script.GrenadeGroupCount);
            RefillIfEmpty();

            D.ShowDestination(D.GrenadeGroupCenter, "wp_next");
            D.SayNow("vo_tut_03_grenade");
            int killed = 0;
            Objective("obj_grenade", 0, group.Count);
            yield return D.WaitUntil(() =>
            {
                RefillIfEmpty();
                if (exploded && D.Waypoints.IsShown) D.HideDestination();
                int dead = TutorialDirector.CountDead(group);
                if (dead != killed)
                {
                    killed = dead;
                    D.MarkProgress();
                    Objective("obj_grenade", killed, group.Count);
                }
                return exploded && killed >= group.Count;
            }, "vo_tut_03_hint");

            D.HideDestination();
            yield return D.Say("vo_tut_03_clear");
            if (D.GrenadeGate != null) D.GrenadeGate.Open();
        }

        public override void Cleanup() => EventBus<GrenadeExplodedEvent>.Unsubscribe(OnGrenadeExploded);

        private void RefillIfEmpty()
        {
            if (exploded == false && D.Items != null && D.Items.Grenades == 0 && D.Items.IsThrowing == false)
                D.Items.AddGrenades(Script.GrenadeGrant);
        }

        private void OnGrenadeExploded(GrenadeExplodedEvent e)
        {
            exploded = true;
            D.MarkProgress();
        }
    }
}
