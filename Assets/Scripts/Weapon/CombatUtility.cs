using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>총알·폭발이 공통으로 쓰는 판정 도우미</summary>
    public static class CombatUtility
    {
        private static readonly RaycastHit[] rayHits = new RaycastHit[32];
        private static readonly Collider[] overlapHits = new Collider[128];
        private static readonly HashSet<IDamageable> damagedThisBlast = new HashSet<IDamageable>();

        /// <summary>
        /// 자기 자신(ignoreRoot 아래 콜라이더)을 무시하고 가장 가까운 명중 지점을 찾는다.
        /// 레이어 설정 없이도 플레이어가 자기 몸에 맞지 않게 하기 위함.
        /// </summary>
        public static bool RaycastIgnoring(Vector3 origin, Vector3 direction, float distance, Transform ignoreRoot, out RaycastHit hit,
                                           int mask = ~0)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, rayHits, distance, mask, QueryTriggerInteraction.Ignore);
            hit = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = rayHits[i];
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            return found;
        }

        /// <summary>
        /// 범위 피해. 한 대상(여러 콜라이더를 가진 몬스터 포함)은 한 번만 맞는다.
        /// 중심에서 멀수록 피해가 줄어든다 (가장자리 = minFalloff 배).
        /// </summary>
        public static void Explode(Vector3 center, float radius, int damage, Faction attacker, GameObject source, int sourceDefId,
                                   float minFalloff = 0.35f)
        {
            int count = Physics.OverlapSphereNonAlloc(center, radius, overlapHits, ~0, QueryTriggerInteraction.Ignore);
            damagedThisBlast.Clear();
            for (int i = 0; i < count; i++)
            {
                var col = overlapHits[i];
                if (col.TryGetDamageable(out var target) == false) continue;
                if (target.CanBeHitBy(attacker) == false) continue;
                if (damagedThisBlast.Add(target) == false) continue;

                Vector3 closest = col.ClosestPoint(center);
                float t = radius > 0f ? Vector3.Distance(center, closest) / radius : 0f;
                int amount = Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Lerp(1f, minFalloff, t)));
                Vector3 dir = closest - center;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;

                var info = new DamageInfo(amount, DamageKind.Explosion, attacker, source, closest, dir.normalized, sourceDefId);
                ApplyDamage(target, col, info);
            }
        }

        /// <summary>피해를 주고 DamageDealtEvent(히트마커용)를 발행</summary>
        public static void ApplyDamage(IDamageable target, Collider hitCollider, in DamageInfo info)
        {
            target.TakeDamage(info);
            GameObject targetObject = target is Component component ? component.gameObject : hitCollider != null ? hitCollider.gameObject : null;
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(targetObject, info, target.IsAlive == false));
        }

        public static void SpawnEffect(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, Transform parent = null)
        {
            if (prefab == null) return;
            var fx = Object.Instantiate(prefab, position, rotation, parent);
            Object.Destroy(fx, lifetime);
        }
    }
}
