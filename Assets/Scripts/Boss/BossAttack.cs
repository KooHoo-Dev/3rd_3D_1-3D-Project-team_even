using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossController))]
    public abstract class BossAttack : MonoBehaviour
    {
        protected BossController Boss { get; private set; }
        protected Transform TargetRoot { get; private set; }
        protected IDamageable TargetDamageable { get; private set; }
        
        public SkillPhase Phase { get; private set; } = SkillPhase.Idle;
        public bool IsRunning => Phase != SkillPhase.Idle;
        
        private bool initialized;
        private float windupTime;
        private float recoveryTime;
        private float phaseEndTime;
        
        protected virtual void Awake()
        {
            Boss = GetComponent<BossController>();
        }
        
        protected virtual void OnDisable()
        {
            Cancel();
        }

        protected void InitializeAttack(IPlayerTarget target, float windupTime, float recoveryTime)
        {
            if (target?.Root == null) throw new ArgumentException("플레이어 Root가 필요합니다.", nameof(target));
            
            var receiver = target.Root.GetComponentInParent<IDamageable>();
            if (receiver == null) throw new ArgumentException("플레이어 Root 또는 부모에 IDamageable이 필요합니다.", nameof(target));

            Cancel();

            TargetRoot = target.Root;
            TargetDamageable = receiver;

            this.windupTime = windupTime;
            this.recoveryTime = recoveryTime;

            initialized = true;
        }

        public bool CanStart()
        {
            if (initialized == false || isActiveAndEnabled == false || IsRunning) return false;
            if (Boss == null || Boss.CanStartAttack == false) return false;

            if (TargetRoot == null) return false;

            if (TargetDamageable.CanBeHitBy(Boss.Faction) == false) return false;

            return MeetsStartCondition();
        }
        public bool TryStart()
        {
            if (CanStart() == false) return false;
            // 다른 패턴이 실행 중이면 시작할 수 없다.
            if (Boss.TryBeginAttack(this) == false) return false;

            Phase = SkillPhase.Windup;
            phaseEndTime = Time.time + windupTime;

            OnWindup();
            
            return IsRunning;
        }
        
        private void Update()
        {
            if (IsRunning == false) return;
            if (Boss.IsAlive == false || Boss.isActiveAndEnabled == false || TargetRoot == null)
            {
                Cancel();
                return;
            }

            switch (Phase)
            {
                case SkillPhase.Windup:
                    if (Time.time < phaseEndTime) return;
                    Phase = SkillPhase.Active;
                    
                    OnActiveStarted();
                    break;
                
                case SkillPhase.Active:
                    OnActiveTick(Time.deltaTime);
                    break;
                
                case SkillPhase.Recover:
                    if (Time.time < phaseEndTime) return;
                    
                    Phase = SkillPhase.Idle;
                    Boss.EndAttack(this, completed: true);
                    break;
            }
        }
        
        protected void CompleteActive()
        {
            if (Phase != SkillPhase.Active) return;

            Phase = SkillPhase.Recover;
            phaseEndTime = Time.time + recoveryTime;
        }
        
        public void Cancel()
        {
            if (IsRunning == false) return;
            Phase = SkillPhase.Idle;

            try
            {
                OnCancelled();
            }
            finally
            {
                Boss.EndAttack(this);
            }
        }
        
        protected virtual bool MeetsStartCondition() => true;

        protected virtual void OnWindup() { }

        protected abstract void OnActiveStarted();

        protected virtual void OnActiveTick(float deltaTime) { }

        protected virtual void OnCancelled() { }
    }
}
