using HybridShooter.Boss.Clypeosaurus;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossController))]
    public sealed class BossDamageTester : MonoBehaviour
    {
        [SerializeField] private int bossId = 1;
        [SerializeField] private MonoBehaviour playerSource;
        
        private BossController boss;

        private void Awake()
        {
            boss = GetComponent<BossController>();
        }

        private void OnEnable()
        {
            boss.Damaged += OnDamaged;
            boss.Died += OnDied;
        }

        private void Start()
        {
            var def = DataManager.Bosses.Get(bossId);
            var target = playerSource as IPlayerTarget;
                
            if (def == null)
            {
                enabled = false;
                return;
            }

            if (target?.Root == null)
            {
                Debug.LogError( "[Boss] IPlayerTarget을 구현한 플레이어를 연결해주세요.", this);
                
                enabled = false;
                return;
            }

            boss.Initialize(def, target);
            
            var attackData = DataManager.Clypeosaurus;

            var entrance = GetComponent<EntranceAttack>();
            entrance.Initialize(attackData, target);
            
            if (entrance.TryStart() == false)
            {
                Debug.LogError("[Boss] 등장 공격 시작 실패. 초기화·NavMesh를 확인해주세요.", this);
            }
            
            var stomp = GetComponent<StompAttack>();
            stomp.Initialize(attackData, target);

            var barrage = GetComponent<MagmaBarrageAttack>();
            barrage.Initialize(attackData, target);
            
            var suction = GetComponent<SuctionAttack>();
            suction.Initialize(attackData, target);

            Debug.Log($"[Boss] 초기화 | ID: {boss.Def.Id} | " + $"HP: {boss.CurrentHp}/{boss.MaxHp}", boss);
        }

        private void OnDisable()
        {
            boss.Damaged -= OnDamaged;
            boss.Died -= OnDied;
        }

        private void OnDamaged(DamageInfo info)
        {
            Debug.Log($"[Boss] 피격 ({info.Kind}) | " + $"HP: {boss.CurrentHp}/{boss.MaxHp} " 
                         + $"({boss.HpRatio:P0})", boss);
        }

        private void OnDied()
        {
            Debug.Log("[Boss] 사망 | HP: 0", boss);
        }
    }
}