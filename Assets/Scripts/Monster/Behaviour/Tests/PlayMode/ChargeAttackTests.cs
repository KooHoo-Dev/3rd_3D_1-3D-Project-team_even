using System.Collections;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// 돌진(ChargeAttack): 통과·접촉 판정(순수 계산)과, 런타임에 구운 NavMesh 위에서의 실제 돌진(접촉 1회, 감속 정지, 벽 기절, 취소, Idle 준비 동작).
    /// ChargeSettings 는 기본값을 쓴다 (돌진 12m/s, 보정 45도/초, 감속 15m/s², 최대 30m, 준비 회전 90도/초, 준비 걷기 1.5m/s).
    /// </summary>
    public sealed class ChargeAttackTests
    {
        private const float TickSeconds = 0.02f;

        // 예고 0.2s, 회복 0, 쿨다운 3s, 사거리 15m, 폭 1m(접촉 반경 0.5m), 피해 25 × 1회, 넉백 5m, 벽 기절 2s
        private static readonly AttackSpec HeadRam = new AttackSpec(
            AttackShape.Line, damage: 25, hitCount: 1, cooldown: 3f,
            telegraphTime: 0.2f, recoveryTime: 0f, range: 15f, width: 1f, knockbackDistance: 5f,
            wallHitStunTime: 2f, sourceDefId: 200101);

        private GameObject groundObject;
        private GameObject wallObject;
        private NavMeshSurface surface;
        private GameObject monsterObject;
        private MonsterMovement movement;
        private MovementProfile profile;
        private MonsterBody body;
        private FakeTarget target;
        private ChargeAttack attack;
        private float now;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            groundObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundObject.transform.localScale = new Vector3(10f, 1f, 10f); // 100m × 100m
            surface = groundObject.AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();

            target = new FakeTarget { Position = new Vector3(0f, 0f, 4f) };
            monsterObject = new GameObject("ChargeAttackTests");
            monsterObject.SetActive(false);
            monsterObject.AddComponent<NavMeshAgent>();
            monsterObject.AddComponent<NavMeshMoveExecutor>();
            movement = monsterObject.AddComponent<MonsterMovement>();
            var settings = monsterObject.AddComponent<ChargeSettings>();
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            movement.ConfigureDirect(profile, target);
            monsterObject.SetActive(true);
            yield return null;

            movement.SetSuspended(true); // Attack·Idle 상태처럼 경로 이동을 멈춘다
            body = new MonsterBody(monsterObject.transform);
            attack = new ChargeAttack(HeadRam, body, t => (IDamageable)t, movement, settings);
            attack.HitTimeRatio = 0f;
            now = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(monsterObject);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(groundObject);
            if (wallObject != null) Object.DestroyImmediate(wallObject);
        }

        // ── 순수 판정 ──

        [Test]
        public void PastTargetNeeds155Degrees()
        {
            Vector3 monster = Vector3.zero;
            Vector3 at150 = Quaternion.Euler(0f, 150f, 0f) * Vector3.forward;
            Vector3 at160 = Quaternion.Euler(0f, 160f, 0f) * Vector3.forward;

            Assert.IsFalse(ChargeAttack.IsPastTarget(Vector3.forward, monster, at150 * 5f));
            Assert.IsTrue(ChargeAttack.IsPastTarget(Vector3.forward, monster, at160 * 5f));
            Assert.IsTrue(ChargeAttack.IsPastTarget(Vector3.forward, monster, new Vector3(0f, 0f, -5f)), "바로 뒤");
            Assert.IsFalse(ChargeAttack.IsPastTarget(Vector3.forward, monster, new Vector3(0f, 0f, 5f)), "바로 앞");
            Assert.IsFalse(ChargeAttack.IsPastTarget(Vector3.forward, monster, new Vector3(0f, 3f, 0.005f)), "수평 0.01m 미만은 판정 안 함");
        }

        [Test]
        public void TouchesWhenSegmentPassesNearTarget()
        {
            Vector3 from = Vector3.zero;
            Vector3 to = new Vector3(0f, 0f, 10f);

            Assert.IsTrue(ChargeAttack.TouchesDuringMove(from, to, new Vector3(0.4f, 0f, 5f), 0.5f), "한 틱에 건너뛴 플레이어도 닿음");
            Assert.IsFalse(ChargeAttack.TouchesDuringMove(from, to, new Vector3(0.6f, 0f, 5f), 0.5f), "옆으로 반경 밖");
            Assert.IsFalse(ChargeAttack.TouchesDuringMove(from, to, new Vector3(0f, 0f, -0.6f), 0.5f), "시작점 뒤 반경 밖");
            Assert.IsTrue(ChargeAttack.TouchesDuringMove(from, to, new Vector3(0f, 0f, 10.4f), 0.5f), "끝점 앞 반경 안");
            Assert.IsTrue(ChargeAttack.TouchesDuringMove(from, from, new Vector3(0.3f, 0f, 0f), 0.5f), "제자리면 점 거리");
        }

        // ── NavMesh 위 실제 돌진 ──

        [UnityTest]
        public IEnumerator ChargeHitsOnceThenBrakesToStop()
        {
            yield return RunWholeCycle();

            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
            Assert.AreEqual(1, target.Received.Count, "접촉 피해는 주기당 1회");
            Assert.AreEqual(25, target.Received[0].Amount);
            Assert.That(body.Position.z, Is.GreaterThan(4f), "부딪힌 뒤에도 감속하며 앞으로 밀고 나간다");
            Assert.AreEqual(0f, attack.BodyMoveSpeed);
        }

        [UnityTest]
        public IEnumerator ChargeHitKnocksTargetBackAlongCharge()
        {
            yield return RunWholeCycle();

            Assert.AreEqual(1, target.Knockbacks.Count, "넉백도 주기당 1회");
            Assert.AreEqual(5f, target.Knockbacks[0].Distance);
            Assert.That(Vector3.Angle(Vector3.forward, target.Knockbacks[0].Direction), Is.LessThan(10f), "돌진 방향(+Z)으로 밀린다");
        }

        [UnityTest]
        public IEnumerator KilledTargetIsNotKnockedBack()
        {
            target.DiesOnHit = true;

            yield return RunWholeCycle();

            Assert.AreEqual(1, target.Received.Count);
            Assert.AreEqual(0, target.Knockbacks.Count, "이번 피해로 죽었으면 밀지 않는다");
        }

        [UnityTest]
        public IEnumerator MissedChargeBrakesWithoutDamage()
        {
            attack.Begin(target, now);
            yield return TickUntilActiveStarted();
            target.Position = new Vector3(3f, 0f, 4f); // 방향을 잡은 뒤 옆으로 피함 → 보정 45도/초로는 닿지 못한다

            yield return TickUntilFinished(6f);

            Assert.AreEqual(AttackResult.Completed, attack.LastResult);
            Assert.AreEqual(0, target.Received.Count);
            Assert.AreEqual(0, target.Knockbacks.Count);
            Assert.AreEqual(0f, attack.BodyMoveSpeed);
        }

        [UnityTest]
        public IEnumerator WallEndsChargeWithWallHit()
        {
            // 플레이어와 몬스터 사이에 높은 벽을 두고 NavMesh 를 다시 굽는다
            wallObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallObject.transform.position = new Vector3(0f, 1f, 6f);
            wallObject.transform.localScale = new Vector3(20f, 2f, 1f);
            surface.BuildNavMesh();
            yield return null;
            target.Position = new Vector3(0f, 0f, 12f);

            yield return RunWholeCycle();

            Assert.AreEqual(AttackResult.WallHit, attack.LastResult);
            Assert.AreEqual(0, target.Received.Count);
            Assert.That(body.Position.z, Is.LessThan(6f), "벽을 넘지 않는다");
        }

        [UnityTest]
        public IEnumerator CancelStopsCharge()
        {
            attack.Begin(target, now);
            yield return TickUntilActiveStarted();
            TickOnce();
            Assert.That(attack.BodyMoveSpeed, Is.GreaterThan(0f), "달리는 중");

            attack.Cancel();

            Assert.AreEqual(AttackResult.Cancelled, attack.LastResult);
            Assert.AreEqual(0f, attack.BodyMoveSpeed);
        }

        [UnityTest]
        public IEnumerator PreparationTurnsAndWalksUntilFacingTarget()
        {
            target.Position = new Vector3(-10f, 0f, 0f); // 정면(+Z)에서 왼쪽 90°
            Vector3 start = body.Position;

            for (int i = 0; i < 50 && attack.IsPrepared == false; i++)
            {
                attack.TickPreparation(target, 0.1f);
                yield return null;
            }

            Assert.IsTrue(attack.IsPrepared);
            Vector3 toTarget = target.Position - body.Position;
            toTarget.y = 0f;
            Assert.That(Vector3.Angle(body.Forward, toTarget), Is.LessThanOrEqualTo(30f));
            Assert.That(Vector3.Distance(start, body.Position), Is.GreaterThan(0.1f), "돌면서 걷는다");
            Assert.AreEqual(0f, attack.BodyMoveSpeed, "준비가 끝나면 멈춘다");
        }

        // ── 진행 도우미 ──

        private IEnumerator RunWholeCycle()
        {
            attack.Begin(target, now);
            yield return TickUntilFinished(4f);
        }

        private IEnumerator TickUntilActiveStarted()
        {
            while (attack.Phase == SkillPhase.Windup)
            {
                TickOnce();
                yield return null;
            }
            TickOnce(); // Active 첫 OnActive: 방향 재설정 + 달리기 시작
            yield return null;
        }

        private IEnumerator TickUntilFinished(float maxSeconds)
        {
            float endTime = now + maxSeconds;
            while (attack.IsAttacking && now < endTime)
            {
                TickOnce();
                yield return null;
            }
            Assert.IsFalse(attack.IsAttacking, "제한 시간 안에 주기가 끝나야 한다");
        }

        private void TickOnce()
        {
            now += TickSeconds;
            attack.Tick(now, TickSeconds);
        }
    }
}
