using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 던져진 수류탄. 물리(Rigidbody)로 포물선을 그리며 굴러가다 FuseTime 뒤에 폭발한다.
    /// PlayerGrenadeThrower 가 생성 후 Throw 를 부른다. 프리팹에 Rigidbody·Collider 가 없으면 자동으로 붙인다.
    /// </summary>
    public class ThrownGrenade : MonoBehaviour
    {
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float explosionEffectLifetime = 3f;

        private int damage;
        private float radius;
        private GrenadeDef def;
        private GameObject owner;
        private float explodeTime;
        private bool armed;

        public void SetExplosionEffect(GameObject effect) => explosionEffect = effect;

        public void Throw(Vector3 velocity, int damage, float radius, float fuseTime, GrenadeDef def, GameObject owner)
        {
            this.damage = damage;
            this.radius = radius;
            this.def = def;
            this.owner = owner;
            explodeTime = Time.time + fuseTime;
            armed = true;

            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.mass = 0.4f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 콜라이더가 없거나 MeshCollider(볼록 아님)면 동작하지 않으므로 구 콜라이더로 대체
            var cols = GetComponentsInChildren<Collider>();
            bool hasUsable = false;
            foreach (var c in cols)
            {
                if (c is MeshCollider mesh && mesh.convex == false) { c.enabled = false; continue; }
                c.enabled = true;
                hasUsable = true;
            }
            if (hasUsable == false) gameObject.AddComponent<SphereCollider>().radius = 0.06f;

            // 던진 사람과는 부딪히지 않게
            if (owner != null)
                foreach (var mine in GetComponentsInChildren<Collider>())
                foreach (var theirs in owner.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(mine, theirs);

            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 8f;
        }

        private void Update()
        {
            if (armed && Time.time >= explodeTime) Explode();
        }

        private void Explode()
        {
            armed = false;
            Vector3 p = transform.position;
            CombatUtility.Explode(p, radius, damage, Faction.Player, owner != null ? owner : gameObject, def != null ? def.Id : 0);
            CombatUtility.SpawnEffect(explosionEffect, p, Quaternion.identity, explosionEffectLifetime);
            if (def != null) EventBus<GrenadeExplodedEvent>.Publish(new GrenadeExplodedEvent(def, p));
            Destroy(gameObject);
        }
    }
}
