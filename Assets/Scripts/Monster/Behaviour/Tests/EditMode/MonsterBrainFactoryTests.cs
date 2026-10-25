using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class MonsterBrainFactoryTests
    {
        private static readonly AttackSpec Bite = new AttackSpec(AttackShape.Melee, 5, 1, 1.2f, 0.4f, 0.3f, 1.5f);

        private GameObject monsterObject;
        private MonsterBody body;

        [SetUp]
        public void SetUp()
        {
            monsterObject = new GameObject("MonsterBrainFactoryTests");
            body = new MonsterBody(monsterObject.transform);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(monsterObject);

        [Test]
        public void MeleeGetsStrikeAttack()
        {
            var attack = MonsterBrainFactory.CreateAttack(MonsterRole.Melee, Bite, body, t => (IDamageable)t);
            Assert.IsInstanceOf<StrikeAttack>(attack);
            Assert.AreEqual(1.5f, attack.Range);
        }

        [TestCase(MonsterRole.Air)]
        [TestCase(MonsterRole.Rusher)]
        [TestCase(MonsterRole.Infiltrator)]
        [TestCase(MonsterRole.ZonePressure)]
        [TestCase(MonsterRole.Boss)]
        public void UnimplementedRolesThrow(MonsterRole role)
        {
            Assert.Throws<NotSupportedException>(() =>
                MonsterBrainFactory.CreateAttack(role, Bite, body, t => (IDamageable)t));
        }
    }
}
