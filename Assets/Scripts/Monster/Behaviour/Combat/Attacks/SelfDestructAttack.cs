using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 접근 후 예고하고 자기 위치를 중심으로 한 번 폭발한다.
    /// Range 는 접근 거리, Radius 는 폭발 반경이다. 판정 완료 뒤 AttackState 가 보상 없이 Dead 로 보낸다.
    /// 예고 중 처치·대상 상실에 의한 취소는 기존 MonsterAttack/상태 머신 흐름을 따른다.
    /// 설정에 따라 처치당한 순간에도 폭발하고(ExplodeOnKilled), 반경 안 몬스터에게도 중립 진영 피해를 준다.
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

            // 피해로 플레이어 사망 이벤트가 발생해도 폭발 완료가 취소로 바뀌지 않게 먼저 주기를 닫는다.
            EndEarly(AttackResult.Completed);
            Explode(target);
            return true;
        }

        /// <summary>
        /// 처치당한 순간 그 자리에서 폭발한다. SelfDestructSettings 의 처치 시 폭발이 꺼져 있으면 아무것도 하지 않는다.
        /// MonsterController 가 체력이 0 이 된 순간, Dead 로 보내기 전에 부른다.
        /// </summary>
        /// <param name="target">폭발 피해를 받을 수 있는 플레이어. null = 대상 없음 (몬스터 피해만)</param>
        public void ExplodeOnKilled(IPlayerTarget target)
        {
            if (settings.ExplodeWhenKilled == false) return;
            Explode(target);
        }

        /// <summary>
        /// 몸 위치에서 폭발한다: 폭발음·판정 알림·이펙트 → 반경 안 플레이어 피해 → (설정 시) 반경 안 몬스터 피해.
        /// OnActive(예고 끝 자폭)와 ExplodeOnKilled(처치당함)가 부른다.
        /// </summary>
        /// <param name="target">플레이어 대상. null = 플레이어 피해 없음</param>
        private void Explode(IPlayerTarget target)
        {
            bool hasTarget = target != null;
            bool isInBlast = hasTarget && area.Contains(Body.Position, Body.Forward, target.PlayerTrackingInfo.Position);

            NotifyBodyContacted(Body.Position); // 기존 ImpactSfx 경로로 폭발음을 재생한다.
            NotifyMonsterHit(area, Body.Position, Body.Forward, isInBlast);
            PlayExplosionEffect();
            if (isInBlast) Hit(target, Body.Position);
            if (settings.DamageMonsters) DamageMonstersInBlast();
        }

        /// <summary>
        /// 폭발 반경(구) 안의 살아 있는 몬스터마다 Damage × HitCount 피해를 1번 준다. 자기 자신은 뺀다.
        /// 공격 진영은 중립(Faction.Neutral)이라 같은 몬스터 진영도 맞는다. 처치되면 일반 처치처럼 보상이 나간다.
        /// </summary>
        /// <remarks>
        /// 맞은 자폭형이 연쇄로 터질 수 있어(재진입) 공유 버퍼 없이 매번 새 목록을 쓴다.
        /// 몸통·약점 콜라이더가 함께 걸려도 MonsterController 단위로 1번만 맞는다 (약점 배율 없음).
        /// </remarks>
        private void DamageMonstersInBlast()
        {
            Vector3 center = Body.Position;
            var self = Body.Root.GetComponent<MonsterController>();
            var damagedMonsters = new HashSet<MonsterController>();
            int amount = Spec.Damage * Spec.HitCount;

            foreach (Collider overlap in Physics.OverlapSphere(center, Spec.Radius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var monster = overlap.GetComponentInParent<MonsterController>();
                if (monster == null) continue;
                if (monster == self) continue;
                if (monster.CanBeHitBy(Faction.Neutral) == false) continue;
                if (damagedMonsters.Add(monster) == false) continue;

                Vector3 hitPoint = overlap.ClosestPoint(center);
                Vector3 direction = HitAreas.Flat(hitPoint - center);
                direction = direction.magnitude > 1e-4f ? direction.normalized : Vector3.zero;
                monster.TakeDamage(new DamageInfo(amount, DamageKind.Explosion, Faction.Neutral, Body.Root.gameObject,
                                                  hitPoint, direction, Spec.SourceDefId));
            }
        }

        /// <summary>
        /// 몸 위치에 폭발 이펙트를 풀에서 꺼내 재생한다. 이펙트는 수명(PooledEffect)이 끝나면 스스로 풀에 돌아간다.
        /// 이펙트는 몬스터의 자식이 아니라서 몬스터가 회수돼도 끝까지 재생된다. OnActive 가 폭발 순간 1번 부른다.
        /// </summary>
        private void PlayExplosionEffect()
        {
            PooledEffect effect = settings.ExplosionPool.Play(Body.Position, Quaternion.identity);
            effect.transform.localScale = Spec.Radius > 3 ? Vector3.one : Vector3.one * Spec.Radius / 3;
        }
    }
}
