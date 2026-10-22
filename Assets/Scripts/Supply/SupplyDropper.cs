using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Supply
{
    /// <summary>
    /// 보급 투하 담당 (BL-018).
    ///
    ///   1) 종류 선택: SupplyDef.Pick (SupplyTable Weight)
    ///   2) 낙하 지점: ISpawnZoneProvider.TryGetSupplyDropPosition (기본 = 플레이어 기준 XZ 30~150m) → 아래로 레이캐스트해 지면 찾기
    ///   3) Resources/{PrefabKey} 프리팹 생성 → SupplyCrate.Launch (지면 + SupplyDropHeight 에서 낙하)
    ///   4) SupplyDroppedEvent 발행 (UI 낙하 위치 표시·사운드)
    ///
    /// 자동 투하: 플레이 시작(또는 RoundStartedEvent) 후 firstDropDelay 초부터 dropInterval 초마다.
    /// 필드 위의 보급이 maxActiveSupplies 개가 되면 수동·자동 모두 투하하지 않는다.
    /// 라운드 정리(RoundCleanupEvent) 시 남아 있는 보급을 전부 제거하고 자동 투하를 멈춘다.
    /// </summary>
    public sealed class SupplyDropper : MonoBehaviour
    {
        /// <summary>낙하 시작 높이 하한 (BL-018). 테이블 값이 이보다 작아도 이 값으로 올린다</summary>
        public const float MinDropHeight = 30f;

        [Tooltip("지면 탐색 레이캐스트 대상 레이어")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("낙하 지점에서 지면을 못 찾았을 때 다시 뽑는 횟수")]
        [SerializeField] private int maxPositionTries = 10;

        
        [Tooltip("플레이 시작 후 첫 투하까지 대기 시간 (초)")]
        [SerializeField, Min(0f)] private float firstDropDelay = 60f;
        [Tooltip("투하 주기 (초)")]
        [SerializeField, Min(0.1f)] private float dropInterval = 25f;
        [Tooltip("필드에 동시에 존재할 수 있는 보급 최대 개수. 이 수에 도달하면 투하하지 않는다")]
        [SerializeField, Min(1)] private int maxActiveSupplies = 10;

        private readonly List<SupplyCrate> activeCrates = new List<SupplyCrate>();
        private readonly Dictionary<string, SupplyCrate> prefabCache = new Dictionary<string, SupplyCrate>();
        private bool autoDrop = true;
        private float nextAutoDropTime = float.PositiveInfinity;
        
        /// <summary>현재 필드에 남아 있는 보급 목록 (낙하 중 + 착지 후)</summary>
        public IReadOnlyList<SupplyCrate> ActiveCrates => activeCrates;
        /// <summary>필드에 동시에 존재할 수 있는 보급 최대 개수</summary>
        public int MaxActiveSupplies => maxActiveSupplies;
        /// <summary>최대 개수에 도달해 더 이상 투하할 수 없는 상태인지</summary>
        public bool IsAtLimit => activeCrates.Count >= maxActiveSupplies;
        /// <summary>다음 자동 투하까지 남은 시간(초). 자동 투하가 꺼져 있거나 멈춘 상태면 Infinity</summary>
        public float TimeUntilNextDrop => autoDrop ? Mathf.Max(0f, nextAutoDropTime - Time.time) : float.PositiveInfinity;

        /// <summary>라운드 시작·정리 이벤트 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        /// <summary>라운드 시작·정리 이벤트 구독 해제</summary>
        private void OnDisable()
        {
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        /// <summary>플레이 시작 시점부터 자동 투하 타이머를 건다 (firstDropDelay 후 첫 투하)</summary>
        private void Start() => RestartAutoDrop();

        /// <summary>
        /// 자동 투하 타이머 처리. 예약 시각이 되면 다음 투하를 dropInterval 뒤로 예약하고,
        /// 최대 개수 미만일 때만 랜덤 보급을 투하한다.
        /// </summary>
        private void Update()
        {
            if (autoDrop == false || Time.time < nextAutoDropTime) return;
            nextAutoDropTime = Time.time + dropInterval;
            // 제한 개수에 도달했으면 이번 주기는 건너뛰고 다음 주기에 다시 시도
            if (IsAtLimit == false) DropRandom();
        }

        /// <summary>자동 투하 타이머 재시작. 지금부터 firstDropDelay 후 첫 투하, 이후 dropInterval 마다</summary>
        public void RestartAutoDrop() => nextAutoDropTime = Time.time + firstDropDelay;

        /// <summary>
        /// SupplyTable 의 Weight(가중치)로 보급 종류(수류탄/회복 팩)를 랜덤 선택해 투하한다.
        /// 테이블이 비었거나 투하에 실패하면 null.
        /// </summary>
        public SupplyCrate DropRandom()
        {
            var def = SupplyDef.Pick(DataManager.Supplies.All, Random.value);
            if (def == null)
            {
                Debug.LogError("[SupplyDropper] SupplyTable 이 비어 있습니다.");
                return null;
            }
            return Drop(def);
        }

        /// <summary>
        /// 지정한 종류의 보급을 투하한다.
        ///   최대 개수 확인 → 낙하 지점(지면) 탐색 → 프리팹 생성 → 낙하 시작(Launch) → 목록 등록 → 사운드·SupplyDroppedEvent 발행.
        /// 최대 개수 도달 / 지면 못 찾음 / 프리팹 없음이면 투하하지 않고 null.
        /// </summary>
        public SupplyCrate Drop(SupplyDef def)
        {
            if (def == null) return null;

            if (IsAtLimit)
            {
                Debug.Log($"[SupplyDropper] 필드의 보급이 최대 {maxActiveSupplies}개라 투하하지 않습니다.");
                return null;
            }

            if (TryFindGroundPoint(out var groundPoint) == false)
            {
                Debug.LogWarning("[SupplyDropper] 낙하 지점에서 지면을 찾지 못했습니다. (Ground 콜라이더 / groundMask 확인)");
                return null;
            }

            var prefab = LoadPrefab(def);
            if (prefab == null) return null;

            float dropHeight = Mathf.Max(MinDropHeight, DataManager.Config.GetFloat(GameConfig.SupplyDropHeight, 40f));
            float landingHeight = DataManager.Config.GetFloat(GameConfig.SupplyLandingHeight, 3f);

            var crate = Instantiate(prefab, groundPoint + Vector3.up * dropHeight, Quaternion.identity, transform);
            crate.name = $"{prefab.name}_{def.Id}";
            crate.Launch(def, groundPoint, dropHeight, landingHeight);
            crate.Removed += OnCrateRemoved;
            activeCrates.Add(crate);

            ServiceLocator.Get<ISoundService>()?.PlaySfx("sfx_supply_drop", crate.LandingPosition);
            EventBus<SupplyDroppedEvent>.Publish(new SupplyDroppedEvent(def, crate.LandingPosition));
            return crate;
        }

        /// <summary>필드에 남은 보급을 전부 제거 (라운드 정리 등)</summary>
        public void ClearAll()
        {
            // Remove 콜백에서 리스트를 수정하므로 뒤에서부터
            for (int i = activeCrates.Count - 1; i >= 0; i--)
                if (activeCrates[i] != null) activeCrates[i].Remove();
            activeCrates.Clear();
        }

        /// <summary>
        /// 보급이 착지할 지면 지점을 찾는다.
        /// ISpawnZoneProvider 에서 후보 XZ 위치를 받아 플레이어 기준 거리 범위(SupplyDropMin~MaxDistance)로 보정한 뒤,
        /// 위에서 아래로 레이캐스트해 지면에 닿으면 그 지점을 반환한다. maxPositionTries 번 모두 실패하면 false.
        /// </summary>
        private bool TryFindGroundPoint(out Vector3 groundPoint)
        {
            var zones = ServiceLocator.Get<ISpawnZoneProvider>();
            bool hasPlayer = ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null;
            Vector3 center = hasPlayer ? player.Root.position : transform.position;
            float rayTop = center.y + Mathf.Max(MinDropHeight, DataManager.Config.GetFloat(GameConfig.SupplyDropHeight, 40f)) + 50f;
            float minDistance = DataManager.Config.GetFloat(GameConfig.SupplyDropMinDistance, 30f);
            float maxDistance = Mathf.Max(minDistance, DataManager.Config.GetFloat(GameConfig.SupplyDropMaxDistance, 150f));

            for (int i = 0; i < Mathf.Max(1, maxPositionTries); i++)
            {
                if (zones == null || zones.TryGetSupplyDropPosition(out var candidate) == false) break;

                // 전장 구현(ISpawnZoneProvider)이 바뀌어도 플레이어 기준 XZ 거리 범위는 지키도록 보정
                if (hasPlayer) candidate = ClampToDropRing(center, candidate, minDistance, maxDistance);

                var origin = new Vector3(candidate.x, rayTop, candidate.z);
                if (Physics.Raycast(origin, Vector3.down, out var hit, rayTop - center.y + 200f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    groundPoint = hit.point;
                    return true;
                }
            }

            groundPoint = default;
            return false;
        }

        /// <summary>
        /// 후보 위치의 방향은 유지하고, 중심(플레이어)과의 XZ 거리만 minDistance ~ maxDistance 로 맞춘다.
        /// 후보가 중심과 겹치면 정면(+Z) 방향으로 보낸다. y 는 후보 값 그대로.
        /// </summary>
        private static Vector3 ClampToDropRing(Vector3 center, Vector3 candidate, float minDistance, float maxDistance)
        {
            Vector3 offset = candidate - center;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < 0.001f) offset = Vector3.forward;
            float clamped = Mathf.Clamp(distance, minDistance, maxDistance);
            Vector3 result = center + offset.normalized * clamped;
            result.y = candidate.y;
            return result;
        }

        /// <summary>
        /// Resources/{PrefabKey} 에서 SupplyCrate 프리팹을 불러온다. 한 번 불러온 결과(없음 포함)는 캐시해서 재사용.
        /// 프리팹이 없거나 SupplyCrate 컴포넌트가 없으면 에러 로그 후 null.
        /// </summary>
        private SupplyCrate LoadPrefab(SupplyDef def)
        {
            if (prefabCache.TryGetValue(def.PrefabKey, out var prefab)) return prefab;

            var go = Resources.Load<GameObject>(def.PrefabKey);
            prefab = go != null ? go.GetComponent<SupplyCrate>() : null;
            if (prefab == null)
                Debug.LogError($"[SupplyDropper] Resources/{def.PrefabKey} 에 SupplyCrate 프리팹이 없습니다. (Supply {def.Id})");
            prefabCache[def.PrefabKey] = prefab;
            return prefab;
        }

        /// <summary>보급이 획득·정리로 사라졌을 때 목록에서 빼서 최대 개수 계산에서 제외</summary>
        private void OnCrateRemoved(SupplyCrate crate)
        {
            crate.Removed -= OnCrateRemoved;
            activeCrates.Remove(crate);
        }

        /// <summary>라운드 시작 시 자동 투하 타이머를 처음부터 다시 건다</summary>
        private void OnRoundStarted(RoundStartedEvent e) => RestartAutoDrop();

        /// <summary>라운드 정리 시 자동 투하를 멈추고 남은 보급을 전부 제거</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            nextAutoDropTime = float.PositiveInfinity;
            ClearAll();
        }
        
        public void StartAutoDropCycle()
        {
            if (autoDrop) return;

            autoDrop = true;
            nextAutoDropTime = Time.time;
        }
        
        public void StopAutoDropCycle()
        {
            autoDrop = false;
            nextAutoDropTime = float.PositiveInfinity;
        }
    }
}
