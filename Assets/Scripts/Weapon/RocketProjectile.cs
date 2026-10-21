using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 로켓런처 탄. 앞으로 곧게 날아가다가 무언가에 닿거나 수명이 다하면 폭발한다.
    /// 물리 엔진 대신 매 프레임 레이캐스트로 충돌을 확인 → 빠른 탄도 벽을 뚫지 않는다.
    /// PlayerWeapons 가 Launch 로 생성·발사한다 (프리팹에 이 컴포넌트가 없으면 자동으로 붙는다).
    /// </summary>
    public class RocketProjectile : MonoBehaviour
    {
        [SerializeField] private float lifetime = 6f;
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float explosionEffectLifetime = 3f;

        private Vector3 velocity;
        private int damage;
        private float radius;
        private Transform owner;
        private float dieTime;
        private bool exploded;

        public void Launch(Vector3 direction, float speed, int damage, float radius, Transform owner, GameObject effect = null)
        {
            velocity = direction.normalized * speed;
            this.damage = damage;
            this.radius = radius;
            this.owner = owner;
            if (effect != null) explosionEffect = effect;
            dieTime = Time.time + lifetime;
            transform.rotation = Quaternion.LookRotation(direction);

            // 프리팹에 붙어 있는 물리는 끈다 (직접 움직이므로)
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var body in GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
        }

        private void Update()
        {
            if (exploded) return;

            Vector3 step = velocity * Time.deltaTime;
            if (CombatUtility.RaycastIgnoring(transform.position, step.normalized, step.magnitude, owner, out var hit))
            {
                transform.position = hit.point;
                Explode();
                return;
            }

            transform.position += step;
            if (Time.time >= dieTime) Explode();
        }

        private void Explode()
        {
            exploded = true;
            CombatUtility.Explode(transform.position, radius, damage, Faction.Player, owner != null ? owner.gameObject : gameObject, 0);
            CombatUtility.SpawnEffect(explosionEffect, transform.position, Quaternion.identity, explosionEffectLifetime);
            Destroy(gameObject);
        }
    }
}
