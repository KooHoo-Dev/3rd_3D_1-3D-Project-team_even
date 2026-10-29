using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Monster.Movement;
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
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.Melee, Bite, body, t => (IDamageable)t, default, null, null);
            Assert.IsInstanceOf<StrikeAttack>(attack);
            Assert.AreEqual(1.5f, attack.Range);
        }

        [Test]
        public void SelfDestructGetsCenteredExplosion()
        {
            var blast = new AttackSpec(AttackShape.Circle, 25, 1, 2f, 1.2f, 0f, 2.2f, radius: 3.5f);
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.SelfDestruct, blast, body,
                                                          t => (IDamageable)t, default, null, null);
            Assert.IsInstanceOf<SelfDestructAttack>(attack);
            Assert.AreEqual(2.2f, attack.Range);
            Assert.AreEqual(AfterAttack.Die, attack.After);
        }

        [Test]
        public void ZonePressureGetsPoisonAreaAttack()
        {
            var mortar = new AttackSpec(AttackShape.Circle, 6, 1, 10f, 1.5f, 1f, 25f,
                                        radius: 4f, projectileSpeed: 12f);
            var settings = monsterObject.AddComponent<PoisonAreaSettings>();
            var attackSettings = new MonsterAttackSettings(settings, null, null, null);
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.ZonePressure, mortar, body, t => (IDamageable)t, attackSettings, null, null);
            Assert.IsInstanceOf<PoisonAreaAttack>(attack);
            Assert.AreEqual(25f, attack.Range);
        }

        [Test]
        public void InfiltratorGetsPounceAttack()
        {
            var pounce = new AttackSpec(AttackShape.Circle, 18, 1, 8f, 0.6f, 1.2f, 8f, radius: 2f);
            var settings = monsterObject.AddComponent<PounceSettings>();
            var attackSettings = new MonsterAttackSettings(null, settings, null, null);
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.Infiltrator, pounce, body,
                                                          t => (IDamageable)t, attackSettings, null, null);
            Assert.IsInstanceOf<PounceAttack>(attack);
            Assert.AreEqual(8f, attack.Range);
        }

        [Test]
        public void RusherGetsChargeAttack()
        {
            var headRam = new AttackSpec(AttackShape.Line, 25, 1, 10f, 1.2f, 0f, 15f, width: 2.5f);
            var movement = monsterObject.AddComponent<MonsterMovement>();
            var settings = monsterObject.AddComponent<ChargeSettings>();
            var attackSettings = new MonsterAttackSettings(null, null, settings, null);
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.Rusher, headRam, body, t => (IDamageable)t, attackSettings, movement, null);
            Assert.IsInstanceOf<ChargeAttack>(attack);
            Assert.AreEqual(15f, attack.Range);
            Assert.AreEqual(AfterAttack.RestInIdle, attack.After);
        }

        [Test]
        public void AirGetsDiveAttack()
        {
            var movement = monsterObject.AddComponent<MonsterMovement>();
            var settings = monsterObject.AddComponent<DiveSettings>();
            var flyer = monsterObject.AddComponent<FlyingMoveExecutor>();
            var attackSettings = new MonsterAttackSettings(null, null, null, settings);
            var attack = MonsterAttackFactory.CreateAttack(MonsterRole.Air, Bite, body, t => (IDamageable)t, attackSettings, movement, flyer);
            Assert.IsInstanceOf<DiveAttack>(attack);
            Assert.AreEqual(8f, attack.Range);
            Assert.AreEqual(AfterAttack.RepositionInChase, attack.After);
        }

        [TestCase(MonsterRole.Boss)]
        public void UnimplementedRolesThrow(MonsterRole role)
        {
            Assert.Throws<NotSupportedException>(() =>
                MonsterAttackFactory.CreateAttack(role, Bite, body, t => (IDamageable)t, default, null, null));
        }
    }
}
