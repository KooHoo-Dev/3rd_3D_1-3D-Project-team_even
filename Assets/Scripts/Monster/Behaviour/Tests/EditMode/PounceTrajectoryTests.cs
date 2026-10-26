using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class PounceTrajectoryTests
    {
        [Test]
        public void ArcHasExactStartMidpointAndEnd()
        {
            Vector3 start = new Vector3(1f, 2f, 3f);
            Vector3 end = new Vector3(9f, 4f, 3f);
            Assert.That(Vector3.Distance(start, PounceAttack.ArcPosition(start, end, 1.5f, 0f)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(end, PounceAttack.ArcPosition(start, end, 1.5f, 1f)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(new Vector3(5f, 4.5f, 3f), PounceAttack.ArcPosition(start, end, 1.5f, 0.5f)), Is.LessThan(0.0001f));
            Assert.AreEqual(end, PounceAttack.ArcPosition(start, end, 1.5f, 1.5f));
        }

        [Test]
        public void OutOfRangeTargetClampsHorizontalDistanceAndPreservesDirectionAndHeight()
        {
            Vector3 end = PounceAttack.ClampDestination(Vector3.zero, new Vector3(12f, 3f, 16f), 8f);
            Assert.That(Vector3.Distance(end, new Vector3(4.8f, 3f, 6.4f)), Is.LessThan(0.0001f));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), PounceAttack.ClampDestination(Vector3.zero, new Vector3(1f, 2f, 3f), 8f));
        }
    }
}
