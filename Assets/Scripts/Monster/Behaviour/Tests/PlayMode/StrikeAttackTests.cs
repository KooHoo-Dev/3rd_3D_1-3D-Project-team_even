using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class StrikeAttackTests
    {
        private GameObject monsterObject;
        private MonsterMovement movement;
        private MovementProfile profile;
        private MonsterBody body;
        private FakeTarget target;
        private StrikeAttack attack;
        private List<SkillPhase> phases;

        // 예고 0.4s, 회복 0.3s, 쿨다운 1.2s(회복 끝부터), 사거리 1.5m, 피해 5 × 2회. Active 0.5s 는 SetUp 에서 넣는다
        private static readonly AttackSpec Bite = new AttackSpec(
            AttackShape.Melee, damage: 5, hitCount: 2, cooldown: 1.2f,
            telegraphTime: 0.4f, recoveryTime: 0.3f, range: 1.5f, sourceDefId: 100101);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            target = new FakeTarget { Position = new Vector3(0f, 0f, 1f) };
            yield return CreateMonsterWithMovement();
            body = new MonsterBody(monsterObject.transform);
            attack = new StrikeAttack(Bite, body, t => (IDamageable)t) { ActiveSeconds = 0.5f };
            phases = new List<SkillPhase>();
            attack.PhaseChanged += (_, p) => phases.Add(p);
        }

        /// <summary>
        /// 몬스터 오브젝트에 실제 이동 부품(NavMeshAgent·NavMeshMoveExecutor·MonsterMovement)을 붙이고 직접 추적 모드로 켠다.
        /// NavMesh 가 없어서 실제로 움직이지는 않는다(Stop 은 조기 반환, Apply 는 경고 1회). 다음 프레임에 Start 가 돌아 직접 모드 허가가 정해진다.
        /// </summary>
        private IEnumerator CreateMonsterWithMovement()
        {
            monsterObject = new GameObject("StrikeAttackTests");
            monsterObject.SetActive(false);
            monsterObject.AddComponent<NavMeshAgent>();
            monsterObject.AddComponent<NavMeshMoveExecutor>();
            movement = monsterObject.AddComponent<MonsterMovement>();
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            movement.ConfigureDirect(profile, target);
            monsterObject.SetActive(true);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(monsterObject);
            UnityEngine.Object.DestroyImmediate(profile);
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

            // Active = ActiveSeconds 0.5s (0.4 ~ 0.9), 판정 0.65
            attack.Tick(0.7f, 0.3f);
            Assert.AreEqual(SkillPhase.Active, attack.Phase, "판정 뒤에도 Active 시간이 끝날 때까지 유지");

            attack.Tick(1.0f, 0.3f);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase);

            attack.Tick(1.2f, 0.2f);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase, "회복 시간 전");

            attack.Tick(1.35f, 0.15f);
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
        public void WindupTurnsTowardTargetAtTurnSpeed()
        {
            target.Position = new Vector3(1f, 0f, 0f); // 정면에서 오른쪽 90°
            attack.WindupTurnSpeed = 300f;
            attack.Begin(target, 0f);
            Assert.IsTrue(body.Forward == Vector3.forward, "Begin 은 즉시 돌지 않는다");

            attack.Tick(0.1f, 0.1f);
            Assert.AreEqual(30f, Vector3.Angle(Vector3.forward, body.Forward), 0.1f, "300도/초 × 0.1s");

            attack.Tick(0.3f, 0.2f);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, body.Forward), 0.1f, "목표를 넘지 않고 멈춘다");
        }

        [Test]
        public void DodgingOutOfRangeDuringWindupAvoidsDamage()
        {
            attack.Begin(target, 0f);
            target.Position = new Vector3(0f, 0f, 3f);
            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.7f, 0.3f);
            attack.Tick(1.0f, 0.3f);
            Assert.AreEqual(0, target.Received.Count);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase, "빗나가도 회복(반격 시간)은 진행");
        }

        [Test]
        public void HitRangeRatioExtendsMeleeHitRangeOnly()
        {
            // 진입 거리 1.5m, 비율 2 → 판정 거리 3m. 사거리(Range)는 그대로
            attack.HitRangeRatio = 2f;
            attack.Begin(target, 0f);
            target.Position = new Vector3(0f, 0f, 2.9f);
            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.7f, 0.3f);
            Assert.AreEqual(2, target.Received.Count, "진입 거리 밖이어도 판정 거리 안이면 맞는다");
            Assert.AreEqual(1.5f, attack.Range, "진입 거리는 Spec.Range 그대로");
        }

        [Test]
        public void TargetBeyondHitRangeIsNotDamaged()
        {
            attack.HitRangeRatio = 2f;
            attack.Begin(target, 0f);
            target.Position = new Vector3(0f, 0f, 3.1f);
            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.7f, 0.3f);
            Assert.AreEqual(0, target.Received.Count, "판정 거리 3m 밖");
        }

        [Test]
        public void DeadTargetIsNotDamaged()
        {
            // 생존 판정은 공격이 아니라 소유자 몫이다: 사망 = Brain.SetTarget(null) 이 진행 중인 공격을 취소한다
            var brain = new MonsterStateMachine(movement, body, attack);
            brain.SetTarget(target);
            attack.Begin(target, 0f);
            brain.SetTarget(null);
            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.AreEqual(0, target.Received.Count);
        }

        [Test]
        public void CooldownCountsFromRecoveryEnd()
        {
            Assert.IsTrue(attack.CanStart(0f));
            attack.Begin(target, 0f);
            Assert.IsFalse(attack.CanStart(0.1f), "진행 중");

            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.7f, 0.3f);
            attack.Tick(0.95f, 0.25f);
            attack.Tick(1.3f, 0.35f); // 회복 끝 → 완료 (1.3s)
            Assert.IsFalse(attack.IsAttacking);
            Assert.IsFalse(attack.CanStart(2.4f), "회복 끝 1.3 + 쿨다운 1.2 = 2.5 전");
            Assert.IsTrue(attack.CanStart(2.5f));
        }

        [Test]
        public void CancelCooldownCountsFromBegin()
        {
            attack.Begin(target, 0f);
            attack.Tick(0.4f, 0.4f);
            attack.Cancel();
            Assert.IsFalse(attack.CanStart(1.1f), "Begin 0 + 쿨다운 1.2 전");
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
            Assert.Throws<InvalidOperationException>(() => attack.Tick(0.7f, 0.3f));
        }

        [Test]
        public void HitHappensOnceAtActiveMiddle()
        {
            attack.Begin(target, 0f);
            attack.Tick(0.4f, 0.4f); // → Active (0.4 ~ 0.9), 판정 0.65

            attack.Tick(0.6f, 0.2f);
            Assert.AreEqual(0, target.Received.Count, "판정 시각 전");

            attack.Tick(0.7f, 0.1f);
            Assert.AreEqual(2, target.Received.Count, "판정 1번 × HitCount 2");

            attack.Tick(0.8f, 0.1f);
            attack.Tick(0.95f, 0.15f);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase);
            Assert.AreEqual(2, target.Received.Count, "Active 끝까지 다시 판정하지 않는다");
        }

        [Test]
        public void ActiveSecondsChangeAppliesFromNextBegin()
        {
            attack.Begin(target, 0f);
            attack.ActiveSeconds = 1.3f;
            attack.Cooldown = 0f;
            Assert.AreEqual(0.5f, attack.ActiveDuration, 1e-4f, "진행 중 주기는 Begin 때 Active 길이(0.5)를 쓴다");

            attack.Tick(0.4f, 0.4f);
            attack.Tick(0.7f, 0.3f);
            attack.Tick(0.95f, 0.25f);
            attack.Tick(1.3f, 0.35f);
            Assert.IsFalse(attack.IsAttacking);
            Assert.IsFalse(attack.CanStart(2.4f), "진행 중 주기는 Begin 때 쿨다운(1.2)을 쓴다");
            Assert.IsTrue(attack.CanStart(2.5f));

            attack.Begin(target, 2.5f);
            Assert.AreEqual(1.3f, attack.ActiveDuration, 1e-4f, "다음 주기부터 새 Active 길이");
        }

        [Test]
        public void CooldownChangeDoesNotChangeActiveDuration()
        {
            attack.Cooldown = 5f;
            attack.Begin(target, 0f);
            Assert.AreEqual(0.5f, attack.ActiveDuration, 1e-4f);
        }

        [Test]
        public void RangeComesFromSpec()
        {
            Assert.AreEqual(1.5f, attack.Range);
        }

        [Test]
        public void BrainWithStrikeAttackDamagesTarget()
        {
            var brain = new MonsterStateMachine(movement, body, attack);
            brain.SetTarget(target);
            float now = 0f;
            for (int i = 0; i < 40; i++)
            {
                now += 0.1f;
                brain.Update(now, 0.1f);
            }

            // 0.1 Chase, 0.2 Attack, 0.3 Begin, ~0.7 Active, ~1.0 판정, ~1.2 회복, ~1.5 종료 → 쿨다운 1.2 대기,
            // ~2.7 재시작, ~3.1 Active, ~3.4 판정, ~3.9 종료 → 다음 판정은 5s 이후
            Assert.AreEqual(4, target.Received.Count, "4.0s 동안 2번 공격 × HitCount 2");
            Assert.AreSame(brain.Attack, brain.Current);
        }

        private void RunToRecover()
        {
            attack.Begin(target, 0f);
            attack.Tick(0.4f, 0.4f); // → Active
            attack.Tick(0.7f, 0.3f); // 판정
            attack.Tick(1.0f, 0.3f); // → 회복
        }
    }
}
