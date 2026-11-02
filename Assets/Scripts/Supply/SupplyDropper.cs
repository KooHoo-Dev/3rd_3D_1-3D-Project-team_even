using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Supply
{
    /// <summary>
    /// 보급 투하 담당 (BL-018).
    ///
    ///   1) 종류 선택: SupplyDef.Pick (SupplyTable Weight × 상황 배율 = SupplyDropWeights: 보유량·몬스터 급증·보급 무기 상태)
    ///   2) 낙하 지점: ISpawnZoneProvider.TryGetSupplyDropPosition (기본 = 플레이어 기준 XZ SupplyDropMin~MaxDistance) → 아래로 레이캐스트.
    ///      처음 맞은 콜라이더가 dropGroundLayers(기본 Ground) 가 아니면(건물 지붕·바위·나무 등) 버리고,
    ///      지면이 플레이어 발 높이 + ConfigTable.SupplyMaxTerrainHeight 보다 높으면(언덕·산 위 등) 버리고 다시 뽑는다
    ///      경사가 MaxSlopeAngle 을 넘거나(상자가 묻힘), NavMesh 밖이거나(맵 밖·지붕), 플레이어에서 걸어서 갈 길이 없으면(구덩이·절벽 위) 버린다.
    ///      플레이어 근처에 NavMesh 가 없으면(NavMesh 없는 테스트 씬 등) NavMesh 검사 둘은 건너뛴다
    ///   3) Resources/{PrefabKey} 프리팹 생성 → SupplyCrate.Launch (지면 + SupplyDropHeight 에서 낙하)
    ///   4) SupplyDroppedEvent 발행 (투하음·안내 방송 = GameSoundRouter, 알림 = SupplyHud, 빛기둥 = SupplyBeaconOverride)
    ///
    /// 자동 투하: RoundSequencer 가 RoundDef.SupplyStartTime 에 StartAutoDropCycle(RoundDef.SupplyInterval) 을 호출하면
    /// 바로 한 번, 이후 interval 초마다. 보스 인트로에서 멈추고 보스전에서 한 간격 뒤부터 다시 돈다 (dropImmediately = false).
    /// 투하 시각·간격의 기준은 RoundTable 하나뿐이다.
    /// 지점을 못 찾아 실패하면 그 회차를 버리지 않고 RetryDelay 초 뒤 다시 시도한다 (MaxRetries 번까지).
    /// 필드 위의 보급이 ConfigTable.SupplyMaxActive 개가 되면 수동·자동 모두 투하하지 않는다.
    /// 라운드 정리(RoundCleanupEvent) 시 남아 있는 보급을 전부 제거하고 자동 투하를 멈춘다.
    /// </summary>
    public sealed class SupplyDropper : MonoBehaviour
    {
        /// <summary>낙하 시작 높이 하한 (BL-018). 테이블 값이 이보다 작아도 이 값으로 올린다</summary>
        public const float MinDropHeight = 30f;
        /// <summary>자동 투하가 실패했을 때 다시 시도하기까지의 시간(초)</summary>
        private const float RetryDelay = 1f;
        /// <summary>한 회차에서 다시 시도하는 최대 횟수. 넘으면 그 회차는 건너뛰고 다음 주기를 기다린다</summary>
        private const int MaxRetries = 5;
        /// <summary>투하 1번에서 낙하 지점 후보를 다시 뽑는 횟수 (대부분 레이캐스트에서 걸러지고 길 찾기는 마지막 검사까지 온 후보만이라 가볍다)</summary>
        private const int MaxPositionTries = 30;
        /// <summary>ConfigTable 에 거리 행이 없을 때의 기본값 (테이블 값과 같게 유지)</summary>
        public const float DefaultDropMinDistance = 20f;
        public const float DefaultDropMaxDistance = 60f;
        /// <summary>dropGroundLayers 를 비워 두면 쓰는 레이어 이름</summary>
        private const string DefaultGroundLayerName = "Ground";
        /// <summary>착지 허용 최대 경사(도). 넘으면 상자 한쪽이 지면에 묻혀 조준 레이가 막힌다</summary>
        private const float MaxSlopeAngle = 25f;
        /// <summary>낙하 지점에서 NavMesh 를 찾는 반경(m). 지면 바로 위에 구워진 NavMesh 만 인정</summary>
        private const float NavMeshSnapDistance = 1f;
        /// <summary>플레이어 위치에서 NavMesh 를 찾는 반경(m). 점프·경사로 발이 살짝 떠 있어도 찾도록</summary>
        private const float PlayerNavMeshSnapDistance = 2f;

        [Tooltip("지면 탐색 레이캐스트가 부딪히는 레이어. 지붕·바위 위를 걸러내려면 장애물 레이어도 포함해야 한다 (기본 Everything)")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("착지를 허용하는 레이어. 레이캐스트가 처음 맞은 콜라이더가 이 레이어여야 한다. 비우면 Ground 레이어")]
        [SerializeField] private LayerMask dropGroundLayers;

        private readonly List<SupplyCrate> activeCrates = new List<SupplyCrate>();
        private readonly Dictionary<string, SupplyCrate> prefabCache = new Dictionary<string, SupplyCrate>();
        private bool autoDrop = false;
        private float dropInterval;
        private float nextAutoDropTime = float.PositiveInfinity;
        private int retryCount;
        private NavMeshPath path;
        private SupplyDropWeights weights;

        /// <summary>현재 필드에 남아 있는 보급 목록 (낙하 중 + 착지 후)</summary>
        public IReadOnlyList<SupplyCrate> ActiveCrates => activeCrates;
        /// <summary>필드에 동시에 존재할 수 있는 보급 최대 개수 (ConfigTable.SupplyMaxActive)</summary>
        public int MaxActiveSupplies => Mathf.Max(1, DataManager.Config.GetInt(GameConfig.SupplyMaxActive, 5));
        /// <summary>최대 개수에 도달해 더 이상 투하할 수 없는 상태인지</summary>
        public bool IsAtLimit => activeCrates.Count >= MaxActiveSupplies;
        /// <summary>다음 자동 투하까지 남은 시간(초). 자동 투하가 꺼져 있거나 멈춘 상태면 Infinity</summary>
        public float TimeUntilNextDrop => autoDrop ? Mathf.Max(0f, nextAutoDropTime - Time.time) : float.PositiveInfinity;

        /// <summary>다음 라운드 시작 지급(RoundStartSupply)·상황별 확률(SupplyDropWeights)이 없으면 붙인다. 보급이 있는 씬이면 따로 배치하지 않아도 동작하도록</summary>
        private void Awake()
        {
            if (GetComponent<RoundStartSupply>() == null) gameObject.AddComponent<RoundStartSupply>();
            weights = GetComponent<SupplyDropWeights>();
            if (weights == null) weights = gameObject.AddComponent<SupplyDropWeights>();
        }

        /// <summary>라운드 정리 이벤트 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        /// <summary>라운드 정리 이벤트 구독 해제</summary>
        private void OnDisable()
        {
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        /// <summary>
        /// 자동 투하 타이머 처리. 예약 시각이 되면 최대 개수 미만일 때 랜덤 보급을 투하하고 다음 주기를 예약한다.
        /// 최대 개수면 이번 주기는 건너뛴다. 투하에 실패하면 RetryDelay 뒤 다시 시도 (MaxRetries 번 넘으면 다음 주기).
        /// </summary>
        private void Update()
        {
            if (autoDrop == false || Time.time < nextAutoDropTime) return;

            if (IsAtLimit || DropRandom() != null || retryCount >= MaxRetries)
            {
                retryCount = 0;
                nextAutoDropTime = Time.time + dropInterval;
                return;
            }

            retryCount++;
            nextAutoDropTime = Time.time + RetryDelay;
        }

        /// <summary>
        /// SupplyTable 의 Weight(가중치) × 상황 배율(SupplyDropWeights)로 보급 종류를 랜덤 선택해 투하한다.
        /// 확률 조정용으로 각 상자의 최종 확률을 로그로 남긴다. 테이블이 비었거나 투하에 실패하면 null.
        /// </summary>
        public SupplyCrate DropRandom()
        {
            var all = DataManager.Supplies.All;
            var def = SupplyDef.Pick(all, Random.value, d => weights.WeightOf(d, activeCrates));
            if (def == null)
            {
                Debug.LogError("[SupplyDropper] SupplyTable 이 비어 있습니다.");
                return null;
            }
            Debug.Log($"[SupplyDropper] 투하 확률: {weights.Describe(all, activeCrates)} → {def.DisplayName}", this);
            return Drop(def);
        }

        /// <summary>
        /// 지정한 종류의 보급을 투하한다.
        ///   최대 개수 확인 → 낙하 지점(지면) 탐색 → 프리팹 생성 → 낙하 시작(Launch) → 목록 등록 → SupplyDroppedEvent 발행.
        /// 최대 개수 도달 / 지면 못 찾음 / 프리팹 없음이면 투하하지 않고 null.
        /// </summary>
        public SupplyCrate Drop(SupplyDef def)
        {
            if (def == null) return null;

            if (IsAtLimit)
            {
                Debug.Log($"[SupplyDropper] 필드의 보급이 최대 {MaxActiveSupplies}개라 투하하지 않습니다.");
                return null;
            }

            if (TryFindGroundPoint(out var groundPoint) == false) return null; // 탈락 사유는 TryFindGroundPoint 가 남긴다
            return Spawn(def, groundPoint);
        }

        /// <summary>
        /// 지정한 종류의 보급을 지정한 위치(XZ)에 투하한다 (튜토리얼 등 연출용).
        /// 낙하 지점 탐색(ISpawnZoneProvider·플레이어 거리 범위·NavMesh 검사)을 하지 않고, 그 위치 바로 아래 지면에 떨어뜨린다.
        /// 최대 개수 도달 / 아래에 지면 없음 / 프리팹 없음이면 투하하지 않고 null.
        /// </summary>
        public SupplyCrate Drop(SupplyDef def, Vector3 position)
        {
            if (def == null) return null;

            if (IsAtLimit)
            {
                Debug.Log($"[SupplyDropper] 필드의 보급이 최대 {MaxActiveSupplies}개라 투하하지 않습니다.");
                return null;
            }

            float rayTop = position.y + Mathf.Max(MinDropHeight, DataManager.Config.GetFloat(GameConfig.SupplyDropHeight, 40f)) + 50f;
            var origin = new Vector3(position.x, rayTop, position.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, rayTop - position.y + 200f, groundMask, QueryTriggerInteraction.Ignore) == false)
            {
                Debug.LogWarning($"[SupplyDropper] {position} 아래에 지면이 없어 투하하지 않습니다.", this);
                return null;
            }
            return Spawn(def, hit.point);
        }

        /// <summary>프리팹 생성 → 낙하 시작(Launch) → 목록 등록 → SupplyDroppedEvent 발행. 프리팹이 없으면 null</summary>
        private SupplyCrate Spawn(SupplyDef def, Vector3 groundPoint)
        {
            var prefab = LoadPrefab(def);
            if (prefab == null) return null;

            float dropHeight = Mathf.Max(MinDropHeight, DataManager.Config.GetFloat(GameConfig.SupplyDropHeight, 40f));
            float landingHeight = DataManager.Config.GetFloat(GameConfig.SupplyLandingHeight, 3f);

            var crate = Instantiate(prefab, groundPoint + Vector3.up * dropHeight, Quaternion.identity, transform);
            crate.name = $"{prefab.name}_{def.Id}";
            crate.Launch(def, groundPoint, dropHeight, landingHeight);
            crate.Removed += OnCrateRemoved;
            activeCrates.Add(crate);

            weights.NotifyDropped(def);
            EventBus<SupplyDroppedEvent>.Publish(new SupplyDroppedEvent(def, crate.LandingPosition, crate.gameObject));
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
        /// 위에서 아래로 레이캐스트해 아래 조건을 모두 통과하면 반환한다 (값싼 검사부터, 길 찾기는 마지막):
        ///   처음 맞은 콜라이더가 허용 레이어(dropGroundLayers) / 플레이어 발 높이 + 제한 이하 / 경사 MaxSlopeAngle 이하 /
        ///   NavMesh 위 / 플레이어에서 걸어서 갈 수 있음 (NavMesh 검사 둘은 플레이어 근처에 NavMesh 가 있을 때만)
        /// MaxPositionTries 번 모두 실패하면 탈락 사유별 횟수를 경고로 남기고 false.
        /// </summary>
        private bool TryFindGroundPoint(out Vector3 groundPoint)
        {
            var zones = ServiceLocator.Get<ISpawnZoneProvider>();
            bool hasPlayer = ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null;
            Vector3 center = hasPlayer ? player.Root.position : transform.position;
            float rayTop = center.y + Mathf.Max(MinDropHeight, DataManager.Config.GetFloat(GameConfig.SupplyDropHeight, 40f)) + 50f;
            float minDistance = DataManager.Config.GetFloat(GameConfig.SupplyDropMinDistance, DefaultDropMinDistance);
            float maxDistance = Mathf.Max(minDistance, DataManager.Config.GetFloat(GameConfig.SupplyDropMaxDistance, DefaultDropMaxDistance));
            // 높이 기준 = 플레이어 발 높이 (맵 원점·보스 아레나 높이와 무관하게 "플레이어보다 N m 이상 높은 곳" 을 거른다)
            float maxHeight = center.y + DataManager.Config.GetFloat(GameConfig.SupplyMaxTerrainHeight, 5f);
            int allowedLayers = dropGroundLayers.value != 0 ? dropGroundLayers.value : LayerMask.GetMask(DefaultGroundLayerName);
            float minNormalY = Mathf.Cos(MaxSlopeAngle * Mathf.Deg2Rad);
            // 플레이어가 NavMesh 위에 있어야 "걸어서 갈 수 있는지" 를 따질 수 있다. 없으면 NavMesh 검사를 통째로 건너뛴다
            NavMeshHit playerNav = default;
            bool useNavMesh = hasPlayer && NavMesh.SamplePosition(center, out playerNav, PlayerNavMeshSnapDistance, NavMesh.AllAreas);
            if (useNavMesh && path == null) path = new NavMeshPath();
            int missed = 0, wrongLayer = 0, tooHigh = 0, tooSteep = 0, offNavMesh = 0, unreachable = 0;

            for (int i = 0; i < MaxPositionTries; i++)
            {
                if (zones == null || zones.TryGetSupplyDropPosition(out var candidate) == false) break;

                // 전장 구현(ISpawnZoneProvider)이 바뀌어도 플레이어 기준 XZ 거리 범위는 지키도록 보정
                if (hasPlayer) candidate = ClampToDropRing(center, candidate, minDistance, maxDistance);

                var origin = new Vector3(candidate.x, rayTop, candidate.z);
                if (Physics.Raycast(origin, Vector3.down, out var hit, rayTop - center.y + 200f, groundMask, QueryTriggerInteraction.Ignore) == false)
                {
                    missed++;
                    continue;
                }
                // 처음 맞은 것이 허용 레이어가 아니면 (지붕·바위·나무 위) 버린다. 허용 레이어만 마스크로 쏘면 지붕을 뚫고 건물 안에 떨어진다
                if ((allowedLayers & (1 << hit.collider.gameObject.layer)) == 0)
                {
                    wrongLayer++;
                    continue;
                }
                if (hit.point.y > maxHeight)
                {
                    tooHigh++;
                    continue;
                }
                if (hit.normal.y < minNormalY)
                {
                    tooSteep++;
                    continue;
                }
                if (useNavMesh)
                {
                    // 맵 밖·고립된 지붕 등 NavMesh 가 없는 곳
                    if (NavMesh.SamplePosition(hit.point, out var dropNav, NavMeshSnapDistance, NavMesh.AllAreas) == false)
                    {
                        offNavMesh++;
                        continue;
                    }
                    // 구덩이·절벽 위처럼 NavMesh 는 있지만 플레이어 쪽과 이어지지 않은 곳
                    if (NavMesh.CalculatePath(playerNav.position, dropNav.position, NavMesh.AllAreas, path) == false || path.status != NavMeshPathStatus.PathComplete)
                    {
                        unreachable++;
                        continue;
                    }
                }

                // 착지는 NavMesh 보정 지점이 아니라 보이는 지면(레이캐스트 지점)에
                groundPoint = hit.point;
                return true;
            }

            Debug.LogWarning($"[SupplyDropper] 낙하 지점을 찾지 못했습니다. 탈락 사유: 지면 없음 {missed} / 허용 레이어 아님 {wrongLayer} / 너무 높음 {tooHigh}" +
                             $" / 경사 {tooSteep} / NavMesh 밖 {offNavMesh} / 길 없음 {unreachable}" +
                             $" (플레이어 y {center.y:0.0} + SupplyMaxTerrainHeight → 최대 y {maxHeight:0.0}{(useNavMesh ? string.Empty : " · NavMesh 검사 생략")})" +
                             $"{(zones == null ? " · ISpawnZoneProvider 없음" : string.Empty)}", this);
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

        /// <summary>라운드 정리 시 자동 투하를 멈추고 남은 보급을 전부 제거</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            StopAutoDropCycle();
            ClearAll();
        }

        /// <summary>
        /// 자동 투하 시작 (RoundSequencer 가 RoundDef 값으로 호출). dropImmediately 면 바로 한 번, 아니면 interval 뒤 첫 투하.
        /// 이후 interval 초마다. 이미 돌고 있으면 무시.
        /// </summary>
        public void StartAutoDropCycle(float interval, bool dropImmediately = true)
        {
            if (autoDrop) return;

            dropInterval = Mathf.Max(0.1f, interval);
            autoDrop = true;
            retryCount = 0;
            nextAutoDropTime = dropImmediately ? Time.time : Time.time + dropInterval;
        }

        /// <summary>자동 투하 중지 (보스 인트로·라운드 종료). 필드의 보급은 그대로</summary>
        public void StopAutoDropCycle()
        {
            autoDrop = false;
            nextAutoDropTime = float.PositiveInfinity;
        }
    }
}
