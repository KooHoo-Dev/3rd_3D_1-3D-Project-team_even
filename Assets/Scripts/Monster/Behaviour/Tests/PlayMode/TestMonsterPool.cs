using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterPool 의 Warmup·소환·반납·전체 회수를 실제 프리팹과 실제 테이블(DataManager)로 확인한다 (B2 완료 기준 1~8).
    /// 쉬는 개체 수는 풀 아래 종류 루트("Idle {Id}")의 자식 수로 센다. NavMesh 가 없어 몬스터는 움직이지 않는다.
    /// </summary>
    public sealed class TestMonsterPool
    {
        private const int Letalobrach = 2001;
        private const int Pardathrox = 2002;
        private const float DespawnWaitSeconds = 5f;   // despawnDelay 2s + 여유

        private MonsterPool pool;
        private readonly List<GameObject> spawnedMonsters = new List<GameObject>();
        private int spawnedEventCount;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            spawnedEventCount = 0;
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            foreach (GameObject monster in spawnedMonsters.Distinct()) Object.DestroyImmediate(monster);
            spawnedMonsters.Clear();
            if (pool != null) Object.DestroyImmediate(pool.gameObject);
        }

        private void OnMonsterSpawned(MonsterSpawnedEvent e) => spawnedEventCount++;

        private IEnumerator CreatePool()
        {
            LogAssert.ignoreFailingMessages = true; 
            pool = new GameObject("MonsterPool").AddComponent<MonsterPool>();
            yield return null; // Start: 씬 몬스터 등록 + Warmup
        }

        private Transform TypeRoot(int monsterId) => pool.transform.Find($"Idle {monsterId}");

        private int IdleCount(int monsterId) => TypeRoot(monsterId).childCount;

        private GameObject Spawn(int monsterId, Vector3 position)
        {
            GameObject monster = pool.Spawn(DataManager.Monsters.Get(monsterId), position, Quaternion.identity);
            spawnedMonsters.Add(monster);
            return monster;
        }

        private static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition)
        {
            float deadline = Time.time + DespawnWaitSeconds;
            while (condition() == false && Time.time < deadline) yield return null;
        }

        private static int ExpectedWarmup(int spawnCount) => Mathf.Max(10, Mathf.CeilToInt(spawnCount * 0.5f));

        private static Dictionary<int, int> SpawnCountsOf(WaveStageDef stage) =>
            stage.Waves.SelectMany(w => w.Spawners)
                 .GroupBy(s => s.MonsterId)
                 .ToDictionary(g => g.Key, g => g.Sum(s => s.Count));

        // ── 완료 기준 1 ──

        [UnityTest]
        public IEnumerator AfterStart_EverySpawnerMonsterHasTenIdleUnderInactiveRoot()
        {
            yield return CreatePool();

            foreach (int monsterId in DataManager.WaveSpawners.All.Select(s => s.MonsterId).Distinct())
            {
                Assert.AreEqual(10, IdleCount(monsterId), $"Monster {monsterId}");
                Assert.IsFalse(TypeRoot(monsterId).gameObject.activeSelf, $"Monster {monsterId} 종류 루트는 꺼져 있어야 한다");
            }
        }

        [UnityTest]
        public IEnumerator SceneInactiveMonsterIsRegisteredAndCountsTowardTen()
        {
            // Awake 가 돌지 않게 꺼진 부모 아래에서 만든 뒤 꺼진 채 씬 루트로 옮긴다
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            GameObject sceneMonster = Object.Instantiate(DataManager.Monsters.Get(Letalobrach).Prefab, holder.transform);
            sceneMonster.SetActive(false);
            sceneMonster.transform.SetParent(null);
            Object.DestroyImmediate(holder);
            spawnedMonsters.Add(sceneMonster);

            yield return CreatePool();

            Assert.AreSame(TypeRoot(Letalobrach), sceneMonster.transform.parent);
            Assert.AreEqual(10, IdleCount(Letalobrach));
        }

        // ── 완료 기준 1, 5 ──

        [UnityTest]
        public IEnumerator WarmupStage_FillsOnlyShortTypesUpToMaxOfTenAndHalf()
        {
            yield return CreatePool();

            foreach (WaveStageDef stage in DataManager.WaveStages.All)
            {
                Dictionary<int, int> idleBefore = SpawnCountsOf(stage).Keys.ToDictionary(id => id, IdleCount);
                pool.WarmupStage(stage);

                foreach (KeyValuePair<int, int> pair in SpawnCountsOf(stage))
                {
                    int expected = Mathf.Max(idleBefore[pair.Key], ExpectedWarmup(pair.Value));
                    Assert.AreEqual(expected, IdleCount(pair.Key), $"Stage {stage.Id} Monster {pair.Key}");
                }
            }
        }

        // ── 완료 기준 3, 8 ──

        [UnityTest]
        public IEnumerator FirstSpawn_IsAssembledNextFrameAtPositionUnderSceneRoot()
        {
            yield return CreatePool();
            var position = new Vector3(3f, 0f, 4f);

            GameObject monster = Spawn(Letalobrach, position);
            yield return null;

            var controller = monster.GetComponent<MonsterController>();
            Assert.IsNull(monster.transform.parent);
            Assert.IsTrue(monster.activeInHierarchy);
            Assert.AreEqual(position, monster.transform.position);
            Assert.IsNotNull(controller.StateMachine);
            Assert.AreEqual(9, IdleCount(Letalobrach));
            Assert.AreEqual(1, spawnedEventCount);
        }

        // ── 완료 기준 2, 7, 8 ──

        [UnityTest]
        public IEnumerator WipeAll_ReturnsMonsters_AndNextSpawnReusesSameInstanceFresh()
        {
            yield return CreatePool();
            GameObject monster = Spawn(Letalobrach, Vector3.zero);
            yield return null;
            var controller = monster.GetComponent<MonsterController>();
            int aliveLayer = monster.layer;
            Renderer bodyRenderer = monster.GetComponentInChildren<Renderer>();
            controller.TakeDamage(new DamageInfo(100, DamageKind.Bullet, Faction.Player, null, Vector3.zero, Vector3.forward));
            Assert.IsTrue(controller.IsLowHealth, "125 중 100 피해 → 저체력");
            Assert.IsTrue(bodyRenderer.HasPropertyBlock(), "저체력이면 붉은 기 MaterialPropertyBlock");
            MonsterStateMachine oldStateMachine = controller.StateMachine;

            pool.WipeAll();
            yield return null;
            var corpseBody = monster.GetComponent<Rigidbody>();
            Assert.IsNotNull(corpseBody, "죽으면 시체 Rigidbody 가 붙는다");
            Assert.IsFalse(corpseBody.isKinematic);
            Assert.AreEqual(LayerMask.NameToLayer("MonsterCorpse"), monster.layer);
            Assert.IsFalse(monster.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled);
            yield return WaitUntilOrTimeout(() => monster.transform.parent == TypeRoot(Letalobrach));

            Assert.AreSame(TypeRoot(Letalobrach), monster.transform.parent, "despawnDelay 뒤 종류 루트로 돌아와야 한다");
            Assert.IsFalse(monster.activeSelf);

            GameObject reused = Spawn(Letalobrach, new Vector3(1f, 0f, 1f));

            Assert.AreSame(monster, reused);
            Assert.IsNull(reused.transform.parent);
            Assert.AreEqual(controller.MaxHp, controller.CurrentHp);
            Assert.AreNotSame(oldStateMachine, controller.StateMachine);
            Assert.IsFalse(controller.StateMachine.IsDead);
            Assert.AreEqual(2, spawnedEventCount);

            yield return null; // Restore 의 Destroy(Rigidbody) 는 프레임 끝에 반영된다
            Assert.IsNull(reused.GetComponent<Rigidbody>(), "재사용하면 시체 Rigidbody 를 뗀다");
            Assert.AreEqual(aliveLayer, reused.layer);
            Assert.IsTrue(reused.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled);
            Assert.IsFalse(bodyRenderer.HasPropertyBlock(), "재사용하면 붉은 기를 지운다");
            Assert.IsFalse(controller.IsLowHealth);
        }

        // ── 완료 기준 4 ──

        [UnityTest]
        public IEnumerator Spawn_WhenNoIdleLeft_CreatesNewInstance()
        {
            yield return CreatePool();
            for (int i = 0; i < 10; i++) Spawn(Letalobrach, new Vector3(i * 2f, 0f, 0f));
            Assert.AreEqual(0, IdleCount(Letalobrach));

            GameObject extra = Spawn(Letalobrach, new Vector3(30f, 0f, 0f));
            yield return null;

            Assert.IsNotNull(extra);
            Assert.AreEqual(11, spawnedMonsters.Distinct().Count());
            Assert.AreEqual(Letalobrach, extra.GetComponent<MonsterController>().Def.Id);
            Assert.AreEqual(11, spawnedEventCount);
        }

        // ── 완료 기준 6 ──

        [UnityTest]
        public IEnumerator ReturnedMonsterOfOneType_IsNotUsedForAnotherType()
        {
            yield return CreatePool();
            GameObject letalobrach = Spawn(Letalobrach, Vector3.zero);
            yield return null;
            pool.WipeAll();
            yield return WaitUntilOrTimeout(() => letalobrach.transform.parent == TypeRoot(Letalobrach));
            Assert.AreSame(TypeRoot(Letalobrach), letalobrach.transform.parent);

            GameObject pardathrox = Spawn(Pardathrox, Vector3.zero);
            yield return null;

            Assert.AreNotSame(letalobrach, pardathrox);
            Assert.AreEqual(Pardathrox, pardathrox.GetComponent<MonsterController>().Def.Id);
            Assert.AreSame(TypeRoot(Letalobrach), letalobrach.transform.parent);
        }
    }
}
