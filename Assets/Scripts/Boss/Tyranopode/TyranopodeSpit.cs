using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 산성 담즙 주머니 (침 뱉기). 보스 발광 주머니와 같은 생체막에 싸인 덩어리가 포물선으로 날아가 예고 원 자리에 떨어져 터진다.
    ///  - 날아가는 동안 진행 방향으로 늘어나고 꿀렁인다 (몸 Sac)
    ///  - 떨어진 자리 반경 안(수평) 플레이어에게 피해. 공중에서 플레이어에 닿아도 터진다. 보스 몸에는 터지지 않는다
    ///  - 터지면 예고 원이 번쩍이며 사라진다
    /// </summary>
    public sealed class TyranopodeSpit : MonoBehaviour
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private static readonly Color BurstColor = new Color(0.75f, 1f, 0.3f, 0.85f);
        /// <summary>burstVfx 가 만들어진 기준 반경(m)</summary>
        private const float BurstBaseRadius = 2.5f;

        [SerializeField, Min(0.1f)] private float lifeTime = 4f;
        [SerializeField] private GameObject burstVfx;
        [Tooltip("늘어나고 꿀렁이는 겉막")]
        [SerializeField] private Transform sac;

        private TyranopodeController owner;
        private GroundTelegraph marker;
        private Vector3 velocity;
        private float gravity;
        private int damage;
        private float radius;
        private float dieTime;
        private Vector3 sacScale;

        /// <summary>연출이 연결됐는지 (TyranopodeController 가 생성 순간 검사)</summary>
        public bool HasReferences => burstVfx != null && sac != null;

        /// <summary>
        /// target(바닥)에 flightTime 초 뒤 떨어지도록 쏜다. arcHeight = 출발·도착을 잇는 선 가운데에서 꼭대기까지 높이(m).
        /// landingMarker = 떨어질 자리 예고 원 (터질 때 번쩍인다)
        /// </summary>
        public void Launch(TyranopodeController controller, Vector3 target, float flightTime, float arcHeight, int hitDamage, float hitRadius, GroundTelegraph landingMarker)
        {
            owner = controller;
            marker = landingMarker;
            damage = hitDamage;
            radius = hitRadius;
            gravity = 8f * arcHeight / (flightTime * flightTime);
            velocity = (target - transform.position) / flightTime + Vector3.up * (gravity * flightTime * 0.5f);
            dieTime = Time.time + lifeTime;
            transform.localScale *= controller.BodyScale;
            sacScale = sac.localScale;
        }

        private void Update()
        {
            if (owner == null || Time.time >= dieTime)
            {
                if (marker != null) marker.Hide();
                Destroy(gameObject);
                return;
            }

            Vector3 from = transform.position;
            velocity += Vector3.down * gravity * Time.deltaTime;
            Vector3 step = velocity * Time.deltaTime;

            if (Vector3.Distance(from, Chest) <= radius * 0.6f)
            {
                Burst(from);
                return;
            }
            if (TryHitWorld(from, step, out Vector3 point))
            {
                Burst(point);
                return;
            }
            transform.SetPositionAndRotation(from + step, Quaternion.LookRotation(velocity));

            // 진행 방향으로 늘어나며 꿀렁인다
            float wobble = Mathf.Sin(Time.time * 18f) * 0.08f;
            sac.localScale = Vector3.Scale(sacScale, new Vector3(0.88f + wobble, 0.88f - wobble, 1.25f));
        }

        private Vector3 Chest => owner.Target.position + Vector3.up * 1.2f;

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
            // 바닥 예고 원과 같은 범위: 수평 반경 안 + 높이 차가 크지 않으면 (공중에서 몸에 닿은 경우 포함)
            Vector3 feet = owner.Target.position;
            Vector3 flat = feet - point;
            float height = flat.y;
            flat.y = 0f;
            bool inside = flat.magnitude <= radius && height > -radius && height < radius + 1.2f;
            if (inside && owner.TargetDamageable.CanBeHitBy(Faction.Monster))
            {
                Vector3 dir = velocity;
                dir.y = 0f;
                owner.TargetDamageable.TakeDamage(new DamageInfo(damage, DamageKind.MonsterSkill, Faction.Monster, owner.gameObject, point, dir.normalized));
            }

            owner.RaiseSoundCue(TyranopodeSoundCue.SpitImpact, point);
            marker.Detonate(BurstColor);
            var vfx = Instantiate(burstVfx, point, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * (radius / BurstBaseRadius);
            Destroy(vfx, 3f);
            Destroy(gameObject);
        }
    }
}
