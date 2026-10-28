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

        GameObject terrainObject;
        GameObject finderObject;
        GameObject monsterObject;
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
        }

        // heightAtX: 월드 x(m) → 월드 높이(m). 지형은 원점 중심, y 위치 = terrainY
        void CreateTerrain(System.Func<float, float> heightAtX, float terrainY = 0f)
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
            serialized.FindProperty("maxHeightDifference").floatValue = 0.5f;
            serialized.FindProperty("candidateSpacing").floatValue = 1f;
            serialized.FindProperty("heightSampleSpacing").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

            bool found = finder.TryFindSpawnPoint(player, Vector3.zero, 10f, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            // 2x2 사각형이 반지름 10 원 안에 드는 가장 먼 격자점: (8, ±3) → 거리 약 28.16
            Assert.That(FlatDistance(spawnPoint, player), Is.GreaterThan(28.1f));
            Assert.That(FlatDistance(spawnPoint, Vector3.zero) + Mathf.Sqrt(2f), Is.LessThanOrEqualTo(10f + Tolerance));
        }

        [Test]
        public void SteepTerrainEverywhere_ReturnsFalse()
        {
            CreateTerrain(x => (x + 32f) * 0.3f);   // 2m 사각형 고저차 0.6m > 0.5m

            bool found = finder.TryFindSpawnPoint(new Vector3(-20f, 0f, 0f), Vector3.zero, 10f, monsterCollider, out Vector3 spawnPoint);

            Assert.IsFalse(found);
            Assert.AreEqual(Vector3.zero, spawnPoint);
        }

        [Test]
        public void SteepFarSide_PicksFlatSide()
        {
            CreateTerrain(x => x > 0f ? x * 0.5f : 0f);   // x > 0 쪽만 급경사
            Vector3 player = new Vector3(-20f, 0f, 0f);

            bool found = finder.TryFindSpawnPoint(player, Vector3.zero, 10f, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            Assert.That(spawnPoint.x, Is.LessThanOrEqualTo(0f));
            // 평탄한 쪽 중에서는 플레이어에서 가장 먼 쪽(x=0 근처)이어야 한다
            Assert.That(spawnPoint.x, Is.GreaterThanOrEqualTo(-2f));
        }

        [Test]
        public void SpawnHeight_IsTerrainSurface()
        {
            CreateTerrain(x => 3f, terrainY: 5f);

            bool found = finder.TryFindSpawnPoint(new Vector3(-20f, 0f, 0f), Vector3.zero, 10f, monsterCollider, out Vector3 spawnPoint);

            Assert.IsTrue(found);
            Assert.That(spawnPoint.y, Is.EqualTo(8f).Within(Tolerance));
        }

        [Test]
        public void FootprintLargerThanZone_ReturnsFalse()
        {
            CreateTerrain(x => 0f);
            monsterCollider.size = new Vector3(30f, 2f, 30f);
            Physics.SyncTransforms();

            bool found = finder.TryFindSpawnPoint(Vector3.zero, Vector3.zero, 10f, monsterCollider, out _);

            Assert.IsFalse(found);
        }
    }
}
