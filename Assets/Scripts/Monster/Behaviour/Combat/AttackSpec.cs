using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 공격 1개의 값 묶음. 공격 전략은 이 값만 읽는다 (테이블 Def 에 직접 의존하지 않음).
    /// 테이블에서 만들 때는 From(MonsterAttackDef) 한 곳만 쓴다.
    /// </summary>
    public readonly struct AttackSpec
    {
        /// <summary>MonsterAttackTable Id (DamageInfo.SourceDefId). 0 = 알 수 없음</summary>
        public readonly int SourceDefId;
        public readonly AttackShape Shape;
        public readonly int Damage;
        public readonly int HitCount;
        /// <summary>공격 시작부터 다음 시작까지 최소 간격 (s)</summary>
        public readonly float Cooldown;
        /// <summary>예고 (s)</summary>
        public readonly float TelegraphTime;
        /// <summary>공격 후 경직 (s)</summary>
        public readonly float RecoveryTime;
        /// <summary>사거리/길이 (m). 공격 상태 진입 거리이기도 하다</summary>
        public readonly float Range;
        public readonly float Radius;
        /// <summary>부채꼴 전체 각도 (도)</summary>
        public readonly float Angle;
        /// <summary>직선 폭 (m)</summary>
        public readonly float Width;
        public readonly float ProjectileSpeed;
        public readonly float KnockbackDistance;
        public readonly float WallHitStunTime;
        public readonly float TriggerValue;
        public readonly string AnimTrigger;

        public AttackSpec(AttackShape shape, int damage, int hitCount, float cooldown,
                          float telegraphTime, float recoveryTime, float range,
                          float radius = 0f, float angle = 0f, float width = 0f,
                          float projectileSpeed = 0f, float knockbackDistance = 0f,
                          float wallHitStunTime = 0f, float triggerValue = 0f,
                          string animTrigger = null, int sourceDefId = 0)
        {
            SourceDefId = sourceDefId;
            Shape = shape;
            Damage = damage;
            HitCount = hitCount;
            Cooldown = cooldown;
            TelegraphTime = telegraphTime;
            RecoveryTime = recoveryTime;
            Range = range;
            Radius = radius;
            Angle = angle;
            Width = width;
            ProjectileSpeed = projectileSpeed;
            KnockbackDistance = knockbackDistance;
            WallHitStunTime = wallHitStunTime;
            TriggerValue = triggerValue;
            AnimTrigger = animTrigger;
        }

        public static AttackSpec From(MonsterAttackDef def) => new AttackSpec(
            def.Shape, def.Damage, def.HitCount, def.Cooldown, def.TelegraphTime, def.RecoveryTime, def.Range,
            def.Radius, def.Angle, def.Width, def.ProjectileSpeed, def.KnockbackDistance,
            def.WallHitStunTime, def.TriggerValue, def.AnimTrigger, def.Id);
    }
}
