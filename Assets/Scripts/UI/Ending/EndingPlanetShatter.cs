using UnityEngine;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 크레딧 중 하이브 근원 행성의 붕괴 (EndingTimeline.CrackStart → Shatter).
    ///  - 균열: 행성 재질(HivePlanet 셰이더)의 _CrackAmount 를 0 → 1. 금빛 금이 퍼지고 붉은 핏줄 빛은 꺼져 간다 (정화 — 위협이 아니라 해방)
    ///  - 폭발: 섬광이 가장 밝을 때 행성을 끄고, 빛나는 잔해 입자(불씨)와 먼지 구름이 퍼진다. 가운데 핵 잔광은 몇 초 남는다.
    ///    + 기울어진 충격파 고리 · 순간 광원
    /// 얇은 껍질 파편 대신 입자를 쓴다 (먼 거리에서 두께가 드러나지 않고 폭발로 읽힌다).
    /// 화면 상태는 시각 t 하나로 계산한다(Evaluate) → 건너뛰기·Timeline 이동에도 같은 화면.
    /// 입자는 시드를 고정하고, 앞으로 흐를 때는 이어서, 시간이 뒤로 가거나 크게 뛸 때만 처음부터 다시 시뮬레이션한다.
    /// 공유 재질은 건드리지 않고 MaterialPropertyBlock 으로 바꾼다
    /// </summary>
    public sealed class EndingPlanetShatter : MonoBehaviour
    {
        private static readonly int CrackAmountId = Shader.PropertyToID("_CrackAmount");
        private static readonly int VeinFadeId = Shader.PropertyToID("_VeinFade");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        /// <summary>이보다 크게 뛰면(건너뛰기·Timeline 이동) 입자를 처음부터 다시 계산</summary>
        private const float MaxContinueStep = 0.5f;

        [SerializeField] private Renderer planet;
        [Tooltip("균열이 퍼지는 빠르기 (1 = 일정, 클수록 끝에 몰려 퍼진다)")]
        [SerializeField] private float crackCurve = 1.6f;
        [Tooltip("폭발 뒤 행성을 끄기까지(초) — 섬광이 가장 밝을 때 사라진다")]
        [SerializeField] private float vanishDelay = 0.1f;

        [Header("잔해 (입자, 폭발 순간부터 시뮬레이션)")]
        [Tooltip("잔해 입자들의 부모 (폭발 때 켠다)")]
        [SerializeField] private GameObject debrisRoot;
        [SerializeField] private ParticleSystem[] debris;

        [Header("섬광·고리·광원")]
        [SerializeField] private Renderer core;
        [ColorUsage(false, true)] [SerializeField] private Color coreColor = new Color(6f, 4.5f, 3f);
        [Tooltip("섬광 최대 크기 (m)")]
        [SerializeField] private float coreSize = 2600f;
        [Tooltip("섬광이 사그라든 뒤 남는 핵 잔광 비율 · 사라지는 시간(초)")]
        [SerializeField] private float remnant = 0.25f;
        [SerializeField] private float remnantTime = 6f;
        [SerializeField] private LineRenderer ring;
        [ColorUsage(false, true)] [SerializeField] private Color ringColor = new Color(3f, 2.4f, 1.6f);
        [Tooltip("고리 반지름 (m) = ringSpeed × 경과초^0.7")]
        [SerializeField] private float ringSpeed = 900f;
        [SerializeField] private float ringFade = 6f;
        [SerializeField] private Light blast;
        [SerializeField] private float blastIntensity = 40f;

        private MaterialPropertyBlock block;
        /// <summary>입자를 몇 초까지 시뮬레이션했는지 (음수 = 비어 있음)</summary>
        private float simulated = -1f;

        /// <summary>시각 t 의 화면 (EndingTimeline 기준 초)</summary>
        public void Evaluate(float t)
        {
            block ??= new MaterialPropertyBlock();
            bool burst = t >= EndingTimeline.Shatter;
            float since = Mathf.Max(0f, t - EndingTimeline.Shatter);
            float crack = Mathf.Pow(Mathf.Clamp01((t - EndingTimeline.CrackStart) / (EndingTimeline.Shatter - EndingTimeline.CrackStart)), crackCurve);

            planet.enabled = burst == false || since < vanishDelay;
            if (planet.enabled)
            {
                planet.GetPropertyBlock(block);
                block.SetFloat(CrackAmountId, crack);
                block.SetFloat(VeinFadeId, crack);
                planet.SetPropertyBlock(block);
            }

            debrisRoot.SetActive(burst);
            SimulateDebris(burst, since);

            // 섬광: 0.12초에 커졌다가 사그라들고, 핵 잔광이 조금 더 남는다
            float rise = Mathf.Clamp01(since / 0.12f);
            float flash = burst ? rise * ((1f - remnant) * Mathf.Exp(-since / 1.4f) + remnant * Mathf.Exp(-since / remnantTime)) : 0f;
            core.enabled = flash > 0.001f;
            if (core.enabled)
            {
                var camera = Camera.main;
                if (camera != null) core.transform.rotation = Quaternion.LookRotation(core.transform.position - camera.transform.position);
                core.transform.localScale = Vector3.one * coreSize * (0.35f + 0.65f * Mathf.Sqrt(Mathf.Clamp01(since / 0.6f)));
                core.GetPropertyBlock(block);
                block.SetColor(ColorId, coreColor * flash);
                core.SetPropertyBlock(block);
            }

            // 충격파 고리: 빠르게 퍼지며 옅어진다
            float ringAlpha = burst ? Mathf.Clamp01(since / 0.2f) * Mathf.Clamp01(1f - since / ringFade) : 0f;
            ring.enabled = ringAlpha > 0.001f;
            if (ring.enabled)
            {
                float radius = ringSpeed * Mathf.Pow(since, 0.7f);
                ring.transform.localScale = Vector3.one * radius;
                ring.widthMultiplier = radius * 0.06f;
                var color = ringColor * ringAlpha;
                ring.startColor = color;
                ring.endColor = color;
            }

            float light = burst ? rise * Mathf.Exp(-since / 1.4f) : 0f;
            blast.intensity = blastIntensity * light;
            blast.enabled = light > 0.001f;
        }

        /// <summary>폭발 뒤 since 초의 잔해. 앞으로 조금 흐르면 이어서, 뒤로 가거나 크게 뛰면 처음부터 (시드 고정이라 같은 모양)</summary>
        private void SimulateDebris(bool burst, float since)
        {
            if (burst == false)
            {
                if (simulated >= 0f)
                    foreach (var particles in debris) particles.Clear(true);
                simulated = -1f;
                return;
            }

            bool restart = simulated < 0f || since < simulated || since - simulated > MaxContinueStep;
            float step = restart ? since : since - simulated;
            foreach (var particles in debris) particles.Simulate(step, true, restart, true);
            simulated = since;
        }
    }
}
