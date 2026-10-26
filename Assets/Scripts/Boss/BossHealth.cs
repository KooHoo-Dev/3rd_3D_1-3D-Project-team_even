using System;

namespace HybridShooter.Boss
{
    public class BossHealth
    {
        public int MaxHp { get; }
        public int CurrentHp { get; private set; }

        public float HpRatio => (float)CurrentHp / MaxHp;
        public bool IsAlive => CurrentHp > 0;

        public BossHealth(int maxHp)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp));

            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

        /// <summary>
        /// 실제 데미지를 받았을 때 true 반환.
        /// </summary>
        public bool ApplyDamage(int amount)
        {
            if (IsAlive == false || amount <= 0) return false;
            
            CurrentHp = Math.Max(0, CurrentHp - amount);
            return true;
        }
    }
}