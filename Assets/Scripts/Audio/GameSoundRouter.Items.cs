using HybridShooter.Core.Events;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 아이템·강화: 강화 구매.
    /// ※ 보급 획득음은 R6 에서 SupplyTable.CollectSfxKey 가 생기면 SupplyCollectedEvent 로 추가한다
    /// </summary>
    public sealed partial class GameSoundRouter
    {
        private void OnUpgradePurchased(UpgradePurchasedEvent e) => service.PlaySfx(upgradeSelectKey);
    }
}
