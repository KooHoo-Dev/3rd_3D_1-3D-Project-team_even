using System;
using HybridShooter.Boss;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Boss
{
    public class BossController : MonoBehaviour, IDamageable
    {
        private BossHealth bossHealth;
        public BossDef Def { get; private set; }

        public Faction Faction => Faction.Monster;
        public bool IsAlive => bossHealth != null && bossHealth.IsAlive;

        public int MaxHp => bossHealth?.MaxHp ?? 0;
        public int CurrentHp => bossHealth?.CurrentHp ?? 0;
        public float HpRatio => bossHealth?.HpRatio ?? 0f;
        
        public event Action<DamageInfo> Damaged;
        public event Action Died;
        
        public void Initialize(BossDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            Def = def;
            bossHealth = new BossHealth(def.MaxHp);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (this.CanBeHitBy(info.AttackerFaction) == false) return;
            if (bossHealth.ApplyDamage(info.Amount) == false) return;

            bool died = (bossHealth.IsAlive == false);
            
            Damaged?.Invoke(info);
            
            if (died) Died?.Invoke();
        }
    }
}
