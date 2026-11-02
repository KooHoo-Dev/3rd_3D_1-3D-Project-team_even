using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 약점 히트박스 하나 (뼈 아래 자식 구 콜라이더). 총알이 이 콜라이더를 맞히면
    /// MonsterPartTable 의 DamageMultiplier 를 곱한 피해를 MonsterController 에 약점 피해로 넘긴다.
    /// 무기는 TryGetDamageable(부모 방향 탐색)로 몸통보다 먼저 이 컴포넌트를 찾는다.
    /// 콜라이더와 같은 크기의 발광 구를 직접 만들어, 약점이 드러났을 때 밝게·숨었을 때 어둡게 보여준다.
    /// </summary>
    /// <remarks>
    /// 폭발 피해는 받지 않는다. 수류탄은 IDamageable 마다 1번 피해를 주므로, 받으면 몸통과 약점에서 두 번 깎인다.
    /// Exposure(노출 조건)가 WhileAttacking 이면 공격 중에만 약점이고, 그 외에는 몸통 피해(배율 없음)로 넘긴다.
    /// 발광 구는 프리팹이 아니라 Awake 에서 만든다 (몬스터 프리팹 수정 불필요). 풀 재사용 후에도 Update 가 매 프레임 상태로 다시 칠한다.
    /// </remarks>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class MonsterWeakPoint : MonoBehaviour, IDamageable
    {
        // URP 기본 Unlit. 조명과 상관없이 색 그대로 보여 어두운 맵에서도 보인다.
        // 빌드 포함은 Resources 의 M_Telegraph.mat 이 같은 셰이더를 써서 보장된다.
        private const string GlowShaderName = "Universal Render Pipeline/Unlit";

        [Tooltip("MonsterPartTable 의 PartKey. 이 몬스터 Def 의 부위에서 배율을 찾는다")]
        [SerializeField] private string partKey = "Gland";

        [Header("발광")]
        [Tooltip("약점이 드러났을 때(배율 적용) 발광 색. HDR 1 초과면 Bloom 이 켜진 화면에서 번진다")]
        [ColorUsage(false, true)]
        [SerializeField] private Color exposedGlowColor = new Color(2f, 1.6f, 0.3f);
        [Tooltip("약점이 숨었을 때(몸통 판정) 발광 색. 위치만 알 수 있게 어둡게")]
        [ColorUsage(false, true)]
        [SerializeField] private Color hiddenGlowColor = new Color(0.25f, 0.2f, 0.05f);

        private MonsterController owner;

        // ── 발광 구 ──
        private Renderer glowRenderer;
        private Material glowMaterial; // 약점마다 1개. OnDestroy 에서 지운다

        public Faction Faction => Faction.Monster;
        public bool IsAlive => owner.IsAlive;

        /// <summary>피해를 넘길 몸(부모의 MonsterController)을 잡고, 콜라이더 크기의 발광 구를 만든다</summary>
        private void Awake()
        {
            owner = GetComponentInParent<MonsterController>();
            CreateGlowSphere();
        }

        /// <summary>
        /// 발광을 지금 상태로 칠한다. 죽었으면 끄고, 살아 있으면 노출 여부에 따라 밝게·어둡게.
        /// 매 프레임 상태에서 다시 계산하므로 풀 재사용(Respawn) 뒤 따로 되돌릴 필요가 없다.
        /// </summary>
        private void Update()
        {
            glowRenderer.enabled = owner.IsAlive;
            if (owner.IsAlive == false) return;

            MonsterPartDef part = owner.Def.FindPart(partKey);
            if (IsExposed(part.Exposure)) glowMaterial.color = exposedGlowColor;
            else glowMaterial.color = hiddenGlowColor;
        }

        /// <summary>Awake 에서 만든 머티리얼을 지운다 (Renderer.material 과 달리 직접 만든 것은 자동으로 안 지워진다)</summary>
        private void OnDestroy()
        {
            Destroy(glowMaterial);
        }

        /// <summary>
        /// 총알 피해에 부위 배율을 곱해 몸에 약점 피해로 넘긴다. 폭발 피해는 무시한다 (몸통이 받는다).
        /// 약점이 드러나지 않은 때(노출 조건 불만족)는 원래 피해를 몸통 피해로 넘긴다.
        /// 약점 피해를 넘기기 직전 WeakPointHitEvent 를 발행해 사운드가 같은 발의 타격음을 약점 명중음으로 바꾸게 한다.
        /// </summary>
        /// <param name="info">무기가 준 원래 피해</param>
        public void TakeDamage(in DamageInfo info)
        {
            if (info.Kind == DamageKind.Explosion) return;

            MonsterPartDef part = owner.Def.FindPart(partKey); // 없으면 null → 아래에서 NullReferenceException
            if (IsExposed(part.Exposure) == false)
            {
                owner.TakeDamage(info);
                return;
            }

            int amount = ScaleDamage(info.Amount, part.DamageMultiplier);
            var weakPointInfo = new DamageInfo(amount, info.Kind, info.AttackerFaction, info.Attacker,
                                               info.HitPoint, info.Direction, info.SourceDefId);
            EventBus<WeakPointHitEvent>.Publish(new WeakPointHitEvent(owner.gameObject, info.HitPoint));
            owner.TakeWeakPointDamage(weakPointInfo);
        }

        /// <summary>
        /// 구 콜라이더와 같은 위치·크기의 발광 구를 자식으로 만든다. 판정 구와 겉모습이 같아,
        /// 몸통 밖으로 보이는 부분이 곧 실제로 맞힐 수 있는 부분이다.
        /// </summary>
        private void CreateGlowSphere()
        {
            var hitSphere = GetComponent<SphereCollider>();
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "WeakPointGlow";
            Destroy(glow.GetComponent<Collider>()); // 판정은 이 오브젝트의 콜라이더가 맡는다
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = hitSphere.center;
            glow.transform.localScale = Vector3.one * (hitSphere.radius * 2f); // 기본 구 지름 1

            glowMaterial = new Material(Shader.Find(GlowShaderName));
            glowRenderer = glow.GetComponent<Renderer>();
            glowRenderer.sharedMaterial = glowMaterial;
            glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
        }

        /// <summary>노출 조건을 지금 몬스터 상태로 판단한다. TakeDamage 가 배율을 곱할지 정할 때 부른다</summary>
        /// <param name="exposure">MonsterPartTable.Exposure</param>
        /// <returns>true = 약점이 드러나 배율을 곱한다</returns>
        /// <exception cref="NotSupportedException">WhileRecovering 처럼 아직 판단 방법이 없는 조건일 때</exception>
        private bool IsExposed(PartExposure exposure)
        {
            switch (exposure)
            {
                case PartExposure.Always:
                    return true;
                case PartExposure.WhileAttacking:
                    return owner.IsAttacking;
                default:
                    throw new NotSupportedException($"{exposure} 노출 조건은 아직 일반 몬스터 약점에서 쓸 수 없다");
            }
        }

        /// <summary>약점 피해 = 원래 피해 × 부위 배율, 반올림 (.5 는 올림. 7 × 1.5 = 11)</summary>
        /// <param name="amount">원래 피해</param>
        /// <param name="multiplier">MonsterPartTable.DamageMultiplier (0 초과)</param>
        /// <returns>약점 피해 (정수)</returns>
        public static int ScaleDamage(int amount, float multiplier)
        {
            return (int)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
        }
    }
}
