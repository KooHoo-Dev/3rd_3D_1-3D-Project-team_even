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

        [Header("착지")]
        [Tooltip("처음 부딪힐 때 남길 속도 비율 (작을수록 그 자리에 멈춘다)")]
        [SerializeField, Range(0f, 1f)] private float impactVelocityKeep = 0.2f;
        [Tooltip("착지 후 이동 감쇠 (구르지 않게)")]
        [SerializeField] private float landedLinearDamping = 3f;
        [Tooltip("착지 후 회전 감쇠")]
        [SerializeField] private float landedAngularDamping = 5f;

        [Header("몬스터")]
        [Tooltip("부딪히면 튕기지 않고 그 자리에서 떨어지는 레이어 (살아 있는 몬스터·보스). 이후 이 레이어와는 충돌하지 않는다")]
        [SerializeField] private LayerMask monsterMask;
        [Tooltip("몬스터에 부딪힌 뒤 아래로 주는 속도 (m/s)")]
        [SerializeField] private float monsterDropSpeed = 1f;

        private Rigidbody body;
        private GrenadeStats stats;
        private ProjectileContext context;
        private GrenadeDef def;
        private float explodeTime;
        private bool launched;
        private bool landed;

        private void OnEnable() => EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);

        private void OnDisable() => EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);

        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            if (launched == false) return;
            launched = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

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

            landed = false;
            body.excludeLayers = 0;
            body.isKinematic = false;
            // 던지는 쪽이 감쇠 없는 포물선으로 착지점을 계산하므로 날아가는 동안은 감쇠를 끈다
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.onUnitSphere * spin;
        }

        private void Update()
        {
            if (launched && Time.time >= explodeTime) Explode();
        }

        /// <summary>
        /// 처음 부딪히면 거의 멈춘다 (조준한 곳에서 굴러가지 않게). 벽에 맞으면 그 앞으로 떨어진다.
        /// 몬스터는 착지 여부와 상관없이 매번: 이동하는 몸(NavMeshAgent)이 밀어내 튕기지 않게 속도를 버리고 몸을 통과해 발밑으로 떨어진다
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (launched == false) return;
            if ((monsterMask.value & (1 << collision.collider.gameObject.layer)) != 0)
            {
                DropThroughMonsters();
                return;
            }
            if (landed) return;
            landed = true;
            body.linearVelocity *= impactVelocityKeep;
            body.angularVelocity *= impactVelocityKeep;
            body.linearDamping = landedLinearDamping;
            body.angularDamping = landedAngularDamping;
        }

        /// <summary>
        /// 충돌 반작용으로 받은 속도를 버리고 아래로 떨어뜨린다. 이후 몬스터와는 충돌하지 않는다.
        /// landed 는 건드리지 않는다: 공중이면 감쇠 없이 떨어지다가 바닥에 닿을 때 보통 착지 처리를 받는다
        /// </summary>
        private void DropThroughMonsters()
        {
            body.excludeLayers = body.excludeLayers.value | monsterMask.value;
            body.linearVelocity = Vector3.down * monsterDropSpeed;
            body.angularVelocity = Vector3.zero;
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
