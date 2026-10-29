using System;
using HybridShooter.Core.Contracts;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 접근 후 예고하고 자기 위치를 중심으로 한 번 폭발한다.
    /// Range 는 접근 거리, Radius 는 폭발 반경이다. 판정 완료 뒤 AttackState 가 보상 없이 Dead 로 보낸다.
    /// 예고 중 처치·대상 상실에 의한 취소는 기존 MonsterAttack/상태 머신 흐름을 따른다.
    /// </summary>
    public sealed class SelfDestructAttack : MonsterAttack
    {
        private readonly CircleArea area;

        public override AfterAttack After => AfterAttack.Die;

        public SelfDestructAttack(in AttackSpec spec, MonsterBody body,
                                  Func<IPlayerTarget, IDamageable> findDamageable)
            : base(spec, body, findDamageable)
        {
            area = new CircleArea(0f, spec.Radius);
        }

        protected override bool OnActive(float now, float deltaTime)
        {
            var target = Target;
            bool isInBlast = area.Contains(Body.Position, Body.Forward, target.PlayerTrackingInfo.Position);

            // 피해로 플레이어 사망 이벤트가 발생해도 폭발 완료가 취소로 바뀌지 않게 먼저 주기를 닫는다.
            EndEarly(AttackResult.Completed);
            NotifyBodyContacted(Body.Position); // 기존 ImpactSfx 경로로 폭발음을 재생한다.
            if (isInBlast) Hit(target, Body.Position);
            return true;
        }
    }
}
