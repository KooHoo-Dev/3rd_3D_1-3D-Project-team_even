using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Monster.Behaviour;
using UnityEngine;

namespace HybridShooter.Boss
{
    /// <summary>
    /// 보스 약점 히트박스 하나 (뼈 아래 자식 콜라이더). 총알이 이 콜라이더를 맞히면
    /// MonsterPartTable 의 BossId 행 DamageMultiplier 를 곱한 피해를 BossController 에 넘긴다.
    /// 무기는 TryGetDamageable(부모 방향 탐색)로 보스 몸통보다 먼저 이 컴포넌트를 찾는다.
    /// </summary>
    /// <remarks>
    /// 폭발 피해는 받지 않는다. 수류탄은 IDamageable 마다 1번 피해를 주므로, 받으면 몸통과 약점에서 두 번 깎인다.
    /// Exposure(노출 조건)는 쓰지 않는다. 지금 보스 부위는 모두 Always 다.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public sealed class BossWeakPoint : MonoBehaviour, IDamageable
    {
        [Tooltip("MonsterPartTable 의 PartKey. 이 보스 Def(BossId 행)의 부위에서 배율을 찾는다")]
        [SerializeField] private string partKey = "Head";

        private BossController owner;

        public Faction Faction => Faction.Monster;
        public bool IsAlive => owner.IsAlive;

        /// <summary>피해를 넘길 보스(부모의 BossController)를 잡는다</summary>
        private void Awake()
        {
            owner = GetComponentInParent<BossController>();
        }

        /// <summary>
        /// 총알 피해에 부위 배율을 곱해 보스에 넘긴다. 폭발 피해는 무시한다 (몸통 콜라이더가 받는다).
        /// </summary>
        /// <param name="info">무기가 준 원래 피해</param>
        public void TakeDamage(in DamageInfo info)
        {
            if (info.Kind == DamageKind.Explosion) return;

            MonsterPartDef part = owner.Def.FindPart(partKey); // 없으면 null → 아래에서 NullReferenceException

            int amount = MonsterWeakPoint.ScaleDamage(info.Amount, part.DamageMultiplier);
            var weakPointInfo = new DamageInfo(amount, info.Kind, info.AttackerFaction, info.Attacker,
                                               info.HitPoint, info.Direction, info.SourceDefId);
            owner.TakeDamage(weakPointInfo);
        }
    }
}
