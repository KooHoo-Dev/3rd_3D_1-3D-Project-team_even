using System.Collections;
using HybridShooter.Core.Events;
using HybridShooter.Player;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ③ 수류탄. 휴면 개체 GrenadeGroupCount 마리를 한곳에 모아 세우고, 수류탄 폭발 1회 + 무리 전멸로 통과.
    /// 첫 폭발 전에는 총을 막아(이동계 + 수류탄만) 총으로 다 잡는 우회를 막고, 터진 뒤에는 남은 개체를 총으로 처리해도 된다.
    /// 튜토리얼에서는 수류탄이 줄지 않는다(Director). 그래도 0개면(테이블 시작 개수 0 등) GrenadeGrant 개를 채워 준다
    /// </summary>
    public sealed class GrenadeStep : TutorialStep
    {
        public override string Title => "투척";

        private bool exploded;

        public override IEnumerator Run()
        {
            EventBus<GrenadeExplodedEvent>.Subscribe(OnGrenadeExploded);
            D.Allow(PlayerAbility.Movement | PlayerAbility.Grenade);
            var group = D.SpawnDormantGroup(D.GrenadeGroupCenter, Script.GrenadeGroupCount);
            RefillIfEmpty();

            D.ShowDestination(D.GrenadeGroupCenter, "wp_next");
            D.SayNow("vo_tut_03_grenade");
            int killed = 0;
            bool gunsAllowed = false;
            Objective("obj_grenade", 0, group.Count);
            yield return D.WaitUntil(() =>
            {
                RefillIfEmpty();
                if (exploded && gunsAllowed == false)
                {
                    gunsAllowed = true;
                    D.Allow(PlayerAbility.Movement | PlayerAbility.Grenade | PlayerAbility.Shooting | PlayerAbility.SwitchWeapon);
                    if (D.Waypoints.IsShown) D.HideDestination();
                }
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
            D.Allow(PlayerAbility.Movement);
            yield return D.Say("vo_tut_03_clear");
            if (D.GrenadeGate != null) D.GrenadeGate.Open();
        }

        public override void Cleanup() => EventBus<GrenadeExplodedEvent>.Unsubscribe(OnGrenadeExploded);

        private void RefillIfEmpty()
        {
            if (D.Items != null && D.Items.Grenades == 0 && D.Items.IsThrowing == false)
                D.Items.AddGrenades(Script.GrenadeGrant);
        }

        private void OnGrenadeExploded(GrenadeExplodedEvent e)
        {
            exploded = true;
            D.MarkProgress();
        }
    }
}
