using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    [RequireComponent(typeof(BossView))]
    public class ClypeosaurusBasicAttack : BossAttack
    {
        private const string AttackState = "Base Layer.ClawStrike";

        [SerializeField] private BoxCollider attackArea;
        [SerializeField] private LayerMask targetMask;
        
        private BossView view;
        private ClypeosaurusDef data;
        
        private float nextBasicAttackTime;
        private int animationStartedFrame;
        private bool hitApplied;
        
        public override bool UsesPatternCooldown => false;
        
        protected override void Awake()
        {
            base.Awake();
            view = GetComponent<BossView>();
        }
        
        public void Initialize(ClypeosaurusDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (attackArea == null) throw new InvalidOperationException("Attack Area를 연결해주세요.");

            attackArea.isTrigger = true;
            attackArea.enabled = false;
            
            InitializeAttack(target, 0f, 0f);

            data = def;
            nextBasicAttackTime = Time.time;
        }
        
        protected override bool MeetsStartCondition()
        {
            return Time.time >= nextBasicAttackTime && TryFindTarget(out _);
        }
        
        protected override void OnWindup()
        {
            hitApplied = false;
        }
        
        protected override void OnActiveStarted()
        {
            animationStartedFrame = Time.frameCount;
            view.PlayState(AttackState);
        }
        
        protected override void OnActiveTick(float deltaTime)
        {
            if (Time.frameCount <= animationStartedFrame) return;

            if (view.IsStateFinished(AttackState) == false) return;

            if (hitApplied == false)
            {
                Debug.LogWarning("[BasicAttack] OnBasicAttackHit 이벤트를 확인해주세요.", this);
            }

            nextBasicAttackTime = Time.time + data.BasicAttackCooldown;

            view.PlayIdle();
            CompleteActive();
        }
        
        public void OnBasicAttackHit()
        {
            if (isActiveAndEnabled == false || Phase != SkillPhase.Active || hitApplied || Boss.IsAlive == false 
                || Boss.isActiveAndEnabled == false)
                return;

            hitApplied = true;

            Vector3 center = attackArea.transform.TransformPoint(attackArea.center);

            RaiseImpacted(center);

            if (TargetDamageable.CanBeHitBy(Boss.Faction) == false) return;

            // 공격 시작 후 플레이어가 피했는지 다시 검사한다.
            if (TryFindTarget(out var hitCollider) == false) return;

            Vector3 direction = TargetRoot.position - center;
            direction.y = 0f;

            direction = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : transform.forward;

            TargetDamageable.TakeDamage(new DamageInfo(data.BasicAttackDamage, DamageKind.MonsterAttack, Boss.Faction, Boss.gameObject, hitCollider.ClosestPoint(center), direction));
        }
        
        private bool TryFindTarget(out Collider hitCollider)
        {
            hitCollider = null;

            if (attackArea == null || attackArea.gameObject.activeInHierarchy == false || TargetRoot == null)
                return false;

            Transform areaTransform = attackArea.transform;

            Vector3 center = areaTransform.TransformPoint(attackArea.center);

            Vector3 scale = areaTransform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            Vector3 halfExtents = Vector3.Scale(attackArea.size, scale) * 0.5f;

            var overlaps = Physics.OverlapBox(center, halfExtents, areaTransform.rotation, targetMask, QueryTriggerInteraction.Ignore);

            foreach (var col in overlaps)
            {
                if (col.TryGetDamageable(out var candidate) == false) continue;

                if (object.ReferenceEquals(candidate, TargetDamageable) == false) continue;

                hitCollider = col;
                return true;
            }

            return false;
        }
        
        protected override void OnCancelled()
        {
            if (data != null)
            {
                nextBasicAttackTime = Time.time + data.BasicAttackCooldown;
            }

            if (Boss != null && Boss.IsAlive && gameObject.activeInHierarchy)
            {
                view.PlayIdle();
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            if (attackArea == null) return;

            Matrix4x4 previous = Gizmos.matrix;

            Gizmos.color = Color.yellow;
            Gizmos.matrix = attackArea.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(attackArea.center, attackArea.size);

            Gizmos.matrix = previous;
        }
    }
}