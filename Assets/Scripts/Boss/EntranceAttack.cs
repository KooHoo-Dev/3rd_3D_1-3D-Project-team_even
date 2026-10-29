using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using UnityEngine;
using UnityEngine.AI;

namespace  HybridShooter.Boss.Clypeosaurus
{
    [RequireComponent(typeof(BossView), typeof(UnityEngine.AI.NavMeshAgent))]
    public class EntranceAttack : BossAttack
    {
        private const string IdleState = "Base Layer.IdleBreathe";
        private const string RoarState = "Base Layer.RoarAntennaElectricAttack";
        
        private enum EntranceStage
        {
            Falling,
            Roaring
        }
        
        [Header("낙하 연출")]
        [SerializeField, Min(0.01f)] private float fallHeight = 30f;
        [SerializeField, Min(0.01f)] private float fallDuration = 0.8f;

        [Header("이펙트")]
        [SerializeField] private Transform shadowPrefab;
        [SerializeField] private GameObject shockwavePrefab;

        private BossView view;
        private NavMeshAgent agent;
        private ClypeosaurusDef data;

        private Transform shadow;
        private Vector3 landingPoint;
        private Vector3 fallStartPoint;

        private float windupStatedAt;
        private float fallStartedAt;

        private EntranceStage stage;
        private bool used;
        private bool agentDetached;
        
        protected override void Awake()
        {
            base.Awake();

            view = GetComponent<BossView>();
            agent = GetComponent<NavMeshAgent>();
        }

        public void Initialize(ClypeosaurusDef def, IPlayerTarget target)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            if (shadowPrefab == null) throw new InvalidOperationException("Shadow Prefab을 연결해주세요.");

            if (fallHeight <= 0f || fallDuration <= 0f) throw new InvalidOperationException("낙하 높이와 시간은 0보다 커야 합니다.");

            InitializeAttack(target, def.EntranceTelegraphTime, def.EntranceRecoveryTime);

            data = def;
            used = false;
        }
        
        protected override bool MeetsStartCondition()
        {
            return (used == false) && agent.isActiveAndEnabled && agent.isOnNavMesh;
        }

        protected override void OnWindup()
        {
            Boss.SetInvulnerable(true);
            
            used = true;
            windupStatedAt = Time.time;

            landingPoint = transform.position;
            fallStartPoint = landingPoint + Vector3.up * fallHeight;

            agent.enabled = false;
            agentDetached = true;
            transform.position = fallStartPoint;
            
            // * 0.05f: 바닥에 겹치는 걸 막기 위해 곱함
            shadow = Instantiate(shadowPrefab, landingPoint + Vector3.up * 0.05f, Quaternion.identity);

            SetShadowScale(0f);
            view.PlayState(IdleState);
        }

        private void LateUpdate()
        {
            if (Phase != SkillPhase.Windup || shadow == null) return;
            float duration = data.EntranceTelegraphTime;
            
            float progress = duration > 0f ? Mathf.Clamp01((Time.time - windupStatedAt) / duration) : 1f;
            SetShadowScale(progress);
        }

        protected override void OnActiveStarted()
        {
            stage = EntranceStage.Falling;
            fallStartedAt = Time.time;

            SetShadowScale(1f);
        }
        
        protected override void OnActiveTick(float deltaTime)
        {
            switch (stage)
            {
                case EntranceStage.Falling:
                    TickFall();
                    break;
                
                case EntranceStage.Roaring:
                    if (view.IsStateFinished(RoarState))
                    {
                        Boss.SetInvulnerable(false);
                        
                        CompleteActive();
                        view.PlayState(IdleState);
                    }
                    break;
            }
        }
        
        private void TickFall()
        {
            float progress = Mathf.Clamp01((Time.time - fallStartedAt) / fallDuration);
            transform.position = Vector3.Lerp(fallStartPoint, landingPoint, progress * progress);

            if (progress >= 1f) Land();
        }

        private void Land()
        {
            stage = EntranceStage.Roaring;

            if (RestoreAgent() == false)
            {
                Debug.LogError("[Entrance] 착지 지점에 navMesh가 없습니다.", this);

                Cancel();
                return;
            }

            ClearShadow();

            view.PlayState(RoarState);
            RaiseImpacted(landingPoint);

            if (shockwavePrefab != null)
            {
                var effect = Instantiate(shockwavePrefab, landingPoint, Quaternion.identity);
                var wave = effect.GetComponent<EntranceShockwave>();
                wave.Initialize(data.EntranceRadius);

                Destroy(effect, 3f);
            }

            ApplyLandingDamage();
        }

        private void ApplyLandingDamage()
        {
            if (TargetRoot == null || TargetDamageable.CanBeHitBy(Boss.Faction) == false) return;

            Vector3 offset = TargetRoot.position - landingPoint;
            offset.y = 0f;

            float radius = data.EntranceRadius;

            if (offset.sqrMagnitude > radius * radius) return;

            Vector3 direction = offset.sqrMagnitude > 0.001f
                ? offset.normalized
                : transform.forward;
            
            TargetDamageable.TakeDamage(new DamageInfo(data.EntranceDamage, DamageKind.MonsterSkill, Boss.Faction, Boss.gameObject, TargetRoot.position, direction));
        }
        
        private void SetShadowScale(float progress)
        {
            if (shadow == null) return;

            float diameter = data.EntranceRadius * 2f * progress;
            shadow.localScale = new Vector3(diameter, 1f, diameter);
        }

        private bool RestoreAgent()
        {
            if (agentDetached == false) return true;

            transform.position = landingPoint;
            agentDetached = false;
            agent.enabled = true;

            if (gameObject.activeInHierarchy == false) return true;

            return agent.isOnNavMesh && agent.Warp(landingPoint);
        }

        private void ClearShadow()
        {
            if (shadow == null) return;
            Destroy(shadow.gameObject);
            shadow = null;
        }

        protected override void OnCancelled()
        {
            ClearShadow();
            RestoreAgent();
            
            if (Boss.IsAlive && gameObject.activeInHierarchy) view.PlayState(IdleState);
        }
    }

}