using System.Collections.Generic;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Core.Battlefield
{
    /// <summary>
    /// 플레이어 근처에 몬스터를 스폰해도 되는 평탄한 위치를 찾는다.
    /// 먼저 ZonePlacement 로 맵 경계 안에서 플레이어에 가장 가까운 원형 구역을 정하고,
    /// 그 구역 안에서 몬스터 Collider 크기의 사각형(XZ, 회전 무시) 안 지형 고저차가 허용치 이하인 곳 중 플레이어에서 가장 먼 곳을 고른다.
    /// 평탄해도 obstacleLayers 의 장애물 Collider(기둥·건물·나무 등)와 겹치는 위치는 제외한다.
    /// 스폰 직후 경사나 장애물 때문에 몬스터가 밀리거나 상태머신이 꼬이는 것을 막는 용도다. 몬스터 생성은 호출자가 한다.
    /// 일반 몬스터 스폰(RingSpawnZoneProvider)도 같은 맵 경계·장애물 값을 쓰도록 켜질 때 ServiceLocator 에 자기를 등록한다.
    /// </summary>
    public class SpawnPointFinder : MonoBehaviour
    {
        [Header("지형")]
        [Tooltip("높이를 잴 지형")]
        [SerializeField] private Terrain terrain;

        [Header("맵 경계·구역")]
        [Tooltip("원형 맵 경계의 중심")]
        [SerializeField] private Transform mapCenter;
        [Tooltip("원형 맵 경계 반지름 (m)")]
        [SerializeField] private float mapRadius = 50f;
        [Tooltip("스폰 위치를 찾을 원형 구역 반지름 (m). 맵 반지름보다 크면 예외")]
        [SerializeField] private float zoneRadius = 15f;

        [Header("판정 기준")]
        [Tooltip("몬스터 사각형 안 지형 높이의 (최대 - 최소) 허용치 (m)")]
        [SerializeField] private float maxHeightDifference = 0.5f;
        [Tooltip("구역 안 후보 위치 격자 간격 (m)")]
        [SerializeField] private float candidateSpacing = 1f;
        [Tooltip("몬스터 사각형 안 높이 측정 격자 간격 (m)")]
        [SerializeField] private float heightSampleSpacing = 0.5f;

        [Header("장애물")]
        [Tooltip("스폰 위치를 피할 장애물 레이어 (Pillars·Buildings·Trees 등). Terrain 레이어는 넣지 말 것: 지형 Collider 에 항상 걸린다. 0 = 장애물 판정 안 함")]
        [SerializeField] private LayerMask obstacleLayers;
        [Tooltip("몬스터 사각형 둘레에 더 확보할 여유 (m)")]
        [SerializeField] private float obstacleMargin = 0.5f;
        [Tooltip("검사 상자 바닥을 지형 표면보다 띄우는 높이 (m). 바닥에 걸친 얕은 Collider 에 걸리지 않게 한다")]
        [SerializeField] private float obstacleGroundClearance = 0.1f;

        [Header("일반 몬스터 스폰")]
        [Tooltip("Nest 묶음 스폰에서 둥지 중심 주위로 흩뿌리는 원 반지름 (m)")]
        [SerializeField] private float nestRadius = 6f;
        [Tooltip("링 위 점에서 NavMesh 를 찾는 반경 (m). 못 찾으면 그 점은 스폰 불가")]
        [SerializeField] private float navMeshSampleDistance = 5f;
        [Tooltip("일반 몬스터 장애물 검사 사각형 한 변 (m). 몬스터 Collider 대신 쓰는 고정 크기")]
        [SerializeField] private float groundMonsterFootprint = 1.5f;
        [Tooltip("일반 몬스터 장애물 검사 상자 높이 (m)")]
        [SerializeField] private float groundMonsterHeight = 2f;

        // ── 테스트 도구가 구역을 그릴 때 읽는 값 ──
        public Vector3 MapCenter => mapCenter.position;
        public float MapRadius => mapRadius;
        public float ZoneRadius => zoneRadius;

        // ── 일반 몬스터 스폰이 읽는 값 ──
        public float NestRadius => nestRadius;

        private void OnEnable() => ServiceLocator.Register(this);

        private void OnDisable() => ServiceLocator.Unregister(this);

        /// <summary>
        /// 점이 원형 맵 경계 안인지 본다. 높이 차이는 무시한다. 공중 몬스터 스폰처럼 지면 판정이 필요 없을 때 RingSpawnZoneProvider 가 부른다.
        /// </summary>
        /// <param name="point">검사할 월드 위치 (Y는 무시)</param>
        /// <returns>mapCenter 와의 수평(XZ) 거리가 mapRadius 이하면 true</returns>
        public bool IsInsideMap(Vector3 point)
        {
            Vector2 flatPoint = new Vector2(point.x, point.z);
            Vector2 flatMapCenter = new Vector2(mapCenter.position.x, mapCenter.position.z);
            return Vector2.Distance(flatPoint, flatMapCenter) <= mapRadius;
        }

        /// <summary>
        /// 링 포인트를 일반 지상 몬스터가 설 수 있는 점으로 확정한다: 가까운 NavMesh 점으로 옮긴 뒤 맵 경계 안이고 장애물과 겹치지 않는지 본다.
        /// RingSpawnZoneProvider 가 링 포인트·둥지 중심·둥지 주위 점을 받아들이기 전에 부른다.
        /// </summary>
        /// <param name="point">링·둥지에서 뽑은 후보 점</param>
        /// <param name="groundPoint">NavMesh 위로 옮긴 점. 실패하면 후보 점 그대로</param>
        /// <returns>NavMesh 를 찾았고 맵 경계 안이며 장애물이 없으면 true</returns>
        /// <remarks>장애물 상자 높이는 Terrain 표면 기준이라 terrain 이 연결돼 있어야 한다.</remarks>
        public bool TryGetGroundSpawnPoint(Vector3 point, out Vector3 groundPoint)
        {
            groundPoint = point;

            bool isNearNavMesh = NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas);
            if (isNearNavMesh == false) return false;

            groundPoint = hit.position;
            Vector2 footprintSize = Vector2.one * groundMonsterFootprint;
            if (IsInsideMap(groundPoint) == false) return false;
            if (IsFreeOfObstacles(groundPoint, footprintSize, groundMonsterHeight) == false) return false;

            return true;
        }

        /// <summary>
        /// 맵 경계 안에서 플레이어에 가장 가까운 구역을 정하고, 그 구역 안에서 몬스터 사각형이 평탄하고 장애물과 겹치지 않는 위치 중 플레이어에서 가장 먼 위치를 찾는다.
        /// 스폰 담당(호출자 미정)이 몬스터를 놓기 전에 부른다.
        /// </summary>
        /// <param name="playerPosition">플레이어 위치 (Y는 무시)</param>
        /// <param name="monsterCollider">스폰할 몬스터의 Collider. 씬에 있는 활성 인스턴스여야 bounds 크기가 나온다</param>
        /// <param name="spawnPoint">찾은 위치. Y = 그 위치의 지형 높이. 못 찾으면 Vector3.zero</param>
        /// <returns>적합한 위치를 찾았으면 true</returns>
        /// <exception cref="System.ArgumentException">zoneRadius 가 mapRadius 보다 클 때 (ZonePlacement 가 던짐)</exception>
        /// <remarks>장애물은 obstacleLayers 에 지정한 Collider 만 본다. 호출 전에 움직인 Collider 는 Physics.SyncTransforms 가 반영돼 있어야 한다.</remarks>
        public bool TryFindSpawnPoint(Vector3 playerPosition, Collider monsterCollider, out Vector3 spawnPoint)
        {
            Vector3 zoneCenter = ZonePlacement.FindClosestCenter(playerPosition, mapCenter.position, mapRadius, zoneRadius);
            Vector2 footprintSize = new Vector2(monsterCollider.bounds.size.x, monsterCollider.bounds.size.z);
            float monsterHeight = monsterCollider.bounds.size.y;
            Vector3 flatPlayerPosition = new Vector3(playerPosition.x, 0f, playerPosition.z);

            List<Vector3> candidates = BuildCandidates(zoneCenter, zoneRadius, footprintSize);
            candidates.Sort((first, second) =>
                Vector3.Distance(second, flatPlayerPosition).CompareTo(Vector3.Distance(first, flatPlayerPosition)));

            foreach (Vector3 candidate in candidates)
            {
                if (IsFlatEnough(candidate, footprintSize) == false) continue;
                if (IsFreeOfObstacles(candidate, footprintSize, monsterHeight) == false) continue;

                spawnPoint = new Vector3(candidate.x, HeightAt(candidate), candidate.z);
                return true;
            }

            spawnPoint = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 구역 중심 기준 candidateSpacing 격자점 중, 몬스터 사각형의 네 모서리가 모두 원 안에 드는 점을 모은다.
        /// </summary>
        /// <param name="zoneCenter">원형 구역 중심</param>
        /// <param name="zoneRadius">원형 구역 반지름 (m)</param>
        /// <param name="footprintSize">몬스터 사각형 크기 (x = 가로, y = 세로(Z), m)</param>
        /// <returns>후보 위치 목록 (Y = 0). 없으면 빈 목록</returns>
        private List<Vector3> BuildCandidates(Vector3 zoneCenter, float zoneRadius, Vector2 footprintSize)
        {
            List<Vector3> candidates = new List<Vector3>();
            Vector2 halfSize = footprintSize * 0.5f;
            int stepCount = Mathf.FloorToInt(zoneRadius / candidateSpacing);

            for (int xStep = -stepCount; xStep <= stepCount; xStep++)
            {
                for (int zStep = -stepCount; zStep <= stepCount; zStep++)
                {
                    Vector2 offset = new Vector2(xStep, zStep) * candidateSpacing;
                    Vector2 farthestCornerOffset = new Vector2(Mathf.Abs(offset.x) + halfSize.x, Mathf.Abs(offset.y) + halfSize.y);
                    bool isFootprintInsideZone = farthestCornerOffset.magnitude <= zoneRadius;
                    if (isFootprintInsideZone == false) continue;

                    candidates.Add(new Vector3(zoneCenter.x + offset.x, 0f, zoneCenter.z + offset.y));
                }
            }

            return candidates;
        }

        /// <summary>
        /// 사각형 안을 heightSampleSpacing 격자(테두리·모서리 포함)로 재서 지형 높이의 최대 - 최소가 허용치 이하인지 본다.
        /// </summary>
        /// <param name="center">사각형 중심 (Y는 무시)</param>
        /// <param name="footprintSize">사각형 크기 (x = 가로, y = 세로(Z), m)</param>
        /// <returns>고저차가 maxHeightDifference 이하면 true</returns>
        private bool IsFlatEnough(Vector3 center, Vector2 footprintSize)
        {
            int xSampleCount = Mathf.CeilToInt(footprintSize.x / heightSampleSpacing);
            int zSampleCount = Mathf.CeilToInt(footprintSize.y / heightSampleSpacing);
            Vector3 minimumCorner = center - new Vector3(footprintSize.x, 0f, footprintSize.y) * 0.5f;

            float lowestHeight = float.PositiveInfinity;
            float highestHeight = float.NegativeInfinity;

            for (int xIndex = 0; xIndex <= xSampleCount; xIndex++)
            {
                for (int zIndex = 0; zIndex <= zSampleCount; zIndex++)
                {
                    float xRatio = xSampleCount == 0 ? 0.5f : (float)xIndex / xSampleCount;
                    float zRatio = zSampleCount == 0 ? 0.5f : (float)zIndex / zSampleCount;
                    Vector3 samplePoint = minimumCorner + new Vector3(footprintSize.x * xRatio, 0f, footprintSize.y * zRatio);

                    float height = HeightAt(samplePoint);
                    lowestHeight = Mathf.Min(lowestHeight, height);
                    highestHeight = Mathf.Max(highestHeight, height);
                }
            }

            return highestHeight - lowestHeight <= maxHeightDifference;
        }

        /// <summary>
        /// 몬스터 사각형(+ obstacleMargin)과 몬스터 높이만큼의 상자가 obstacleLayers Collider 와 겹치는지 본다.
        /// 상자 바닥은 후보 위치 지형 표면보다 obstacleGroundClearance 만큼 위다.
        /// </summary>
        /// <param name="center">사각형 중심 (Y는 무시, 지형 높이로 대체)</param>
        /// <param name="footprintSize">사각형 크기 (x = 가로, y = 세로(Z), m)</param>
        /// <param name="monsterHeight">몬스터 Collider 높이 (m)</param>
        /// <returns>겹치는 장애물이 없으면 true</returns>
        private bool IsFreeOfObstacles(Vector3 center, Vector2 footprintSize, float monsterHeight)
        {
            float boxHeight = monsterHeight - obstacleGroundClearance;
            Vector3 halfExtents = new Vector3(
                footprintSize.x * 0.5f + obstacleMargin,
                boxHeight * 0.5f,
                footprintSize.y * 0.5f + obstacleMargin);
            float boxCenterHeight = HeightAt(center) + obstacleGroundClearance + boxHeight * 0.5f;
            Vector3 boxCenter = new Vector3(center.x, boxCenterHeight, center.z);

            bool isOverlapping = Physics.CheckBox(boxCenter, halfExtents, Quaternion.identity, obstacleLayers, QueryTriggerInteraction.Ignore);
            return isOverlapping == false;
        }

        /// <summary>주어진 XZ에서 지형 표면의 월드 높이를 잰다</summary>
        /// <param name="position">월드 위치 (Y는 무시)</param>
        /// <returns>지형 표면의 월드 Y (m)</returns>
        private float HeightAt(Vector3 position)
        {
            return terrain.SampleHeight(position) + terrain.transform.position.y;
        }
    }
}
