using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 몬스터 등급 규칙 (일반체/중간체/보스). 런타임 값이 아니라 MonsterTable 검증 기준이다.
    /// 처치 타수 = ceil(MaxHp / 라이플 Damage), 공격 패턴 수 = 기본 공격(PatternTag None) 개수.
    /// </summary>
    public sealed class MonsterGradeDef
    {
        public const string Table = "MonsterGradeTable";

        public int GradeId { get; }
        public MonsterGrade Grade { get; }
        public string Name { get; }
        public int MinHitsToKill { get; }
        /// <summary>-1 = 제한 없음</summary>
        public int MaxHitsToKill { get; }
        /// <summary>-1 = 보스 별도 (BossPhaseTable 로 정의)</summary>
        public int AttackPatternCount { get; }

        internal MonsterGradeDef(MonsterGradeDto d, DataLoadReport r)
        {
            GradeId = d.GradeId;
            Grade = ParseUtil.Enum<MonsterGrade>(d.Grade, r, Table, GradeId, nameof(d.Grade));
            Name = d.Name;
            MinHitsToKill = d.MinHitsToKill;
            MaxHitsToKill = d.MaxHitsToKill;
            AttackPatternCount = d.AttackPatternCount;

            r.Check(MinHitsToKill >= 1, Table, GradeId, "MinHitsToKill 은 1 이상");
            r.Check(MaxHitsToKill == -1 || MaxHitsToKill >= MinHitsToKill, Table, GradeId, "MaxHitsToKill 은 -1 또는 MinHitsToKill 이상");
        }

        public bool IsHitsInRange(int hits) => hits >= MinHitsToKill && (MaxHitsToKill < 0 || hits <= MaxHitsToKill);
    }
}
