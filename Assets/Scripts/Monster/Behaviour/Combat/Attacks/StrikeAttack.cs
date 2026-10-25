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

        public StrikeAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable)
            : base(spec, findDamageable)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            area = HitAreas.From(spec);
        }

        protected override void OnWindup() => body.Face(Target.PlayerTrackingInfo.Position);

        protected override bool OnActive(float now, float deltaTime)
        {
            if (area.Contains(body.Position, body.Forward, Target.PlayerTrackingInfo.Position))
                Hit(Target, body.Position);
            return true;
        }
    }
}
