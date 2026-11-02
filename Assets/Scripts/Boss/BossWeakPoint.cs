using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Monster.Behaviour;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Boss
{
    /// <summary>
    /// 보스 약점 히트박스 하나 (뼈 아래 자식 콜라이더). 총알이 이 콜라이더를 맞히면
    /// MonsterPartTable 의 BossId 행 DamageMultiplier 를 곱한 피해를 BossController 에 넘긴다.
    /// 무기는 TryGetDamageable(부모 방향 탐색)로 보스 몸통보다 먼저 이 컴포넌트를 찾는다.
    /// 콜라이더와 같은 크기의 반투명 발광 구(가장자리만 은은하게 빛남)를 직접 만들어, 보스가 살아 있는 동안 맥동하며 보여준다.
    /// 약점 피해가 들어가면 발광 구가 흰빛으로 번쩍이며 잠깐 커져, 플레이어가 그 부위를 맞혔다는 것을 바로 알게 한다.
    /// </summary>
    /// <remarks>
    /// 폭발 피해는 받지 않는다. 수류탄은 IDamageable 마다 1번 피해를 주므로, 받으면 몸통과 약점에서 두 번 깎인다.
    /// Exposure(노출 조건)는 쓰지 않는다. 지금 보스 부위는 모두 Always 다. 그래서 발광도 살아 있으면 늘 노출 밝기로 맥동한다.
    /// 발광 구는 프리팹이 아니라 Awake 에서 만든다 (보스 프리팹 수정 불필요). 셰이더·발광 방식은 MonsterWeakPoint 와 같다.
    /// 판정 콜라이더는 지금 프리팹(Clypeosaurus)이 쓰는 SphereCollider 만 지원한다.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public sealed class BossWeakPoint : MonoBehaviour, IDamageable
    {
        // 일반 몬스터 약점과 같은 반투명 테두리 발광 셰이더 (HybridShooter/Monster/WeakPointGlow).
        // Resources 폴더에 있어 빌드에 포함된다. 경로는 Resources 기준, 확장자 없이.
        private const string GlowShaderPath = "Shaders/MonsterWeakPointGlow";

        // 셰이더 프로퍼티 이름 (MonsterWeakPointGlow.shader 의 Properties 와 같아야 한다)
        private const string GlowColorProperty = "_GlowColor";
        private const string CoreAlphaProperty = "_CoreAlpha";
        private const string RimPowerProperty = "_RimPower";
        private const string BrightnessProperty = "_Brightness";

        [Tooltip("MonsterPartTable 의 PartKey. 이 보스 Def(BossId 행)의 부위에서 배율을 찾는다")]
        [SerializeField] private string partKey = "Head";

        [Header("발광 겉모습")]
        [Tooltip("발광 색. HDR 1 초과면 Bloom 이 켜진 화면에서 번진다")]
        [ColorUsage(false, true)]
        [SerializeField] private Color glowColor = new Color(0.5f, 1.4f, 1.1f);
        [Tooltip("구 가운데 불투명도. 0 = 완전 투명(테두리만 보임), 1 = 테두리만큼 진함")]
        [Range(0f, 1f)]
        [SerializeField] private float coreAlpha = 0.12f;
        [Tooltip("테두리 빛 두께. 클수록 가장자리에만 얇게 빛난다")]
        [Range(0.5f, 8f)]
        [SerializeField] private float rimPower = 3f;

        [Header("발광 밝기")]
        [Tooltip("살아 있는 동안 밝기 0~1. 맥동의 가장 밝은 순간 값")]
        [Range(0f, 1f)]
        [SerializeField] private float exposedBrightness = 0.9f;
        [Tooltip("맥동 빠르기 (초당 횟수)")]
        [Range(0f, 5f)]
        [SerializeField] private float pulsesPerSecond = 1.2f;
        [Tooltip("맥동 깊이 0~1. 0 = 맥동 없음, 0.35 = 가장 어두울 때 밝기의 65%")]
        [Range(0f, 1f)]
        [SerializeField] private float pulseDepth = 0.35f;

        [Header("피격 반짝임")]
        [Tooltip("약점 명중 순간 발광 색. HDR 1 초과면 Bloom 이 켜진 화면에서 번쩍 번진다")]
        [ColorUsage(false, true)]
        [SerializeField] private Color hitFlashColor = new Color(3f, 3f, 3f);
        [Tooltip("반짝임이 사라지기까지 걸리는 시간 (초)")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float hitFlashSeconds = 0.15f;
        [Tooltip("명중 순간 구 가운데 불투명도. 클수록 구 전체가 꽉 차게 번쩍인다")]
        [Range(0f, 1f)]
        [SerializeField] private float hitFlashCoreAlpha = 0.7f;
        [Tooltip("명중 순간 발광 구 크기 배율. 1 = 크기 변화 없음, 1.3 = 30% 커졌다가 돌아온다")]
        [Range(1f, 2f)]
        [SerializeField] private float hitFlashScale = 1.3f;

        private BossController owner;

        // ── 발광 구 ──
        private Renderer glowRenderer;
        private Material glowMaterial; // 약점마다 1개. OnDestroy 에서 지운다
        private Vector3 glowBaseScale; // 콜라이더 지름에 맞춘 크기. 반짝임 크기 배율의 기준

        // ── 피격 반짝임 ──
        private float hitFlashRemainingSeconds; // 0 = 반짝이는 중 아님

        public Faction Faction => Faction.Monster;
        public bool IsAlive => owner.IsAlive;

        /// <summary>피해를 넘길 보스(부모의 BossController)를 잡고, 콜라이더 크기의 발광 구를 만든다</summary>
        private void Awake()
        {
            owner = GetComponentInParent<BossController>();
            CreateGlowSphere();
        }

        /// <summary>
        /// 발광을 지금 상태로 칠한다. 죽었으면 끄고, 살아 있으면 맥동한다. 약점 명중 직후면 그 위에 반짝임을 얹는다.
        /// 매 프레임 상태에서 다시 계산하므로 보스를 다시 초기화(체력 회복)한 뒤 따로 되돌릴 필요가 없다.
        /// </summary>
        private void Update()
        {
            glowRenderer.enabled = owner.IsAlive;
            if (owner.IsAlive == false)
            {
                hitFlashRemainingSeconds = 0f; // 죽이는 한 발의 반짝임이 재초기화 뒤에 보이지 않게
                return;
            }

            ApplyGlow(PulsingBrightness(), TickHitFlash());
        }

        /// <summary>Awake 에서 만든 머티리얼을 지운다 (Renderer.material 과 달리 직접 만든 것은 자동으로 안 지워진다)</summary>
        private void OnDestroy()
        {
            Destroy(glowMaterial);
        }

        /// <summary>
        /// 총알 피해에 부위 배율을 곱해 보스에 넘기고, 발광 구를 반짝인다. 폭발 피해는 무시한다 (몸통 콜라이더가 받는다).
        /// </summary>
        /// <param name="info">무기가 준 원래 피해</param>
        public void TakeDamage(in DamageInfo info)
        {
            if (info.Kind == DamageKind.Explosion) return;

            MonsterPartDef part = owner.Def.FindPart(partKey); // 없으면 null → 아래에서 NullReferenceException

            int amount = MonsterWeakPoint.ScaleDamage(info.Amount, part.DamageMultiplier);
            var weakPointInfo = new DamageInfo(amount, info.Kind, info.AttackerFaction, info.Attacker,
                                               info.HitPoint, info.Direction, info.SourceDefId);
            StartHitFlash();
            owner.TakeDamage(weakPointInfo);
        }

        /// <summary>
        /// 구 콜라이더와 같은 위치·크기의 발광 구를 자식으로 만든다. 판정 구와 겉모습이 같아,
        /// 몸통 밖으로 보이는 부분이 곧 실제로 맞힐 수 있는 부분이다.
        /// </summary>
        /// <exception cref="NotSupportedException">판정 콜라이더가 SphereCollider 가 아닐 때 (지금 보스 프리팹은 구만 쓴다)</exception>
        private void CreateGlowSphere()
        {
            var hitSphere = GetComponent<Collider>() as SphereCollider;
            if (hitSphere == null)
                throw new NotSupportedException($"{name}: 보스 약점 발광은 SphereCollider 만 지원한다");

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "WeakPointGlow";
            Destroy(glow.GetComponent<Collider>()); // 판정은 이 오브젝트의 콜라이더가 맡는다
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = hitSphere.center;
            glowBaseScale = Vector3.one * (hitSphere.radius * 2f); // 기본 구 지름 1
            glow.transform.localScale = glowBaseScale;

            glowMaterial = new Material(Resources.Load<Shader>(GlowShaderPath));
            glowRenderer = glow.GetComponent<Renderer>();
            glowRenderer.sharedMaterial = glowMaterial;
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
        }

        /// <summary>살아 있는 동안 밝기. 사인파로 exposedBrightness 와 그보다 pulseDepth 만큼 어두운 값 사이를 오간다</summary>
        /// <returns>밝기 0~1</returns>
        private float PulsingBrightness()
        {
            float wave = (Mathf.Sin(Time.time * pulsesPerSecond * 2f * Mathf.PI) + 1f) * 0.5f; // 0~1
            return exposedBrightness * (1f - pulseDepth * wave);
        }

        /// <summary>약점 명중 반짝임을 처음부터 다시 시작한다. TakeDamage 가 약점 피해를 넘길 때 부른다</summary>
        private void StartHitFlash()
        {
            hitFlashRemainingSeconds = hitFlashSeconds;
        }

        /// <summary>
        /// 이번 프레임 반짝임 세기를 구하고 남은 시간을 줄인다. 명중 순간 가장 세고 빠르게 사그라든다 (제곱 감쇠).
        /// </summary>
        /// <returns>반짝임 세기 0~1. 0 = 반짝임 없음, 1 = 명중 순간</returns>
        private float TickHitFlash()
        {
            float remainingRatio = hitFlashRemainingSeconds / hitFlashSeconds; // 1 → 0
            hitFlashRemainingSeconds = Mathf.Max(0f, hitFlashRemainingSeconds - Time.deltaTime);
            return remainingRatio * remainingRatio;
        }

        /// <summary>
        /// Inspector 값과 이번 프레임 밝기·반짝임을 머티리얼과 구 크기에 넣는다. 겉모습 값도 매 프레임 넣어 플레이 중 Inspector 조정이 바로 보인다.
        /// 반짝임 세기만큼 색은 hitFlashColor 로, 가운데 불투명도는 hitFlashCoreAlpha 로, 밝기는 1 로, 크기는 hitFlashScale 배로 옮겨 간다.
        /// </summary>
        /// <param name="brightness">평소 전체 밝기 0~1 (셰이더가 알파에 곱한다)</param>
        /// <param name="hitFlash">반짝임 세기 0~1 (0 = 평소 모습)</param>
        private void ApplyGlow(float brightness, float hitFlash)
        {
            glowMaterial.SetColor(GlowColorProperty, Color.Lerp(glowColor, hitFlashColor, hitFlash));
            glowMaterial.SetFloat(CoreAlphaProperty, Mathf.Lerp(coreAlpha, hitFlashCoreAlpha, hitFlash));
            glowMaterial.SetFloat(RimPowerProperty, rimPower);
            glowMaterial.SetFloat(BrightnessProperty, Mathf.Lerp(brightness, 1f, hitFlash));
            glowRenderer.transform.localScale = glowBaseScale * Mathf.Lerp(1f, hitFlashScale, hitFlash);
        }
    }
}
