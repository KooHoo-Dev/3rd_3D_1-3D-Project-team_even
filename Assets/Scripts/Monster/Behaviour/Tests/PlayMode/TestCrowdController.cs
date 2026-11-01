using System.Collections;
using System.Collections.Generic;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// CrowdController 묶음 배정·직접 추적 기본·묶음 수명(스폰 완료 + 전멸 → 파괴)을 확인한다.
    /// 실제 MonsterMovement + NavMeshMoveExecutor(NavMesh 없음) 를 쓰고, 목표는 대부분 null 로 둬 NavMesh 경로 계산을 피한다.
    /// </summary>
    public sealed class TestCrowdController
    {
        private readonly List<Object> createdObjects = new List<Object>();
        private MovementProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<MovementProfile>();
            createdObjects.Add(profile);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
                if (createdObjects[i] != null) Object.DestroyImmediate(createdObjects[i]);
            createdObjects.Clear();
        }

        private CrowdController CreateController()
        {
            var controllerObject = new GameObject("TestCrowdController");
            createdObjects.Add(controllerObject);
            return controllerObject.AddComponent<CrowdController>();
        }

        // controller == null 이면 직접 추적 모드로 켜진다. 사거리 5m 는 슬롯 반경 검사(2.8 + 0.6 + 0.2)를 통과하는 값
        private MonsterMovement CreateMonster(CrowdController controller)
        {
            var monsterObject = new GameObject("TestCrowdMonster");
            createdObjects.Add(monsterObject);
            monsterObject.SetActive(false);
            monsterObject.AddComponent<NavMeshAgent>();
            monsterObject.AddComponent<NavMeshMoveExecutor>();
            var movement = monsterObject.AddComponent<MonsterMovement>();
            movement.Configure(profile, controller);
            movement.SetCombatTuning(3.5f, 5f);
            monsterObject.SetActive(true);
            return movement;
        }

        [UnityTest]
        public IEnumerator MonsterWithoutControllerTracksDirectlyAndIsNotRegistered()
        {
            var otherCrowd = CreateController();
            var movement = CreateMonster(null);
            yield return null;   // Start → Activate
            yield return null;

            var target = new FakeTarget();
            movement.SetTarget(target);

            Assert.AreEqual(0, otherCrowd.Agents.Count);
            Assert.AreEqual(0, otherCrowd.MemberCount);
            Assert.AreSame(target, movement.Target);
            Assert.IsNull(otherCrowd.Target);
            Assert.IsTrue(movement.CanAttack, "직접 추적은 Unrestricted 지시라 거리만 맞으면 공격 허가");
        }

        [UnityTest]
        public IEnumerator MonsterWithControllerJoinsOnlyThatController()
        {
            var crowd = CreateController();
            var otherCrowd = CreateController();
            var movement = CreateMonster(crowd);
            yield return null;
            yield return null;

            var target = new FakeTarget();
            movement.SetTarget(target);

            Assert.AreEqual(1, crowd.Agents.Count);
            Assert.AreEqual(1, crowd.MemberCount);
            Assert.AreEqual(0, otherCrowd.Agents.Count);
            Assert.AreSame(target, crowd.Target, "군중 개체의 목표는 묶음 컨트롤러로 간다");
            Assert.AreSame(target, movement.Target);
        }

        [UnityTest]
        public IEnumerator ControllerSurvivesUntilSpawnFinishedAndAllMembersLeave()
        {
            var crowd = CreateController();
            var first = CreateMonster(crowd);
            var second = CreateMonster(crowd);
            yield return null;
            yield return null;

            first.gameObject.SetActive(false);   // 사망·풀 반환
            second.gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd != null, "소환이 끝나기 전에는 0마리여도 유지");

            crowd.MarkSpawnFinished();
            yield return null;
            Assert.IsTrue(crowd == null, "소환 완료 + 소속 0 이면 파괴");
        }

        [UnityTest]
        public IEnumerator ControllerIsDestroyedWhenLastMemberLeavesAfterSpawnFinished()
        {
            var crowd = CreateController();
            var first = CreateMonster(crowd);
            var second = CreateMonster(crowd);
            yield return null;
            yield return null;
            crowd.MarkSpawnFinished();

            first.gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd != null, "아직 1마리 남음");

            second.gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd == null);
        }

        [UnityTest]
        public IEnumerator SuspendedMemberKeepsControllerAliveAndRegistersAgainOnResume()
        {
            var crowd = CreateController();
            var movement = CreateMonster(crowd);
            yield return null;
            yield return null;
            crowd.MarkSpawnFinished();

            movement.SetSuspended(true);   // 경직 등: 예약 유지 없이 등록 해제
            yield return null;
            Assert.AreEqual(0, crowd.Agents.Count);
            Assert.IsTrue(crowd != null, "정지로 등록만 빠진 개체도 소속이라 묶음 유지");

            movement.SetSuspended(false);
            Assert.AreEqual(1, crowd.Agents.Count);
        }

        [UnityTest]
        public IEnumerator MonsterReenabledAfterControllerDestroyedTracksDirectly()
        {
            var crowd = CreateController();
            var movement = CreateMonster(crowd);
            yield return null;
            yield return null;
            crowd.MarkSpawnFinished();
            movement.gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd == null);

            movement.gameObject.SetActive(true);
            yield return null;
            var target = new FakeTarget();
            movement.SetTarget(target);

            Assert.AreSame(target, movement.Target);
            Assert.IsTrue(movement.enabled);
        }
    }
}
