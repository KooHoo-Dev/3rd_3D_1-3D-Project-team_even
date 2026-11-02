using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class DiveAttackTests
    {
        private const float StartOffset = -8f;
        private const float StartHeight = 5f;
        private const float MinHeight = 1f;
        private const float Tolerance = 0.0001f;
        private const int CreateCount = 200;

        private static readonly AttackSpec Graze = new AttackSpec(AttackShape.Melee, 5, 1, 1.2f, 0.4f, 0.3f, 1.5f);

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

        [TestCase(8f, 1.5f)]
        [TestCase(0.5f, 2f)]
        public void RangeStaysInsideEntryDistanceRandomRangeAndAboveMinimum(float entryDistance, float randomRange)
        {
            var monsterObject = new GameObject("DiveAttackTests");
            try
            {
                var movement = monsterObject.AddComponent<MonsterMovement>();
                var settings = monsterObject.AddComponent<DiveSettings>();
                var flyer = monsterObject.AddComponent<FlyingMoveExecutor>();
                var serializedSettings = new SerializedObject(settings);
                serializedSettings.FindProperty("entryDistance").floatValue = entryDistance;
                serializedSettings.FindProperty("entryDistanceRandomRange").floatValue = randomRange;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                var body = new MonsterBody(monsterObject.transform);

                float smallest = float.MaxValue;
                float largest = float.MinValue;
                for (int i = 0; i < CreateCount; i++)
                {
                    var attack = new DiveAttack(Graze, body, t => (IDamageable)t, movement, settings, flyer);
                    smallest = Mathf.Min(smallest, attack.Range);
                    largest = Mathf.Max(largest, attack.Range);
                }

                Assert.GreaterOrEqual(smallest, Mathf.Max(0.5f, entryDistance - randomRange));
                Assert.LessOrEqual(largest, entryDistance + randomRange);
                Assert.Greater(largest - smallest, 0f, "여러 번 만들면 Range 가 퍼져야 한다");
            }
            finally
            {
                Object.DestroyImmediate(monsterObject);
            }
        }
    }
}
