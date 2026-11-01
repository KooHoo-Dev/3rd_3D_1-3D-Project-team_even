using System;
using HybridShooter.Core.Contracts;
using UnityEngine;

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
        private readonly SelfDestructSettings settings;

        public override AfterAttack After => AfterAttack.Die;

        /// <summary>MonsterAttackFactory 가 SelfDestruct 역할에 만든다</summary>
        /// <param name="spec">공격 값 묶음. Range = 접근 거리, Radius = 폭발 반경</param>
        /// <param name="body">공격자 몸체. 폭발 중심 = 몸 위치</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <param name="settings">자폭 프리팹의 폭발 연출 설정 (폭발 이펙트 풀)</param>
        /// <exception cref="ArgumentNullException">settings 가 null 일 때 (프리팹에 SelfDestructSettings 가 없을 때). body 는 기반 생성자가 던진다</exception>
        public SelfDestructAttack(in AttackSpec spec, MonsterBody body,
                                  Func<IPlayerTarget, IDamageable> findDamageable,
                                  SelfDestructSettings settings)
            : base(spec, body, findDamageable)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings), "자폭 몬스터 프리팹에 SelfDestructSettings 가 없다");

            area = new CircleArea(0f, spec.Radius);
            this.settings = settings;
        }

        protected override bool OnActive(float now, float deltaTime)
        {
            var target = Target;
            bool isInBlast = area.Contains(Body.Position, Body.Forward, target.PlayerTrackingInfo.Position);

            // 피해로 플레이어 사망 이벤트가 발생해도 폭발 완료가 취소로 바뀌지 않게 먼저 주기를 닫는다.
            EndEarly(AttackResult.Completed);
            NotifyBodyContacted(Body.Position); // 기존 ImpactSfx 경로로 폭발음을 재생한다.
            PlayExplosionEffect();
            if (isInBlast) Hit(target, Body.Position);
            return true;
        }

        /// <summary>
        /// 몸 위치에 폭발 이펙트를 풀에서 꺼내 재생한다. 이펙트는 수명(PooledEffect)이 끝나면 스스로 풀에 돌아간다.
        /// 이펙트는 몬스터의 자식이 아니라서 몬스터가 회수돼도 끝까지 재생된다. OnActive 가 폭발 순간 1번 부른다.
        /// </summary>
        private void PlayExplosionEffect()
        {
            settings.ExplosionPool.Play(Body.Position, Quaternion.identity);
        }
    }
}
