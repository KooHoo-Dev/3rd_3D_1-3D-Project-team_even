using System;
using System.Collections.Generic;
using System.Globalization;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// ConfigTable(Key-Value) 전역 상수. 테이블 하나 만들기엔 애매한 값들을 둔다.
    /// 사용: DataManager.Config.GetFloat(GameConfig.SpawnSafeRadius)
    /// 키는 아래 상수로만 접근해서 오타를 막는다. (키 추가 시 ConfigTable.tsv 와 함께)
    /// </summary>
    public sealed class GameConfig
    {
        public const string Table = "ConfigTable";

        // ── 키 ──
        public const string SpawnSafeRadius = "SpawnSafeRadius";             // BL-015
        public const string SpawnRingRadius = "SpawnRingRadius";
        public const string CrowdRadius = "CrowdRadius";                     // BL-016 근접 과밀 판정 반경
        public const string CrowdMaxMelee = "CrowdMaxMelee";                 // BL-016 과밀 판정 근접 몬스터 수
        public const string StationaryRadius = "StationaryRadius";           // 정지 판정 반경 (독안개 트리거)
        public const string BossCameraFovAdd = "BossCameraFovAdd";           // 보스전 3인칭 FOV 추가(도)
        public const string PlayerInvincibleTime = "PlayerInvincibleTime";
        public const string LowHpRatio = "LowHpRatio";                       // BL-007
        public const string MonsterLowHpRatio = "MonsterLowHpRatio";         // BL-017
        public const string SupplyDropMinDistance = "SupplyDropMinDistance"; // BL-018 플레이어 기준 최소 낙하 거리(XZ)
        public const string SupplyDropMaxDistance = "SupplyDropMaxDistance"; // BL-018 플레이어 기준 최대 낙하 거리(XZ)
        public const string SupplyDropHeight = "SupplyDropHeight";           // BL-018 낙하 시작 높이(지면 기준)
        public const string SupplyLandingHeight = "SupplyLandingHeight";     // BL-018 감속 착지 시작 높이(지면 기준)
        public const string SupplyMaxTerrainHeight = "SupplyMaxTerrainHeight"; // 플레이어 발 높이 + 이 값 이하의 지면에만 낙하 (넘으면 다시 뽑음)
        public const string SupplyMaxActive = "SupplyMaxActive";               // 필드 동시 보급 최대 개수
        public const string SupplyLifetime = "SupplyLifetime";                 // 착지 후 이 시간(초)이 지나면 사라짐 (0 = 사라지지 않음)
        // 상황별 보급 확률 배율 (SupplyDropWeights): 최종 가중치 = SupplyTable.Weight × 배율
        public const string SupplyNeedWeightMin = "SupplyNeedWeightMin";       // 수류탄·회복 팩이 가득일 때 그 상자 배율
        public const string SupplyNeedWeightMax = "SupplyNeedWeightMax";       // 〃 0개일 때 배율 (사이는 보유 비율로 선형)
        public const string SupplyWeaponSurgeMultiplier = "SupplyWeaponSurgeMultiplier";         // 몬스터 급증 중 무기 상자 배율
        public const string SupplyWeaponDepletedMultiplier = "SupplyWeaponDepletedMultiplier";   // 보급 무기를 다 쓴 뒤(다음 무기 상자 투하까지) 무기 상자 배율
        public const string SupplyWeaponHeldMultiplier = "SupplyWeaponHeldMultiplier";           // 보급 무기를 들고 있을 때 무기 상자 배율
        public const string SupplyWeaponMultiplierCap = "SupplyWeaponMultiplierCap";             // 무기 상자 배율 상한
        public const string SupplySurgeSpawnCount = "SupplySurgeSpawnCount";   // 최근 SupplySurgeWindow 초 소환 수가 이 이상이면 몬스터 급증
        public const string SupplySurgeWindow = "SupplySurgeWindow";           // 급증 판정 구간(초)
        public const string SupplySurgeDuration = "SupplySurgeDuration";       // 급증 판정 후 유지 시간(초)
        public const string RoundStartGrenades = "RoundStartGrenades";         // 보스 클리어 후 다음 라운드 시작 지급: 수류탄
        public const string RoundStartHealPacks = "RoundStartHealPacks";       // 〃 회복 팩
        public const string RoundStartMagazines = "RoundStartMagazines";       // 〃 1~3번 칸 모든 총 예비 탄창
        public const string CameraTransitionTime = "CameraTransitionTime";   // BL-022

        private readonly Dictionary<string, string> values = new Dictionary<string, string>();

        internal GameConfig() { }

        internal void Add(ConfigDto d, DataLoadReport r)
        {
            if (string.IsNullOrEmpty(d.Key)) { r.Error(Table, "-", "Key 가 비었습니다"); return; }
            if (values.ContainsKey(d.Key)) { r.Error(Table, d.Key, "Key 중복"); return; }
            values.Add(d.Key, d.Value);
        }

        public bool Has(string key) => values.ContainsKey(key);

        public string GetString(string key, string fallback = "")
            => values.TryGetValue(key, out var raw) ? raw : Fail(key, fallback);

        public int GetInt(string key, int fallback = 0)
            => values.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : Fail(key, fallback);

        public float GetFloat(string key, float fallback = 0f)
            => values.TryGetValue(key, out var raw) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : Fail(key, fallback);

        public bool GetBool(string key, bool fallback = false)
            => values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var v) ? v : Fail(key, fallback);

        /// <summary>"0.7;0.4;0.15" → [0.7, 0.4, 0.15]</summary>
        public float[] GetFloatList(string key)
        {
            if (values.TryGetValue(key, out var raw) == false) return Fail(key, Array.Empty<float>());
            var parts = raw.Split(ParseUtil.ListSeparator);
            var list = new List<float>(parts.Length);
            foreach (var p in parts)
                if (float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) list.Add(v);
            return list.ToArray();
        }

        private static T Fail<T>(string key, T fallback)
        {
            Debug.LogWarning($"[GameConfig] '{key}' 가 없거나 형식이 맞지 않아 기본값 사용");
            return fallback;
        }
    }
}
