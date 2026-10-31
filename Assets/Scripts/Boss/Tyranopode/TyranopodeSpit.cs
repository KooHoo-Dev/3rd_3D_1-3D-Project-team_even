using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 침 덩어리. 곧게 날아가다 플레이어 근처·지형에 닿으면 터져 반경 안의 플레이어에게 피해. 보스 몸에는 터지지 않는다
    /// </summary>
    public sealed class TyranopodeSpit : MonoBehaviour
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        [SerializeField, Min(0.1f)] private float lifeTime = 4f;
        [SerializeField] private GameObject burstVfx;

        private TyranopodeController owner;

        /// <summary>연출이 연결됐는지 (TyranopodeController 가 생성 순간 검사)</summary>
        public bool HasReferences => burstVfx != null;
        private Vector3 velocity;
        private int damage;
        private float radius;
        private float dieTime;

        public void Launch(TyranopodeController controller, Vector3 launchVelocity, int hitDamage, float hitRadius)
        {
            owner = controller;
            velocity = launchVelocity;
            damage = hitDamage;
            radius = hitRadius;
            dieTime = Time.time + lifeTime;
        }

        private void Update()
        {
            if (owner == null || Time.time >= dieTime)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 from = transform.position;
            Vector3 step = velocity * Time.deltaTime;
            Vector3 chest = owner.Target.position + Vector3.up * 1.2f;

            if (Vector3.Distance(from, chest) <= radius * 0.6f)
            {
                Burst(from);
                return;
            }
            if (TryHitWorld(from, step, out Vector3 point))
            {
                Burst(point);
                return;
            }
            transform.position = from + step;
        }

        private bool TryHitWorld(Vector3 from, Vector3 step, out Vector3 point)
        {
            point = from;
            float length = step.magnitude;
            if (length < 0.0001f) return false;
            int count = Physics.RaycastNonAlloc(from, step / length, Hits, length, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.IsChildOf(owner.transform)) continue;
                if (Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    point = Hits[i].point;
                }
            }
            return best < float.MaxValue;
        }

        private void Burst(Vector3 point)
        {
            Vector3 chest = owner.Target.position + Vector3.up * 1.2f;
            if (Vector3.Distance(point, chest) <= radius && owner.TargetDamageable.CanBeHitBy(Faction.Monster))
            {
                Vector3 dir = velocity;
                dir.y = 0f;
                owner.TargetDamageable.TakeDamage(new DamageInfo(damage, DamageKind.MonsterSkill, Faction.Monster, owner.gameObject, point, dir.normalized));
            }
            Destroy(Instantiate(burstVfx, point, Quaternion.identity), 3f);
            Destroy(gameObject);
        }
    }
}
