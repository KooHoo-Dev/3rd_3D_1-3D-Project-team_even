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
using Object = UnityEngine.Object;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>런타임 베이크 NavMesh + 실제 Agent 로 도약/충돌/착지/취소와 기존 FSM 연결을 검증한다.</summary>
    public sealed class PounceAttackTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private NavMeshData navData;
        private NavMeshDataInstance navInstance;
        private GameObject monster;
        private GameObject floor;
        private NavMeshAgent agent;
        private NavMeshMoveExecutor executor;
        private MonsterMovement movement;
        private MovementProfile profile;
        private MonsterBody body;
        private PounceAttack attack;
        private Player target;
        private Vector3 start;
        private static readonly AttackSpec Spec = new AttackSpec(AttackShape.Circle, 18, 1, 8f, 0.6f, 1.2f, 8f, radius: 2f);
        private const float Takeoff = 0.61f;
        private const float Landing = Takeoff + 6.2f + 0.001f;

        private sealed class Player : IPlayerTarget, IDamageable
        {
            public Transform Root { get; set; }
            public Transform ViewPoint => Root;
            public PlayerTrackingInfo PlayerTrackingInfo => new PlayerTrackingInfo(Root.position, Root.forward, Root.rotation);
            public Vector3 Position { get => Root.position; set => Root.position = value; }
            public Faction Faction => Faction.Player;
            public bool IsAlive => true;
            public int Hits;
            public void TakeDamage(in DamageInfo info) { Assert.AreEqual(18, info.Amount); Hits++; }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            floor = Box("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(40f, 0.2f, 40f));
            Bake(new Vector3(40f, 0.2f, 40f), Matrix4x4.TRS(floor.transform.position, Quaternion.identity, Vector3.one));
            var playerObject = new GameObject("Player");
            objects.Add(playerObject);
            playerObject.AddComponent<Camera>();
            var playerCollider = playerObject.AddComponent<CapsuleCollider>();
            playerCollider.center = Vector3.up;
            target = new Player { Root = playerObject.transform, Position = new Vector3(0f, 0f, 6f) };
            monster = new GameObject("PounceMonster");
            objects.Add(monster);
            monster.SetActive(false);
            agent = monster.AddComponent<NavMeshAgent>();
            executor = monster.AddComponent<NavMeshMoveExecutor>();
            var selfCollider = monster.AddComponent<CapsuleCollider>();
            selfCollider.center = Vector3.up;
            movement = monster.AddComponent<MonsterMovement>();
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            movement.ConfigureDirect(profile, target);
            var settings = monster.AddComponent<PounceSettings>();
            monster.SetActive(true);
            yield return null;
            Assert.IsTrue(agent.isOnNavMesh);
            movement.SetSuspended(true);
            start = monster.transform.position;
            body = new MonsterBody(monster.transform, executor);
            attack = new PounceAttack(Spec, body, _ => target, settings) { HitTimeRatio = 0f, WindupTurnSpeed = 360f };
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            attack?.Cancel();
            foreach (var obj in objects) Object.DestroyImmediate(obj);
            objects.Clear();
            Object.DestroyImmediate(profile);
            navInstance.Remove();
            Object.DestroyImmediate(navData);
        }

        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var obj = new GameObject(name);
            objects.Add(obj);
            obj.transform.position = position;
            obj.AddComponent<BoxCollider>().size = size;
            return obj;
        }

        private void Bake(Vector3 size, Matrix4x4 transform)
        {
            if (navInstance.valid) navInstance.Remove();
            if (navData != null) Object.DestroyImmediate(navData);
            var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = transform, area = 0 };
            navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { source },
                                                     new Bounds(Vector3.zero, new Vector3(60f, 30f, 60f)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(navData);
            navInstance = NavMesh.AddNavMeshData(navData);
        }

        private void Launch()
        {
            attack.Begin(target, 0f);
            attack.Tick(0.6f, 0.6f);
            attack.Tick(Takeoff, 0.01f);
        }

        [Test]
        public void TargetIsCapturedAtTakeoffAndNeverHomedDuringFlight()
        {
            attack.Begin(target, 0f);
            target.Position = new Vector3(0f, 0f, 7f);
            attack.Tick(0.6f, 0.6f);
            attack.Tick(Takeoff, 0.01f);
            Assert.IsTrue(executor.HasAttackMotion);
            Assert.IsFalse(agent.updatePosition);
            target.Position = new Vector3(0f, 0f, 15f);
            attack.Tick(Takeoff + 3.1f, 3.1f);
            Assert.AreEqual(3.5f, body.Position.z, 0.02f);
            Assert.AreEqual(start.y + 1.5f, body.Position.y, 0.03f);
            Assert.AreEqual(0, target.Hits);
            attack.Tick(Landing, 3.101f);
            Assert.AreEqual(7f, body.Position.z, 0.02f);
            Assert.AreEqual(0, target.Hits);
            Assert.IsTrue(agent.updatePosition);
            Assert.IsTrue(agent.isOnNavMesh);
        }

        [Test]
        public void ActiveLastsEntireTableDurationAndHitsOnceOnlyOnLanding()
        {
            Launch();
            attack.Tick(Takeoff + 6.19f, 6.19f);
            Assert.AreEqual(0, target.Hits);
            Assert.AreEqual(SkillPhase.Active, attack.Phase);
            attack.Tick(Landing, 0.011f);
            Assert.AreEqual(1, target.Hits);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase);
            attack.Tick(Landing + 0.5f, 0.5f);
            Assert.AreEqual(1, target.Hits);
            attack.Tick(Landing + 1.201f, 0.701f);
            Assert.IsFalse(attack.IsAttacking);
            Assert.AreEqual(8f, attack.Cooldown);
        }

        [TestCase(2.1f, 0f)]
        [TestCase(0f, 0.9f)]
        public void PlayerCanDodgeRadiusOrHeightBeforeLanding(float x, float height)
        {
            Launch();
            target.Position = new Vector3(x, height, 6f);
            attack.Tick(Landing, 6.201f);
            Assert.AreEqual(0, target.Hits);
        }

        [Test]
        public void OutOfRangeDuringWindupStillJumpsEightMeters()
        {
            attack.Begin(target, 0f);
            target.Position = new Vector3(0f, 0f, 16f);
            attack.Tick(0.6f, 0.6f);
            attack.Tick(Takeoff, 0.01f);
            attack.Tick(Landing, 6.201f);
            Assert.AreEqual(start.z + 8f, body.Position.z, 0.02f);
            Assert.AreEqual(0, target.Hits);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WallOrCeilingPreventsTakeoff(bool ceiling)
        {
            if (ceiling) Box("Ceiling", new Vector3(0f, 2.8f, 3f), new Vector3(5f, 0.2f, 8f));
            else Box("Wall", new Vector3(0f, 2f, 3f), new Vector3(5f, 4f, 0.2f));
            Physics.SyncTransforms();
            Launch();
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.That(Vector3.Distance(start, body.Position), Is.LessThan(0.01f));
            attack.Tick(Landing, 6.201f);
            Assert.AreEqual(0, target.Hits);
            Assert.AreEqual(SkillPhase.Recover, attack.Phase);
        }

        [Test]
        public void MissingLandingNavMeshPreventsTakeoff()
        {
            Bake(new Vector3(4f, 0.2f, 4f), Matrix4x4.TRS(floor.transform.position, Quaternion.identity, Vector3.one));
            Assert.IsTrue(agent.Warp(start));
            Launch();
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.AreEqual(0, target.Hits);
        }

        [Test]
        public void MissingPhysicalGroundPreventsTakeoff()
        {
            floor.GetComponent<BoxCollider>().enabled = false;
            Physics.SyncTransforms();
            Launch();
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.AreEqual(0, target.Hits);
        }

        [Test]
        public void ChangedLandingNavMeshUsesTerrainNearestPointAndActualDamageCenter()
        {
            Launch();
            Bake(new Vector3(6f, 0.2f, 6f), Matrix4x4.TRS(new Vector3(0f, -0.1f, 12f), Quaternion.identity, Vector3.one));
            Vector3 expected = TerrainGround.ClosestNavMeshPoint(new Vector3(0f, 0f, 6f), body.AttackNavigationFilter);
            attack.Tick(Landing, 6.201f);
            Assert.That(Vector3.Distance(body.Position, expected), Is.LessThan(0.15f));
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.IsTrue(agent.updatePosition);
            Assert.AreEqual(0, target.Hits, "보정 전 목표가 아니라 실제 착지 좌표 중심으로 판정");
        }

        [UnityTest]
        public IEnumerator WholeObjectDisableDefersWarpUntilEnabled()
        {
            Launch();
            attack.Tick(Takeoff + 3.1f, 3.1f);
            monster.SetActive(false);
            attack.Cancel();
            Assert.AreEqual(3f, body.Position.z, 0.05f);
            Assert.IsTrue(agent.updatePosition);
            Assert.IsFalse(executor.HasAttackMotion);
            monster.SetActive(true);
            movement.SetSuspended(true);
            yield return null;
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.AreEqual(3f, body.Position.z, 0.05f);
            Assert.Less(body.Position.y, 0.2f);
        }

        [Test]
        public void NewObstacleDuringFlightStopsWithoutDamageOrWallStun()
        {
            Launch();
            attack.Tick(Takeoff + 1.55f, 1.55f);
            Box("NewWall", new Vector3(0f, 3f, 3.5f), new Vector3(5f, 6f, 0.2f));
            Physics.SyncTransforms();
            attack.Tick(Takeoff + 4f, 2.45f);
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.AreEqual(1.5f, body.Position.z, 0.03f);
            Assert.Less(body.Position.y, 0.2f);
            Assert.AreEqual(0, target.Hits);
            Assert.AreNotEqual(AttackResult.WallHit, attack.LastResult);
            Assert.IsTrue(agent.updatePosition);
        }

        [Test]
        public void CancelDropsBelowCurrentPositionAndRestoresAgent()
        {
            Launch();
            attack.Tick(Takeoff + 3.1f, 3.1f);
            attack.Cancel();
            Assert.AreEqual(3f, body.Position.z, 0.03f);
            Assert.Less(body.Position.y, 0.2f);
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.IsTrue(agent.updatePosition);
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.AreEqual(0, target.Hits);
        }

        [Test]
        public void CancelOffNavMeshUsesTerrainNearestPointToDropDestination()
        {
            Launch();
            attack.Tick(Takeoff + 3.1f, 3.1f);
            Bake(new Vector3(6f, 0.2f, 6f), Matrix4x4.TRS(new Vector3(0f, -0.1f, 11f), Quaternion.identity, Vector3.one));
            Vector3 expected = TerrainGround.ClosestNavMeshPoint(new Vector3(0f, 0f, 3f), body.AttackNavigationFilter);
            attack.Cancel();
            Assert.That(Vector3.Distance(body.Position, expected), Is.LessThan(0.15f));
            Assert.Greater(body.Position.z, 7f);
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.IsTrue(agent.updatePosition);
        }

        [UnityTest]
        public IEnumerator DisableExecutorBeforeAttackCancelThenEnableRestoresPosition()
        {
            Launch();
            attack.Tick(Takeoff + 3.1f, 3.1f);
            executor.enabled = false;
            monster.SetActive(false);
            attack.Cancel();
            Assert.AreEqual(3f, body.Position.z, 0.03f);
            Assert.IsTrue(agent.updatePosition);
            executor.enabled = true;
            monster.SetActive(true);
            yield return null;
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.Less(body.Position.y, 0.2f);
        }

        [Test]
        public void OriginallyDisabledUpdatePositionIsPreserved()
        {
            agent.updatePosition = false;
            Launch();
            attack.Tick(Landing, 6.201f);
            Assert.IsFalse(agent.updatePosition);
            Assert.That(Vector3.Distance(body.Position, agent.nextPosition), Is.LessThan(0.1f));
        }

        [Test]
        public void RootOffsetIsPreservedAfterLanding()
        {
            agent.baseOffset = 0.7f;
            Vector3 elevated = start + Vector3.up * 0.7f;
            Assert.IsTrue(agent.Warp(elevated));
            monster.transform.position = elevated;
            Physics.SyncTransforms();
            Launch();
            attack.Tick(Landing, 6.201f);
            Assert.AreEqual(elevated.y, body.Position.y, 0.05f);
            Assert.IsTrue(agent.isOnNavMesh);
        }

        [Test]
        public void GentleSlopeHasValidLandingAndAgentSync()
        {
            floor.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            Bake(new Vector3(40f, 0.2f, 40f), Matrix4x4.TRS(floor.transform.position, floor.transform.rotation, Vector3.one));
            Vector3 ground = TerrainGround.ClosestNavMeshPoint(Vector3.zero, body.AttackNavigationFilter);
            Assert.IsTrue(agent.Warp(ground));
            Physics.SyncTransforms();
            Launch();
            Assert.IsTrue(executor.HasAttackMotion);
            attack.Tick(Landing, 6.201f);
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.AreEqual(6f, body.Position.z, 0.05f);
            Assert.Less(body.Position.y, -0.2f);
        }

        [TestCase("target-change")]
        [TestCase("target-loss")]
        [TestCase("stun")]
        [TestCase("death")]
        [TestCase("cleanup")]
        public void StateMachineCancellationRestoresGroundAndAgent(string action)
        {
            var brain = new MonsterStateMachine(movement, body, attack);
            brain.SetTarget(target);
            brain.ChangeState(brain.Attack);
            brain.Update(0f, 0f);
            brain.Update(0.6f, 0.6f);
            brain.Update(Takeoff, 0.01f);
            brain.Update(Takeoff + 3.1f, 3.1f);
            switch (action)
            {
                case "target-change": brain.SetTarget(new FakeTarget()); break;
                case "target-loss": brain.SetTarget(null); break;
                case "stun": brain.Stun(1f); break;
                case "death": brain.Die(false); break;
                case "cleanup": brain.Cleanup(); break;
            }
            Assert.IsFalse(executor.HasAttackMotion);
            Assert.IsTrue(agent.updatePosition);
            Assert.IsTrue(agent.isOnNavMesh);
            Assert.AreEqual(3f, body.Position.z, 0.03f);
            Assert.Less(body.Position.y, 0.2f);
            Assert.AreEqual(0, target.Hits);
        }

        [UnityTest]
        public IEnumerator ConsecutiveAttacksAndChaseResumeAfterLanding()
        {
            var brain = new MonsterStateMachine(movement, body, attack);
            brain.SetTarget(target);
            brain.ChangeState(brain.Attack);
            brain.Update(0f, 0f);
            brain.Update(0.6f, 0.6f);
            brain.Update(Takeoff, 0.01f);
            brain.Update(Landing, 6.201f);
            brain.Update(Landing + 1.21f, 1.21f);
            Assert.IsFalse(attack.IsAttacking);
            brain.Update(8.1f, 0.1f);
            Assert.AreEqual(SkillPhase.Windup, attack.Phase);
            brain.Update(8.71f, 0.61f);
            brain.Update(8.72f, 0.01f);
            brain.Update(14.93f, 6.21f);
            brain.Update(16.14f, 1.21f);
            Assert.AreEqual(2, target.Hits);
            target.Position = new Vector3(0f, 0f, 18f);
            brain.Update(16.2f, 0.06f);
            Assert.AreSame(brain.Chase, brain.Current);
            float before = body.Position.z;
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(body.Position.z, before, $"movement={movement.State}, execution={executor.ExecutionState}, stopped={agent.isStopped}, path={agent.hasPath}");
            Assert.IsTrue(agent.isOnNavMesh);
        }
    }
}
