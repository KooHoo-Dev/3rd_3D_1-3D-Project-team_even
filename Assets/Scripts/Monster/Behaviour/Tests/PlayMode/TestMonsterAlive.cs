using System.Collections;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// 대상 상실(= Brain.SetTarget(null), 플레이어 사망 시 MonsterController 가 호출) 뒤 몬스터가 멈추고 피해를 주지 않는지 확인한다.
    /// 실제 MonsterBrain + StrikeAttack + TransformMonsterBody + MonsterMovement(NavMesh 없음) 를 쓰고, 대역은 FakeTarget(피격)만 쓴다.
    /// </summary>
    public sealed class TestMonsterAlive
    {
        // 예고 0.4s, 회복 0.3s, 쿨다운 1.2s, 사거리 1.5m, 피해 5 × 2회
        private static readonly AttackSpec Bite = new AttackSpec(
            AttackShape.Melee, damage: 5, hitCount: 2, cooldown: 1.2f,
            telegraphTime: 0.4f, recoveryTime: 0.3f, range: 1.5f, sourceDefId: 100101);

        private GameObject monsterObject;
        private MonsterMovement movement;
        private MovementProfile profile;
        private FakeTarget target;
        private StrikeAttack attack;
        private MonsterStateMachine _stateMachine;
        private float now;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            target = new FakeTarget { Position = new Vector3(0f, 0f, 1f) };
            yield return CreateMonsterWithMovement();
            attack = new StrikeAttack(Bite, new MonsterBody(monsterObject.transform), t => (IDamageable)t) { ActiveSeconds = 0.5f };
            _stateMachine = new MonsterStateMachine(movement, new MonsterBody(monsterObject.transform), attack);
            now = 0f;
        }

        /// <summary>
        /// 몬스터 오브젝트에 실제 이동 부품(NavMeshAgent·NavMeshMoveExecutor·MonsterMovement)을 붙이고 직접 추적 모드로 켠다.
        /// NavMesh 가 없어서 실제로 움직이지는 않는다(Stop 은 조기 반환, Apply 는 경고 1회). 다음 프레임에 Start 가 돌아 직접 모드 허가가 정해진다.
        /// </summary>
        private IEnumerator CreateMonsterWithMovement()
        {
            monsterObject = new GameObject("TestMonsterAlive");
            monsterObject.SetActive(false);
            monsterObject.AddComponent<NavMeshAgent>();
            monsterObject.AddComponent<NavMeshMoveExecutor>();
            movement = monsterObject.AddComponent<MonsterMovement>();
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            movement.ConfigureDirect(profile, target);
            movement.SetCombatTuning(3.5f, 1.8f); // 이전 MovementProfile 기본 속도·사거리
            monsterObject.SetActive(true);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(monsterObject);
            Object.DestroyImmediate(profile);
        }

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

        // 플레이어 사망 중(군중 목표 null) 스포너가 새 몬스터를 만들면 Activate 가 ViewPoint 에 접근해 NRE 가 났다 (MonsterMovement.cs:210)
        [UnityTest]
        public IEnumerator ActivatingCrowdMonsterWhileCoordinatorHasNoTargetThrowsNothing()
        {
            var coordinatorObject = new GameObject("TestCrowdCoordinator");
            var crowdCoordinator = coordinatorObject.AddComponent<CrowdCoordinator>();
            var crowdMonsterObject = new GameObject("TestCrowdMonster");
            crowdMonsterObject.SetActive(false);
            crowdMonsterObject.AddComponent<NavMeshAgent>();
            crowdMonsterObject.AddComponent<NavMeshMoveExecutor>();
            var crowdMovement = crowdMonsterObject.AddComponent<MonsterMovement>();
            crowdMovement.Configure(profile, crowdCoordinator);

            crowdMonsterObject.SetActive(true);
            yield return null;   // Start → Activate
            yield return null;   // CrowdCoordinator.Update → Think (목표 없음)

            Assert.IsNull(crowdCoordinator.Target);
            Assert.IsTrue(crowdMovement.enabled);
            Object.DestroyImmediate(crowdMonsterObject);
            Object.DestroyImmediate(coordinatorObject);
        }
    }
}
