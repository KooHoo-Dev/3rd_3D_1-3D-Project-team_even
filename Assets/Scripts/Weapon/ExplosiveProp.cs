using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 맵에 놓인 폭발물 (SciFiMap 의 굴착 연료 셀). 쏴서 내구도를 다 깎으면 잠깐 번쩍인 뒤 크게 터진다.
    ///  - 중립 진영: 플레이어 총·폭발, 다른 연료 셀 폭발(연쇄) 모두에 맞고, 폭발은 플레이어·몬스터·보스·다른 셀을 모두 맞힌다
    ///  - 플레이어는 playerDamageRatio, 보스(IStaggerable)는 bossDamageRatio 만큼만 피해 → 보스에게는 피해 대신 경직(TryStagger)이 핵심
    ///  - 피드백은 화면 표시 없이 셀 자체로: 맞으면 금속 울림, 손상되면 경고등이 빨라지고 가스가 쉭 새며 연기가 난다, 기폭 직전 압력 비명과 함께 번쩍인다
    ///    (소리는 SoundTable 의 sfx_fuelcell_* , 클립은 tools/audio/FuelCellSfx 로 합성)
    ///  - 폭발은 맞은 그 자리에서 바로 하지 않고 fuse 뒤에 한다 (연쇄가 시차를 두고 이어지고, 다른 폭발 처리 도중에 다시 폭발하지 않는다)
    ///  - 보스 몸이 닿으면: 연출 중(등장 착지 등)이면 피해 없이 찌그러지고, 아니면 contactFuse 뒤 터진다
    ///  - 부서져도 지우지 않고 숨긴다 → RoundCleanupEvent 때 되살린다
    /// </summary>
    public sealed class ExplosiveProp : MonoBehaviour, IDamageable
    {
        private enum Phase { Intact, Critical, Fusing, Broken }

        private static readonly Collider[] contactHits = new Collider[16];
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("내구도")]
        [Tooltip("권총(WeaponTable 2003, 15) 두 방에 터지는 값. 권총 피해를 바꾸면 함께 맞춘다")]
        [SerializeField, Min(1)] private int maxHp = 30;
        [Tooltip("남은 체력 비율이 이 아래면 위험 단계 (연기가 새고 경고등이 빨라진다)")]
        [SerializeField, Range(0f, 1f)] private float criticalHpRatio = 0.5f;

        [Header("기폭")]
        [Tooltip("부서진 뒤 터지기까지(초). 연쇄 폭발이 시차를 두도록 min~max 에서 고른다")]
        [SerializeField, Min(0f)] private float fuseMin = 0.15f;
        [SerializeField, Min(0f)] private float fuseMax = 0.3f;
        [Tooltip("보스 몸이 닿았을 때 터지기까지(초)")]
        [SerializeField, Min(0f)] private float contactFuse = 0.5f;

        [Header("폭발")]
        [Tooltip("damage = 중심 피해. effectPrefab 은 ExplosionVisual 이 있으면 반경에 맞춰 키운다")]
        [SerializeField] private ExplosionSettings explosion = new ExplosionSettings
        {
            damage = 160, radius = 9f, minDamageRatio = 0.3f, force = 20f, sfxKey = "sfx_explosion_large",
        };
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("폭발음에 겹쳐 내는 연료 불길 소리 (SoundTable 키, 비우면 없음)")]
        [SerializeField] private string burnSfxKey = "sfx_fuelcell_burn";
        [Tooltip("폭발 이펙트를 지우기까지(초). 잔불·연기가 남는 이펙트라 GunExplosion(5초)보다 길다")]
        [SerializeField, Min(0.5f)] private float effectLifetime = 8f;
        [Tooltip("플레이어가 받는 피해 비율")]
        [SerializeField, Range(0f, 1f)] private float playerDamageRatio = 0.35f;
        [Tooltip("보스(IStaggerable)가 받는 피해 비율. 피해보다 경직이 목적")]
        [SerializeField, Range(0f, 1f)] private float bossDamageRatio = 0.15f;
        [SerializeField] private CameraShakeSettings blastShake = CameraShakePresets.Blast;
        [Tooltip("흔들림이 닿는 거리 = 폭발 반경 × 이 값")]
        [SerializeField, Min(0f)] private float blastShakeRadiusMultiplier = 3f;

        [Header("보스 접촉")]
        [SerializeField] private LayerMask bossContactMask = ~0;
        [Tooltip("셀 중심에서 이 거리 안에 보스 콜라이더가 있으면 닿은 것")]
        [SerializeField, Min(0f)] private float bossContactRadius = 1.2f;
        [SerializeField, Min(0.05f)] private float bossContactInterval = 0.25f;
        [Tooltip("연출 중인 보스에 깔렸을 때 이펙트 (피해 없음). 비우면 없음")]
        [SerializeField] private GameObject crushEffectPrefab;
        [SerializeField] private string crushSfxKey = "sfx_fuelcell_crush";

        [Header("겉모양")]
        [Tooltip("부서지면 숨길 외형")]
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Light warningLight;
        [Tooltip("발광 띠. 머티리얼에 Emission 이 켜져 있어야 한다")]
        [SerializeField] private Renderer[] glowRenderers;
        [SerializeField, ColorUsage(false, true)] private Color glowColor = new Color(4f, 0.25f, 0.15f);
        [SerializeField] private Color criticalLightColor = new Color(1f, 0.45f, 0.1f);
        [Tooltip("깜빡임 빠르기(Hz): 평소 / 위험 / 기폭 직전")]
        [SerializeField] private float idlePulseSpeed = 0.8f;
        [SerializeField] private float criticalPulseSpeed = 4f;
        [SerializeField] private float fusePulseSpeed = 18f;
        [Tooltip("위험 단계에서 켜지는 누출 연기 (평소에는 꺼 둔다)")]
        [SerializeField] private GameObject leakEffect;
        [Tooltip("소리 (SoundTable 키, 비우면 없음): 맞을 때 / 위험 단계에 들어설 때 / 위험 단계 동안 반복 / 기폭 시작 때")]
        [SerializeField] private string hitSfxKey = "sfx_fuelcell_hit";
        [SerializeField] private string criticalSfxKey = "sfx_fuelcell_critical";
        [SerializeField] private string leakLoopKey = "sfx_fuelcell_leak_loop";
        [SerializeField] private string fuseSfxKey = "sfx_fuelcell_fuse";

        private Collider[] colliders;
        private NavMeshObstacle obstacle;
        private MaterialPropertyBlock block;
        private Vector3 centerOffset;
        private Color baseLightColor;
        private float baseLightIntensity;
        private float pulseSeed;
        private float nextContactCheck;
        private int hp;
        private Phase phase;
        private SoundHandle leakLoop;

        public Faction Faction => Faction.Neutral;
        public bool IsAlive => phase == Phase.Intact || phase == Phase.Critical;
        /// <summary>폭발 중심 (콜라이더 가운데)</summary>
        public Vector3 Center => transform.TransformPoint(centerOffset);

        private void Awake()
        {
            colliders = GetComponentsInChildren<Collider>(true);
            obstacle = GetComponentInChildren<NavMeshObstacle>(true);
            block = new MaterialPropertyBlock();
            pulseSeed = Random.Range(0f, 10f);
            if (warningLight != null)
            {
                baseLightColor = warningLight.color;
                baseLightIntensity = warningLight.intensity;
            }

            centerOffset = Vector3.zero;
            if (colliders.Length > 0)
            {
                Bounds bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
                centerOffset = transform.InverseTransformPoint(bounds.center);
            }
            Restore();
        }

        private void OnEnable() => EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);

        private void OnDisable()
        {
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            StopLeakLoop();
        }

        private void OnRoundCleanup(RoundCleanupEvent e) => Restore();

        /// <summary>처음 상태로 (라운드 정리·테스트 도우미)</summary>
        public void Restore()
        {
            StopAllCoroutines();
            StopLeakLoop();
            hp = maxHp;
            phase = Phase.Intact;
            SetPresent(true);
            if (leakEffect != null) leakEffect.SetActive(false);
            if (warningLight != null) warningLight.color = baseLightColor;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (IsAlive == false || info.Amount <= 0) return;
            hp -= info.Amount;
            PlaySfx(hitSfxKey, info.HitPoint);
            if (hp <= 0)
            {
                Arm(Random.Range(fuseMin, Mathf.Max(fuseMin, fuseMax)));
                return;
            }
            if (phase == Phase.Intact && hp <= maxHp * criticalHpRatio) EnterCritical();
        }

        private void EnterCritical()
        {
            phase = Phase.Critical;
            if (leakEffect != null) leakEffect.SetActive(true);
            if (warningLight != null) warningLight.color = criticalLightColor;
            PlaySfx(criticalSfxKey, Center);
            if (string.IsNullOrEmpty(leakLoopKey) == false && leakLoop.IsValid == false && ServiceLocator.TryGet<ISoundService>(out var sound))
                leakLoop = sound.PlayLoop(leakLoopKey, transform);
        }

        private void StopLeakLoop()
        {
            if (leakLoop.IsValid && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.StopLoop(leakLoop);
            leakLoop = default;
        }

        /// <summary>터질 준비: fuse 동안 번쩍이다 폭발</summary>
        private void Arm(float fuse)
        {
            phase = Phase.Fusing;
            if (warningLight != null) warningLight.color = criticalLightColor;
            PlaySfx(fuseSfxKey, Center);
            StartCoroutine(FuseRoutine(fuse));
        }

        private IEnumerator FuseRoutine(float fuse)
        {
            if (fuse > 0f) yield return new WaitForSeconds(fuse);
            Detonate();
        }

        private void Update()
        {
            if (phase == Phase.Broken) return;
            UpdateGlow();
            if (IsAlive && Time.time >= nextContactCheck)
            {
                nextContactCheck = Time.time + bossContactInterval;
                CheckBossContact();
            }
        }

        /// <summary>경고등·발광 띠 깜빡임. 손상될수록 빨라진다</summary>
        private void UpdateGlow()
        {
            float speed = phase == Phase.Fusing ? fusePulseSpeed : phase == Phase.Critical ? criticalPulseSpeed : idlePulseSpeed;
            float wave = 0.5f + 0.5f * Mathf.Sin((Time.time + pulseSeed) * speed * Mathf.PI * 2f);
            float k = phase == Phase.Fusing ? (wave > 0.5f ? 1.6f : 0.2f) : Mathf.Lerp(0.35f, 1f, wave);
            // 위험 단계는 불이 붙은 듯 불규칙하게 일렁인다
            if (phase == Phase.Critical) k *= Mathf.Lerp(0.7f, 1.2f, Mathf.PerlinNoise(pulseSeed, Time.time * 8f));

            if (warningLight != null) warningLight.intensity = baseLightIntensity * k;
            if (glowRenderers == null) return;
            block.SetColor(EmissionColorId, glowColor * k);
            foreach (var r in glowRenderers)
                if (r != null) r.SetPropertyBlock(block);
        }

        /// <summary>보스 몸이 닿았나: 연출 중이면 찌그러지고, 아니면 잠깐 뒤 터진다</summary>
        private void CheckBossContact()
        {
            int count = Physics.OverlapSphereNonAlloc(Center, bossContactRadius, contactHits, bossContactMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = contactHits[i];
                if (col.transform.IsChildOf(transform)) continue;
                var boss = col.GetComponentInParent<IStaggerable>();
                if (boss == null) continue;
                if (boss.IsInCinematic) Crush();
                else Arm(contactFuse);
                return;
            }
        }

        /// <summary>연출 중인 보스에 깔림: 피해 없이 사라진다</summary>
        private void Crush()
        {
            phase = Phase.Broken;
            Vector3 center = Center;
            SetPresent(false);
            if (crushEffectPrefab != null) Destroy(Instantiate(crushEffectPrefab, center, Quaternion.identity), 5f);
            PlaySfx(crushSfxKey, center);
        }

        private void Detonate()
        {
            phase = Phase.Broken;
            Vector3 center = Center;
            SetPresent(false); // 자기 콜라이더를 먼저 꺼서 폭발에 자기가 맞지 않게
            Explode(center);
        }

        /// <summary>
        /// GunExplosion 과 같은 거리 감쇠 범위 피해에 대상별 비율(플레이어·보스)과 보스 경직을 더한 것.
        /// GunExplosion 의 공유 버퍼는 쓰지 않는다 (맞은 대상이 그 자리에서 또 폭발을 일으켜도 안전하게)
        /// </summary>
        private void Explode(Vector3 center)
        {
            ExplosionVisual visual = null;
            if (explosion.effectPrefab != null)
            {
                var effect = Instantiate(explosion.effectPrefab, center, Quaternion.identity);
                if (effect.TryGetComponent(out visual)) visual.Fit(explosion.radius);
                Destroy(effect, effectLifetime);
            }
            PlaySfx(explosion.sfxKey, center);
            PlaySfx(burnSfxKey, center);
            EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(
                blastShake.At(center, explosion.radius * blastShakeRadiusMultiplier)));

            var damaged = new HashSet<IDamageable>();
            var pushed = new HashSet<Rigidbody>();
            var staggered = new List<IStaggerable>();
            foreach (Collider col in Physics.OverlapSphere(center, explosion.radius, hitMask, QueryTriggerInteraction.Ignore))
            {
                Rigidbody body = col.attachedRigidbody;
                if (body != null && body.isKinematic == false && pushed.Add(body))
                    body.AddExplosionForce(explosion.force, center, explosion.radius, 0.5f, ForceMode.Impulse);

                if (col.TryGetDamageable(out var target) == false || target.CanBeHitBy(Faction) == false) continue;
                if (damaged.Add(target) == false) continue;

                var boss = col.GetComponentInParent<IStaggerable>();
                float ratio = boss != null ? bossDamageRatio : target.Faction == Faction.Player ? playerDamageRatio : 1f;
                if (ratio <= 0f) continue;

                // 오목 MeshCollider 는 ClosestPoint 를 지원하지 않아 경계상자 기준으로 거리 계산
                Vector3 closest = col.bounds.ClosestPoint(center);
                float t = Mathf.Clamp01(Vector3.Distance(center, closest) / Mathf.Max(0.01f, explosion.radius));
                int amount = Mathf.Max(1, Mathf.RoundToInt(explosion.damage * Mathf.Lerp(1f, explosion.minDamageRatio, t) * ratio));
                Vector3 direction = closest - center;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
                target.TakeDamage(new DamageInfo(amount, DamageKind.Explosion, Faction, gameObject, closest, direction));
                if (visual != null) visual.PlayHit(closest, direction);
                if (boss != null && staggered.Contains(boss) == false) staggered.Add(boss);
            }

            // 피해를 다 준 뒤에 경직 (피해로 이미 움찔·전환이 시작됐으면 보스가 알아서 거절한다)
            foreach (var boss in staggered) boss.TryStagger(center);
        }

        /// <summary>외형·콜라이더·NavMesh 장애물·불빛을 함께 켜고 끈다</summary>
        private void SetPresent(bool present)
        {
            if (visualRoot != null) visualRoot.SetActive(present);
            foreach (var col in colliders) col.enabled = present;
            if (obstacle != null) obstacle.enabled = present;
            if (warningLight != null) warningLight.enabled = present;
            if (present) return;
            if (leakEffect != null) leakEffect.SetActive(false);
            StopLeakLoop();
        }

        private static void PlaySfx(string key, Vector3 position)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var sound))
                sound.PlaySfx(key, position);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
            Gizmos.DrawWireSphere(Application.isPlaying ? Center : transform.position, explosion.radius);
        }
    }
}
