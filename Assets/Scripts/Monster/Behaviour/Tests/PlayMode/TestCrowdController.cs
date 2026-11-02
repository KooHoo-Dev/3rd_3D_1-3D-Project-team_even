using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using HybridShooter.Monster.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// CrowdController 묶음 배정·직접 추적 기본·묶음 수명(스폰 완료 + 전멸 → 파괴)과 웨이브의 Crowd 형식 소환을 확인한다.
    /// 실제 MonsterMovement + NavMeshMoveExecutor(NavMesh 없음) 를 쓰고, 목표는 대부분 null 로 둬 NavMesh 경로 계산을 피한다.
    /// 웨이브 테스트용 팩토리·위치 계산도 이 클래스가 대신한다 (팩토리는 실제 이동 부품을 붙인 몬스터를 만든다).
    /// </summary>
    public sealed class TestCrowdController : IMonsterFactory, ISpawnZoneProvider
    {
        private readonly List<Object> createdObjects = new List<Object>();
        private readonly List<MonsterMovement> spawnedMonsters = new List<MonsterMovement>();
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
            spawnedMonsters.Clear();
            foreach (CrowdController clone in Object.FindObjectsByType<CrowdController>(FindObjectsSortMode.None))
                Object.DestroyImmediate(clone.gameObject);
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

        // ── 웨이브 Crowd 형식 ──

        [UnityTest]
        public IEnumerator CrowdRowsWithSameStartDelayShareOneControllerAndOtherRowsTrackDirectly()
        {
            var template = CreateController();
            WaveDef waveDef = CreateWaveDef(300,
                CreateSpawnerDef(301, 2, 0f, 0f, "Crowd"),
                CreateSpawnerDef(302, 1, 0f, 0f, "Crowd"),
                CreateSpawnerDef(303, 1, 0f, 0f, "Surround"));
            var wave = new MonsterWave(waveDef, this, this, template);

            wave.Tick(0f);
            yield return null;   // 새 몬스터 Start → Activate (배정된 묶음 소속)
            yield return null;

            CrowdController[] clones = FindCrowdClones(template);
            Assert.AreEqual(1, clones.Length, "같은 StartDelay 의 Crowd 두 줄은 묶음 하나");
            Assert.AreEqual(3, clones[0].MemberCount);
            Assert.AreEqual(3, clones[0].Agents.Count);
            Assert.AreEqual(0, template.MemberCount, "템플릿은 복제 원본일 뿐 아무도 통제하지 않는다");
            int directMonsterCount = spawnedMonsters.Count(monster => clones[0].Agents.Contains(monster) == false);
            Assert.AreEqual(4, spawnedMonsters.Count);
            Assert.AreEqual(1, directMonsterCount, "Surround 줄 몬스터는 직접 추적");
        }

        [UnityTest]
        public IEnumerator CrowdRowsWithDifferentStartDelayGetSeparateControllers()
        {
            var template = CreateController();
            WaveDef waveDef = CreateWaveDef(310,
                CreateSpawnerDef(311, 1, 0f, 0f, "Crowd"),
                CreateSpawnerDef(312, 1, 1f, 0f, "Crowd"));
            var wave = new MonsterWave(waveDef, this, this, template);

            wave.Tick(5f);
            yield return null;

            Assert.AreEqual(2, FindCrowdClones(template).Length);
        }

        [UnityTest]
        public IEnumerator CrowdControllerIsDestroyedAfterWaveSpawnedAllAndMembersDie()
        {
            var template = CreateController();
            WaveDef waveDef = CreateWaveDef(320, CreateSpawnerDef(321, 2, 0f, 0f, "Crowd"));
            var wave = new MonsterWave(waveDef, this, this, template);

            wave.Tick(0f);   // 2마리 소환 = 스포너 끝 → MarkSpawnFinished
            yield return null;
            yield return null;
            CrowdController crowd = FindCrowdClones(template)[0];

            spawnedMonsters[0].gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd != null, "1마리 남음");

            spawnedMonsters[1].gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd == null);
        }

        [UnityTest]
        public IEnumerator StoppingWaveMarksCrowdSpawnFinished()
        {
            var template = CreateController();
            WaveDef waveDef = CreateWaveDef(330, CreateSpawnerDef(331, 3, 0f, 1f, "Crowd"));
            var wave = new MonsterWave(waveDef, this, this, template);

            wave.Tick(0f);   // 3마리 중 1마리만
            yield return null;
            yield return null;
            CrowdController crowd = FindCrowdClones(template)[0];
            wave.Stop();

            spawnedMonsters[0].gameObject.SetActive(false);
            yield return null;
            Assert.IsTrue(crowd == null, "중단도 소환 완료로 본다");
        }

        [Test]
        public void CrowdRowWithoutTemplateThrows()
        {
            WaveDef waveDef = CreateWaveDef(340, CreateSpawnerDef(341, 1, 0f, 0f, "Crowd"));

            Assert.Throws<InvalidOperationException>(() => new MonsterWave(waveDef, this, this));
        }

        // ── 풀 재사용 (이미 활성인 개체의 배정·해제) ──

        [UnityTest]
        public IEnumerator JoinCrowdOnActiveDirectMonsterSwitchesToCrowdAndHandsOverTarget()
        {
            var crowd = CreateController();
            var movement = CreateMonster(null);
            yield return null;
            yield return null;
            var target = new FakeTarget();
            movement.SetTarget(target);

            movement.JoinCrowd(crowd);

            Assert.AreEqual(1, crowd.MemberCount);
            Assert.AreEqual(1, crowd.Agents.Count);
            Assert.AreSame(target, crowd.Target, "직접 추적 목표를 묶음에 넘긴다");
            Assert.AreSame(target, movement.Target);
        }

        [UnityTest]
        public IEnumerator ClearedAssignmentMakesReusedMonsterTrackDirectly()
        {
            var crowd = CreateController();
            var movement = CreateMonster(crowd);
            yield return null;
            yield return null;

            movement.gameObject.SetActive(false);   // 풀 반납
            movement.ClearCrowdAssignment();
            movement.gameObject.SetActive(true);    // 풀에서 재사용
            yield return null;

            Assert.AreEqual(0, crowd.MemberCount);
            Assert.AreEqual(0, crowd.Agents.Count);
        }

        private static CrowdController[] FindCrowdClones(CrowdController template) =>
            Object.FindObjectsByType<CrowdController>(FindObjectsSortMode.None).Where(controller => controller != template).ToArray();

        // Def 생성자가 internal 이라 리플렉션으로 DataManager Build 단계와 같은 생성자를 부른다 (TestMonsterWave 와 같은 방법). Monster 는 Link 하지 않는다
        private static WaveSpawnerDef CreateSpawnerDef(int id, int count, float startDelay, float spawnInterval, string spawnPattern)
        {
            var dto = new WaveSpawnerDto { Id = id, MonsterId = 1002, Count = count, StartDelay = startDelay, SpawnInterval = spawnInterval, SpawnPattern = spawnPattern };
            return (WaveSpawnerDef)Activator.CreateInstance(typeof(WaveSpawnerDef), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { dto, new DataLoadReport() }, null);
        }

        private static WaveDef CreateWaveDef(int id, params WaveSpawnerDef[] spawners)
        {
            var dto = new WaveDto { Id = id, StartTime = 0f, SpawnList = string.Join(";", spawners.Select(spawner => spawner.Id)) };
            var wave = (WaveDef)Activator.CreateInstance(typeof(WaveDef), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { dto, new DataLoadReport() }, null);
            typeof(WaveDef).GetProperty(nameof(WaveDef.Spawners)).SetValue(wave, spawners);
            return wave;
        }

        // ── IMonsterFactory / ISpawnZoneProvider 대역 ──

        public GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation)
        {
            MonsterMovement movement = CreateMonster(null);
            spawnedMonsters.Add(movement);
            return movement.gameObject;
        }

        public void Despawn(GameObject monster) { }

        public bool TryGetSpawnPose(SpawnPattern pattern, int groupSeed, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return true;
        }

        public bool TryGetNestCenter(out Vector3 center)
        {
            center = Vector3.zero;
            return true;
        }

        public bool TryGetSpawnPoseAroundNest(Vector3 nestCenter, out Vector3 position, out Quaternion rotation)
        {
            position = nestCenter;
            rotation = Quaternion.identity;
            return true;
        }

        public bool TryGetSupplyDropPosition(out Vector3 position)
        {
            position = Vector3.zero;
            return false;
        }
    }
}
