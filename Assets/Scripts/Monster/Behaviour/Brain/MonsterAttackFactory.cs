using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 역할(Role)에 맞는 공격 전략을 만든다. MonsterController.Start 가 조립 때 호출한다.
    /// 새 공격 방식 = 전략 클래스 1개 + case 1줄.
    /// </summary>
    public static class MonsterAttackFactory
    {
        /// <summary>
        /// 역할 → 공격 전략. 만든 공격은 MonsterStateMachine 에 넘겨져 AttackState 가 진행한다.
        /// </summary>
        /// <param name="role">몬스터 역할. 지금은 Melee, Infiltrator, ZonePressure, Rusher, Air 지원</param>
        /// <param name="spec">공격 1개의 값 묶음</param>
        /// <param name="body">공격자 몸체 (위치·방향)</param>
        /// <param name="findDamageable">공격 대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <param name="settings">기존 공격 설정 컴포넌트의 참조 묶음. 역할에 필요한 설정만 사용한다</param>
        /// <param name="movement">돌진·급강하가 몸을 직접 옮길 때 쓰는 이동 컴포넌트. Rusher, Air 가 쓴다</param>
        /// <param name="flyingExecutor">공중(Air) 프리팹의 비행 실행기. 다른 역할은 null</param>
        /// <returns>역할에 맞는 공격 전략</returns>
        /// <exception cref="NotSupportedException">아직 공격이 구현되지 않은 역할일 때</exception>
        public static MonsterAttack CreateAttack(MonsterRole role, in AttackSpec spec, MonsterBody body,
                                                  Func<IPlayerTarget, IDamageable> findDamageable,
                                                  in MonsterAttackSettings settings, MonsterMovement movement,
                                                  FlyingMoveExecutor flyingExecutor)
        {
            switch (role)
            {
                case MonsterRole.Melee: return new StrikeAttack(spec, body, findDamageable);
                case MonsterRole.Infiltrator: return new PounceAttack(spec, body, findDamageable, settings.Pounce);
                case MonsterRole.ZonePressure:
                    StatusEffectDef effect = DataManager.StatusEffects.Get(spec.EffectId);
                    return new PoisonAreaAttack(spec, body, findDamageable, settings.PoisonArea, effect);
                case MonsterRole.Rusher: return new ChargeAttack(spec, body, findDamageable, movement, settings.Charge);
                case MonsterRole.Air: return new DiveAttack(spec, body, findDamageable, movement, settings.Dive, flyingExecutor);
                default: throw new NotSupportedException($"{role} 역할의 공격은 아직 구현되지 않았다");
            }
        }
    }
}
