using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterSpawner 의 소환 시각·횟수·위치 재시도·중단·Finished 발행을 확인한다 (B3 완료 기준 1~6, B5 보정 1·3).
    /// 스포너 행은 이 클래스가 직접 만든다 (실제 WaveSpawnerTable 수치 조정과 무관). 팩토리·위치 구현도 대신한다 (소환 기록만, 몬스터는 안 만든다).
    /// </summary>
    public sealed class TestMonsterSpawner : IMonsterFactory, ISpawnZoneProvider
    {
        private static readonly WaveSpawnerDef SurroundDelay0Interval1 = CreateSpawnerDef(201, 6, 0f, 1f, "Surround");
        private static readonly WaveSpawnerDef SurroundDelay2Interval1 = CreateSpawnerDef(204, 8, 2f, 1f, "Surround");
        private static readonly WaveSpawnerDef HerdAllAtOnce = CreateSpawnerDef(212, 12, 0f, 0f, "Herd");
        private static readonly WaveSpawnerDef NestDelay0Interval1 = CreateSpawnerDef(230, 3, 0f, 1f, "Nest");

        private int spawnCount;
        private int poseFailuresLeft;
        private readonly List<int> requestedSeeds = new List<int>();
        private int nestCenterRequests;
        private int nestCenterFailuresLeft;
        private readonly List<Vector3> requestedNestCenters = new List<Vector3>();

        [SetUp]
        public void SetUp()
        {
            spawnCount = 0;
            poseFailuresLeft = 0;
            requestedSeeds.Clear();
            nestCenterRequests = 0;
            nestCenterFailuresLeft = 0;
            requestedNestCenters.Clear();
        }

        private MonsterSpawner CreateSpawner(WaveSpawnerDef spawnerDef)
        {
            return new MonsterSpawner(spawnerDef, this, this);
        }

        // Def 생성자가 internal 이라 리플렉션으로 DataManager Build 단계와 같은 생성자를 부른다. Monster 는 Link 하지 않는다 (대역 팩토리는 안 씀)
        private static WaveSpawnerDef CreateSpawnerDef(int id, int count, float startDelay, float spawnInterval, string spawnPattern)
        {
            var dto = new WaveSpawnerDto { Id = id, MonsterId = 1002, Count = count, StartDelay = startDelay, SpawnInterval = spawnInterval, SpawnPattern = spawnPattern };
            return (WaveSpawnerDef)Activator.CreateInstance(typeof(WaveSpawnerDef), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { dto, new DataLoadReport() }, null);
        }

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
        public void Nest_SpawnsAroundOneStoredCenter_NotRingPose()
        {
            MonsterSpawner spawner = CreateSpawner(NestDelay0Interval1);

            spawner.Tick(10f);

            Assert.AreEqual(3, spawnCount);
            Assert.AreEqual(1, nestCenterRequests, "둥지 중심은 한 번만 뽑음");
            Assert.AreEqual(3, requestedNestCenters.Count);
            Assert.IsTrue(requestedNestCenters.TrueForAll(center => center == requestedNestCenters[0]), "모두 같은 중심 주위");
            Assert.AreEqual(0, requestedSeeds.Count, "Nest 는 패턴별 링 포즈를 쓰지 않음");
        }

        [Test]
        public void NestCenterFails_SkipsThatMonster_ThenRetriesCenterNextTurn()
        {
            MonsterSpawner spawner = CreateSpawner(NestDelay0Interval1);
            nestCenterFailuresLeft = 5;
            LogAssert.Expect(LogType.Warning, new Regex("건너뜀"));

            spawner.Tick(0f);
            Assert.AreEqual(0, spawnCount);
            spawner.Tick(1f);
            Assert.AreEqual(1, spawnCount, "다음 차례에 중심을 다시 뽑아 소환");
            Assert.AreEqual(6, nestCenterRequests);
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

        [Test]
        public void AllSpawned_RaisesFinishedOnce()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);
            int finishedCount = 0;
            spawner.Finished += _ => finishedCount++;

            spawner.Tick(4f);  // 0~4초 5마리
            Assert.AreEqual(0, finishedCount);
            spawner.Tick(1f);  // 5초 마지막
            Assert.AreEqual(1, finishedCount);

            spawner.Tick(100f);
            Assert.AreEqual(1, finishedCount, "끝난 뒤 Tick 은 다시 발행하지 않음");
        }

        [Test]
        public void Stop_DoesNotRaiseFinished()
        {
            MonsterSpawner spawner = CreateSpawner(SurroundDelay0Interval1);
            int finishedCount = 0;
            spawner.Finished += _ => finishedCount++;
            spawner.Tick(0f);

            spawner.Stop();
            spawner.Tick(100f);

            Assert.AreEqual(0, finishedCount);
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

        public bool TryGetNestCenter(out Vector3 center)
        {
            nestCenterRequests++;
            center = new Vector3(nestCenterRequests, 0f, 0f);
            if (nestCenterFailuresLeft == 0) return true;

            nestCenterFailuresLeft--;
            return false;
        }

        public bool TryGetSpawnPoseAroundNest(Vector3 nestCenter, out Vector3 position, out Quaternion rotation)
        {
            requestedNestCenters.Add(nestCenter);
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
