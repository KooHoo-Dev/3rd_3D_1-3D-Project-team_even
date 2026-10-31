using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Supply
{
    /// <summary>
    /// 보스 클리어 후 다음 라운드 시작 지급. 수류탄·회복 팩·예비 탄창(1~3번 칸 모든 총)을 ConfigTable.RoundStart* 개씩 준다.
    ///
    ///  - RoundStartedEvent 의 Round 가 2 이상일 때만 (Round 1 = 처음 시작·재시작). 다음 라운드는 보스를 처치해야만 시작된다 (RunDirector)
    ///  - 탄약은 앞선 RoundCleanupEvent 에서 시작값으로 돌아간 뒤라 '시작값 + N'. 수류탄·회복 팩은 라운드 사이 유지되므로 그대로 더한다
    ///  - 최대치를 넘는 만큼은 버려진다. 실제로 늘어난 양이 있으면 RoundSupplyGrantedEvent 발행 (알림 = SupplyHud, 소리 = GameSoundRouter)
    ///  - 플레이어는 IPlayerTarget.Root 에서 계약(IItemUser, IPlayerAmmo)으로 찾는다
    /// SupplyDropper 가 Awake 에서 같은 오브젝트에 붙인다 (씬마다 따로 배치하지 않아도 되게).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundStartSupply : MonoBehaviour
    {
        private void OnEnable() => EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);

        private void OnDisable() => EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);

        private void OnRoundStarted(RoundStartedEvent e)
        {
            if (e.Round < 2) return;
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) == false || player.Root == null) return;

            int grenades = 0, healPacks = 0, magazines = 0;
            var items = player.Root.GetComponentInParent<IItemUser>();
            if (items != null)
            {
                int beforeGrenades = items.Grenades, beforeHealPacks = items.HealPacks;
                items.AddGrenades(DataManager.Config.GetInt(GameConfig.RoundStartGrenades, 2));
                items.AddHealPacks(DataManager.Config.GetInt(GameConfig.RoundStartHealPacks, 2));
                grenades = items.Grenades - beforeGrenades;
                healPacks = items.HealPacks - beforeHealPacks;
            }

            var ammo = player.Root.GetComponentInParent<IPlayerAmmo>();
            if (ammo != null) magazines = ammo.AddMagazinesToAll(DataManager.Config.GetInt(GameConfig.RoundStartMagazines, 2));

            if (grenades == 0 && healPacks == 0 && magazines == 0) return;
            EventBus<RoundSupplyGrantedEvent>.Publish(new RoundSupplyGrantedEvent(e.Round, grenades, healPacks, magazines));
        }
    }
}
