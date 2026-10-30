using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 공중 보급 물자 (BL-018, BL-019).
    /// 종류(SupplyType)별 아이템을 AmountMin~AmountMax 개 지급하고, 모든 보급은 예비 탄창도 함께 충전한다
    /// (충전량은 무기별 WeaponDef.SupplyMagazineRatio).
    /// </summary>
    public sealed class SupplyDef
    {
        public const string Table = "SupplyTable";

        public int Id { get; }
        public SupplyType SupplyType { get; }
        /// <summary>HUD·분석에 보이는 이름 (비우면 SupplyType 이름)</summary>
        public string DisplayName { get; }
        /// <summary>Grenade = 수류탄, Heal = 회복 팩 지급 개수 범위 (획득 때 RollAmount 로 뽑음)</summary>
        public int AmountMin { get; }
        public int AmountMax { get; }
        public int Weight { get; }
        /// <summary>빛기둥 색 (기본 URP Unlit 빛기둥과 안개 무시 빛기둥 모두 이 색을 쓴다)</summary>
        public Color BeaconColor { get; }
        public string PrefabKey { get; }
        /// <summary>낙하 위치 표시(마커) 키 (BL-018 "종류와 낙하 위치를 구분")</summary>
        public string MarkerKey { get; }
        /// <summary>투하 시작에 착지 지점에서 (키가 비었으면 null)</summary>
        public SoundDef DropSfx { get; internal set; }
        /// <summary>투하 안내 방송 (2D, 키가 비었으면 null)</summary>
        public SoundDef AnnounceSfx { get; internal set; }
        /// <summary>획득 (키가 비었으면 null)</summary>
        public SoundDef CollectSfx { get; internal set; }
        internal string DropSfxKey { get; }
        internal string AnnounceSfxKey { get; }
        internal string CollectSfxKey { get; }

        internal SupplyDef(SupplyDto supplyDto, DataLoadReport dataLoadReport)
        {
            Id = supplyDto.Id;
            SupplyType = ParseUtil.Enum<SupplyType>(supplyDto.SupplyType, dataLoadReport, Table, Id, nameof(supplyDto.SupplyType));
            DisplayName = string.IsNullOrEmpty(supplyDto.DisplayName) ? SupplyType.ToString() : supplyDto.DisplayName;
            AmountMin = supplyDto.AmountMin;
            AmountMax = supplyDto.AmountMax;
            Weight = supplyDto.Weight;
            PrefabKey = supplyDto.PrefabKey;
            MarkerKey = supplyDto.MarkerKey;
            DropSfxKey = supplyDto.DropSfxKey;
            AnnounceSfxKey = supplyDto.AnnounceSfxKey;
            CollectSfxKey = supplyDto.CollectSfxKey;

            bool hasColor = ColorUtility.TryParseHtmlString(supplyDto.BeaconColor, out var color);
            BeaconColor = hasColor ? color : Color.white;

            dataLoadReport.Check(AmountMin > 0 && AmountMax >= AmountMin, Table, Id, "AmountMin 은 1 이상, AmountMax 는 AmountMin 이상");
            dataLoadReport.Check(Weight > 0, Table, Id, "Weight 는 1 이상");
            dataLoadReport.Check(hasColor, Table, Id, $"BeaconColor '{supplyDto.BeaconColor}' 는 #RRGGBB 형식이어야 합니다");
        }

        /// <summary>이번 획득의 아이템 지급 개수. roll01 (0~1) → AmountMin ~ AmountMax (양끝 포함)</summary>
        public int RollAmount(float roll01)
        {
            int span = AmountMax - AmountMin + 1;
            return AmountMin + Mathf.Min(span - 1, Mathf.FloorToInt(Mathf.Clamp01(roll01) * span));
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
