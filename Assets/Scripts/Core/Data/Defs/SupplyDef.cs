using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 공중 보급 물자 (BL-018, BL-019).
    /// 종류(SupplyType)별 아이템을 AmountMin~AmountMax 개 지급하고, 아이템 보급은 예비 탄창도 함께 충전한다
    /// (충전량은 무기별 WeaponDef.SupplyMagazineRatio). Weapon 은 WeaponId 의 1회용 보급 무기 + 예비 탄창 (보급 무기 자신에는 충전 안 함).
    /// </summary>
    public sealed class SupplyDef
    {
        public const string Table = "SupplyTable";

        public int Id { get; }
        public SupplyType SupplyType { get; }
        /// <summary>HUD·분석에 보이는 이름 (비우면 SupplyType 이름)</summary>
        public string DisplayName { get; }
        /// <summary>짧은 활용 설명 (보급 무기 획득 카드에 표시). 비어 있으면 표시하지 않음</summary>
        public string Description { get; }
        /// <summary>상자 안내(프롬프트) 제목. 테이블이 비었으면 "{DisplayName} 보급 상자"</summary>
        public string CrateName { get; }
        /// <summary>상자 안내(프롬프트) 설명 한 줄. 비어 있으면 표시하지 않음</summary>
        public string CrateDescription { get; }
        /// <summary>Grenade = 수류탄, Heal = 회복 팩 지급 개수 범위 (획득 때 RollAmount 로 뽑음)</summary>
        public int AmountMin { get; }
        public int AmountMax { get; }
        /// <summary>Weapon = 지급할 보급 무기 WeaponTable.Id (Category = Supply). 그 외 종류는 0</summary>
        public int WeaponId { get; }
        public bool IsWeapon => SupplyType == SupplyType.Weapon;
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
            Description = supplyDto.Description ?? "";
            CrateName = string.IsNullOrEmpty(supplyDto.CrateName) ? $"{DisplayName} 보급 상자" : supplyDto.CrateName;
            CrateDescription = supplyDto.CrateDescription ?? "";
            AmountMin = supplyDto.AmountMin;
            AmountMax = supplyDto.AmountMax;
            WeaponId = supplyDto.WeaponId;
            Weight = supplyDto.Weight;
            PrefabKey = supplyDto.PrefabKey;
            MarkerKey = supplyDto.MarkerKey;
            DropSfxKey = supplyDto.DropSfxKey;
            AnnounceSfxKey = supplyDto.AnnounceSfxKey;
            CollectSfxKey = supplyDto.CollectSfxKey;

            bool hasColor = ColorUtility.TryParseHtmlString(supplyDto.BeaconColor, out var color);
            BeaconColor = hasColor ? color : Color.white;

            // 아이템 개수는 Grenade·Heal 만. Weapon 은 WeaponId (WeaponTable 연결 검사는 DataManager 링크 단계)
            if (IsWeapon) dataLoadReport.Check(WeaponId > 0, Table, Id, "Weapon 행은 WeaponId 가 필요합니다");
            else dataLoadReport.Check(AmountMin > 0 && AmountMax >= AmountMin, Table, Id, "AmountMin 은 1 이상, AmountMax 는 AmountMin 이상");
            dataLoadReport.Check(Weight > 0, Table, Id, "Weight 는 1 이상");
            dataLoadReport.Check(hasColor, Table, Id, $"BeaconColor '{supplyDto.BeaconColor}' 는 #RRGGBB 형식이어야 합니다");
        }

        /// <summary>이번 획득의 아이템 지급 개수. roll01 (0~1) → AmountMin ~ AmountMax (양끝 포함)</summary>
        public int RollAmount(float roll01)
        {
            int span = AmountMax - AmountMin + 1;
            return AmountMin + Mathf.Min(span - 1, Mathf.FloorToInt(Mathf.Clamp01(roll01) * span));
        }

        /// <summary>
        /// 상황 배율을 곱한 가중치로 보급 종류 선택. weightOf(def) = 최종 가중치 (기본 Weight × 배율, 0 이하 = 뽑히지 않음).
        /// 전부 0 이하면 기본 Weight 로 뽑는다
        /// </summary>
        public static SupplyDef Pick(IReadOnlyList<SupplyDef> all, float roll01, Func<SupplyDef, float> weightOf)
        {
            float total = 0f;
            foreach (SupplyDef supplyDef in all) total += Mathf.Max(0f, weightOf(supplyDef));
            if (total <= 0f) return Pick(all, roll01);
            float target = roll01 * total, acc = 0f;
            SupplyDef last = null;
            foreach (SupplyDef supplyDef in all)
            {
                float weight = Mathf.Max(0f, weightOf(supplyDef));
                if (weight <= 0f) continue;
                acc += weight;
                last = supplyDef;
                if (target < acc) return supplyDef;
            }
            return last;
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
