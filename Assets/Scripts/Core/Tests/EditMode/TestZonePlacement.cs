using System;
using HybridShooter.Core.Battlefield;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Core.Tests
{
    public class TestZonePlacement
    {
        const float Tolerance = 0.0001f;
        static readonly Vector3 MapCenter = new Vector3(10f, 2f, -5f);
        const float MapRadius = 50f;
        const float ZoneRadius = 10f;

        static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected}, actual {actual}");
        }

        [Test]
        public void PlayerInsideAllowedArea_ReturnsPlayerXZ()
        {
            Vector3 player = MapCenter + new Vector3(20f, 0f, 15f);
            Vector3 result = ZonePlacement.FindClosestCenter(player, MapCenter, MapRadius, ZoneRadius);
            AssertNear(player, result);
        }

        [Test]
        public void PlayerOutsideAllowedArea_ClampsToAllowedDistanceInSameDirection()
        {
            Vector3 offset = new Vector3(60f, 0f, 80f); // 거리 100
            Vector3 result = ZonePlacement.FindClosestCenter(MapCenter + offset, MapCenter, MapRadius, ZoneRadius);
            AssertNear(MapCenter + offset.normalized * (MapRadius - ZoneRadius), result);
        }

        [Test]
        public void PlayerOnAllowedEdge_ReturnsPlayerXZ()
        {
            Vector3 player = MapCenter + new Vector3(0f, 0f, MapRadius - ZoneRadius);
            AssertNear(player, ZonePlacement.FindClosestCenter(player, MapCenter, MapRadius, ZoneRadius));
        }

        [Test]
        public void PlayerAtMapCenter_ReturnsMapCenter()
        {
            AssertNear(MapCenter, ZonePlacement.FindClosestCenter(MapCenter, MapCenter, MapRadius, ZoneRadius));
        }

        [Test]
        public void ZoneRadiusEqualsMapRadius_ReturnsMapCenter()
        {
            Vector3 player = MapCenter + new Vector3(30f, 0f, 0f);
            AssertNear(MapCenter, ZonePlacement.FindClosestCenter(player, MapCenter, MapRadius, MapRadius));
        }

        [Test]
        public void ZoneRadiusLargerThanMapRadius_Throws()
        {
            Assert.Throws<ArgumentException>(() => ZonePlacement.FindClosestCenter(MapCenter, MapCenter, MapRadius, MapRadius + 1f));
        }

        [Test]
        public void ResultHeight_IsMapCenterY()
        {
            Vector3 insidePlayer = MapCenter + new Vector3(5f, 30f, 5f);
            Vector3 outsidePlayer = MapCenter + new Vector3(200f, -30f, 0f);
            Assert.That(ZonePlacement.FindClosestCenter(insidePlayer, MapCenter, MapRadius, ZoneRadius).y, Is.EqualTo(MapCenter.y).Within(Tolerance));
            Assert.That(ZonePlacement.FindClosestCenter(outsidePlayer, MapCenter, MapRadius, ZoneRadius).y, Is.EqualTo(MapCenter.y).Within(Tolerance));
        }

        [Test]
        public void ResultZone_StaysInsideMap()
        {
            Vector3 player = MapCenter + new Vector3(-300f, 0f, 47f);
            Vector3 result = ZonePlacement.FindClosestCenter(player, MapCenter, MapRadius, ZoneRadius);
            Assert.That(Vector3.Distance(result, MapCenter) + ZoneRadius, Is.LessThanOrEqualTo(MapRadius + Tolerance));
        }
    }
}
