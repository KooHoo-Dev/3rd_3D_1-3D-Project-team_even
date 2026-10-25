using System;
using HybridShooter.Core.Contracts;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 제자리 타격 (기본 근접형). 예고 시작에 대상을 향해 방향을 고정하고,
    /// 공격 순간 대상이 판정 범위 안에 있을 때만 피해를 준다 → 예고 중 범위 밖으로 피하면 맞지 않는다.
    /// </summary>
    public sealed class StrikeAttack : MonsterAttack
    {
        private readonly MonsterBody body;
        private readonly IHitArea area;

        /// <summary>
        /// MonsterAttackFactory 가 Melee 역할에 만든다. spec 의 Shape 로 판정 범위를 1번 만들어 둔다.
        /// </summary>
        /// <param name="spec">공격 값 묶음</param>
        /// <param name="body">공격자 몸체. 조준과 판정 기준 위치·방향</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <exception cref="ArgumentNullException">body 가 null 일 때</exception>
        public StrikeAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable)
            : base(spec, findDamageable)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            area = HitAreas.From(spec);
        }

        /// <summary>예고 시작에 대상 쪽으로 방향을 고정한다. 이후 판정은 이 방향 기준이다</summary>
        protected override void OnWindup() => body.Face(Target.PlayerTrackingInfo.Position);

        /// <summary>판정 순간 대상이 범위 안이면 Hit 으로 피해를 준다. 맞든 안 맞든 첫 판정으로 동작이 끝난다</summary>
        /// <returns>항상 true (동작 완료)</returns>
        protected override bool OnActive(float now, float deltaTime)
        {
            if (area.Contains(body.Position, body.Forward, Target.PlayerTrackingInfo.Position))
                Hit(Target, body.Position);
            return true;
        }
    }
}
