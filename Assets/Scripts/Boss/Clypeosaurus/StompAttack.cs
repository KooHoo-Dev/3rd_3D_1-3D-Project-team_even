using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    public class StompAttack : BossAttack
    {
        [SerializeField] private Transform stompCenter;

        private ClypeosaurusDef data;
        private Vector3 lockedCenter;
        
        public void Initialize(ClypeosaurusDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (stompCenter == null) throw new InvalidOperationException("Stomp Center를 연결해주세요.");

            InitializeAttack(target, def.StompTelegraphTime, def.StompRecoveryTime, def.StompCooldown);

            data = def;
        }

        protected override void OnWindup()
        {
            // 준비 시작 시 공격 중심을 고정한다.
            lockedCenter = stompCenter.position;
        }
        
        protected override void OnActiveStarted()
        {
            ApplyHit();
            CompleteActive();
        }
        
        private void ApplyHit()
        {
            if (TargetDamageable.CanBeHitBy(Boss.Faction) == false) return;

            Vector3 offset = TargetRoot.position - lockedCenter;
            offset.y = 0f;

            float radius = data.StompRadius;
            if (offset.sqrMagnitude > radius * radius) return;

            Vector3 direction = offset.sqrMagnitude > 0.0001f
                ? offset.normalized
                : transform.forward;

            var damage = new DamageInfo(data.StompDamage, DamageKind.MonsterSkill, Boss.Faction
                , Boss.gameObject, TargetRoot.position, direction);

            TargetDamageable.TakeDamage(damage);
        }
    }
}