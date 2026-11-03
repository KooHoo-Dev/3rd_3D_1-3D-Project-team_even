using System.Collections;
using System.Collections.Generic;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ② 조준 · 사격 · 처치 보상. 휴면 최약체를 소환 지점마다 하나씩(최대 AimTargetCount) 세우고, 조준 1회 + 전부 처치로 통과.
    /// 첫 처치 때 생체 자원 안내 (보상 표시 자체는 Director 가 CurrencyChangedEvent 로 띄운다).
    /// 허용 행동: 이동계 + 사격계 (수류탄·무기 교체·상호작용은 막힘). 차단막은 다음 과목(WeaponSwitchStep)이 연다
    /// </summary>
    public sealed class AimStep : TutorialStep
    {
        public override string Title => "사격";

        public override IEnumerator Run()
        {
            var targets = new List<MonsterController>();
            int count = Mathf.Min(Script.AimTargetCount, D.AimSpawnPoints.Count);
            for (int i = 0; i < count; i++)
            {
                var point = D.AimSpawnPoints[i];
                if (point == null) continue;
                var monster = D.SpawnDormant(point.position, point.rotation);
                if (monster != null) targets.Add(monster);
            }

            D.Allow(PlayerAbility.Movement);
            if (D.AimSpawnPoints.Count > 0) D.ShowDestination(D.AimSpawnPoints[Mathf.Min(1, D.AimSpawnPoints.Count - 1)], "wp_next"); // 가운데 표적
            yield return D.Say("vo_tut_02_dormant");
            D.Allow(PlayerAbility.Movement | PlayerAbility.Shooting); // 수류탄·무기 교체는 아직
            D.SayNow("vo_tut_02_shoot");

            bool aimed = false;
            bool rewardLineShown = false;
            int killed = 0;
            Objective("obj_aim", 0, targets.Count);
            yield return D.WaitUntil(() =>
            {
                if (aimed == false && D.Guns != null && D.Guns.IsAiming)
                {
                    aimed = true;
                    D.MarkProgress();
                    D.HideDestination(); // 표적을 찾았으니 표식은 거둔다 (조준을 가리지 않게)
                }
                int dead = TutorialDirector.CountDead(targets);
                if (dead != killed)
                {
                    killed = dead;
                    D.MarkProgress();
                    Objective("obj_aim", killed, targets.Count);
                    if (rewardLineShown == false)
                    {
                        rewardLineShown = true;
                        D.SayNow("vo_tut_02_reward");
                    }
                }
                return aimed && killed >= targets.Count;
            }, "vo_tut_02_hint");

            D.HideDestination();
            D.Allow(PlayerAbility.Movement);
            yield return D.Say("vo_tut_02_clear");
            // 차단막은 같은 구역에서 이어지는 무기 교체 과목(WeaponSwitchStep)이 연다
        }
    }
}
