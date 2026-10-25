using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// 대상 상실(= Brain.SetTarget(null), 플레이어 사망 시 MonsterController 가 호출) 뒤 몬스터가 멈추고 피해를 주지 않는지 확인한다.
    /// 실제 MonsterBrain + StrikeAttack + TransformMonsterBody 를 쓰고, 대역은 FakeTarget(피격)과 FakeMover(NavMesh 필요)만 쓴다.
    /// </summary>
    public sealed class TestMonsterAlive
    {
        // 예고 0.4s, 회복 0.3s, 쿨다운 1.2s, 사거리 1.5m, 피해 5 × 2회
        private static readonly AttackSpec Bite = new AttackSpec(
            AttackShape.Melee, damage: 5, hitCount: 2, cooldown: 1.2f,
            telegraphTime: 0.4f, recoveryTime: 0.3f, range: 1.5f, sourceDefId: 100101);

        private GameObject monsterObject;
        private FakeTarget target;
        private StrikeAttack attack;
        private MonsterStateMachine _stateMachine;
        private float now;

        [SetUp]
        public void SetUp()
        {
            monsterObject = new GameObject("TestMonsterAlive");
            target = new FakeTarget { Position = new Vector3(0f, 0f, 1f) };
            attack = new StrikeAttack(Bite, new TransformMonsterBody(monsterObject.transform), t => (IDamageable)t);
            _stateMachine = new MonsterStateMachine(new FakeMover(), new TransformMonsterBody(monsterObject.transform), attack);
            now = 0f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(monsterObject);

        private void Step(float deltaTime = 0.1f)
        {
            now += deltaTime;
            _stateMachine.Update(now, deltaTime);
        }

        private void StepUntilAttackRunning()
        {
            _stateMachine.SetTarget(target);
            for (int i = 0; i < 10 && attack.IsAttacking == false; i++) Step();
            Assert.IsTrue(attack.IsAttacking, "사전 조건: 공격이 진행 중이어야 한다");
        }

        [Test]
        public void LosingTargetDuringAttackCancelsAttackAndDealsNoDamage()
        {
            StepUntilAttackRunning();

            _stateMachine.SetTarget(null);
            for (int i = 0; i < 20; i++) Step();

            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(0, target.Received.Count);
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
        }

        [Test]
        public void LosingTargetWhileChasingReturnsToIdle()
        {
            target.Position = new Vector3(0f, 0f, 10f);
            _stateMachine.SetTarget(target);
            Step();
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);

            _stateMachine.SetTarget(null);

            Assert.IsFalse(_stateMachine.HasTarget);
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
        }

        [Test]
        public void WithoutTargetBrainStaysIdleForever()
        {
            _stateMachine.SetTarget(target);
            _stateMachine.SetTarget(null);

            for (int i = 0; i < 30; i++) Step();

            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
            Assert.AreEqual(0, target.Received.Count);
        }
    }
}
