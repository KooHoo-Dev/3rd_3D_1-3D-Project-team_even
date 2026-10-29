using System;
using System.Collections;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class SelfDestructAttackTests
    {
        private GameObject monster;
        private MovementProfile profile;
        private FakeTarget target;
        private SelfDestructAttack attack;
        private MonsterStateMachine brain;
        private int deathCount;
        private bool grantReward;
        private Action onFindDamageable;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            deathCount = 0;
            grantReward = false;
            onFindDamageable = null;
            target = new FakeTarget { Position = Vector3.forward };
            monster = new GameObject("SelfDestructAttackTests");
            monster.SetActive(false);
            monster.AddComponent<NavMeshAgent>();
            monster.AddComponent<NavMeshMoveExecutor>();
            var movement = monster.AddComponent<MonsterMovement>();
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            movement.ConfigureDirect(profile, target);
            monster.SetActive(true);
            yield return null;

            var body = new MonsterBody(monster.transform);
            var spec = new AttackSpec(AttackShape.Circle, 25, 1, 2f, 1.2f, 0f, 2.2f,
                                      radius: 3.5f, sourceDefId: 100501);
            attack = new SelfDestructAttack(spec, body, t =>
            {
                onFindDamageable?.Invoke();
                return (IDamageable)t;
            }) { ActiveSeconds = 0.5f, HitTimeRatio = 0f };
            brain = new MonsterStateMachine(movement, body, attack);
            brain.Died += reward => { deathCount++; grantReward = reward; };
            brain.SetTarget(target);
            brain.ChangeState(brain.Attack);
            brain.Update(0f, 0f); // Begin
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(monster);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        private void Detonate()
        {
            brain.Update(1.3f, 1.3f); // 예고 종료 → Active
            brain.Update(1.4f, 0.1f); // 폭발 → Dead
        }

        [Test]
        public void WaitsForTelegraphThenExplodesOnceAndDiesWithoutReward()
        {
            brain.Update(1f, 1f);
            Assert.AreEqual(SkillPhase.Windup, attack.Phase);
            Assert.IsEmpty(target.Received);
            Detonate();
            Assert.IsTrue(brain.IsDead);
            Assert.AreEqual(1, deathCount);
            Assert.IsFalse(grantReward);
            Assert.AreEqual(1, target.Received.Count);
            Assert.AreEqual(25, target.Received[0].Amount);
            Assert.AreEqual(100501, target.Received[0].SourceDefId);
            brain.Update(3f, 1.6f);
            Assert.AreEqual(1, target.Received.Count);
            Assert.AreEqual(1, deathCount);
        }

        [Test]
        public void BlastIsCenteredOnBodyAndHitsBehindIt()
        {
            target.Position = Vector3.back * 3f;
            Detonate();
            Assert.AreEqual(1, target.Received.Count);
            Assert.IsTrue(brain.IsDead);
        }

        [Test]
        public void DodgingOutsideBlastAvoidsDamageButMonsterStillDies()
        {
            target.Position = Vector3.forward * 4f;
            Detonate();
            Assert.IsEmpty(target.Received);
            Assert.IsTrue(brain.IsDead);
            Assert.IsFalse(grantReward);
        }

        [Test]
        public void KillingDuringTelegraphCancelsBlastAndGrantsKillReward()
        {
            brain.Die(true);
            Detonate();
            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.IsEmpty(target.Received);
            Assert.IsTrue(grantReward);
            Assert.AreEqual(1, deathCount);
        }

        [Test]
        public void TargetLossDuringWindupCancelsBlast()
        {
            brain.SetTarget(null);
            Detonate();
            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.IsEmpty(target.Received);
            Assert.IsFalse(brain.IsDead);
        }

        [Test]
        public void TargetLossDuringDamageDoesNotCancelSelfDeath()
        {
            onFindDamageable = () => brain.SetTarget(null);
            Detonate();
            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
            Assert.AreEqual(1, target.Received.Count);
            Assert.IsTrue(brain.IsDead);
            Assert.AreEqual(1, deathCount);
            Assert.IsFalse(grantReward);
        }
    }
}
