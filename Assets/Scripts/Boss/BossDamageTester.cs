using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossController))]
    public sealed class BossDamageTester : MonoBehaviour
    {
        [SerializeField] private int bossId = 1;

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

            if (def == null)
            {
                enabled = false;
                return;
            }

            boss.Initialize(def);

            Debug.Log(
                $"[Boss] 초기화 | ID: {boss.Def.Id} | " +
                $"HP: {boss.CurrentHp}/{boss.MaxHp}",
                boss);
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