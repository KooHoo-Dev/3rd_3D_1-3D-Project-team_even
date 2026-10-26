using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>원거리 독장판의 순수 계산: 포물선 위치(PoisonProjectile.ArcPosition), 장판 피해 조건(PoisonZone.IsInsideZone)</summary>
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
    }
}
