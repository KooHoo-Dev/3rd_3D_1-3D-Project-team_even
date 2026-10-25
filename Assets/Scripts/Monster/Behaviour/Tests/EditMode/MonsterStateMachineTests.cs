using System;
using HybridShooter.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    public sealed class MonsterStateMachineTests
    {
        private FakeMover mover;
        private FakeBody body;
        private FakeAttack attack;
        private FakeTarget target;
        private MonsterStateMachine _stateMachine;
        private float now;

        [SetUp]
        public void SetUp()
        {
            mover = new FakeMover();
            body = new FakeBody { Position = Vector3.zero };
            attack = new FakeAttack { Range = 1.5f };
            target = new FakeTarget { Position = new Vector3(0f, 0f, 10f) };
            _stateMachine = new MonsterStateMachine(mover, body, attack);
            now = 0f;
        }

        private void Step(float dt = 0.1f)
        {
            now += dt;
            _stateMachine.Update(now, dt);
        }

        private void EnterAttack()
        {
            _stateMachine.SetTarget(target);
            Step(); // Idle → Chase
            target.Position = new Vector3(0f, 0f, 1f);
            Step(); // Chase → Attack
            Assert.AreSame(_stateMachine.Attack, _stateMachine.Current);
        }

        [Test]
        public void StartsIdleAndStopsMover()
        {
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
            Assert.IsTrue(mover.IsStopped);
        }

        [Test]
        public void IdleStaysWithoutTarget()
        {
            Step();
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
        }

        [Test]
        public void IdleToChaseWhenTargetSetAndMoverResumes()
        {
            _stateMachine.SetTarget(target);
            Step();
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);
            Assert.IsFalse(mover.IsStopped);
        }

        [Test]
        public void ChaseToIdleWhenTargetLost()
        {
            _stateMachine.SetTarget(target);
            Step();
            _stateMachine.SetTarget(null);
            Step();
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
            Assert.IsTrue(mover.IsStopped);
        }

        [Test]
        public void ChaseStaysWhenOutOfRange()
        {
            _stateMachine.SetTarget(target);
            Step();
            Step();
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);
        }

        [Test]
        public void RangeIgnoresHeight()
        {
            _stateMachine.SetTarget(target);
            Step();
            target.Position = new Vector3(0f, 5f, 1f);
            Step();
            Assert.AreSame(_stateMachine.Attack, _stateMachine.Current);
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
            Assert.AreSame(_stateMachine.Attack, _stateMachine.Current);
        }

        [Test]
        public void RunningAttackContinuesEvenIfTargetLeavesRange()
        {
            EnterAttack();
            Step(); // Begin
            target.Position = new Vector3(0f, 0f, 10f);
            Step();
            Assert.AreSame(_stateMachine.Attack, _stateMachine.Current);
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
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);
            Assert.IsFalse(mover.IsStopped);
        }

        [Test]
        public void FinishedAttackWithoutTargetGoesIdle()
        {
            EnterAttack();
            Step(); // Begin
            attack.Finish(AttackResult.Completed);
            _stateMachine.SetTarget(null);
            Step();
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
        }

        [Test]
        public void StunLastsDurationThenChases()
        {
            _stateMachine.SetTarget(target);
            Step(); // → Chase
            _stateMachine.Stun(2f);
            Assert.AreSame(_stateMachine.Stunned, _stateMachine.Current);
            Assert.IsTrue(mover.IsStopped);

            Step(1.9f);
            Assert.AreSame(_stateMachine.Stunned, _stateMachine.Current);
            Step(0.2f);
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);
        }

        [Test]
        public void AttackStateStunsWhenTickEndsWithWallHit()
        {
            var wallAttack = new WallHitOnFirstTickAttack { Range = 1.5f, WallHitStunTime = 1f };
            _stateMachine = new MonsterStateMachine(mover, body, wallAttack);
            _stateMachine.SetTarget(target);
            Step();
            target.Position = new Vector3(0f, 0f, 1f);
            Step(); // → Attack
            Step(); // Begin
            Step(); // Tick → WallHit → Stunned
            Assert.AreSame(_stateMachine.Stunned, _stateMachine.Current);
            Assert.AreEqual(1f, _stateMachine.StunDuration);
        }

        [Test]
        public void DieDuringAttackCancelsAndNotifies()
        {
            bool? reward = null;
            int diedCount = 0;
            _stateMachine.Died += r => { reward = r; diedCount++; };

            EnterAttack();
            Step(); // Begin
            _stateMachine.Die(false);

            Assert.AreSame(_stateMachine.Dead, _stateMachine.Current);
            Assert.AreEqual(1, attack.CancelCount);
            Assert.IsTrue(mover.IsStopped);
            Assert.AreEqual(1, diedCount);
            Assert.AreEqual(false, reward);
        }

        [Test]
        public void DeadIsFinal()
        {
            int diedCount = 0;
            _stateMachine.Died += _ => diedCount++;
            _stateMachine.SetTarget(target);
            _stateMachine.Die(true);

            _stateMachine.Die(true);
            _stateMachine.ChangeState(_stateMachine.Chase);
            _stateMachine.Stun(1f);
            Step();

            Assert.AreSame(_stateMachine.Dead, _stateMachine.Current);
            Assert.AreEqual(1, diedCount);
            Assert.IsTrue(_stateMachine.GrantReward);
        }

        [Test]
        public void StateChangedReportsPreviousAndCurrent()
        {
            MonsterState from = null, to = null;
            _stateMachine.StateChanged += (p, c) => { from = p; to = c; };
            _stateMachine.SetTarget(target);
            Step();
            Assert.AreSame(_stateMachine.Idle, from);
            Assert.AreSame(_stateMachine.Chase, to);
        }

        [Test]
        public void NegativeStunThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _stateMachine.Stun(-1f));
        }

        /// <summary>시작 후 첫 Tick 에서 벽 충돌로 끝나는 공격</summary>
        private sealed class WallHitOnFirstTickAttack : IMonsterAttack
        {
            public float Range { get; set; }
            public bool IsAttacking { get; private set; }
            public AttackResult LastResult { get; private set; }
            public float WallHitStunTime { get; set; }
            public bool CanStart(float now) => true;
            public void Begin(IPlayerTarget target, float now) { IsAttacking = true; LastResult = AttackResult.None; }
            public void Tick(float now, float deltaTime) { IsAttacking = false; LastResult = AttackResult.WallHit; }
            public void Cancel() { IsAttacking = false; LastResult = AttackResult.Cancelled; }
        }
    }
}
