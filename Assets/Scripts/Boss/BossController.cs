using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossMovement))]
    public class BossController : MonoBehaviour, IDamageable
    {
        private BossHealth health;
        private BossMovement movement;
        private BossAttack currentAttack;
        private float nextAttackTime;
        
        public BossDef Def { get; private set; }

        public Faction Faction => Faction.Monster;
        public bool IsAlive => health != null && health.IsAlive;
        public int MaxHp => health?.MaxHp ?? 0;
        public int CurrentHp => health?.CurrentHp ?? 0;
        public float HpRatio => health?.HpRatio ?? 0f;
        public bool IsAttacking => currentAttack != null;
        public bool CanStartAttack => isActiveAndEnabled && IsAlive && IsAttacking == false && Time.time >= nextAttackTime;
        
        public event Action<DamageInfo> Damaged;
        public event Action Died;

        private void Awake()
        {
            movement = GetComponent<BossMovement>();
        }

        public void Initialize(BossDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (target?.Root == null) throw new ArgumentException("플레이어 Root가 필요합니다.", nameof(target));
            
            currentAttack?.Cancel();
            
            Def = def;
            health = new BossHealth(def.MaxHp);
            nextAttackTime = Time.time;
            
            movement.Initialize(def.MoveSpeed, target.Root);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (this.CanBeHitBy(info.AttackerFaction) == false) return;
            if (health.ApplyDamage(info.Amount) == false) return;

            bool died = (health.IsAlive == false);
            
            if (died)
            {
                movement.Stop();
                currentAttack?.Cancel();
            }
            
            Damaged?.Invoke(info);
            
            if (died) Died?.Invoke();
        }
        
        internal bool TryBeginAttack(BossAttack attack)
        {
            if (CanStartAttack == false) return false;

            currentAttack = attack;
            movement.Pause();
            return true;
        }

        internal void EndAttack(BossAttack attack, bool completed = false)
        {
            if (currentAttack != attack) return;

            currentAttack = null;

            // 정상적으로 회복까지 마쳤을 때 공통 쿨타임을 시작한다.
            if (completed) nextAttackTime = Time.time + Def.PatternCooldown;

            if (IsAlive && isActiveAndEnabled) movement.Resume();
            else movement.Stop();
        }
    }
}
