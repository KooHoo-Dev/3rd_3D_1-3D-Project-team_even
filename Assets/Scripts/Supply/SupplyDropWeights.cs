using System.Collections.Generic;
using System.Text;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Supply
{
    /// <summary>
    /// 상황에 따라 바뀌는 보급 확률. 최종 가중치 = SupplyTable.Weight × 상황 배율 (배율 값은 ConfigTable).
    ///
    ///  - 수류탄·회복 팩: 보유 비율 f = 현재 / 최대 → Lerp(SupplyNeedWeightMax, SupplyNeedWeightMin, f) (0개 ×2, 가득 ×0.25)
    ///  - 무기 상자: 몬스터 급증 중 × SupplyWeaponSurgeMultiplier, 보급 무기를 다 쓴 뒤 × SupplyWeaponDepletedMultiplier,
    ///    두 배율의 곱은 SupplyWeaponMultiplierCap 까지. 필드에 무기 상자가 이미 있으면 두 배율은 적용하지 않는다 (연속 투하 방지).
    ///    보급 무기를 들고 있으면 × SupplyWeaponHeldMultiplier (기본 0 = 보유 중에는 무기 상자 안 나옴)
    ///  - 몬스터 급증: MonsterSpawnedEvent 가 최근 SupplySurgeWindow 초에 SupplySurgeSpawnCount 마리 이상 → SupplySurgeDuration 초 유지
    ///  - 보급 무기 소진: SupplyWeaponChangedEvent.Depleted 부터 다음 무기 상자 투하(NotifyDropped)까지
    /// 라운드 정리(RoundCleanupEvent) 때 급증·소진 상태를 비운다.
    /// SupplyDropper 가 Awake 에서 같은 오브젝트에 붙이고, 투하할 때 WeightOf 로 뽑는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupplyDropWeights : MonoBehaviour
    {
        private readonly Queue<float> spawnTimes = new Queue<float>();
        private float surgeUntil = float.NegativeInfinity;
        private bool weaponDepleted;

        /// <summary>몬스터 급증 중인지</summary>
        public bool IsSurging => Time.time < surgeUntil;
        /// <summary>보급 무기를 다 쓴 뒤 아직 무기 상자가 투하되지 않았는지</summary>
        public bool IsWeaponDepleted => weaponDepleted;

        private void OnEnable()
        {
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            EventBus<SupplyWeaponChangedEvent>.Subscribe(OnSupplyWeaponChanged);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        private void OnDisable()
        {
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            EventBus<SupplyWeaponChangedEvent>.Unsubscribe(OnSupplyWeaponChanged);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        /// <summary>이번 투하에서 def 의 최종 가중치 (기본 Weight × 상황 배율). active = 필드에 있는 상자 (무기 상자 중복 확인)</summary>
        public float WeightOf(SupplyDef def, IReadOnlyList<SupplyCrate> active) => def.Weight * MultiplierOf(def, active);

        /// <summary>상황 배율. 플레이어를 못 찾으면 보유량 배율은 1</summary>
        public float MultiplierOf(SupplyDef def, IReadOnlyList<SupplyCrate> active)
        {
            switch (def.SupplyType)
            {
                case SupplyType.Grenade:
                case SupplyType.Heal:
                    return NeedMultiplier(def.SupplyType);
                case SupplyType.Weapon:
                    return WeaponMultiplier(active);
                default:
                    return 1f;
            }
        }

        /// <summary>투하 확정 후 호출. 무기 상자가 나왔으면 소진 배율을 끈다</summary>
        public void NotifyDropped(SupplyDef def)
        {
            if (def != null && def.IsWeapon) weaponDepleted = false;
        }

        /// <summary>디버그 로그용: 각 상자의 최종 확률 "수류탄 팩 41% · 회복 팩 41% ..." + 상태</summary>
        public string Describe(IReadOnlyList<SupplyDef> all, IReadOnlyList<SupplyCrate> active)
        {
            float total = 0f;
            foreach (var def in all) total += Mathf.Max(0f, WeightOf(def, active));
            var text = new StringBuilder();
            foreach (var def in all)
            {
                if (text.Length > 0) text.Append(" · ");
                float p = total > 0f ? Mathf.Max(0f, WeightOf(def, active)) / total : 0f;
                text.Append(def.DisplayName).Append(' ').Append(Mathf.RoundToInt(p * 100f)).Append('%');
            }
            text.Append(" (급증 ").Append(IsSurging ? "O" : "X").Append(" · 무기 소진 ").Append(weaponDepleted ? "O" : "X").Append(')');
            return text.ToString();
        }

        private static float NeedMultiplier(SupplyType type)
        {
            if (TryGetPlayer(out var items, out _) == false) return 1f;
            int current = type == SupplyType.Grenade ? items.Grenades : items.HealPacks;
            int max = type == SupplyType.Grenade ? items.MaxGrenades : items.MaxHealPacks;
            float fill = max > 0 ? Mathf.Clamp01((float)current / max) : 1f;
            float full = DataManager.Config.GetFloat(GameConfig.SupplyNeedWeightMin, 0.25f);
            float empty = DataManager.Config.GetFloat(GameConfig.SupplyNeedWeightMax, 2f);
            return Mathf.Lerp(empty, full, fill);
        }

        private float WeaponMultiplier(IReadOnlyList<SupplyCrate> active)
        {
            float multiplier = 1f;
            if (HasWeaponCrate(active) == false)
            {
                if (IsSurging) multiplier *= DataManager.Config.GetFloat(GameConfig.SupplyWeaponSurgeMultiplier, 2.5f);
                if (weaponDepleted) multiplier *= DataManager.Config.GetFloat(GameConfig.SupplyWeaponDepletedMultiplier, 2.5f);
                multiplier = Mathf.Min(multiplier, DataManager.Config.GetFloat(GameConfig.SupplyWeaponMultiplierCap, 4f));
            }
            if (TryGetPlayer(out _, out var weapons) && weapons.SupplyWeaponId != 0)
                multiplier *= DataManager.Config.GetFloat(GameConfig.SupplyWeaponHeldMultiplier, 0f);
            return multiplier;
        }

        private static bool HasWeaponCrate(IReadOnlyList<SupplyCrate> active)
        {
            if (active == null) return false;
            foreach (var crate in active)
                if (crate != null && crate.Def != null && crate.Def.IsWeapon) return true;
            return false;
        }

        private static bool TryGetPlayer(out IItemUser items, out ISupplyWeaponUser weapons)
        {
            items = null;
            weapons = null;
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) == false || player.Root == null) return false;
            items = player.Root.GetComponentInParent<IItemUser>();
            weapons = player.Root.GetComponentInParent<ISupplyWeaponUser>();
            return items != null;
        }

        /// <summary>최근 SupplySurgeWindow 초 소환 수가 SupplySurgeSpawnCount 이상이면 급증 시작(또는 연장)</summary>
        private void OnMonsterSpawned(MonsterSpawnedEvent e)
        {
            float now = Time.time;
            float window = DataManager.Config.GetFloat(GameConfig.SupplySurgeWindow, 10f);
            spawnTimes.Enqueue(now);
            while (spawnTimes.Count > 0 && now - spawnTimes.Peek() > window) spawnTimes.Dequeue();

            if (spawnTimes.Count < DataManager.Config.GetInt(GameConfig.SupplySurgeSpawnCount, 12)) return;
            if (IsSurging == false) Debug.Log($"[SupplyDropWeights] 몬스터 급증: 최근 {window:0}초 {spawnTimes.Count}마리 → 무기 상자 확률 상승", this);
            surgeUntil = now + DataManager.Config.GetFloat(GameConfig.SupplySurgeDuration, 30f);
        }

        private void OnSupplyWeaponChanged(SupplyWeaponChangedEvent e)
        {
            if (e.Depleted) weaponDepleted = true;
        }

        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            spawnTimes.Clear();
            surgeUntil = float.NegativeInfinity;
            weaponDepleted = false;
        }
    }
}
