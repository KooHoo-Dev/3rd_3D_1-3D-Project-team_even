using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    public class ClypeosaurusController : MonoBehaviour 
    {
        private static readonly float[] HitHpRatios = { 0.75f, 0.50f, 0.25f };
        [SerializeField] private MonoBehaviour playerSource;
        
        [SerializeField] private CameraShakeSettings footstepShake = CameraShakePresets.BossFootstep.WithIntensity(0.25f);
        [SerializeField, Min(0.1f)] private float footstepShakeRadius = 40f;
        
        private BossController boss;
        private int nextHitIndex;
        
        private void Awake()
        {
            boss = GetComponent<BossController>();
        }
        
        private void Start()
        {
            var target = playerSource as IPlayerTarget;

            if (target?.Root == null)
            {
                Debug.LogError("[Clypeosaurus] IPlayerTarget을 구현한 플레이어를 연결해주세요.", this);

                enabled = false;
                return;
            }

            var attackData = DataManager.Clypeosaurus;
            var bossData = DataManager.Bosses.Get(attackData.BossId);

            if (bossData == null)
            {
                enabled = false;
                return;
            }

            nextHitIndex = 0;
            boss.Initialize(bossData, target);

            var entrance = GetComponent<EntranceAttack>();
            entrance.Initialize(attackData, target);

            GetComponent<StompAttack>().Initialize(attackData, target);
            GetComponent<MagmaBarrageAttack>().Initialize(attackData, target);
            GetComponent<SuctionAttack>().Initialize(attackData, target);

            if (entrance.TryStart() == false)
            {
                Debug.LogError("[Clypeosaurus] 등장 공격 시작 실패. 초기화·NavMesh를 확인해주세요.", this);
            }

            Debug.Log($"[Boss] 초기화 | ID: {boss.Def.Id} | " + $"HP: {boss.CurrentHp}/{boss.MaxHp}", boss);
        }
        
        private void OnDamaged(DamageInfo info)
        {
            if (boss.IsAlive == false) return;

            bool crossed = false;

            while (nextHitIndex < HitHpRatios.Length && boss.HpRatio <= HitHpRatios[nextHitIndex])
            {
                nextHitIndex++;
                crossed = true;
            }

            if (crossed)
            {
                boss.StartHitReaction();
            }
        }
        
        public void LeftFoot()
        {
            Debug.Log(
                $"[DEBUG-boss-footstep] LeftFoot | frame: {Time.frameCount}",
                this);

            ShakeFootstep();
        }

        public void RightFoot()
        {
            Debug.Log(
                $"[DEBUG-boss-footstep] RightFoot | frame: {Time.frameCount}",
                this);

            ShakeFootstep();
        }
        
        private void ShakeFootstep()
        {
            if (isActiveAndEnabled == false || boss == null) return;
            if (boss.IsAlive == false || boss.IsAttacking || boss.IsReacting) return;

            var settings = footstepShake.At(transform.position, footstepShakeRadius);

            EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(settings));
        }
        
        private void OnEnable()
        {
            boss.Damaged += OnDamaged;
        }
        
        private void OnDisable()
        {
            boss.Damaged -= OnDamaged;
        }
        
        
    }
}