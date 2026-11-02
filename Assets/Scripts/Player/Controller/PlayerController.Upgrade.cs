using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 강화(IUpgradeState, BL-030) 중 플레이어 몸에 붙는 것: 최대 체력 · 최대 스태미나 · 스태미나 회복 속도.
    /// 기본값은 인스펙터 값 그대로 두고, 강화로 더해진 몫만 따로 기억한다 (Gun · PlayerItems 와 같은 방식).
    /// 강화를 사면(UpgradePurchasedEvent) 다시 계산하고, 늘어난 최대 체력·스태미나만큼 현재 값도 채운다.
    /// 처음부터 재시작으로 강화가 0 이 되면(UpgradesResetEvent) 다시 계산하고 현재 값을 새 최대로 줄인다.
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>초당 스태미나 회복량 (강화 포함)</summary>
        private float StaminaRegenPerSecond => staminaRegenPerSecond * (1f + upgradeStaminaRegenRate);

        private void SubscribeUpgradeEvents()
        {
            EventBus<UpgradePurchasedEvent>.Subscribe(OnUpgradePurchased);
            EventBus<UpgradesResetEvent>.Subscribe(OnUpgradesReset);
        }

        private void UnsubscribeUpgradeEvents()
        {
            EventBus<UpgradePurchasedEvent>.Unsubscribe(OnUpgradePurchased);
            EventBus<UpgradesResetEvent>.Unsubscribe(OnUpgradesReset);
        }

        private void OnUpgradePurchased(UpgradePurchasedEvent evt) => RefreshUpgradeStats(true);
        private void OnUpgradesReset(UpgradesResetEvent evt) => RefreshUpgradeStats(false);

        /// <summary>강화 몫을 다시 계산한다. fillIncrease = 늘어난 최대 체력·스태미나만큼 현재 값도 채울지 (구매 시)</summary>
        private void RefreshUpgradeStats(bool fillIncrease)
        {
            int oldMaxHealth = MaxHealth;
            float oldMaxStamina = MaxStamina;

            upgradeMaxHealth = Mathf.RoundToInt(UpgradeValue(UpgradeType.MaxHealth));
            upgradeMaxStamina = UpgradeValue(UpgradeType.MaxStamina);
            upgradeStaminaRegenRate = UpgradeValue(UpgradeType.StaminaRegen);

            // 쓰러져 있는 동안은 체력을 건드리지 않는다 (부활이 새 최대로 채운다)
            if (fillIncrease && IsDead == false && MaxHealth > oldMaxHealth) currentHealth += MaxHealth - oldMaxHealth;
            currentHealth = Mathf.Min(currentHealth, MaxHealth);
            if (fillIncrease && MaxStamina > oldMaxStamina) stamina += MaxStamina - oldMaxStamina;
            stamina = Mathf.Min(stamina, MaxStamina);

            PublishHp();
            PublishStamina();
        }

        private static float UpgradeValue(UpgradeType type)
            => ServiceLocator.TryGet<IUpgradeState>(out var upgrades) ? upgrades.GetTotalValue(type) : 0f;
    }
}
