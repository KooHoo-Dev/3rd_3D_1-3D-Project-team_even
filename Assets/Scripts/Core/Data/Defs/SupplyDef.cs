using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>공중 보급 물자 (BL-018, BL-019)</summary>
    public sealed class SupplyDef
    {
        public const string Table = "SupplyTable";

        public int Id { get; }
        public SupplyType SupplyType { get; }
        /// <summary>Grenade = 보충 개수, Heal = 회복량</summary>
        public int Amount { get; }
        public int Weight { get; }
        public string PrefabKey { get; }
        /// <summary>낙하 위치 표시(마커) 키 (BL-018 "종류와 낙하 위치를 구분")</summary>
        public string MarkerKey { get; }

        internal SupplyDef(SupplyDto d, DataLoadReport r)
        {
            Id = d.Id;
            SupplyType = ParseUtil.Enum<SupplyType>(d.SupplyType, r, Table, Id, nameof(d.SupplyType));
            Amount = d.Amount;
            Weight = d.Weight;
            PrefabKey = d.PrefabKey;
            MarkerKey = d.MarkerKey;

            r.Check(Amount > 0, Table, Id, "Amount 는 1 이상");
            r.Check(Weight > 0, Table, Id, "Weight 는 1 이상");
        }

        /// <summary>가중치 랜덤으로 보급 종류 선택</summary>
        public static SupplyDef Pick(IReadOnlyList<SupplyDef> all, float roll01)
        {
            int total = 0;
            foreach (var s in all) total += s.Weight;
            if (total <= 0) return null;
            float target = roll01 * total, acc = 0f;
            foreach (var s in all)
            {
                acc += s.Weight;
                if (target < acc) return s;
            }
            return all[all.Count - 1];
        }
    }
}
