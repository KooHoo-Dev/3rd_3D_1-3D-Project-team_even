using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.GameFlow;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class StrikeAttackTests
    {
        private FakeBody body;
        private FakeTarget target;
        private StrikeAttack attack;
        private List<SkillPhase> phases;

        // 예고 0.4s, 회복 0.3s, 쿨다운 1.2s, 사거리 1.5m, 피해 5 × 2회
        private static readonly AttackSpec Bite = new AttackSpec(
            AttackShape.Melee, damage: 5, hitCount: 2, cooldown: 1.2f,
            telegraphTime: 0.4f, recoveryTime: 0.3f, range: 1.5f, sourceDefId: 100101);

        [SetUp]
        public void SetUp()
        {
            body = new FakeBody { Position = Vector3.zero };
            target = new FakeTarget { Position = new Vector3(0f, 0f, 1f) };
            attack = new StrikeAttack(Bite, body, t => (IDamageable)t);
            phases = new List<SkillPhase>();
            attack.PhaseChanged += (_, p) => phases.Add(p);
        }

        [Test]
        public void RunsWindupActiveRecoverInOrder()
        {
            attack.Begin(target, 0f);
            Assert.AreEqual(SkillPhase.Windup, attack.Phase);

            attack.Tick(0.3f, 0.3f);
            Assert.AreEqual(SkillPhase.Windup, attack.Phase, "예고 시간 전");

            attack.Tick(0.4f, 0.1f);
            Assert.AreEqual(SkillPhase.Active, attack.Phase);
            Assert.AreEqual(0, target.Received.Count, "Active 로 넘어간 Tick 에서는 아직 판정하지 않는다");

            attack.Tick(0.5f, 0.1f);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase);

            attack.Tick(0.7f, 0.2f);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase, "회복 시간 전");

            attack.Tick(0.8f, 0.1f);
            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
            CollectionAssert.AreEqual(
                new[] { SkillPhase.Windup, SkillPhase.Active, SkillPhase.Recover, SkillPhase.Idle }, phases);
        }

        [Test]
        public void HitAppliesDamageTimesHitCount()
        {
            RunToRecover();
            Assert.AreEqual(2, target.Received.Count);
            var info = target.Received[0];
            Assert.AreEqual(5, info.Amount);
            Assert.AreEqual(DamageKind.MonsterAttack, info.Kind);
            Assert.AreEqual(Faction.Monster, info.AttackerFaction);
            Assert.AreEqual(100101, info.SourceDefId);
            Assert.AreEqual(Vector3.forward, info.Direction);
        }

        [Test]
        public void WindupLocksFacingTowardTarget()
        {
            target.Position = new Vector3(1f, 0f, 0f);
            attack.Begin(target, 0f);
            Assert.AreEqual(target.Position, body.LastFaced);
            Assert.AreEqual(Vector3.right, body.Forward);
        }

        [Test]
        public void DodgingOutOfRangeDuringWindupAvoidsDamage()
        {
            attack.Begin(target, 0f);
            target.Position = new Vector3(0f, 0f, 3f);
            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.5f, 0.1f);
            Assert.AreEqual(0, target.Received.Count);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase, "빗나가도 회복(반격 시간)은 진행");
        }

        [Test]
        public void DeadTargetIsNotDamaged()
        {
            // 생존 판정은 공격이 아니라 소유자 몫이다: 사망 = Brain.SetTarget(null) 이 진행 중인 공격을 취소한다
            var brain = new MonsterStateMachine(new FakeMover(), body, attack);
            brain.SetTarget(target);
            attack.Begin(target, 0f);
            brain.SetTarget(null);
            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.AreEqual(0, target.Received.Count);
        }

        [Test]
        public void CooldownCountsFromBegin()
        {
            Assert.IsTrue(attack.CanStart(0f));
            attack.Begin(target, 0f);
            Assert.IsFalse(attack.CanStart(0.1f), "진행 중");

            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.5f, 0.1f);
            attack.Tick(0.8f, 0.3f);
            Assert.IsFalse(attack.IsAttacking);
            Assert.IsFalse(attack.CanStart(1.1f));
            Assert.IsTrue(attack.CanStart(1.2f));
        }

        [Test]
        public void CancelStopsWithoutDamage()
        {
            attack.Begin(target, 0f);
            attack.Cancel();
            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.AreEqual(SkillPhase.Idle, phases[phases.Count - 1]);
            Assert.AreEqual(0, target.Received.Count);
        }

        [Test]
        public void CancelWhenIdleDoesNothing()
        {
            attack.Cancel();
            Assert.AreEqual(AttackResult.None, attack.LastResult);
            Assert.AreEqual(0, phases.Count);
        }

        [Test]
        public void BeginWhileRunningThrows()
        {
            attack.Begin(target, 0f);
            Assert.Throws<InvalidOperationException>(() => attack.Begin(target, 0.1f));
        }

        [Test]
        public void TickWhenIdleThrows()
        {
            Assert.Throws<InvalidOperationException>(() => attack.Tick(0f, 0f));
        }

        [Test]
        public void MissingDamageableThrows()
        {
            attack = new StrikeAttack(Bite, body, _ => null);
            attack.Begin(target, 0f);
            attack.Tick(0.4f, 0.4f);
            Assert.Throws<InvalidOperationException>(() => attack.Tick(0.5f, 0.1f));
        }

        [Test]
        public void RangeComesFromSpec()
        {
            Assert.AreEqual(1.5f, attack.Range);
        }

        [Test]
        public void BrainWithStrikeAttackDamagesTarget()
        {
            var brain = new MonsterStateMachine(new FakeMover(), body, attack);
            brain.SetTarget(target);
            float now = 0f;
            for (int i = 0; i < 25; i++)
            {
                now += 0.1f;
                brain.Update(now, 0.1f);
            }

            // 0.1 Chase, 0.2 Attack, 0.3 Begin, ~0.7 Active, ~0.8 판정, ~1.5 재시작(쿨다운 1.2), ~2.0 판정, 다음 시작은 ~2.7
            Assert.AreEqual(4, target.Received.Count, "2.5s 동안 2번 공격 × HitCount 2");
            Assert.AreSame(brain.Attack, brain.Current);
        }

        private void RunToRecover()
        {
            attack.Begin(target, 0f);
            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.5f, 0.1f);
        }
    }
}
