using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 몬스터 공격 한 개. 일반 몬스터는 1개, 보스는 기본 2 + 특수 4(등장 1 포함).
    /// 모든 공격은 "예고(TelegraphTime) → 공격 → 경직(RecoveryTime, 플레이어 반격 시간)" 흐름을 따른다.
    /// Id = MonsterId × 100 + 순번.
    /// </summary>
    public sealed class MonsterAttackDef
    {
        public const string Table = "MonsterAttackTable";

        public int Id { get; }
        public int MonsterId { get; }
        public string AttackKey { get; }
        public string Name { get; }
        public BossPatternTag PatternTag { get; }
        public bool IsSpecial => PatternTag != BossPatternTag.None;
        /// <summary>보스 등장 공격 (1회성)</summary>
        public bool IsEntrance => PatternTag == BossPatternTag.Entrance;
        public int Damage { get; }
        public int HitCount { get; }
        public float Cooldown { get; }
        public float TelegraphTime { get; }
        public float RecoveryTime { get; }
        public AttackShape Shape { get; }
        /// <summary>사거리/길이(m)</summary>
        public float Range { get; }
        public float Radius { get; }
        /// <summary>부채꼴 각도(도)</summary>
        public float Angle { get; }
        /// <summary>직선 폭(m)</summary>
        public float Width { get; }
        public float ProjectileSpeed { get; }
        public int ProjectileCount { get; }
        public float KnockbackDistance { get; }
        /// <summary>벽 충돌 시 자기 기절(초). 0 = 없음</summary>
        public float WallHitStunTime { get; }
        /// <summary>0 = 없음</summary>
        public int EffectId { get; }
        public TriggerType Trigger { get; }
        public float TriggerValue { get; }
        /// <summary>적중 시 살아있는 일반 몬스터를 정리</summary>
        public bool ClearsMinions { get; }
        public string AnimTrigger { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public MonsterDef Owner { get; internal set; }
        public StatusEffectDef Effect { get; internal set; }

        internal MonsterAttackDef(MonsterAttackDto d, DataLoadReport r)
        {
            Id = d.Id;
            MonsterId = d.MonsterId;
            AttackKey = d.AttackKey;
            Name = d.Name;
            PatternTag = ParseUtil.Enum<BossPatternTag>(d.PatternTag, r, Table, Id, nameof(d.PatternTag));
            Damage = d.Damage;
            HitCount = d.HitCount;
            Cooldown = d.Cooldown;
            TelegraphTime = d.TelegraphTime;
            RecoveryTime = d.RecoveryTime;
            Shape = ParseUtil.Enum<AttackShape>(d.Shape, r, Table, Id, nameof(d.Shape));
            Range = d.Range;
            Radius = d.Radius;
            Angle = d.Angle;
            Width = d.Width;
            ProjectileSpeed = d.ProjectileSpeed;
            ProjectileCount = d.ProjectileCount;
            KnockbackDistance = d.KnockbackDistance;
            WallHitStunTime = d.WallHitStunTime;
            EffectId = d.EffectId;
            Trigger = ParseUtil.Enum<TriggerType>(d.Trigger, r, Table, Id, nameof(d.Trigger));
            TriggerValue = d.TriggerValue;
            ClearsMinions = d.ClearsMinions;
            AnimTrigger = d.AnimTrigger;

            r.Check(string.IsNullOrEmpty(AttackKey) == false, Table, Id, "AttackKey 가 비었습니다");
            r.Check(Id / 100 == MonsterId, Table, Id, "Id 는 MonsterId×100+순번 이어야 합니다");
            r.Check(Damage >= 0 && HitCount >= 1, Table, Id, "Damage >= 0, HitCount >= 1");
            r.Check(Cooldown >= 0f && TelegraphTime >= 0f && RecoveryTime >= 0f, Table, Id, "시간 값은 0 이상");
            r.Check(KnockbackDistance >= 0f && WallHitStunTime >= 0f, Table, Id, "KnockbackDistance, WallHitStunTime 은 0 이상");
            if (Trigger == TriggerType.PlayerStationary)
                r.Check(TriggerValue > 0f, Table, Id, "PlayerStationary 는 TriggerValue(초) 가 0 초과여야 합니다");

            switch (Shape)
            {
                case AttackShape.Melee: r.Check(Range > 0f, Table, Id, "Melee 는 Range 0 초과"); break;
                case AttackShape.Circle: r.Check(Radius > 0f, Table, Id, "Circle 은 Radius 0 초과"); break;
                case AttackShape.Cone: r.Check(Range > 0f && Angle > 0f && Angle <= 360f, Table, Id, "Cone 은 Range 0 초과, Angle 0~360"); break;
                case AttackShape.Line: r.Check(Range > 0f && Width > 0f, Table, Id, "Line 은 Range, Width 0 초과"); break;
                case AttackShape.Projectile: r.Check(ProjectileSpeed > 0f && ProjectileCount >= 1, Table, Id, "Projectile 은 ProjectileSpeed 0 초과, ProjectileCount 1 이상"); break;
            }
        }
    }
}
