using System.Collections.Generic;
using System.Text.RegularExpressions;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterSpawner 의 소환 시각·횟수·위치 재시도·중단을 확인한다 (B3 완료 기준 1~6).
    /// 스포너 행은 실제 WaveSpawnerTable(DataManager)에서 읽고, 팩토리·위치 구현은 이 클래스가 대신한다 (소환 기록만, 몬스터는 안 만든다).
    /// </summary>
    public sealed class TestMonsterSpawner : IMonsterFactory, ISpawnZoneProvider
    {
        private const int SurroundDelay0Interval1 = 201;  // Count 6, StartDelay 0, SpawnInterval 1
        private const int SurroundDelay2Interval1 = 204;  // Count 8, StartDelay 2, SpawnInterval 1
        private const int HerdAllAtOnce = 212;            // Count 12, StartDelay 0, SpawnInterval 0, Herd

        private int spawnCount;
        private int poseFailuresLeft;
        private readonly List<int> requestedSeeds = new List<int>();

        [SetUp]
        public void SetUp()
        {
            // DataManager 첫 로드 때 MonsterTable 1001·1004 프리팹 없음 오류 로그가 난다 (B2 구현 결과). 이 테스트와 무관
            LogAssert.ignoreFailingMessages = true;
            spawnCount = 0;
            poseFailuresLeft = 0;
            requestedSeeds.Clear();
        }

        private MonsterSpawner CreateSpawner(int spawnerId) =>
            new MonsterSpawner(DataManager.WaveSpawners.Get(spawnerId), this, this);

        [Test]
        public void StartDelay0_SpawnsOnFirstTick_ThenEveryInterval()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);

            spawner.Tick(0f);
            Assert.AreEqual(1, spawnCount);
            spawner.Tick(0.5f);
            Assert.AreEqual(1, spawnCount);
            spawner.Tick(0.5f);
            Assert.AreEqual(2, spawnCount);
            Assert.IsFalse(spawner.IsFinished);

            spawner.Tick(100f);
            Assert.AreEqual(6, spawnCount);
            Assert.IsTrue(spawner.IsFinished);
        }

        [Test]
        public void StartDelay_NoSpawnBeforeDelay()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay2Interval1);

            spawner.Tick(1.9f);
            Assert.AreEqual(0, spawnCount);
            spawner.Tick(0.1f);
            Assert.AreEqual(1, spawnCount);
        }

        [Test]
        public void LongTick_CatchesUpMissedSpawns()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);

            spawner.Tick(2.5f);  // 0·1·2초 차례
            Assert.AreEqual(3, spawnCount);
        }

        [Test]
        public void IntervalZero_SpawnsAllAtOnce_WithOneHerdSeed()
        {
            MonsterSpawner spawner = CreateSpawner(HerdAllAtOnce);

            spawner.Tick(0f);
            Assert.AreEqual(12, spawnCount);
            Assert.IsTrue(spawner.IsFinished);
            Assert.AreEqual(1, new HashSet<int>(requestedSeeds).Count, "한 실행 안의 Herd 는 같은 seed");
        }

        [Test]
        public void NewRun_UsesNewHerdSeed()
        {
            CreateSpawner(HerdAllAtOnce).Tick(0f);
            CreateSpawner(HerdAllAtOnce).Tick(0f);

            Assert.AreNotEqual(requestedSeeds[0], requestedSeeds[requestedSeeds.Count - 1]);
        }

        [Test]
        public void PoseFailsFiveTimes_SkipsThatMonsterOnly()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);
            poseFailuresLeft = 5;
            LogAssert.Expect(LogType.Warning, new Regex("건너뜀"));

            spawner.Tick(0f);
            Assert.AreEqual(0, spawnCount);
            spawner.Tick(1f);
            Assert.AreEqual(1, spawnCount);
        }

        [Test]
        public void PoseFailsFourTimes_StillSpawns()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);
            poseFailuresLeft = 4;

            spawner.Tick(0f);
            Assert.AreEqual(1, spawnCount);
        }

        [Test]
        public void Stop_CancelsRemainingSpawns()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);
            spawner.Tick(0f);

            spawner.Stop();
            spawner.Tick(100f);

            Assert.AreEqual(1, spawnCount);
            Assert.IsTrue(spawner.IsFinished);
        }

        // ── IMonsterFactory / ISpawnZoneProvider 대역 ──

        public GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation)
        {
            spawnCount++;
            return null;
        }

        public void Despawn(GameObject monster) { }

        public bool TryGetSpawnPose(SpawnPattern pattern, int groupSeed, out Vector3 position, out Quaternion rotation)
        {
            requestedSeeds.Add(groupSeed);
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (poseFailuresLeft == 0) return true;

            poseFailuresLeft--;
            return false;
        }

        public bool TryGetSupplyDropPosition(out Vector3 position)
        {
            position = Vector3.zero;
            return false;
        }
    }
}
