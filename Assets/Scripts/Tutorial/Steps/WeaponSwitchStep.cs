using System.Collections;
using System.Collections.Generic;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Player;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ②-B 무기 교체. ② 사격 구역을 그대로 쓴다.
    ///  1) 주무기 칸(GunSlot.LoadoutSlots 중 첫 번째로 찬 칸, 처음엔 돌격소총)으로 바꾸면 통과 (번호 키 · 휠 모두 인정). 이 동안 사격은 막는다
    ///  2) 휴면 표적을 aim 소환 지점에 다시 세우고(최대 SwitchTargetCount) 주무기로 전부 처치. 권총으로 돌아가 잡지 않게 교체는 막는다
    ///  3) ② 차단막(AimGate)을 연다
    /// 주무기 칸이 비어 있으면(해금 데이터 이상) 과목을 건너뛰고 차단막만 연다
    /// </summary>
    public sealed class WeaponSwitchStep : TutorialStep
    {
        public override string Title => "무기 교체";

        public override IEnumerator Run()
        {
            int primary = PrimarySlot();
            if (primary < 0)
            {
                Debug.LogWarning("[Tutorial] 주무기 칸이 비어 있어 무기 교체 과목을 건너뜁니다.", D);
                if (D.AimGate != null) D.AimGate.Open();
                yield break;
            }

            // 1) 교체
            if (D.Guns.CurrentIndex != primary)
            {
                D.Allow(PlayerAbility.Movement | PlayerAbility.SwitchWeapon);
                Objective("obj_switch");
                D.SayNow("vo_tut_02b_switch");
                yield return D.WaitUntil(() => D.Guns.CurrentIndex == primary, "vo_tut_02b_switch_hint");
            }

            // 2) 주무기로 처치
            D.Allow(PlayerAbility.Movement | PlayerAbility.Shooting);
            var targets = new List<MonsterController>();
            int count = Mathf.Min(Script.SwitchTargetCount, D.AimSpawnPoints.Count);
            for (int i = 0; i < count; i++)
            {
                var point = D.AimSpawnPoints[i];
                if (point == null) continue;
                var monster = D.SpawnDormant(point.position, point.rotation);
                if (monster != null) targets.Add(monster);
            }

            D.SayNow("vo_tut_02b_switch_target");
            int killed = 0;
            Objective("obj_switch_kill", 0, targets.Count);
            yield return D.WaitUntil(() =>
            {
                int dead = TutorialDirector.CountDead(targets);
                if (dead != killed)
                {
                    killed = dead;
                    D.MarkProgress();
                    Objective("obj_switch_kill", killed, targets.Count);
                }
                return killed >= targets.Count;
            }, "vo_tut_02b_switch_target");

            // 3) 통과
            D.Allow(PlayerAbility.Movement);
            yield return D.Say("vo_tut_02b_switch_clear");
            if (D.AimGate != null) D.AimGate.Open();
        }

        /// <summary>무기가 들어 있는 첫 주무기 칸. 없으면 -1</summary>
        private int PrimarySlot()
        {
            if (D.Guns == null) return -1;
            foreach (int slot in GunSlot.LoadoutSlots)
                if (D.Guns.GetGun(slot) != null) return slot;
            return -1;
        }
    }
}
