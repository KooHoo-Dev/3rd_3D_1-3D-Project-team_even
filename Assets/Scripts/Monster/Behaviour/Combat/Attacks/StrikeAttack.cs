using System;
using HybridShooter.Core.Contracts;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 제자리 타격 (기본 근접형). 예고 중 움직이는 대상을 따라 회전 속도만큼 돌고(MonsterAttack.Tick),
    /// 공격 순간 대상이 판정 범위 안에 있을 때만 피해를 준다 → 예고 중 범위 밖으로 피하면 맞지 않는다.
    /// </summary>
    public sealed class StrikeAttack : MonsterAttack
    {
        private readonly IHitArea area;

        /// <summary>
        /// MonsterAttackFactory 가 Melee 역할에 만든다. spec 의 Shape 로 판정 범위를 1번 만들어 둔다.
        /// </summary>
        /// <param name="spec">공격 값 묶음</param>
        /// <param name="body">공격자 몸체. 예고 중 회전과 판정 기준 위치·방향</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <exception cref="ArgumentNullException">body 가 null 일 때 (기반 생성자가 던진다)</exception>
        public StrikeAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable)
            : base(spec, body, findDamageable)
        {
            area = HitAreas.From(spec);
        }

        /// <summary>판정 순간 대상이 범위 안이면 Hit 으로 피해를 준다. 맞든 안 맞든 첫 판정으로 동작이 끝난다</summary>
        /// <returns>항상 true (동작 완료)</returns>
        protected override bool OnActive(float now, float deltaTime)
        {
            if (area.Contains(Body.Position, Body.Forward, Target.PlayerTrackingInfo.Position))
                Hit(Target, Body.Position);
            return true;
        }
    }
}
