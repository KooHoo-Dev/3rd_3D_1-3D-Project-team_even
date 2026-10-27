using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    [RequireComponent(typeof(BossView))]
    public class StompAttack : BossAttack
    {
        private const string WindupState = "Base Layer.IdleLookAround";
        private const string SmashState = "Base Layer.SmashAttackForward";
        private const string IdleState = "Base Layer.IdleBreathe";
        
        [SerializeField] private Transform stompCenter;

        private BossView view;
        private ClypeosaurusDef data;
        private Vector3 lockedCenter;
        private bool hitApplied;
        
        protected override void Awake()
        {
            base.Awake();
            view = GetComponent<BossView>();
        }
        
        public void Initialize(ClypeosaurusDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (stompCenter == null) throw new InvalidOperationException("Stomp Center를 연결해주세요.");

            InitializeAttack(target, def.StompTelegraphTime, def.StompRecoveryTime);

            data = def;
        }
        
        protected override void OnWindup()
        {
            // 준비 시작 시 공격 중심을 고정한다.
            lockedCenter = stompCenter.position;
            hitApplied = false;
            
            view.PlayState(WindupState);
        }
        
        protected override void OnActiveStarted()
        {
            view.PlayState(SmashState);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            if (view.IsStateFinished(SmashState) == false) return;
            if (hitApplied == false)
            {
                Debug.LogWarning("[StompAttack] 타격 이벤트가 호출되지 않았습니다.", this);
            }
            
            CompleteActive();
            view.PlayState(IdleState);
        }

        public void OnStompImpact()
        {
            if (isActiveAndEnabled == false || Phase != SkillPhase.Active || hitApplied
                || Boss.IsAlive == false || TargetRoot == null) return;

            hitApplied = true;
            ApplyHit();
        }

        protected override void OnCancelled()
        {
            hitApplied = false;
            
            if (Boss.IsAlive && gameObject.activeInHierarchy)
                view.PlayState(IdleState);
        }

        private void ApplyHit()
        {
            if (TargetDamageable.CanBeHitBy(Boss.Faction) == false) return;

            Vector3 offset = TargetRoot.position - lockedCenter;
            offset.y = 0f;

            float radius = data.StompRadius;
            if (offset.sqrMagnitude > radius * radius) return;

            Vector3 direction = offset.sqrMagnitude > 0.001f
                ? offset.normalized
                : transform.forward;

            var damage = new DamageInfo(data.StompDamage, DamageKind.MonsterSkill, Boss.Faction
                , Boss.gameObject, TargetRoot.position, direction);

            TargetDamageable.TakeDamage(damage);
        }
        
        #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false ||
                data == null ||
                stompCenter == null)
                return;

            Vector3 center = IsRunning
                ? lockedCenter
                : stompCenter.position;

            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(
                center, Vector3.up, data.StompRadius);
        }
        #endif
    }
}
