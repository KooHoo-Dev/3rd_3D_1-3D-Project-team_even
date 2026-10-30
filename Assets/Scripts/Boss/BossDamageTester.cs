using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(BossController))]
    public sealed class BossDamageTester : MonoBehaviour
    {
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

        private void OnDisable()
        {
            boss.Damaged -= OnDamaged;
            boss.Died -= OnDied;
        }

        private void OnDamaged(DamageInfo info)
        {
            Debug.Log($"[Boss] 피격 ({info.Kind}) | " + $"HP: {boss.CurrentHp}/{boss.MaxHp} " + $"({boss.HpRatio:P0})", boss);
        }

        private void OnDied()
        {
            Debug.Log("[Boss] 사망 | HP: 0", boss);
        }
    }
}