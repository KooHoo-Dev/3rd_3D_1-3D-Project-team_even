using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 부위 종류. Body 는 부서지지 않는 몸통 (플레이어를 막는 콜라이더도 겸한다).
    /// Head = 맨 위 돔과 그 둘레 턱 꽃잎 6장 (입. 포효·침·포자가 여기서 나온다)
    /// </summary>
    public enum TyranopodePartType { Body, FrontTentacle, LegTentacle, Sac, Head }

    /// <summary>
    /// 부위 히트박스 하나 (뼈 아래 콜라이더). 총·수류탄은 이 콜라이더를 맞히고, 피해는 TyranopodeController 가
    /// 부위 배율·그로기 배율을 곱해 BossController 로 넘긴다. 같은 종류 히트박스는 체력을 함께 쓴다.
    ///  - 몸통 외 히트박스는 BossHitbox 레이어: 총알·수류탄 판정만 받고 플레이어와 부딪치지 않는다 (TyranopodeController)
    ///  - 부서지면 severBone 을 severScale 로 줄여 잘린 것처럼 보이게 하고 (애니메이션 뒤 LateUpdate), 상처 연출을 켠다
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TyranopodePart : MonoBehaviour, IDamageable
    {
        [SerializeField] private TyranopodePartType type;
        [Tooltip("부서지면 줄일 뼈 (없으면 줄이지 않음)")]
        [SerializeField] private Transform severBone;
        [SerializeField, Range(0.01f, 1f)] private float severScale = 0.05f;
        [Tooltip("부서진 뒤 켜 둘 상처 연출 (잘린 자리의 연기·체액). 꺼진 채로 둔다")]
        [SerializeField] private GameObject stumpVfx;

        private TyranopodeController owner;
        private bool severed;

        public TyranopodePartType Type => type;
        public bool HasStump => stumpVfx != null;
        public Faction Faction => Faction.Monster;
        public bool IsAlive => owner.IsAlive;

        private void Awake()
        {
            owner = GetComponentInParent<TyranopodeController>()
                    ?? throw new System.InvalidOperationException($"[Tyranopode] 히트박스 {name} 위에 TyranopodeController 가 없습니다.");
            // 몸통은 부서지지 않아 상처 연출이 없다
            if (stumpVfx != null) stumpVfx.SetActive(false);
        }

        public void TakeDamage(in DamageInfo info)
        {
            owner.ApplyPartDamage(this, info);
        }

        /// <summary>부위 파괴 시 컨트롤러가 부른다</summary>
        public void Sever()
        {
            severed = severBone != null;
            stumpVfx.SetActive(true);
        }

        private void LateUpdate()
        {
            if (severed) severBone.localScale = Vector3.one * severScale;
        }
    }
}
