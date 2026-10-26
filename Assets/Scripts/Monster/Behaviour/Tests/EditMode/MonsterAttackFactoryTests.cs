using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class MonsterAttackFactoryTests
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
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.Melee, Bite, body, t => (IDamageable)t, null);
            Assert.IsInstanceOf<StrikeAttack>(attack);
            Assert.AreEqual(1.5f, attack.Range);
        }

        [Test]
        public void ZonePressureGetsPoisonAreaAttack()
        {
            var mortar = new AttackSpec(AttackShape.Circle, 6, 1, 10f, 1.5f, 1f, 25f,
                                        radius: 4f, projectileSpeed: 12f, effectId: 1);
            var settings = monsterObject.AddComponent<PoisonAreaSettings>();
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.ZonePressure, mortar, body, t => (IDamageable)t, settings);
            Assert.IsInstanceOf<PoisonAreaAttack>(attack);
            Assert.AreEqual(25f, attack.Range);
        }

        [Test]
        public void InfiltratorGetsPounceAttack()
        {
            var pounce = new AttackSpec(AttackShape.Circle, 18, 1, 8f, 0.6f, 1.2f, 8f, radius: 2f);
            var settings = monsterObject.AddComponent<PounceSettings>();
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.Infiltrator, pounce, body,
                                                          t => (IDamageable)t, null, settings);
            Assert.IsInstanceOf<PounceAttack>(attack);
            Assert.AreEqual(8f, attack.Range);
        }

        [TestCase(MonsterRole.Air)]
        [TestCase(MonsterRole.Rusher)]

        [TestCase(MonsterRole.Boss)]
        public void UnimplementedRolesThrow(MonsterRole role)
        {
            Assert.Throws<NotSupportedException>(() =>
                MonsterAttackFactory.CreateAttack(role, Bite, body, t => (IDamageable)t, null));
        }
    }
}
