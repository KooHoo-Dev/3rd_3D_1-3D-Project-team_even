using System;
using HybridShooter.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class MonsterBrainTests
    {
        private FakeMover mover;
        private FakeBody body;
        private FakeAttack attack;
        private FakeTarget target;
        private MonsterBrain brain;
        private float now;

        [SetUp]
        public void SetUp()
        {
            mover = new FakeMover();
            body = new FakeBody { Position = Vector3.zero };
            attack = new FakeAttack { Range = 1.5f };
            target = new FakeTarget { Position = new Vector3(0f, 0f, 10f) };
            brain = new MonsterBrain(mover, body, attack);
            now = 0f;
        }

        private void Step(float dt = 0.1f)
        {
            now += dt;
            brain.Update(now, dt);
        }

        private void EnterAttack()
        {
            brain.SetTarget(target);
            Step(); // Idle → Chase
            target.Position = new Vector3(0f, 0f, 1f);
            Step(); // Chase → Attack
            Assert.AreSame(brain.Attack, brain.Current);
        }

        [Test]
        public void StartsIdleAndStopsMover()
        {
            Assert.AreSame(brain.Idle, brain.Current);
            Assert.IsTrue(mover.IsStopped);
        }

        [Test]
        public void IdleStaysWithoutTarget()
        {
            Step();
            Assert.AreSame(brain.Idle, brain.Current);
        }

        [Test]
        public void IdleToChaseWhenTargetSetAndMoverResumes()
        {
            brain.SetTarget(target);
            Step();
            Assert.AreSame(brain.Chase, brain.Current);
            Assert.IsFalse(mover.IsStopped);
        }

        [Test]
        public void ChaseToIdleWhenTargetLost()
        {
            brain.SetTarget(target);
            Step();
            brain.SetTarget(null);
            Step();
            Assert.AreSame(brain.Idle, brain.Current);
            Assert.IsTrue(mover.IsStopped);
        }

        [Test]
        public void ChaseStaysWhenOutOfRange()
        {
            brain.SetTarget(target);
            Step();
            Step();
            Assert.AreSame(brain.Chase, brain.Current);
        }

        [Test]
        public void RangeIgnoresHeight()
        {
            brain.SetTarget(target);
            Step();
            target.Position = new Vector3(0f, 5f, 1f);
            Step();
            Assert.AreSame(brain.Attack, brain.Current);
        }

        [Test]
        public void ChaseToAttackInRangeStopsMover()
        {
            EnterAttack();
            Assert.IsTrue(mover.IsStopped);
            Assert.AreEqual(0, attack.BeginCount, "전환한 판단에서는 공격을 시작하지 않는다");
        }

        [Test]
        public void AttackBeginsWhenReadyAndFacesTarget()
        {
            EnterAttack();
            Step();
            Assert.AreEqual(1, attack.BeginCount);
            Assert.AreEqual(target.Position, body.LastFaced);
        }

        [Test]
        public void AttackWaitsWhileNotReady()
        {
            attack.Ready = false;
            EnterAttack();
            Step();
            Step();
            Assert.AreEqual(0, attack.BeginCount);
            Assert.AreSame(brain.Attack, brain.Current);
        }

        [Test]
        public void RunningAttackContinuesEvenIfTargetLeavesRange()
        {
            EnterAttack();
            Step(); // Begin
            target.Position = new Vector3(0f, 0f, 10f);
            Step();
            Assert.AreSame(brain.Attack, brain.Current);
            Assert.AreEqual(1, attack.TickCount);
            Assert.AreEqual(0, attack.CancelCount);
        }

        [Test]
        public void FinishedAttackOutOfRangeReturnsToChase()
        {
            EnterAttack();
            Step(); // Begin
            target.Position = new Vector3(0f, 0f, 10f);
            attack.Finish(AttackResult.Completed);
            Step();
            Assert.AreSame(brain.Chase, brain.Current);
            Assert.IsFalse(mover.IsStopped);
        }

        [Test]
        public void FinishedAttackWithoutTargetGoesIdle()
        {
            EnterAttack();
            Step(); // Begin
            attack.Finish(AttackResult.Completed);
            brain.SetTarget(null);
            Step();
            Assert.AreSame(brain.Idle, brain.Current);
        }

        [Test]
        public void StunLastsDurationThenChases()
        {
            brain.SetTarget(target);
            Step(); // → Chase
            brain.Stun(2f);
            Assert.AreSame(brain.Stunned, brain.Current);
            Assert.IsTrue(mover.IsStopped);

            Step(1.9f);
            Assert.AreSame(brain.Stunned, brain.Current);
            Step(0.2f);
            Assert.AreSame(brain.Chase, brain.Current);
        }

        [Test]
        public void AttackStateStunsWhenTickEndsWithWallHit()
        {
            var wallAttack = new WallHitOnFirstTickAttack { Range = 1.5f, WallHitStunTime = 1f };
            brain = new MonsterBrain(mover, body, wallAttack);
            brain.SetTarget(target);
            Step();
            target.Position = new Vector3(0f, 0f, 1f);
            Step(); // → Attack
            Step(); // Begin
            Step(); // Tick → WallHit → Stunned
            Assert.AreSame(brain.Stunned, brain.Current);
            Assert.AreEqual(1f, brain.StunDuration);
        }

        [Test]
        public void DieDuringAttackCancelsAndNotifies()
        {
            bool? reward = null;
            int diedCount = 0;
            brain.Died += r => { reward = r; diedCount++; };

            EnterAttack();
            Step(); // Begin
            brain.Die(false);

            Assert.AreSame(brain.Dead, brain.Current);
            Assert.AreEqual(1, attack.CancelCount);
            Assert.IsTrue(mover.IsStopped);
            Assert.AreEqual(1, diedCount);
            Assert.AreEqual(false, reward);
        }

        [Test]
        public void DeadIsFinal()
        {
            int diedCount = 0;
            brain.Died += _ => diedCount++;
            brain.SetTarget(target);
            brain.Die(true);

            brain.Die(true);
            brain.ChangeState(brain.Chase);
            brain.Stun(1f);
            Step();

            Assert.AreSame(brain.Dead, brain.Current);
            Assert.AreEqual(1, diedCount);
            Assert.IsTrue(brain.GrantReward);
        }

        [Test]
        public void StateChangedReportsPreviousAndCurrent()
        {
            MonsterState from = null, to = null;
            brain.StateChanged += (p, c) => { from = p; to = c; };
            brain.SetTarget(target);
            Step();
            Assert.AreSame(brain.Idle, from);
            Assert.AreSame(brain.Chase, to);
        }

        [Test]
        public void NegativeStunThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => brain.Stun(-1f));
        }

        /// <summary>시작 후 첫 Tick 에서 벽 충돌로 끝나는 공격</summary>
        private sealed class WallHitOnFirstTickAttack : IMonsterAttack
        {
            public float Range { get; set; }
            public bool IsRunning { get; private set; }
            public AttackResult LastResult { get; private set; }
            public float WallHitStunTime { get; set; }
            public bool CanStart(float now) => true;
            public void Begin(IPlayerTarget target, float now) { IsRunning = true; LastResult = AttackResult.None; }
            public void Tick(float now, float deltaTime) { IsRunning = false; LastResult = AttackResult.WallHit; }
            public void Cancel() { IsRunning = false; LastResult = AttackResult.Cancelled; }
        }
    }
}
