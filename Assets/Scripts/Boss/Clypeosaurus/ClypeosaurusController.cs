using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    public class ClypeosaurusController : MonoBehaviour 
    {
        private static readonly float[] HitHpRatios = { 0.75f, 0.50f, 0.25f };
        [SerializeField] private MonoBehaviour playerSource;
        
        private BossController boss;
        private int nextHitIndex;
        private bool initialized;
        
        private void Awake()
        {
            boss = GetComponent<BossController>();
        }
        
        private void Start()
        {
            if (initialized) return;

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

            Initialize(bossData, target);
        }

        /// <summary>라운드에서 생성할 때 플레이어를 직접 받아 초기화하고, Start의 중복 초기화를 막는다.</summary>
        public void Initialize(BossDef def, IPlayerTarget target)
        {
            if (initialized) return;

            var attackData = DataManager.Clypeosaurus;
            nextHitIndex = 0;
            boss.Initialize(def, target);

            var entrance = GetComponent<EntranceAttack>();
            entrance.Initialize(attackData, target);

            GetComponent<StompAttack>().Initialize(attackData, target);
            GetComponent<MagmaBarrageAttack>().Initialize(attackData, target);
            GetComponent<SuctionAttack>().Initialize(attackData, target);

            initialized = true;
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
