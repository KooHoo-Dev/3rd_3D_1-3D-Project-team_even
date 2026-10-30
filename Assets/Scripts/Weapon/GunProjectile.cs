using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>투사체·폭발이 들고 다니는 "누가 쐈나" 정보</summary>
    public readonly struct ProjectileContext
    {
        public readonly Faction Faction;
        public readonly GameObject Attacker;
        /// <summary>이 아래 콜라이더는 맞지 않는다 (쏜 사람 몸)</summary>
        public readonly Transform IgnoreRoot;
        public readonly int SourceDefId;
        public readonly LayerMask HitMask;

        public ProjectileContext(Faction faction, GameObject attacker, Transform ignoreRoot, int sourceDefId, LayerMask hitMask)
        {
            Faction = faction;
            Attacker = attacker;
            IgnoreRoot = ignoreRoot;
            SourceDefId = sourceDefId;
            HitMask = hitMask;
        }

        public bool IsOwn(Collider col) => IgnoreRoot != null && col.transform.IsChildOf(IgnoreRoot);
    }

    [System.Serializable]
    public struct ExplosionSettings
    {
        [Tooltip("중심 피해")]
        public int damage;
        public float radius;
        [Tooltip("반경 끝에서의 피해 비율")]
        [Range(0f, 1f)] public float minDamageRatio;
        [Tooltip("범위 안 리지드바디에 주는 힘")]
        public float force;
        public GameObject effectPrefab;
        public string sfxKey;
    }

    /// <summary>범위 피해. 같은 대상의 콜라이더가 여러 개여도 한 번만 맞는다. 가까울수록 강하다</summary>
    public static class GunExplosion
    {
        private static readonly Collider[] overlaps = new Collider[128];
        private static readonly HashSet<IDamageable> damaged = new HashSet<IDamageable>();
        private static readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

        public static void Explode(Vector3 center, in ExplosionSettings settings, in ProjectileContext context)
        {
            if (settings.effectPrefab != null)
                Object.Destroy(Object.Instantiate(settings.effectPrefab, center, Quaternion.identity), 5f);
            if (string.IsNullOrEmpty(settings.sfxKey) == false && ServiceLocator.TryGet<ISoundService>(out var sound))
                sound.PlaySfx(settings.sfxKey, center);
            Debug.DrawRay(center, Vector3.up * settings.radius, new Color(1f, 0.5f, 0f), 1f);

            int count = Physics.OverlapSphereNonAlloc(center, settings.radius, overlaps, context.HitMask, QueryTriggerInteraction.Ignore);
            damaged.Clear();
            pushed.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider col = overlaps[i];
                if (context.IsOwn(col)) continue;

                Rigidbody body = col.attachedRigidbody;
                if (body != null && body.isKinematic == false && pushed.Add(body))
                    body.AddExplosionForce(settings.force, center, settings.radius, 0.5f, ForceMode.Impulse);

                if (col.TryGetDamageable(out var target) == false || target.CanBeHitBy(context.Faction) == false) continue;
                if (damaged.Add(target) == false) continue;

                // 오목 MeshCollider 는 ClosestPoint 를 지원하지 않아 경계상자 기준으로 거리 계산
                Vector3 closest = col.bounds.ClosestPoint(center);
                float t = Mathf.Clamp01(Vector3.Distance(center, closest) / Mathf.Max(0.01f, settings.radius));
                int amount = Mathf.Max(1, Mathf.RoundToInt(settings.damage * Mathf.Lerp(1f, settings.minDamageRatio, t)));
                Vector3 direction = closest - center;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
                target.TakeDamage(new DamageInfo(amount, DamageKind.Explosion, context.Faction, context.Attacker, closest, direction, context.SourceDefId));
            }
        }
    }

    /// <summary>
    /// 로켓·유탄 같은 날아가는 탄. 물리 엔진 대신 매 프레임 SphereCast 로 직접 움직여 빠른 탄도 벽을 뚫지 않는다.
    /// 외형 프리팹은 그대로 쓰고(콜라이더는 끔) 이 컴포넌트를 런타임에 붙인다
    /// </summary>
    public sealed class GunProjectile : MonoBehaviour
    {
        public struct Settings
        {
            /// <summary>중력 가속도(m/s²). 0 = 직선 (로켓)</summary>
            public float Gravity;
            /// <summary>충돌 판정 반경</summary>
            public float Radius;
            /// <summary>벽·바닥에 닿으면 바로 폭발 (로켓). false 면 튕긴다 (유탄)</summary>
            public bool ExplodeOnSurface;
            /// <summary>튕길 때 속도 유지 비율</summary>
            public float Bounciness;
            /// <summary>이 시간이 지나면 폭발. 0 = 시간 폭발 없음</summary>
            public float FuseTime;
            /// <summary>이 거리만큼 날아가기 전에 터지면 피해가 줄어든다 (근접 남용 방지). 0 = 감소 없음</summary>
            public float ArmDistance;
            /// <summary>날아간 거리 0 에서 터졌을 때의 피해 비율. ArmDistance 까지 1 로 올라간다</summary>
            public float UnarmedDamageRatio;
            /// <summary>적에게 직격했을 때 폭발 피해에 더하는 피해</summary>
            public int DirectHitBonus;
            public float MaxLifetime;
            public ExplosionSettings Explosion;
            /// <summary>탄 뒤에 붙는 꼬리 이펙트 (선택). 탄이 사라지면 떼어내 남은 꼬리가 자연스럽게 사라진다</summary>
            public ProjectileTrail Trail;
        }

        private static readonly RaycastHit[] hits = new RaycastHit[16];

        private Settings settings;
        private ProjectileContext context;
        private Vector3 velocity;
        private float age;
        private float traveled;
        private bool resting;
        private ProjectileTrail trail;

        private void OnEnable() => EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);

        private void OnDisable() => EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);

        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            enabled = false;
            Despawn();
        }

        /// <summary>visualPrefab 이 없으면 작은 구를 만든다</summary>
        public static GunProjectile Spawn(GameObject visualPrefab, Vector3 position, Vector3 velocity, in Settings settings, in ProjectileContext context)
        {
            Quaternion rotation = velocity.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
            GameObject go;
            if (visualPrefab != null)
            {
                go = Instantiate(visualPrefab, position, rotation);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.SetPositionAndRotation(position, rotation);
                go.transform.localScale = Vector3.one * Mathf.Max(0.05f, settings.Radius * 2f);
            }

            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var body in go.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;

            var projectile = go.AddComponent<GunProjectile>();
            projectile.settings = settings;
            projectile.context = context;
            projectile.velocity = velocity;
            if (settings.Trail != null)
            {
                // 월드 크기를 유지한 채 붙인다 (외형 프리팹의 스케일을 따라가지 않게)
                projectile.trail = Instantiate(settings.Trail, position, rotation);
                projectile.trail.transform.SetParent(go.transform, true);
            }
            return projectile;
        }

        /// <summary>꼬리를 떼어내고 탄을 지운다. 탄이 사라지는 모든 경로는 여기를 거친다</summary>
        private void Despawn()
        {
            if (trail != null) trail.Detach();
            Destroy(gameObject);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (settings.FuseTime > 0f && age >= settings.FuseTime) { Explode(transform.position); return; }
            if (age >= settings.MaxLifetime) { Despawn(); return; }
            if (resting) return;

            velocity += Vector3.down * (settings.Gravity * dt);
            Vector3 step = velocity * dt;
            float distance = step.magnitude;
            if (distance < 0.0001f) return;

            if (CastClosest(transform.position, step / distance, distance, out var hit))
            {
                traveled += hit.distance;
                HandleHit(hit);
                return;
            }

            transform.position += step;
            traveled += distance;
            transform.rotation = Quaternion.LookRotation(velocity);
        }

        private void HandleHit(in RaycastHit hit)
        {
            bool enemy = hit.collider.TryGetDamageable(out var target) && target.CanBeHitBy(context.Faction);

            if (enemy)
            {
                int bonus = Mathf.RoundToInt(settings.DirectHitBonus * DamageScale());
                if (bonus > 0)
                    target.TakeDamage(new DamageInfo(bonus, DamageKind.Explosion, context.Faction, context.Attacker, hit.point, velocity.normalized, context.SourceDefId));
                Explode(hit.point);
                return;
            }

            if (settings.ExplodeOnSurface)
            {
                Explode(hit.point + hit.normal * 0.05f);
                return;
            }

            // 튕김: 벽에서 살짝 떨어뜨리고 반사. 바닥에서 거의 멈추면 굴러다니지 않게 정지
            transform.position = hit.point + hit.normal * (settings.Radius + 0.01f);
            velocity = Vector3.Reflect(velocity, hit.normal) * settings.Bounciness;
            if (hit.normal.y > 0.7f && velocity.magnitude < 1f)
            {
                velocity = Vector3.zero;
                resting = true;
            }
        }

        private bool CastClosest(Vector3 origin, Vector3 direction, float distance, out RaycastHit closest)
        {
            int count = Physics.SphereCastNonAlloc(origin, settings.Radius, direction, hits, distance, context.HitMask, QueryTriggerInteraction.Ignore);
            closest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (context.IsOwn(hits[i].collider) || hits[i].distance >= best) continue;
                // 시작부터 겹친 경우(distance 0) 는 point 가 비어 있고 normal 이 진행 반대 방향이라 직접 채운다
                if (hits[i].distance <= 0f)
                {
                    hits[i].point = origin;
                    hits[i].normal = OverlapNormal(hits[i].collider, origin);
                }
                best = hits[i].distance;
                closest = hits[i];
            }
            return best < float.MaxValue;
        }

        /// <summary>겹친 콜라이더에서 밀려나는 방향. 구할 수 없으면 위쪽 (바닥에 묻힌 경우가 대부분)</summary>
        private static Vector3 OverlapNormal(Collider col, Vector3 origin)
        {
            // 오목 MeshCollider 는 ClosestPoint 를 지원하지 않는다
            if (col is MeshCollider mesh && mesh.convex == false) return Vector3.up;
            Vector3 away = origin - col.ClosestPoint(origin);
            return away.sqrMagnitude > 0.000001f ? away.normalized : Vector3.up;
        }

        /// <summary>무장 거리 전에 터지면 UnarmedDamageRatio → 1 로 피해가 오른다</summary>
        private float DamageScale()
        {
            if (settings.ArmDistance <= 0f) return 1f;
            return Mathf.Lerp(settings.UnarmedDamageRatio, 1f, Mathf.Clamp01(traveled / settings.ArmDistance));
        }

        private void Explode(Vector3 position)
        {
            ExplosionSettings explosion = settings.Explosion;
            explosion.damage = Mathf.Max(1, Mathf.RoundToInt(explosion.damage * DamageScale()));
            GunExplosion.Explode(position, explosion, context);
            Despawn();
        }
    }
}
