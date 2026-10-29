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
        /// <summary>Grenade = 수류탄 보충 개수, Heal = 회복 팩 보충 개수</summary>
        public int Amount { get; }
        public int Weight { get; }
        public string PrefabKey { get; }
        /// <summary>낙하 위치 표시(마커) 키 (BL-018 "종류와 낙하 위치를 구분")</summary>
        public string MarkerKey { get; }
        /// <summary>투하 시작에 착지 지점에서 (키가 비었으면 null)</summary>
        public SoundDef DropSfx { get; internal set; }
        /// <summary>획득 (키가 비었으면 null)</summary>
        public SoundDef CollectSfx { get; internal set; }
        internal string DropSfxKey { get; }
        internal string CollectSfxKey { get; }

        internal SupplyDef(SupplyDto supplyDto, DataLoadReport dataLoadReport)
        {
            Id = supplyDto.Id;
            SupplyType = ParseUtil.Enum<SupplyType>(supplyDto.SupplyType, dataLoadReport, Table, Id, nameof(supplyDto.SupplyType));
            Amount = supplyDto.Amount;
            Weight = supplyDto.Weight;
            PrefabKey = supplyDto.PrefabKey;
            MarkerKey = supplyDto.MarkerKey;
            DropSfxKey = supplyDto.DropSfxKey;
            CollectSfxKey = supplyDto.CollectSfxKey;

            dataLoadReport.Check(Amount > 0, Table, Id, "Amount 는 1 이상");
            dataLoadReport.Check(Weight > 0, Table, Id, "Weight 는 1 이상");
        }

        /// <summary>가중치 랜덤으로 보급 종류 선택</summary>
        public static SupplyDef Pick(IReadOnlyList<SupplyDef> all, float roll01)
        {
            int total = 0;
            foreach (SupplyDef supplyDef in all) total += supplyDef.Weight;
            if (total <= 0) return null;
            float target = roll01 * total, acc = 0f;
            foreach (SupplyDef supplyDef in all)
            {
                acc += supplyDef.Weight;
                if (target < acc) return supplyDef;
            }
            return all[all.Count - 1];
        }
    }
}
