using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>체력, 회복, 피격, 사망</summary>
    public partial class PlayerController
    {
        public bool IsAlive => currentHealth > 0;
        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;
        /// <summary>수류탄을 던지는 동작 중 (이 동안은 총을 쏠 수 없다)</summary>
        public bool IsThrowingGrenade => items != null && items.IsThrowing;

        public Faction Faction => Faction.Player;

        /// <summary>죽었거나, 이미 가득 찼거나, amount 가 0 이하면 false (아이템을 소모하지 않게)</summary>
        public bool Heal(int amount)
        {
            if (amount <= 0 || IsAlive == false || currentHealth >= maxHealth) return false;
            currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
            PublishHp();
            return true;
        }

        private void PublishHp() => EventBus<PlayerHpChangedEvent>.Publish(new PlayerHpChangedEvent(currentHealth, maxHealth));

        public void TakeDamage(in DamageInfo info)
        {
            currentHealth = Mathf.Max(0, currentHealth - info.Amount);
            PublishHp();
            if (IsDead == false && info.Amount > 0) EventBus<PlayerDamagedEvent>.Publish(new PlayerDamagedEvent(info));
            if (IsDead == false && currentHealth == 0)
            {
                Die();
            }
        }

        private void Die()
        {
            IsDead = true;
            SetMoveInput(Vector3.zero, false); // 죽은 뒤 마지막 이동 입력으로 미끄러지지 않게
            Died?.Invoke();
        }
    }
}
