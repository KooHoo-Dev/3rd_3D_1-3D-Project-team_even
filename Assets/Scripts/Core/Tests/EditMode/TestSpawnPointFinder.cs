using HybridShooter.Core.Battlefield;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Core.Tests
{
    public class TestSpawnPointFinder
    {
        const int Resolution = 65;       // 64m 지형, 1m 당 높이 샘플 1개
        const float TerrainSize = 64f;
        const float TerrainHeight = 20f;
        const float Tolerance = 0.01f;
        const int ObstacleLayer = 10;        // 장애물 마스크에 넣는 레이어
        const int OtherLayer = 11;           // 마스크 밖 레이어

        GameObject terrainObject;
        GameObject finderObject;
        GameObject monsterObject;
        GameObject obstacleObject;
        SpawnPointFinder finder;
        BoxCollider monsterCollider;

        [SetUp]
        public void SetUp()
        {
            monsterObject = new GameObject("Monster");
            monsterCollider = monsterObject.AddComponent<BoxCollider>();
            monsterCollider.size = new Vector3(2f, 2f, 2f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (terrainObject != null) Object.DestroyImmediate(terrainObject);
            if (finderObject != null) Object.DestroyImmediate(finderObject);
            if (monsterObject != null) Object.DestroyImmediate(monsterObject);
            if (obstacleObject != null) Object.DestroyImmediate(obstacleObject);
        }

        // heightAtX: 월드 x(m) → 월드 높이(m). 지형은 원점 중심, y 위치 = terrainY
        void CreateTerrain(System.Func<float, float> heightAtX, float terrainY = 0f, float mapRadius = 10f)
        {
            var data = new TerrainData { heightmapResolution = Resolution };
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            var heights = new float[Resolution, Resolution];
            for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                {
                    float worldX = x * TerrainSize / (Resolution - 1) - TerrainSize * 0.5f;
                    heights[z, x] = heightAtX(worldX) / TerrainHeight;
                }
            data.SetHeights(0, 0, heights);

            terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.transform.position = new Vector3(-TerrainSize * 0.5f, terrainY, -TerrainSize * 0.5f);

            finderObject = new GameObject("Finder");
            finder = finderObject.AddComponent<SpawnPointFinder>();
            var serialized = new SerializedObject(finder);
            serialized.FindProperty("terrain").objectReferenceValue = terrainObject.GetComponent<Terrain>();
            serialized.FindProperty("mapCenter").objectReferenceValue = finderObject.transform;   // 맵 중심 = 원점
            serialized.FindProperty("mapRadius").floatValue = mapRadius;
            serialized.FindProperty("zoneRadius").floatValue = 10f;   // 맵 반지름 10 이면 구역 = 맵 (중심 고정)
            serialized.FindProperty("maxHeightDifference").floatValue = 0.5f;
            serialized.FindProperty("candidateSpacing").floatValue = 1f;
            serialized.FindProperty("heightSampleSpacing").floatValue = 0.5f;
            serialized.FindProperty("obstacleLayers").intValue = 0;   // 기본: 장애물 판정 끔
            serialized.FindProperty("obstacleMargin").floatValue = 0.5f;
            serialized.FindProperty("obstacleGroundClearance").floatValue = 0.1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        void SetObstacleLayers(int layerMask)
        {
            var serialized = new SerializedObject(finder);
            serialized.FindProperty("obstacleLayers").intValue = layerMask;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 바닥(지형 y=0)에서 위로 3m 서 있는 BoxCollider 장애물
        void CreateObstacle(Vector3 floorCenter, Vector2 sizeXZ, int layer)
        {
            obstacleObject = new GameObject("Obstacle") { layer = layer };
            obstacleObject.transform.position = floorCenter + Vector3.up * 1.5f;
            obstacleObject.AddComponent<BoxCollider>().size = new Vector3(sizeXZ.x, 3f, sizeXZ.y);
            Physics.SyncTransforms();
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [Test]
        public void ColliderBounds_HasBoxSize()
        {
            Assert.That(monsterCollider.bounds.size.x, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(monsterCollider.bounds.size.z, Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void FlatTerrain_ReturnsFarthestPointFromPlayer()
        {
            CreateTerrain(x => 0f);
            Vector3 player = new Vector3(-20f, 0f, 0f);

            bool found = finder.TryFindSpawnPoint(player, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            // 2x2 사각형이 반지름 10 원 안에 드는 가장 먼 격자점: (8, ±3) → 거리 약 28.16
            Assert.That(FlatDistance(spawnPoint, player), Is.GreaterThan(28.1f));
            Assert.That(FlatDistance(spawnPoint, Vector3.zero) + Mathf.Sqrt(2f), Is.LessThanOrEqualTo(10f + Tolerance));
        }

        [Test]
        public void SteepTerrainEverywhere_ReturnsFalse()
        {
            CreateTerrain(x => (x + 32f) * 0.3f);   // 2m 사각형 고저차 0.6m > 0.5m

            bool found = finder.TryFindSpawnPoint(new Vector3(-20f, 0f, 0f), monsterCollider, out Vector3 spawnPoint);

            Assert.IsFalse(found);
            Assert.AreEqual(Vector3.zero, spawnPoint);
        }

        [Test]
        public void SteepFarSide_PicksFlatSide()
        {
            CreateTerrain(x => x > 0f ? x * 0.5f : 0f);   // x > 0 쪽만 급경사
            Vector3 player = new Vector3(-20f, 0f, 0f);

            bool found = finder.TryFindSpawnPoint(player, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            Assert.That(spawnPoint.x, Is.LessThanOrEqualTo(0f));
            // 평탄한 쪽 중에서는 플레이어에서 가장 먼 쪽(x=0 근처)이어야 한다
            Assert.That(spawnPoint.x, Is.GreaterThanOrEqualTo(-2f));
        }

        [Test]
        public void SpawnHeight_IsTerrainSurface()
        {
            CreateTerrain(x => 3f, terrainY: 5f);

            bool found = finder.TryFindSpawnPoint(new Vector3(-20f, 0f, 0f), monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            Assert.That(spawnPoint.y, Is.EqualTo(8f).Within(Tolerance));
        }

        [Test]
        public void FootprintLargerThanZone_ReturnsFalse()
        {
            CreateTerrain(x => 0f);
            monsterCollider.size = new Vector3(30f, 2f, 30f);
            Physics.SyncTransforms();

            bool found = finder.TryFindSpawnPoint(Vector3.zero, monsterCollider, out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void ZoneFollowsPlayer_SpawnsInsideZoneNearPlayer()
        {
            CreateTerrain(x => 0f, mapRadius: 30f);
            Vector3 player = new Vector3(-25f, 0f, 0f);   // 구역 중심 = (-20, 0, 0)

            bool found = finder.TryFindSpawnPoint(player, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            Assert.That(FlatDistance(spawnPoint, new Vector3(-20f, 0f, 0f)) + Mathf.Sqrt(2f), Is.LessThanOrEqualTo(10f + Tolerance));
            // 구역 안에서 플레이어 반대편 끝: x ≈ -12
            Assert.That(spawnPoint.x, Is.GreaterThan(-13.5f));
        }

        [Test]
        public void ObstacleAtFarthestPoint_PicksOtherPoint()
        {
            CreateTerrain(x => 0f);
            Vector3 player = new Vector3(-20f, 0f, 0f);
            finder.TryFindSpawnPoint(player, monsterCollider, out Vector3 withoutObstacle);
            CreateObstacle(withoutObstacle, new Vector2(1f, 1f), ObstacleLayer);
            SetObstacleLayers(1 << ObstacleLayer);

            bool found = finder.TryFindSpawnPoint(player, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            // 몬스터 절반 1 + 여유 0.5 + 장애물 절반 0.5 = 2m 이상 떨어져야 겹치지 않는다
            float offsetX = Mathf.Abs(spawnPoint.x - withoutObstacle.x);
            float offsetZ = Mathf.Abs(spawnPoint.z - withoutObstacle.z);
            Assert.That(Mathf.Max(offsetX, offsetZ), Is.GreaterThanOrEqualTo(2f - Tolerance));
        }

        [Test]
        public void ObstacleCoversZone_ReturnsFalse()
        {
            CreateTerrain(x => 0f);
            CreateObstacle(Vector3.zero, new Vector2(40f, 40f), ObstacleLayer);
            SetObstacleLayers(1 << ObstacleLayer);

            bool found = finder.TryFindSpawnPoint(new Vector3(-20f, 0f, 0f), monsterCollider, out Vector3 spawnPoint);

            Assert.IsFalse(found);
            Assert.AreEqual(Vector3.zero, spawnPoint);
        }

        [Test]
        public void ObstacleOutsideMask_IsIgnored()
        {
            CreateTerrain(x => 0f);
            CreateObstacle(Vector3.zero, new Vector2(40f, 40f), OtherLayer);
            SetObstacleLayers(1 << ObstacleLayer);

            bool found = finder.TryFindSpawnPoint(new Vector3(-20f, 0f, 0f), monsterCollider, out _);

            Assert.IsTrue(found);
        }
    }
}
