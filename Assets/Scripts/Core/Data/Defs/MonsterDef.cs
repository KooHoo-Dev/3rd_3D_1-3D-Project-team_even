using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 몬스터 정의. 일반 몬스터(E3)와 보스(E5)가 같은 테이블을 쓰고 Role 로 구분한다.
    /// 공격 패턴은 MonsterSkillTable 에서 연결된 Skills 로 접근한다.
    /// </summary>
    public sealed class MonsterDef
    {
        public const string Table = "MonsterTable";

        public int Id { get; }
        public string Name { get; }
        public MonsterRole Role { get; }
        public bool IsBoss => Role == MonsterRole.Boss;
        public int MaxHp { get; }
        public float MoveSpeed { get; }
        public int AttackDamage { get; }
        public float AttackRange { get; }
        public float AttackInterval { get; }
        public float DetectRange { get; }
        /// <summary>처치 시 자동 획득 재화 (BL-029)</summary>
        public int Reward { get; }
        public string PrefabKey { get; }

        /// <summary>Link 단계에서 채워짐. 테이블 순서 유지</summary>
        public IReadOnlyList<MonsterSkillDef> Skills { get; private set; } = Array.Empty<MonsterSkillDef>();
        /// <summary>보스 등장 공격 (없으면 null)</summary>
        public MonsterSkillDef EntranceSkill { get; private set; }
        /// <summary>보스 반복 패턴 목록 (BL-027)</summary>
        public IReadOnlyList<MonsterSkillDef> PatternSkills { get; private set; } = Array.Empty<MonsterSkillDef>();
        public int PatternTotalWeight { get; private set; }

        internal MonsterDef(MonsterDto d, DataLoadReport r)
        {
            Id = d.Id;
            Name = d.Name;
            Role = ParseUtil.Enum<MonsterRole>(d.Role, r, Table, Id, nameof(d.Role));
            MaxHp = d.MaxHp;
            MoveSpeed = d.MoveSpeed;
            AttackDamage = d.AttackDamage;
            AttackRange = d.AttackRange;
            AttackInterval = d.AttackInterval;
            DetectRange = d.DetectRange;
            Reward = d.Reward;
            PrefabKey = d.PrefabKey;

            r.Check(MaxHp > 0, Table, Id, "MaxHp 는 1 이상");
            r.Check(AttackInterval > 0f, Table, Id, "AttackInterval 은 0 초과");
            r.Check(Reward >= 0, Table, Id, "Reward 는 0 이상");
            r.Check(string.IsNullOrEmpty(PrefabKey) == false, Table, Id, "PrefabKey 가 비었습니다");
        }

        internal void SetSkills(List<MonsterSkillDef> skills)
        {
            Skills = skills;
            var patterns = new List<MonsterSkillDef>();
            int weight = 0;
            foreach (var s in skills)
            {
                if (s.Slot == SkillSlot.Entrance) EntranceSkill = s;
                else if (s.Slot == SkillSlot.Pattern) { patterns.Add(s); weight += s.Weight; }
            }
            PatternSkills = patterns;
            PatternTotalWeight = weight;
        }

        /// <summary>SkillKey 로 스킬 찾기 (없으면 null)</summary>
        public MonsterSkillDef FindSkill(string skillKey)
        {
            foreach (var s in Skills)
                if (s.SkillKey == skillKey) return s;
            return null;
        }

        /// <summary>
        /// 다음 보스 패턴을 가중치 랜덤으로 선택. roll01 은 [0,1) 난수 (UnityEngine.Random.value).
        /// exclude 를 넘기면 같은 패턴 연속을 피한다 (패턴이 2개 이상일 때만).
        /// </summary>
        public MonsterSkillDef PickPattern(float roll01, MonsterSkillDef exclude = null)
        {
            if (PatternSkills.Count == 0) return null;
            bool skip = exclude != null && PatternSkills.Count > 1;
            float total = skip ? PatternTotalWeight - exclude.Weight : PatternTotalWeight;
            float target = roll01 * total;
            float acc = 0f;
            foreach (var p in PatternSkills)
            {
                if (skip && p == exclude) continue;
                acc += p.Weight;
                if (target < acc) return p;
            }
            for (int i = PatternSkills.Count - 1; i >= 0; i--)
                if (skip == false || PatternSkills[i] != exclude) return PatternSkills[i];
            return null;
        }
    }
}
