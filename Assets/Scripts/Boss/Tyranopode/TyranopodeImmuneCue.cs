using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 포효 소환 중 무적(외피 경화)을 보여 주는 차가운 막. 보호막과 같은 생체막 셰이더를 회색·푸른빛으로 칠해 구분한다.
    ///  - Show / Hide: 천천히 나타나고 사라진다
    ///  - Hit: 맞은 자리에서 차가운 물결 (총알이 막혔다는 신호)
    /// </summary>
    public sealed class TyranopodeImmuneCue : MonoBehaviour
    {
        private const int HitSlots = 4;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int VeinColorId = Shader.PropertyToID("_VeinColor");
        private static readonly int DamagedVeinColorId = Shader.PropertyToID("_DamagedVeinColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int HitColorId = Shader.PropertyToID("_HitColor");
        private static readonly int BoostId = Shader.PropertyToID("_Boost");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int[] HitIds =
        {
            Shader.PropertyToID("_Hit0"), Shader.PropertyToID("_Hit1"), Shader.PropertyToID("_Hit2"), Shader.PropertyToID("_Hit3"),
        };

        [SerializeField] private Renderer shell;
        [SerializeField] private Color membrane = new Color(0.25f, 0.35f, 0.45f, 0.12f);
        [SerializeField, ColorUsage(false, true)] private Color vein = new Color(0.5f, 1.1f, 1.6f);
        [SerializeField, ColorUsage(false, true)] private Color rim = new Color(0.7f, 1.3f, 2f);
        [SerializeField, ColorUsage(false, true)] private Color hit = new Color(1.2f, 2.2f, 3f);
        [SerializeField, Min(0.05f)] private float fadeTime = 0.4f;

        private MaterialPropertyBlock block;
        private readonly Vector4[] hits = new Vector4[HitSlots];
        private int nextHitSlot;
        private float visible;
        private float target;
        private float boost;

        public bool HasReferences => shell != null;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            for (int i = 0; i < HitSlots; i++) hits[i] = new Vector4(0f, 0f, 0f, -100f);
            shell.enabled = false;
        }

        public void Show()
        {
            target = 1f;
            boost = 2f;
            shell.enabled = true;
        }

        public void Hide() => target = 0f;

        public void Hit(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            Vector3 surface = dir.sqrMagnitude > 0.001f ? transform.position + dir.normalized * 0.5f * transform.lossyScale.x : point;
            hits[nextHitSlot] = new Vector4(surface.x, surface.y, surface.z, Time.timeSinceLevelLoad);
            nextHitSlot = (nextHitSlot + 1) % HitSlots;
            boost = Mathf.Max(boost, 0.5f);
        }

        private void Update()
        {
            if (shell.enabled == false) return;
            visible = Mathf.MoveTowards(visible, target, Time.deltaTime / fadeTime);
            boost = Mathf.MoveTowards(boost, 0f, Time.deltaTime * 3f);
            if (visible <= 0f && target <= 0f)
            {
                shell.enabled = false;
                return;
            }

            Color baseColor = membrane;
            baseColor.a *= visible;
            shell.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(VeinColorId, vein * visible);
            block.SetColor(DamagedVeinColorId, vein * visible);
            block.SetColor(RimColorId, rim * visible);
            block.SetColor(HitColorId, hit);
            block.SetFloat(BoostId, boost);
            block.SetFloat(OpacityId, visible);
            for (int i = 0; i < HitSlots; i++) block.SetVector(HitIds[i], hits[i]);
            shell.SetPropertyBlock(block);
        }
    }
}
