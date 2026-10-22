using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 역할(Role) → 공격 전략. 새 공격 방식 = 전략 클래스 1개 + case 1줄.
    /// </summary>
    public static class MonsterBrainFactory
    {
        public static IMonsterAttack CreateAttack(MonsterRole role, in AttackSpec spec, IMonsterBody body,
                                                  Func<IPlayerTarget, IDamageable> findDamageable)
        {
            switch (role)
            {
                case MonsterRole.Melee: return new StrikeAttack(spec, body, findDamageable);
                default: throw new NotSupportedException($"{role} 역할의 공격은 아직 구현되지 않았다");
            }
        }
    }
}
