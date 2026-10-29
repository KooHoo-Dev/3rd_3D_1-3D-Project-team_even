using HybridShooter.Core.Events;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 아이템·강화
    ///   보급 투하: 착지 지점 투하음(SupplyTable.DropSfxKey, 3D) + 안내 방송(AnnounceSfxKey, 2D)
    ///   보급 획득: 랜덤 장전음(CollectSfxKey, SoundTable Path 여러 개 → 랜덤)
    ///   강화 구매
    /// </summary>
    public sealed partial class GameSoundRouter
    {
        private void OnSupplyDropped(SupplyDroppedEvent e)
        {
            if (e.Def == null) return;
            service.PlaySfx(e.Def.DropSfx, e.Position);
            service.PlaySfx(e.Def.AnnounceSfx);
        }

        private void OnSupplyCollected(SupplyCollectedEvent e) => service.PlaySfx(e.Def?.CollectSfx);

        private void OnUpgradePurchased(UpgradePurchasedEvent e) => service.PlaySfx(upgradeSelectKey);
    }
}
