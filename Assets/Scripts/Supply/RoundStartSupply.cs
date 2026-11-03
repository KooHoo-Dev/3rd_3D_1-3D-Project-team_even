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
    ///  - RoundStartedEvent 의 Round 가 2 이상일 때만 (Round 1 = 처음 시작·재시작). 다음 라운드는 라운드를 클리어해야만(보스 처치·섬멸 목표·생존 시간) 시작된다 (RunDirector)
    ///  - 값이 음수(-1)면 '최대치까지 가득' (예: 수류탄을 매 라운드 채워 수류탄 최대 보유량 강화가 의미 있게)
    ///  - 탄약은 앞선 RoundCleanupEvent 에서 '아껴 둔 탄창 유지(시작값 이상)' 처리된 뒤에 더한다. 수류탄·회복 팩은 라운드 사이 유지되므로 그대로 더한다
    ///  - 최대치를 넘는 만큼은 버려진다. 실제로 늘어난 양이 있으면 RoundSupplyGrantedEvent 발행 (알림 = RoundSupplyToastHud·SupplyHud, 소리 = GameSoundRouter)
    ///  - 플레이어는 IPlayerTarget.Root 에서 계약(IItemUser, IPlayerAmmo)으로 찾는다
    /// SupplyDropper 가 Awake 에서 같은 오브젝트에 붙인다 (씬마다 따로 배치하지 않아도 되게).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundStartSupply : MonoBehaviour
    {
        /// <summary>'가득' 일 때 예비 탄창 지급 개수 (무기별 최대치에서 잘린다. int.MaxValue 는 더하다 넘칠 수 있어 충분히 큰 값)</summary>
        private const int FillMagazines = 9999;

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
                items.AddGrenades(Amount(GameConfig.RoundStartGrenades, items.MaxGrenades - items.Grenades));
                items.AddHealPacks(Amount(GameConfig.RoundStartHealPacks, items.MaxHealPacks - items.HealPacks));
                grenades = items.Grenades - beforeGrenades;
                healPacks = items.HealPacks - beforeHealPacks;
            }

            var ammo = player.Root.GetComponentInParent<IPlayerAmmo>();
            if (ammo != null) magazines = ammo.AddMagazinesToAll(Amount(GameConfig.RoundStartMagazines, FillMagazines));

            if (grenades == 0 && healPacks == 0 && magazines == 0) return;
            EventBus<RoundSupplyGrantedEvent>.Publish(new RoundSupplyGrantedEvent(e.Round, grenades, healPacks, magazines));
        }

        /// <summary>테이블 값. 음수면 fill(가득 채우는 데 필요한 양)</summary>
        private static int Amount(string key, int fill)
        {
            int value = DataManager.Config.GetInt(key, 2);
            return value < 0 ? Mathf.Max(0, fill) : value;
        }
    }
}
