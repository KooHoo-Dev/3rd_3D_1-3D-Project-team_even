using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ④ 보급. 보급 구역에 들어오면:
    ///  1) 체력이 ShockAboveRatio 를 넘으면 훈련 충격으로 HealthAfterShock 까지 낮춘다 (회복 팩을 쓸 이유)
    ///  2) 회복 팩 상자 + 수류탄 상자를 같이 투하 → 빛기둥 색 = 종류, 각 역할 소개 → 둘 다 회수
    ///  3) V 로 회복 팩 사용 (PlayerItems.HealEnded)
    ///  4) 무기 상자 투하 → 회수 → 보급 무기 규칙 안내 → 휴면 무리를 보급 무기로 처리 (전멸 또는 보급 무기 소진)
    /// 허용 행동: 이동계 기본, 상자 회수 때만 +상호작용, 3) 에서만 +회복 팩, 4) 처치 때 +사격계 (무기 교체는 막힘)
    /// 상자 안내는 기존 보급 UI(상자 프롬프트·정보 카드·빛기둥 색)가 맡는다. 투하 위치는 마커 바로 아래 지면 (SupplyDropper.Drop(def, position))
    /// </summary>
    public sealed class SupplyStep : TutorialStep
    {
        public override string Title => "보급";

        private readonly HashSet<int> collected = new HashSet<int>();
        private bool healed;
        private bool weaponDepleted;

        public override IEnumerator Run()
        {
            EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);
            EventBus<SupplyWeaponChangedEvent>.Subscribe(OnSupplyWeaponChanged);
            if (D.Items != null) D.Items.HealEnded += OnHealEnded;

            D.Allow(PlayerAbility.Movement);
            Objective("obj_next");
            if (D.SupplyEntrance != null)
            {
                D.ShowDestination(D.SupplyEntrance, "wp_next");
                yield return D.WaitUntil(() => D.SupplyEntrance.Reached, null);
                D.HideDestination(); // 여기부터는 보급 빛기둥이 위치를 보여 준다
            }

            // 1) 훈련 충격
            if (D.Player.CurrentHealth > D.Player.MaxHealth * Script.ShockAboveRatio)
            {
                yield return D.Say("vo_tut_04_shock");
                yield return Shock();
            }

            // 2) 회복 팩 + 수류탄 상자. 수류탄이 가득이면 상자를 받을 수 없으니(SupplyCrate) 한 칸 비운다
            if (D.Items != null && D.Items.Grenades >= D.Items.MaxGrenades) D.Items.SetGrenades(D.Items.MaxGrenades - 1);
            Drop(Script.HealSupplyId, D.HealDropPoint);
            Drop(Script.GrenadeSupplyId, D.GrenadeDropPoint);
            yield return D.Say("vo_tut_04_drop");
            yield return D.Say("vo_tut_04_heal_crate");
            yield return D.Say("vo_tut_04_grenade_crate");
            D.Allow(PlayerAbility.Movement | PlayerAbility.Interact);
            D.SayNow("vo_tut_04_pickup");
            Objective("obj_pickup", ItemsCollected(), 2);
            yield return D.WaitUntil(() => ItemsCollected() >= 2, "vo_tut_04_pickup_hint");

            // 3) 회복 팩 사용. 체력이 가득이면 팩을 쓸 수 없으니(PlayerItems.TryUseHeal) 훈련 충격을 다시 준다
            D.Allow(PlayerAbility.Movement);
            if (D.Items != null && D.Items.HealPacks == 0) D.Items.AddHealPacks(1);
            if (D.Player.CurrentHealth >= D.Player.MaxHealth) yield return Shock();
            D.Allow(PlayerAbility.Movement | PlayerAbility.Heal);
            Objective("obj_heal");
            D.SayNow("vo_tut_04_heal");
            yield return D.WaitUntil(() => healed, "vo_tut_04_heal_hint");

            // 4) 무기 상자 → 보급 무기
            D.Allow(PlayerAbility.Movement | PlayerAbility.Interact);
            Drop(Script.WeaponSupplyId, D.WeaponDropPoint);
            Objective("obj_weapon_pickup");
            D.SayNow("vo_tut_04_weapon_drop");
            yield return D.WaitUntil(() => collected.Contains(Script.WeaponSupplyId), "vo_tut_04_pickup_hint");

            D.Allow(PlayerAbility.Movement);
            yield return D.Say("vo_tut_04_weapon");
            yield return D.Say("vo_tut_04_weapon_rule");
            var group = D.SpawnDormantGroup(D.WeaponGroupCenter, Script.WeaponGroupCount);
            D.Allow(PlayerAbility.Movement | PlayerAbility.Shooting); // 기본 총으로 바꿔 잡지 않게 교체는 막는다
            D.SayNow("vo_tut_04_weapon_target");
            int killed = 0;
            Objective("obj_weapon", 0, group.Count);
            yield return D.WaitUntil(() =>
            {
                int dead = TutorialDirector.CountDead(group);
                if (dead != killed)
                {
                    killed = dead;
                    D.MarkProgress();
                    Objective("obj_weapon", killed, group.Count);
                }
                return killed >= group.Count || weaponDepleted;
            }, "vo_tut_04_weapon_hint");
            TutorialDirector.DespawnAlive(group);

            D.Allow(PlayerAbility.Movement);
            yield return D.Say("vo_tut_04_clear");
            if (D.SupplyGate != null) D.SupplyGate.Open();
        }

        public override void Cleanup()
        {
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
            EventBus<SupplyWeaponChangedEvent>.Unsubscribe(OnSupplyWeaponChanged);
            if (D.Items != null) D.Items.HealEnded -= OnHealEnded;
        }

        private int ItemsCollected() => (collected.Contains(Script.HealSupplyId) ? 1 : 0) + (collected.Contains(Script.GrenadeSupplyId) ? 1 : 0);

        /// <summary>체력을 HealthAfterShock 까지 낮춘다. 구르기 무적 중이면 풀릴 때까지 기다린다</summary>
        private IEnumerator Shock()
        {
            int target = Mathf.Max(1, Mathf.RoundToInt(D.Player.MaxHealth * Script.HealthAfterShock));
            while (D.Player.IsAlive && D.Player.CurrentHealth > target)
            {
                if (D.Player.IsInvulnerable == false)
                {
                    var self = D.Player.transform;
                    D.Player.TakeDamage(new DamageInfo(D.Player.CurrentHealth - target, DamageKind.Explosion, Faction.Neutral, null,
                                                       self.position + Vector3.up, -self.forward));
                }
                yield return null;
            }
        }

        private void Drop(int supplyId, Transform point)
        {
            if (D.Dropper == null || point == null) return;
            if (DataManager.Supplies.TryGet(supplyId, out SupplyDef def) == false)
            {
                Debug.LogError($"[Tutorial] SupplyTable 에 {supplyId} 가 없습니다.", Script);
                return;
            }
            D.Dropper.Drop(def, point.position);
        }

        private void OnSupplyCollected(SupplyCollectedEvent e)
        {
            if (e.Def == null) return;
            collected.Add(e.Def.Id);
            D.MarkProgress();
            if (e.Def.Id != Script.WeaponSupplyId) Objective("obj_pickup", ItemsCollected(), 2);
        }

        private void OnSupplyWeaponChanged(SupplyWeaponChangedEvent e)
        {
            if (e.Depleted) weaponDepleted = true;
        }

        private void OnHealEnded()
        {
            healed = true;
            D.MarkProgress();
        }
    }
}
