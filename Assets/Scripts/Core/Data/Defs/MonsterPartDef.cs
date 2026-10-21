using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>약점 · 파괴 가능 부위. 명중 피해 = 무기 Damage × DamageMultiplier</summary>
    public sealed class MonsterPartDef
    {
        public const string Table = "MonsterPartTable";

        public int Id { get; }
        public int MonsterId { get; }
        public string PartKey { get; }
        public string Name { get; }
        public float DamageMultiplier { get; }
        public PartExposure Exposure { get; }
        /// <summary>0 = 파괴 불가</summary>
        public int PartHp { get; }
        public bool IsDestructible => PartHp > 0;
        /// <summary>파괴 시 약화되는 공격 Id (0 = 없음)</summary>
        public int LinkedAttackId { get; }
        /// <summary>파괴 시 전환될 몬스터 Id (0 = 없음). 남은 체력은 유지</summary>
        public int TransformToMonsterId { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public MonsterDef Owner { get; internal set; }
        public MonsterAttackDef LinkedAttack { get; internal set; }
        public MonsterDef TransformTo { get; internal set; }

        internal MonsterPartDef(MonsterPartDto d, DataLoadReport r)
        {
            Id = d.Id;
            MonsterId = d.MonsterId;
            PartKey = d.PartKey;
            Name = d.Name;
            DamageMultiplier = d.DamageMultiplier;
            Exposure = ParseUtil.Enum<PartExposure>(d.Exposure, r, Table, Id, nameof(d.Exposure));
            PartHp = d.PartHp;
            LinkedAttackId = d.LinkedAttackId;
            TransformToMonsterId = d.TransformToMonsterId;

            r.Check(string.IsNullOrEmpty(PartKey) == false, Table, Id, "PartKey 가 비었습니다");
            r.Check(DamageMultiplier > 0f, Table, Id, "DamageMultiplier 는 0 초과");
            r.Check(PartHp >= 0, Table, Id, "PartHp 는 0 이상");
            if (IsDestructible == false)
                r.Check(LinkedAttackId == 0 && TransformToMonsterId == 0, Table, Id, "파괴 불가 부위(PartHp 0)는 LinkedAttackId/TransformToMonsterId 를 쓸 수 없습니다");
        }
    }
}
