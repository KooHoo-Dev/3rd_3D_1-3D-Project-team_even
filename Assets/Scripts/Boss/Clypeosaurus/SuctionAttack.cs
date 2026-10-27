using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    [RequireComponent(typeof(BossView))]
    public class SuctionAttack : BossAttack
    {
        private const string WindupState = "Base Layer.IdleLookAround";
        private const string StartState = "Base Layer.SuctionStart";
        private const string EndState = "Base Layer.SuctionEnd";
        private const string IdleState = "Base Layer.IdleBreathe";
        
        [SerializeField] private Transform inhaleCenter;
        [SerializeField, Min(0f)] private float stopRadius = 2.5f;
        [SerializeField, Min(0f)] private float fadeIn = 0.3f;

        private BossView view;
        private ClypeosaurusDef data;
        private InhaleSettings settings;
        private InhaleSource activeInhale;
        private IPlayerExternalVelocity receiver;

        protected override void Awake()
        {
            base.Awake();
            view = GetComponent<BossView>();
        }
        
        public void Initialize(ClypeosaurusDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (inhaleCenter == null) throw new InvalidOperationException("Inhale Center를 연결해주세요.");
            if (stopRadius < 0f || stopRadius >= def.SuctionNearRadius) 
                throw new InvalidOperationException("Stop Radius는 0 이상이고 SuctionNearRadius보다 작아야 합니다.");
            if (fadeIn < 0f) throw new InvalidOperationException("Fade In은 0 이상이어야 합니다.");
            
            InitializeAttack(target, def.SuctionTelegraphTime, def.SuctionRecoveryTime);
            data = def;

            settings = new InhaleSettings
            {
                NearRadius = def.SuctionNearRadius,
                StopRadius = stopRadius,
                FarExtraSpeed = def.SuctionFarExtraSpeed,
                NearExtraSpeed = def.SuctionNearExtraSpeed,
                MaxDuration = def.SuctionMaxDuration,
                NearDwellTime = def.SuctionNearDwellTime,
                FadeIn = fadeIn
            };
        }
        
        protected override void OnWindup()
        {
            view.PlayState(WindupState);
        }
        
        protected override void OnActiveStarted()
        {
            if (inhaleCenter == null || ServiceLocator.TryGet(out receiver) == false)
            {
                Debug.LogWarning("[Suction] 흡입 중심 또는 플레이어 API를 찾을 수 없습니다.", this);
                Cancel();
                return;
            }

            activeInhale = InhaleSource.Start(settings, inhaleCenter);

            if (activeInhale.IsRunning == false)
            {
                Debug.LogWarning($"[Suction] 시작 실패: {activeInhale.EndReason}", this);
                Cancel();
                return;
            }
            
            view.PlayState(StartState);
            
            Debug.Log("[Suction] 흡입 시작", this);
        }
        
        protected override void OnActiveTick(float deltaTime)
        {
            if (activeInhale == null || TargetDamageable.CanBeHitBy(Boss.Faction) == false)
            {
                Cancel();
                return;
            }

            if (activeInhale.IsRunning) return;

            InhaleEndReason reason = activeInhale.EndReason;
            StopInhale();

            // 시간 또는 체류 조건으로 끝난 경우에 정상 회복으로 진행한다.
            if (reason != InhaleEndReason.Timeout && reason != InhaleEndReason.NearDwell)
            {
                Cancel();
                return;
            }

            view.PlayState(EndState);
            CompleteActive();
            
            Debug.Log($"[Suction] 흡입 종료: {reason}, 회복 {data.SuctionRecoveryTime}초", this);
        }
        
        protected override void OnCancelled()
        {
            StopInhale();

            if (view != null && Boss != null && Boss.IsAlive && gameObject.activeInHierarchy)
            {
                view.PlayState(IdleState);
            }
        }

        private void StopInhale()
        {
            if (activeInhale != null)
            {
                activeInhale.Stop();
                receiver?.RemoveVelocitySource(activeInhale);
            }

            activeInhale = null;
            receiver = null;
        }
        
    }
}