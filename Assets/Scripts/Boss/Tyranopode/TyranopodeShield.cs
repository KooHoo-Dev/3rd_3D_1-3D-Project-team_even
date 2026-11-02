using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 보호막 겉모습: 보스 몸에서 자라난 생체 막 (셰이더 HybridShooter/TyranopodeMembrane). 수치는 TyranopodeController 가 갖는다.
    ///  - Absorb: 에너지가 하나 들어올 때마다 막이 부풀고 혈관이 밝게 맥박친다
    ///  - Hit: 맞은 자리에서 빛 물결이 퍼지고 체액이 튄다 (연사에도 화면이 덮이지 않게 간격)
    ///  - BreakLayer: 한 겹이 깨질 때마다 막이 찢겨 구멍이 커지고 혈관이 붉어진다 → 남은 양이 눈에 보인다
    ///  - Shatter: 막이 순식간에 찢겨 흩어진다 (디졸브 + 살점 파편)
    /// </summary>
    public sealed class TyranopodeShield : MonoBehaviour
    {
        private const int HitSlots = 4;
        private static readonly int DamageId = Shader.PropertyToID("_Damage");
        private static readonly int TearId = Shader.PropertyToID("_Tear");
        private static readonly int BoostId = Shader.PropertyToID("_Boost");
        private static readonly int[] HitIds =
        {
            Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3"),
        };

        [SerializeField] private Renderer bubble;
        [SerializeField] private GameObject hitVfx;
        [SerializeField] private GameObject crackVfx;
        [SerializeField] private GameObject shatterVfx;
        [SerializeField, Min(0.01f)] private float growSpeed = 1.5f;
        [Tooltip("맞은 자리 체액 최소 간격(초)")]
        [SerializeField, Min(0f)] private float hitVfxInterval = 0.1f;
        [Tooltip("마지막 겹 직전까지 찢기는 정도 (0~1)")]
        [SerializeField, Range(0f, 0.8f)] private float maxTearBeforeShatter = 0.45f;
        [Tooltip("겹이 깨질 때 줄어드는 크기 비율")]
        [SerializeField, Range(0f, 0.3f)] private float layerShrink = 0.06f;
        [SerializeField, Min(0.05f)] private float shatterTime = 0.45f;

        private MaterialPropertyBlock block;
        private readonly Vector4[] hits = new Vector4[HitSlots];
        private int nextHitSlot;
        private Vector3 baseScale;
        private float targetSize;
        private float size;
        private float damage;
        private float damageTarget;
        private float tear;
        private float tearTarget;
        private float boost;
        private float nextHitVfxTime;
        private float shatterStart = -1f;

        /// <summary>연출이 모두 연결됐는지 (TyranopodeController 가 생성 순간 검사)</summary>
        public bool HasReferences => bubble != null && hitVfx != null && crackVfx != null && shatterVfx != null;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
            for (int i = 0; i < HitSlots; i++) hits[i] = new Vector4(0f, 0f, 0f, -100f);
            Hide();
        }

        /// <summary>에너지가 들어왔다. fill = 최대 보호막 대비 지금 양 (0~1)</summary>
        public void Absorb(float fill)
        {
            bubble.enabled = true;
            shatterStart = -1f;
            targetSize = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(fill));
            damageTarget = 0f;
            tearTarget = 0f;
            boost = Mathf.Max(boost, 2f);
        }

        /// <summary>남은 양 비율 (0~1). 낮을수록 혈관이 붉어진다</summary>
        public void SetRatio(float value) => damageTarget = 1f - Mathf.Clamp01(value);

        public void Hit(Vector3 point)
        {
            Vector3 surface = ClosestSurfacePoint(point);
            hits[nextHitSlot] = new Vector4(surface.x, surface.y, surface.z, Time.timeSinceLevelLoad);
            nextHitSlot = (nextHitSlot + 1) % HitSlots;
            boost = Mathf.Max(boost, 0.6f);

            if (Time.time < nextHitVfxTime) return;
            nextHitVfxTime = Time.time + hitVfxInterval;
            Spawn(hitVfx, surface, 2f);
        }

        /// <summary>한 겹이 깨졌다 (layersLeft = 남은 겹, total = 전체 겹)</summary>
        public void BreakLayer(int layersLeft, int total)
        {
            float broken = 1f - (float)layersLeft / Mathf.Max(1, total);
            tearTarget = maxTearBeforeShatter * broken;
            targetSize = Mathf.Max(0.4f, targetSize - layerShrink);
            boost = 4f;
            for (int i = 0; i < 4; i++)
                Spawn(crackVfx, transform.position + Random.onUnitSphere * WorldRadius, 3f);
        }

        public void Shatter()
        {
            Spawn(shatterVfx, transform.position, 5f);
            shatterStart = Time.time;
            boost = 5f;
        }

        /// <summary>즉시 숨긴다 (보스 사망·최후 연출)</summary>
        public void Hide()
        {
            bubble.enabled = false;
            shatterStart = -1f;
            targetSize = size = 0f;
            tear = tearTarget = 0f;
            damage = damageTarget = 0f;
            transform.localScale = Vector3.zero;
        }

        private float WorldRadius => 0.5f * transform.lossyScale.x;

        private Vector3 ClosestSurfacePoint(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            return dir.sqrMagnitude > 0.001f ? transform.position + dir.normalized * WorldRadius : point;
        }

        private static void Spawn(GameObject prefab, Vector3 position, float life)
            => Destroy(Instantiate(prefab, position, Quaternion.identity), life);

        private void Update()
        {
            if (bubble.enabled == false) return;

            float dt = Time.deltaTime;
            if (shatterStart >= 0f)
            {
                // 순식간에 찢겨 사라진다
                float t = (Time.time - shatterStart) / shatterTime;
                tear = Mathf.Lerp(tearTarget, 1f, t);
                if (t >= 1f)
                {
                    Hide();
                    return;
                }
            }
            else
            {
                tear = Mathf.MoveTowards(tear, tearTarget, dt * 0.8f);
            }

            size = Mathf.MoveTowards(size, targetSize, growSpeed * dt);
            damage = Mathf.MoveTowards(damage, damageTarget, dt * 2f);
            boost = Mathf.MoveTowards(boost, 0f, dt * 4f);
            transform.localScale = baseScale * (size * (1f + 0.03f * boost));

            bubble.GetPropertyBlock(block);
            block.SetFloat(DamageId, damage);
            block.SetFloat(TearId, tear);
            block.SetFloat(BoostId, boost);
            for (int i = 0; i < HitSlots; i++) block.SetVector(HitIds[i], hits[i]);
            bubble.SetPropertyBlock(block);
        }
    }
}
