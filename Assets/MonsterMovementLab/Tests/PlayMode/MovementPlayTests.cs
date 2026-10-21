using System.Collections;
using System.Collections.Generic;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Movement.Lab.Tests
{
    public class MovementPlayTests
    {
        private GameObject root;
        private MovementProfile profile;
        private TransformMovementTarget target;
        private readonly List<MonsterMovement> monsters = new List<MonsterMovement>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            root = new GameObject("Movement test owned root");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0, -0.5f, 0);
            floor.transform.localScale = new Vector3(70, 1, 70);
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            var player = new GameObject("Target proxy");
            player.transform.SetParent(root.transform);
            target = player.AddComponent<TransformMovementTarget>();
            profile = MovementProfile.CreateRuntime(MovementPolicyKind.Melee);
            monsters.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            Object.Destroy(profile);
            yield return null;
        }

        private MonsterMovement Spawn(Vector3 position, CrowdCoordinator coordinator = null)
        {
            var go = new GameObject("Test monster");
            go.SetActive(false);
            go.transform.SetParent(root.transform);
            go.transform.position = position;
            var nav = go.AddComponent<NavMeshAgent>();
            nav.radius = 0.45f;
            nav.height = 1.8f;
            nav.acceleration = 20f;
            nav.angularSpeed = 720f;
            nav.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            go.AddComponent<NavMeshMoveExecutor>();
            var movement = go.AddComponent<MonsterMovement>();
            if (coordinator == null) movement.ConfigureDirect(profile, target);
            else movement.Configure(profile, coordinator);
            go.SetActive(true);
            Assert.That(nav.isOnNavMesh, Is.True);
            monsters.Add(movement);
            return movement;
        }

        [UnityTest]
        public IEnumerator MvpSingleTracksMovingTargetAndStopsWhenInvalid()
        {
            var monster = Spawn(new Vector3(0, 0, 10));
            yield return new WaitForSeconds(2f);
            Assert.That(monster.transform.position.z, Is.LessThan(8f), "실제 이동 필요");
            target.transform.position = new Vector3(7, 0, 0);
            yield return new WaitForSeconds(0.5f);
            Assert.That(monster.LastIntent.Destination.x, Is.EqualTo(7f).Within(0.01f));
            yield return new WaitForSeconds(4f);
            Assert.That(Vector3.Distance(monster.transform.position, target.Position), Is.LessThan(3f));
            target.SetValid(false);
            yield return new WaitForSeconds(0.3f);
            Assert.That(monster.State, Is.EqualTo(MovementState.Idle));
            Assert.That(monster.Velocity.magnitude, Is.LessThan(0.05f));
        }

        [UnityTest]
        public IEnumerator MvpCrowdTracksWithoutPersistentSevereOverlap()
        {
            const int count = 18; // 재현 fixture. 게임의 성능/밸런스 확정값이 아니다.
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2 / count;
                Spawn(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 10f);
            }
            yield return new WaitForSeconds(5f);
            target.transform.position = new Vector3(6, 0, -4);
            yield return new WaitForSeconds(0.5f);
            foreach (var m in monsters)
                Assert.That((m.LastIntent.Destination - target.Position).sqrMagnitude, Is.LessThan(0.01f));

            var severeTime = new float[count, count];
            float maxSevere = 0;
            float begin = Time.time;
            while (Time.time - begin < 7f)
            {
                for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    // 지름의 절반 미만 간격이 1초 이상 지속되는 경우를 이 실험의 심각 중첩으로 정의.
                    bool severe = Vector3.Distance(monsters[i].transform.position, monsters[j].transform.position) < 0.45f;
                    severeTime[i, j] = severe ? severeTime[i, j] + Time.deltaTime : 0f;
                    maxSevere = Mathf.Max(maxSevere, severeTime[i, j]);
                }
                yield return null;
            }
            float maxDistance = 0;
            foreach (var m in monsters)
                maxDistance = Mathf.Max(maxDistance, Vector3.Distance(m.transform.position, target.Position));
            Debug.Log($"MVP fixture agents={count} maxSevereSeconds={maxSevere:F3} maxDistance={maxDistance:F3}");
            Assert.That(maxSevere, Is.LessThan(1f));
            Assert.That(maxDistance, Is.LessThan(6f), "변경된 목표 쪽으로 전원 진행해야 한다");
        }

        private CrowdCoordinator CreateCoordinator()
        {
            var go = new GameObject("Test coordinator");
            go.transform.SetParent(root.transform);
            var coordinator = go.AddComponent<CrowdCoordinator>();
            coordinator.SetTarget(target);
            coordinator.RefreshSlots();
            return coordinator;
        }

        [UnityTest]
        public IEnumerator FinalCandidatesExistWithoutMonstersAndFollowTarget()
        {
            var coordinator = CreateCoordinator();
            Assert.That(coordinator.SlotCount, Is.EqualTo(24));
            var first = coordinator.GetSlotSnapshot(0);
            Assert.That(first.Validity, Is.EqualTo(SlotValidity.Valid));
            for (int i = 0; i < coordinator.SlotCount; i++)
                Assert.That(coordinator.GetSlotSnapshot(i).Use, Is.EqualTo(SlotUse.Free));
            target.transform.position = Vector3.right * 3;
            yield return new WaitForSeconds(0.4f);
            var moved = coordinator.GetSlotSnapshot(0);
            Assert.That(moved.Id, Is.EqualTo(first.Id));
            Assert.That((moved.Position - first.Position - Vector3.right * 3).magnitude, Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator FinalCrowdWaitsReentersAndUpdatesActualTargets()
        {
            var coordinator = CreateCoordinator();
            for (int i = 0; i < 18; i++)
            {
                float angle = i * Mathf.PI * 2f / 18;
                Spawn(new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 11f, coordinator);
            }
            yield return new WaitForSeconds(1f);
            var owners = new HashSet<int>();
            int approach = 0, waiting = 0;
            int removedSlot = -1, removedOwner = 0;
            for (int i = 0; i < coordinator.SlotCount; i++)
            {
                var s = coordinator.GetSlotSnapshot(i);
                if (s.Use == SlotUse.Free) continue;
                Assert.That(owners.Add(s.OwnerId), Is.True, "한 개체가 여러 슬롯 소유 불가");
                if (s.Kind == SlotKind.Approach) { approach++; removedSlot = i; removedOwner = s.OwnerId; }
                else waiting++;
            }
            Assert.That(approach, Is.EqualTo(8));
            Assert.That(waiting, Is.GreaterThan(0));
            var removed = monsters.Find(m => m.GetInstanceID() == removedOwner);
            removed.gameObject.SetActive(false);
            Assert.That(coordinator.GetSlotSnapshot(removedSlot).Use, Is.EqualTo(SlotUse.Free));
            yield return new WaitForSeconds(0.6f);
            var promoted = coordinator.GetSlotSnapshot(removedSlot);
            Assert.That(promoted.OwnerId, Is.Not.EqualTo(removedOwner));
            Assert.That(promoted.Use, Is.Not.EqualTo(SlotUse.Free), "외곽 개체가 빈 접근 슬롯 재사용");

            target.transform.position += Vector3.right * 2;
            float maxTick = 0, sumTick = 0;
            int frames = 0;
            float begin = Time.time;
            while (Time.time - begin < 8f)
            {
                var ms = coordinator.GetSnapshot().LastTickMs;
                maxTick = Mathf.Max(maxTick, ms); sumTick += ms; frames++;
                yield return null;
            }
            int occupied = 0;
            foreach (var m in monsters)
            {
                if (!m.isActiveAndEnabled || !m.Directive.HasSlotPosition) continue;
                Assert.That((m.LastIntent.Destination - m.Directive.SlotPosition).magnitude, Is.LessThan(0.01f));
                if (Vector3.Distance(m.transform.position, m.Directive.SlotPosition) < coordinator.Settings.Slots.ArrivalDistance)
                    occupied++;
            }
            Debug.Log($"Final fixture agents=17 occupied={occupied} tickMeanMs={sumTick / frames:F4} tickMaxMs={maxTick:F4}");
            Assert.That(occupied, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator FinalFixedWallRoutesAndInvalidCandidatesAreRejected()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0, 1, 3);
            wall.transform.localScale = new Vector3(8, 2, 1.5f);
            root.GetComponent<NavMeshSurface>().BuildNavMesh();
            var coordinator = CreateCoordinator();
            int invalid = 0;
            for (int i = 0; i < coordinator.SlotCount; i++)
                if (coordinator.GetSlotSnapshot(i).Validity != SlotValidity.Valid) invalid++;
            Assert.That(invalid, Is.GreaterThan(0), "벽 내부 기하 후보를 유효 슬롯으로 표시하면 안 된다");
            var monster = Spawn(new Vector3(0, 0, 10));
            yield return new WaitForSeconds(0.5f);
            var nav = monster.GetComponent<NavMeshAgent>();
            Assert.That(nav.pathStatus, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(nav.path.corners.Length, Is.GreaterThan(2));
            yield return new WaitForSeconds(9f);
            Assert.That(Vector3.Distance(monster.transform.position, target.Position), Is.LessThan(3f));
        }

        [UnityTest]
        public IEnumerator FinalTargetLossClearsReservationsAndStopsMovement()
        {
            var coordinator = CreateCoordinator();
            var monster = Spawn(Vector3.forward * 9, coordinator);
            yield return new WaitForSeconds(0.5f);
            Assert.That(monster.Directive.HasSlotPosition, Is.True);
            target.SetValid(false);
            yield return new WaitForSeconds(0.5f);
            Assert.That(monster.LastIntent.HasDestination, Is.False);
            for (int i = 0; i < coordinator.SlotCount; i++)
                Assert.That(coordinator.GetSlotSnapshot(i).Use, Is.EqualTo(SlotUse.Free));
        }

        [UnityTest]
        public IEnumerator FinalFlankReachesAssignedSlotFromFront()
        {
            Object.Destroy(profile);
            profile = MovementProfile.CreateRuntime(MovementPolicyKind.Flank);
            var coordinator = CreateCoordinator();
            var monster = Spawn(Vector3.forward * 15, coordinator);
            yield return new WaitForSeconds(0.5f);
            Assert.That(Mathf.Abs(monster.LastIntent.Destination.x), Is.GreaterThan(5f));
            yield return new WaitForSeconds(6f);
            Assert.That(monster.Directive.HasSlotPosition, Is.True);
            Assert.That(Vector3.Distance(monster.transform.position, monster.Directive.SlotPosition),
                Is.LessThan(coordinator.Settings.Slots.ArrivalDistance));
        }
    }
}
