using HybridShooter.Core.Events;

namespace HybridShooter.Audio
{
    /// <summary>아이템·강화: 보급 획득(SupplyTable.CollectSfxKey), 강화 구매. 보급 투하음은 SupplyDropper 가 직접 재생</summary>
    public sealed partial class GameSoundRouter
    {
        private void OnSupplyCollected(SupplyCollectedEvent e) => service.PlaySfx(e.Def?.CollectSfx);

        private void OnUpgradePurchased(UpgradePurchasedEvent e) => service.PlaySfx(upgradeSelectKey);
    }
}
