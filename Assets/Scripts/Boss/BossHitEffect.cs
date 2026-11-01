using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Behaviour;
using UnityEngine;

namespace HybridShooter.Boss
{
    /// <summary>
    /// 보스가 총에 맞으면 맞은 지점에 피를 튀긴다. 보스 루트(BossController 옆)에 붙인다.
    /// BossController.Damaged 를 구독하므로 몸통 직격(Clypeosaurus)과 부위 히트박스(Tyranopode → ApplyPartDamage → BossController) 모두 같은 길로 온다.
    /// 피 이펙트는 일반 몬스터와 같은 프리팹·EffectPool 을 재사용한다.
    /// </summary>
    [RequireComponent(typeof(BossController))]
    public sealed class BossHitEffect : MonoBehaviour
    {
        [Header("피격 이펙트")]
        [Tooltip("총에 맞은 자리에 재생할 피 (PooledEffect 포함, 예: Prefabs/Effects/Blood_Hit)")]
        [SerializeField] private PooledEffect hitBloodPrefab;
        [Tooltip("피 이펙트 크기 배율 (1 = 일반 몬스터와 같은 크기). 보스 몸집에 맞춰 키운다")]
        [SerializeField, Min(0.1f)] private float bloodScale = 2f;

        private BossController boss;
        private EffectPool hitBloodPool;

        private void Awake()
        {
            boss = GetComponent<BossController>();
            hitBloodPool = new EffectPool(hitBloodPrefab);
        }

        private void OnEnable()
        {
            boss.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            boss.Damaged -= OnDamaged;
        }

        /// <summary>BossController 가 피해를 실제로 깎을 때마다 부른다. 총알 피해만 피를 튀긴다</summary>
        /// <param name="info">받은 피해. HitPoint = 명중 위치, Direction = 공격자 → 보스</param>
        private void OnDamaged(DamageInfo info)
        {
            if (info.Kind != DamageKind.Bullet) return;

            PlayHitBlood(info.HitPoint, info.Direction);
        }

        /// <summary>맞은 지점에서 총알이 들어온 쪽(공격자 쪽)으로 피를 튀긴다</summary>
        /// <param name="hitPoint">명중 위치 (월드)</param>
        /// <param name="incomingDirection">피해가 들어온 방향 (공격자 → 보스, 월드). 0 = 방향 없음 → 위로 튄다</param>
        private void PlayHitBlood(Vector3 hitPoint, Vector3 incomingDirection)
        {
            Vector3 sprayDirection = Vector3.up;
            if (incomingDirection != Vector3.zero) sprayDirection = -incomingDirection;

            PooledEffect blood = hitBloodPool.Play(hitPoint, Quaternion.LookRotation(sprayDirection));
            blood.transform.localScale = Vector3.one * bloodScale;
        }
    }
}
