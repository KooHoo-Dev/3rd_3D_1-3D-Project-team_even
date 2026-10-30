using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Core.Battlefield
{
    /// <summary>
    /// 플레이어 근처에 몬스터를 스폰해도 되는 평탄한 위치를 찾는다.
    /// 먼저 ZonePlacement 로 맵 경계 안에서 플레이어에 가장 가까운 원형 구역을 정하고,
    /// 그 구역 안에서 몬스터 Collider 크기의 사각형(XZ, 회전 무시) 안 지형 고저차가 허용치 이하인 곳 중 플레이어에서 가장 먼 곳을 고른다.
    /// 스폰 직후 경사 때문에 몬스터가 밀리거나 상태머신이 꼬이는 것을 막는 용도다. 몬스터 생성은 호출자가 한다.
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

        // ── 테스트 도구가 구역을 그릴 때 읽는 값 ──
        public Vector3 MapCenter => mapCenter.position;
        public float MapRadius => mapRadius;
        public float ZoneRadius => zoneRadius;

        /// <summary>
        /// 맵 경계 안에서 플레이어에 가장 가까운 구역을 정하고, 그 구역 안에서 몬스터 사각형이 평탄하게 놓이는 위치 중 플레이어에서 가장 먼 위치를 찾는다.
        /// 스폰 담당(호출자 미정)이 몬스터를 놓기 전에 부른다.
        /// </summary>
        /// <param name="playerPosition">플레이어 위치 (Y는 무시)</param>
        /// <param name="monsterCollider">스폰할 몬스터의 Collider. 씬에 있는 활성 인스턴스여야 bounds 크기가 나온다</param>
        /// <param name="spawnPoint">찾은 위치. Y = 그 위치의 지형 높이. 못 찾으면 Vector3.zero</param>
        /// <returns>적합한 위치를 찾았으면 true</returns>
        /// <exception cref="System.ArgumentException">zoneRadius 가 mapRadius 보다 클 때 (ZonePlacement 가 던짐)</exception>
        public bool TryFindSpawnPoint(Vector3 playerPosition, Collider monsterCollider, out Vector3 spawnPoint)
        {
            Vector3 zoneCenter = ZonePlacement.FindClosestCenter(playerPosition, mapCenter.position, mapRadius, zoneRadius);
            Vector2 footprintSize = new Vector2(monsterCollider.bounds.size.x, monsterCollider.bounds.size.z);
            Vector3 flatPlayerPosition = new Vector3(playerPosition.x, 0f, playerPosition.z);

            List<Vector3> candidates = BuildCandidates(zoneCenter, zoneRadius, footprintSize);
            candidates.Sort((first, second) =>
                Vector3.Distance(second, flatPlayerPosition).CompareTo(Vector3.Distance(first, flatPlayerPosition)));

            foreach (Vector3 candidate in candidates)
            {
                if (IsFlatEnough(candidate, footprintSize) == false) continue;

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

        /// <summary>주어진 XZ에서 지형 표면의 월드 높이를 잰다</summary>
        /// <param name="position">월드 위치 (Y는 무시)</param>
        /// <returns>지형 표면의 월드 Y (m)</returns>
        private float HeightAt(Vector3 position)
        {
            return terrain.SampleHeight(position) + terrain.transform.position.y;
        }
    }
}
