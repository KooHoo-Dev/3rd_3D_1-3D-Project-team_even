using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 손으로 던지는 수류탄 프리팹 (BL-005).
    ///
    ///  - 수치(피해·반경·폭발 시간)는 갖지 않는다. 던지는 쪽(PlayerItems)이 Launch 로 계산된 값을 넘긴다.
    ///    → 강화가 바뀌어도 프리팹은 그대로.
    ///  - Launch 전에는 Rigidbody 가 Kinematic 이라 씬에 놓여 있어도 떨어지거나 터지지 않는다.
    ///  - 날아가는 건 Rigidbody 물리 (튕기고 구른다). 폭발은 로켓과 같은 GunExplosion.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Grenade : MonoBehaviour
    {
        [Header("폭발")]
        [Tooltip("반경 끝에서의 피해 비율")]
        [SerializeField, Range(0f, 1f)] private float minDamageRatio = 0.25f;
        [Tooltip("범위 안 리지드바디에 주는 힘")]
        [SerializeField] private float force = 10f;
        [Tooltip("폭발 이펙트 (선택)")]
        [SerializeField] private GameObject explosionEffectPrefab;

        [Header("투척")]
        [Tooltip("던질 때 주는 회전 속도 (rad/s)")]
        [SerializeField] private float spin = 10f;

        private Rigidbody body;
        private GrenadeStats stats;
        private ProjectileContext context;
        private GrenadeDef def;
        private float explodeTime;
        private bool launched;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (launched == false) body.isKinematic = true;
            body.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f); // BL-005 모델이 너무 커서 줄임
        }

        /// <summary>던지기. ignoreColliders = 던진 사람 몸 (처음에 부딪혀 튕기지 않게)</summary>
        public void Launch(in GrenadeStats grenadeStats, in ProjectileContext projectileContext, GrenadeDef grenadeDef,
                           Vector3 velocity, Collider[] ignoreColliders)
        {
            stats = grenadeStats;
            context = projectileContext;
            def = grenadeDef;
            launched = true;
            explodeTime = Time.time + Mathf.Max(0f, stats.fuseTime);

            if (ignoreColliders != null)
            {
                var own = GetComponentsInChildren<Collider>();
                foreach (var a in own)
                    foreach (var b in ignoreColliders)
                        if (a != null && b != null) Physics.IgnoreCollision(a, b);
            }

            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.onUnitSphere * spin;
        }

        private void Update()
        {
            if (launched && Time.time >= explodeTime) Explode();
        }

        private void Explode()
        {
            launched = false;
            Vector3 center = transform.position;
            var settings = new ExplosionSettings
            {
                damage = stats.damage,
                radius = stats.radius,
                minDamageRatio = minDamageRatio,
                force = force,
                effectPrefab = explosionEffectPrefab,
                sfxKey = def?.ExplodeSfx?.Key,
            };
            GunExplosion.Explode(center, settings, context);
            EventBus<GrenadeExplodedEvent>.Publish(new GrenadeExplodedEvent(def, center));
            Destroy(gameObject);
        }
    }
}
