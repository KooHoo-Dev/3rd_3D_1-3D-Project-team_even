using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossMovement), typeof(BossView))]
    public class BossController : MonoBehaviour, IDamageable
    {
        private static readonly float[] HitHpRatios = { 0.75f, 0.50f, 0.25f };
        
        private BossHealth health;
        private BossMovement movement;
        private BossView view;
        private BossAttack currentAttack;
        
        private float nextAttackTime;
        private int nextHitIndex;
        
        public BossDef Def { get; private set; }

        public Faction Faction => Faction.Monster;
        public bool IsAlive => health != null && health.IsAlive;
        public int MaxHp => health?.MaxHp ?? 0;
        public int CurrentHp => health?.CurrentHp ?? 0;
        public float HpRatio => health?.HpRatio ?? 0f;
        public bool IsAttacking => currentAttack != null;
        public bool IsInvulnerable { get; private set; }
        public bool IsReacting { get; private set; }
        
        public bool CanStartAttack => isActiveAndEnabled && IsAlive && IsAttacking == false && IsReacting == false && Time.time >= nextAttackTime;
        
        public event Action<DamageInfo> Damaged;
        public event Action Died;

        private void Awake()
        {
            movement = GetComponent<BossMovement>();
            view = GetComponent<BossView>();
        }

        public void Initialize(BossDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (target?.Root == null) throw new ArgumentException("플레이어 Root가 필요합니다.", nameof(target));
            
            currentAttack?.Cancel();
            
            Def = def;
            health = new BossHealth(def.MaxHp);
            nextAttackTime = Time.time;
            nextHitIndex = 0;

            IsReacting = false;
            IsInvulnerable = false;
            
            movement.Initialize(def.MoveSpeed, target.Root);
            view.ResetState();
        }

        private void Update()
        {
            if (IsReacting == false || view.IsHitFinished == false) return;

            IsReacting = false;

            if (IsAlive)
            {
                view.PlayIdle();
                movement.Resume();
            }
        }
        
        public void TakeDamage(in DamageInfo info)
        {
            if (IsInvulnerable) return;
            if (this.CanBeHitBy(info.AttackerFaction) == false) return;
            if (health.ApplyDamage(info.Amount) == false) return;
            
            bool died = (health.IsAlive == false);
            
            if (died)
            {
                IsReacting = false;
                
                movement.Stop();
                currentAttack?.Cancel();
            }
            else
            {
                CheckHitReaction();
            }
            
            
            Damaged?.Invoke(info);
            // 일반 몬스터와 같은 피격 사실 → 히트마커·타격음·데미지 숫자
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, died));

            if (died) Died?.Invoke();
        }

        private void CheckHitReaction()
        {
            bool crossed = false;

            // 한 번에 여러 기준을 넘으면 모두 사용 처리하도록
            while (nextHitIndex < HitHpRatios.Length && HpRatio <= HitHpRatios[nextHitIndex])
            {
                nextHitIndex++;
                crossed = true;
            }

            if (crossed == false) return;

            // 공격 취소 중에도 이동이 재개되지 않도록 먼저 설정
            IsReacting = true;

            currentAttack?.Cancel();
            movement.Pause();

            view.PlayHit();
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

            if (completed) nextAttackTime = Time.time + Def.PatternCooldown;

            if (IsAlive == false || isActiveAndEnabled == false)
            {
                movement.Stop();
            }
            else if (IsReacting == false)
            {
                movement.Resume();
            }
        }
        
        public void SetInvulnerable(bool value)
        {
            IsInvulnerable = value;
        }
    }
}
