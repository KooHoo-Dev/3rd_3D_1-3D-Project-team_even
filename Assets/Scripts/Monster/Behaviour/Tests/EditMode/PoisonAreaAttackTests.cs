using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// 원거리 독장판: 포물선 위치(PoisonProjectile.ArcPosition), 장판 피해 조건(PoisonZone.IsInsideZone),
    /// 지형 맞춤 원판 꼭짓점(TerrainDiscMesh.LocalVertices), 테이블 Damage → 장판 틱 피해(PoisonZone)
    /// </summary>
    public sealed class PoisonAreaAttackTests
    {
        private static readonly Vector3 Start = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 End = new Vector3(0f, 0f, 20f);
        private const float Tolerance = 1e-4f;

        [Test]
        public void ArcStartsAtStartAndEndsAtEnd()
        {
            Assert.That(Vector3.Distance(PoisonProjectile.ArcPosition(Start, End, 4f, 0f), Start), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(PoisonProjectile.ArcPosition(Start, End, 4f, 1f), End), Is.LessThan(Tolerance));
        }

        [Test]
        public void ArcPeaksAtHalfWithArcHeightAboveStraightLine()
        {
            Vector3 straightMiddle = Vector3.Lerp(Start, End, 0.5f);
            Vector3 middle = PoisonProjectile.ArcPosition(Start, End, 4f, 0.5f);
            Assert.AreEqual(straightMiddle.y + 4f, middle.y, Tolerance);
            Assert.AreEqual(straightMiddle.z, middle.z, Tolerance);
        }

        [Test]
        public void ArcClampsProgressPastEnd()
        {
            Assert.That(Vector3.Distance(PoisonProjectile.ArcPosition(Start, End, 4f, 1.7f), End), Is.LessThan(Tolerance));
        }

        [Test]
        public void ZoneHitsInsideRadiusOnGround()
        {
            Assert.IsTrue(PoisonZone.IsInsideZone(Vector3.zero, 4f, new Vector3(3.9f, 0f, 0f), 0f, 0.5f));
            Assert.IsTrue(PoisonZone.IsInsideZone(Vector3.zero, 4f, new Vector3(4f, 0f, 0f), 0f, 0.5f), "반경 경계는 포함");
        }

        [Test]
        public void ZoneMissesOutsideRadius()
        {
            Assert.IsFalse(PoisonZone.IsInsideZone(Vector3.zero, 4f, new Vector3(4.1f, 0f, 0f), 0f, 0.5f));
        }

        [Test]
        public void ZoneMissesWhenStandingAboveGroundTolerance()
        {
            // 바위 위: 지면(0)보다 0.6m 높이 서 있음 → 허용 0.5m 초과
            Assert.IsFalse(PoisonZone.IsInsideZone(Vector3.zero, 4f, new Vector3(1f, 0.6f, 0f), 0f, 0.5f));
            Assert.IsTrue(PoisonZone.IsInsideZone(Vector3.zero, 4f, new Vector3(1f, 0.5f, 0f), 0f, 0.5f), "허용 경계는 포함");
        }

        [Test]
        public void ZoneHitsOnSlopedTerrainWithinSphere()
        {
            // 언덕: 중심(0,0,0)에서 수평 3m, 지면 높이 2m 에 서 있음 → 구 거리 √13 ≈ 3.6 ≤ 4, 지면 위
            Assert.IsTrue(PoisonZone.IsInsideZone(Vector3.zero, 4f, new Vector3(3f, 2f, 0f), 2f, 0.5f));
        }

        [Test]
        public void DiscOnFlatGroundLiesAtLiftHeightWithinUnitRadius()
        {
            Vector3[] vertices = TerrainDiscMesh.LocalVertices(new Vector3(10f, 2f, 5f), 8f, 0.05f, point => 2f);

            foreach (Vector3 vertex in vertices)
            {
                Assert.AreEqual(0.05f, vertex.y, Tolerance);
                Assert.That(new Vector2(vertex.x, vertex.z).magnitude, Is.LessThanOrEqualTo(0.5f + Tolerance));
            }
            Assert.AreEqual(0.5f, new Vector2(vertices[vertices.Length - 1].x, vertices[vertices.Length - 1].z).magnitude, Tolerance,
                            "마지막 꼭짓점은 가장자리 고리");
        }

        [Test]
        public void DiscFollowsSlopedGround()
        {
            // 경사: 지면 높이 = 월드 x. 중심 (0, 0, 0), 지름 8m → 오브젝트 x 0.5 = 월드 x 4m = 높이 4m
            Vector3[] vertices = TerrainDiscMesh.LocalVertices(Vector3.zero, 8f, 0.05f, point => point.x);

            foreach (Vector3 vertex in vertices)
                Assert.AreEqual(vertex.x * 8f + 0.05f, vertex.y, Tolerance);
        }

        [Test]
        public void ZoneTickDealsTableDamage()
        {
            // 엘리트 독 포격(210301) 은 Damage 9. 예전에는 PoisonAreaSettings.tickDamage(6) 가 쓰였다
            var eliteMortar = new AttackSpec(AttackShape.Circle, 9, 1, 10f, 1.5f, 1f, 25f,
                                             radius: 4f, projectileSpeed: 12f, sourceDefId: 210301);
            LogAssert.ignoreFailingMessages = true; // Edit Mode 에서 MeshFilter.mesh 접근 시 "메시 누수" 오류 로그가 난다 (게임 실행 중에는 없음)
            var terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(100f, 10f, 100f) };
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.transform.position = new Vector3(-50f, 0f, -50f);
            var settingsObject = new GameObject("PoisonSettings");
            var zoneObject = new GameObject("PoisonZone");
            try
            {
                PoisonAreaSettings settings = settingsObject.AddComponent<PoisonAreaSettings>();
                zoneObject.AddComponent<MeshFilter>().sharedMesh = new Mesh();
                PoisonZone zone = zoneObject.AddComponent<PoisonZone>();
                var player = new RecordingPlayer(new Vector3(1f, 0f, 1f));

                zone.Begin(settings, eliteMortar, Vector3.zero, player, target => (IDamageable)target);
                zoneObject.SendMessage("Update"); // 첫 틱은 깔리는 순간

                Assert.AreEqual(1, player.HitCount);
                Assert.AreEqual(9, player.LastDamage.Amount);
                Assert.AreEqual(210301, player.LastDamage.SourceDefId);
            }
            finally
            {
                Object.DestroyImmediate(zoneObject);
                Object.DestroyImmediate(settingsObject);
                Object.DestroyImmediate(terrainObject);
            }
        }

        // 장판 피해 대상: 위치만 갖고, 받은 피해를 기록한다
        private sealed class RecordingPlayer : IPlayerTarget, IDamageable
        {
            public RecordingPlayer(Vector3 position) => PlayerTrackingInfo = new PlayerTrackingInfo(position, Vector3.forward, Quaternion.identity);

            public Transform Root => null;
            public Transform ViewPoint => null;
            public PlayerTrackingInfo PlayerTrackingInfo { get; }
            public Faction Faction => Faction.Player;
            public bool IsAlive => true;
            public int HitCount { get; private set; }
            public DamageInfo LastDamage { get; private set; }

            public void TakeDamage(in DamageInfo info)
            {
                HitCount++;
                LastDamage = info;
            }
        }
    }
}
