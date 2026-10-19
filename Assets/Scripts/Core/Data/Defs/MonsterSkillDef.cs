using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 몬스터 스킬 한 개. 모든 스킬은 "준비(Windup, 예고) → 공격 → 회복(Recover, 반격 시간)" 흐름을 따른다. (BL-027)
    /// SkillKey → 구현 클래스 매핑과 Param1/Param2 의미는 Docs/Tables/README.md 에 기록.
    /// </summary>
    public sealed class MonsterSkillDef
    {
        public const string Table = "MonsterSkillTable";

        public int Id { get; }
        public int MonsterId { get; }
        public string SkillKey { get; }
        public SkillSlot Slot { get; }
        public int Damage { get; }
        /// <summary>공격/예고 범위 반경(m)</summary>
        public float Radius { get; }
        /// <summary>준비 동작·예고 표시 시간(초). 플레이어가 피할 시간</summary>
        public float WindupTime { get; }
        /// <summary>공격 후 경직(초). 플레이어가 반격할 시간</summary>
        public float RecoverTime { get; }
        public float Cooldown { get; }
        /// <summary>보스 패턴 선택 가중치</summary>
        public int Weight { get; }
        public float Param1 { get; }
        public float Param2 { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public MonsterDef Owner { get; internal set; }

        internal MonsterSkillDef(MonsterSkillDto d, DataLoadReport r)
        {
            Id = d.Id;
            MonsterId = d.MonsterId;
            SkillKey = d.SkillKey;
            Slot = ParseUtil.Enum<SkillSlot>(d.Slot, r, Table, Id, nameof(d.Slot));
            Damage = d.Damage;
            Radius = d.Radius;
            WindupTime = d.WindupTime;
            RecoverTime = d.RecoverTime;
            Cooldown = d.Cooldown;
            Weight = d.Weight;
            Param1 = d.Param1;
            Param2 = d.Param2;

            r.Check(string.IsNullOrEmpty(SkillKey) == false, Table, Id, "SkillKey 가 비었습니다");
            r.Check(WindupTime >= 0f && RecoverTime >= 0f && Cooldown >= 0f, Table, Id, "시간 값은 0 이상");
            if (Slot == SkillSlot.Pattern) r.Check(Weight > 0, Table, Id, "Pattern 스킬은 Weight 1 이상");
        }
    }
}
