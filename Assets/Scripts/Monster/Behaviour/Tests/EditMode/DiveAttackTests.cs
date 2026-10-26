using NUnit.Framework;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class DiveAttackTests
    {
        private const float StartOffset = -8f;
        private const float StartHeight = 5f;
        private const float MinHeight = 1f;
        private const float Tolerance = 0.0001f;

        [Test]
        public void DiveHeightStartsAtStartHeight()
        {
            Assert.AreEqual(StartHeight, DiveAttack.DiveHeight(StartOffset, StartOffset, StartHeight, MinHeight), Tolerance);
        }

        [Test]
        public void DiveHeightIsMinHeightAtCenter()
        {
            Assert.AreEqual(MinHeight, DiveAttack.DiveHeight(0f, StartOffset, StartHeight, MinHeight), Tolerance);
        }

        [Test]
        public void DiveHeightReturnsToStartHeightAtExit()
        {
            Assert.AreEqual(StartHeight, DiveAttack.DiveHeight(-StartOffset, StartOffset, StartHeight, MinHeight), Tolerance);
        }

        [Test]
        public void DiveSlopeIsDownwardAtStart()
        {
            Assert.AreEqual(-1f, DiveAttack.DiveSlope(StartOffset, StartOffset, StartHeight, MinHeight), Tolerance);
        }

        [Test]
        public void DiveSlopeIsFlatAtCenter()
        {
            Assert.AreEqual(0f, DiveAttack.DiveSlope(0f, StartOffset, StartHeight, MinHeight), Tolerance);
        }

        [Test]
        public void DiveSlopeIsUpwardAtExit()
        {
            Assert.AreEqual(1f, DiveAttack.DiveSlope(-StartOffset, StartOffset, StartHeight, MinHeight), Tolerance);
        }

        [TestCase(1f)]
        [TestCase(3.5f)]
        [TestCase(6f)]
        public void DiveHeightIsSymmetric(float distanceFromCenter)
        {
            float before = DiveAttack.DiveHeight(-distanceFromCenter, StartOffset, StartHeight, MinHeight);
            float after = DiveAttack.DiveHeight(distanceFromCenter, StartOffset, StartHeight, MinHeight);
            Assert.AreEqual(before, after, Tolerance);
        }

        [Test]
        public void DiveHeightIsContinuousNearStart()
        {
            float atStart = DiveAttack.DiveHeight(StartOffset, StartOffset, StartHeight, MinHeight);
            float justAfter = DiveAttack.DiveHeight(StartOffset + 0.01f, StartOffset, StartHeight, MinHeight);
            Assert.Less(atStart - justAfter, 0.02f);
        }
    }
}
