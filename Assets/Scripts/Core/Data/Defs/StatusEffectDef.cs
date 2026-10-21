using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>플레이어에게 걸리는 상태 효과 (독안개 등). 몬스터 자신의 기절은 MonsterAttackDef.WallHitStunTime 사용</summary>
    public sealed class StatusEffectDef
    {
        public const string Table = "StatusEffectTable";

        public int Id { get; }
        public string Key { get; }
        public string Name { get; }
        public EffectType Type { get; }
        /// <summary>틱 피해량</summary>
        public float Value { get; }
        public float Duration { get; }
        public float TickInterval { get; }
        /// <summary>바닥 구역 효과 (구역 안에 있는 동안 적용)</summary>
        public bool IsZone { get; }

        internal StatusEffectDef(StatusEffectDto d, DataLoadReport r)
        {
            Id = d.Id;
            Key = d.Key;
            Name = d.Name;
            Type = ParseUtil.Enum<EffectType>(d.Type, r, Table, Id, nameof(d.Type));
            Value = d.Value;
            Duration = d.Duration;
            TickInterval = d.TickInterval;
            IsZone = d.IsZone;

            r.Check(Id > 0, Table, Id, "Id 는 1 이상 (0 = 효과 없음)");
            r.Check(Duration > 0f, Table, Id, "Duration 은 0 초과");
            r.Check(TickInterval > 0f, Table, Id, "TickInterval 은 0 초과");
        }
    }
}
