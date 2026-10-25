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
    public sealed class MonsterStateMachineTests
    {
        private GameObject monsterObject;
        private MonsterMovement movement;
        private MovementProfile profile;
        private MonsterBody body;
        private StrikeAttack attack;
        private int beginCount; // 공격 시작(예고 진입) 횟수
        private FakeTarget target;
        private MonsterStateMachine _stateMachine;
        private float now;

        // 예고 0.4s, 회복 0.3s, 쿨다운 1.2s, 사거리 1.5m
        private static readonly AttackSpec Bite = new AttackSpec(
            AttackShape.Melee, damage: 5, hitCount: 1, cooldown: 1.2f,
            telegraphTime: 0.4f, recoveryTime: 0.3f, range: 1.5f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            target = new FakeTarget { Position = new Vector3(0f, 0f, 10f) };
            yield return CreateMonsterWithMovement();
            body = new MonsterBody(monsterObject.transform);
            UseAttack(Bite);
            now = 0f;
        }

        /// <summary>
        /// 몬스터 오브젝트에 실제 이동 부품(NavMeshAgent·NavMeshMoveExecutor·MonsterMovement)을 붙이고 직접 추적 모드로 켠다.
        /// NavMesh 가 없어서 실제로 움직이지는 않는다(Stop 은 조기 반환, Apply 는 경고 1회). 다음 프레임에 Start 가 돌아 직접 모드 허가가 정해진다.
        /// </summary>
        private IEnumerator CreateMonsterWithMovement()
        {
            monsterObject = new GameObject("MonsterStateMachineTests");
            monsterObject.SetActive(false);
            monsterObject.AddComponent<NavMeshAgent>();
            monsterObject.AddComponent<NavMeshMoveExecutor>();
            movement = monsterObject.AddComponent<MonsterMovement>();
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            movement.ConfigureDirect(profile, target);
            monsterObject.SetActive(true);
            yield return null;
        }

        private void UseAttack(in AttackSpec spec)
        {
            attack = new StrikeAttack(spec, body, t => (IDamageable)t);
            beginCount = 0;
            attack.PhaseChanged += (_, phase) => { if (phase == SkillPhase.Windup) beginCount++; };
            _stateMachine = new MonsterStateMachine(movement, body, attack);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(monsterObject);
            UnityEngine.Object.DestroyImmediate(profile);
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

        /// <summary>
        /// 진행 중인 공격을 끝까지 진행한다: 예고 → 공격 → 회복 → 종료.
        /// Bite 기준 Active = 1.2 − 0.4 − 0.3 = 0.5s, 판정은 Active 시작 +0.25s. 경계값을 피해 간격을 잡는다.
        /// </summary>
        private void FinishRunningAttack()
        {
            Step(0.5f); // 예고 끝 → 공격(Active)
            Step(0.3f); // 판정 (+0.3s ≥ 0.25s)
            Step(0.3f); // Active 끝 (+0.6s ≥ 0.5s) → 회복
            Step(0.5f); // 회복 끝 → 종료
        }

        [Test]
        public void StartsIdleAndStopsMover()
        {
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
            Assert.IsTrue(movement.IsSuspended);
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
            Assert.IsFalse(movement.IsSuspended);
        }

        [Test]
        public void ChaseToIdleWhenTargetLost()
        {
            _stateMachine.SetTarget(target);
            Step();
            _stateMachine.SetTarget(null);
            Step();
            Assert.AreSame(_stateMachine.Idle, _stateMachine.Current);
            Assert.IsTrue(movement.IsSuspended);
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
            Assert.IsTrue(movement.IsSuspended);
            Assert.AreEqual(0, beginCount, "전환한 판단에서는 공격을 시작하지 않는다");
        }

        [Test]
        public void AttackBeginsWhenReadyAndFacesTarget()
        {
            EnterAttack();
            target.Position = new Vector3(1f, 0f, 0f); // 사거리 안 측면: 바라보면 정면이 오른쪽이 된다
            Step();
            Assert.AreEqual(1, beginCount);
            Assert.IsTrue(body.Forward == Vector3.right);
        }

        [Test]
        public void ConsecutiveAttackStaysInAttackWithoutStateChange()
        {
            EnterAttack();
            int stateChangedCount = 0;
            _stateMachine.StateChanged += (previous, current) => stateChangedCount++;

            Step(); // Begin
            FinishRunningAttack();
            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
            Assert.AreSame(_stateMachine.Attack, _stateMachine.Current, "완료 틱에 상태를 바꾸지 않는다");

            Step(); // 다음 틱: 사거리 안 + 쿨다운 끝 → 다시 Begin
            Assert.AreEqual(2, beginCount);
            Assert.AreEqual(0, stateChangedCount, "연속 공격 중 Attack ↔ Chase 왕복이 없다");
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
            Assert.IsTrue(attack.IsAttacking);
            Assert.AreEqual(AttackResult.None, attack.LastResult, "취소되지 않았다");
        }

        [Test]
        public void FinishedAttackOutOfRangeReturnsToChase()
        {
            EnterAttack();
            Step(); // Begin
            target.Position = new Vector3(0f, 0f, 10f);
            FinishRunningAttack();
            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
            Assert.AreSame(_stateMachine.Attack, _stateMachine.Current, "완료 틱에는 Attack 에 머문다");

            Step(); // 다음 틱에 사거리 밖 → Chase
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);
            Assert.IsFalse(movement.IsSuspended);
        }

        [Test]
        public void FinishedAttackWithoutTargetGoesIdle()
        {
            EnterAttack();
            Step(); // Begin
            FinishRunningAttack();
            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
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
            Assert.IsTrue(movement.IsSuspended);

            Step(1.9f);
            Assert.AreSame(_stateMachine.Stunned, _stateMachine.Current);
            Step(0.2f);
            Assert.AreSame(_stateMachine.Chase, _stateMachine.Current);
        }

        [Test]
        public void AttackStateStunsWhenTickEndsWithWallHit()
        {
            var wallSpec = new AttackSpec(AttackShape.Melee, damage: 5, hitCount: 1, cooldown: 1f,
                                          telegraphTime: 0f, recoveryTime: 0.3f, range: 1.5f, wallHitStunTime: 1f);
            var wallAttack = new WallHitOnFirstActiveAttack(wallSpec);
            wallAttack.HitTimeRatio = 0f; // 판정 대기 없이 Active 첫 판정 틱에 OnActive
            _stateMachine = new MonsterStateMachine(movement, body, wallAttack);
            _stateMachine.SetTarget(target);
            Step();
            target.Position = new Vector3(0f, 0f, 1f);
            Step(); // → Attack
            Step(); // Begin
            Step(); // 예고(0s) 끝 → 공격
            Step(); // 공격 → WallHit → Stunned
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
            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.IsTrue(movement.IsSuspended);
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

        /// <summary>공격 단계 첫 Tick 에서 벽 충돌로 끝나는 공격 (돌진형 대역)</summary>
        private sealed class WallHitOnFirstActiveAttack : MonsterAttack
        {
            public WallHitOnFirstActiveAttack(in AttackSpec spec) : base(spec, t => (IDamageable)t) { }

            protected override bool OnActive(float now, float deltaTime)
            {
                EndEarly(AttackResult.WallHit);
                return false;
            }
        }
    }
}
