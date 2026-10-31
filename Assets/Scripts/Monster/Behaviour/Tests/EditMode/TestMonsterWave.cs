using System;
using System.Linq;
using System.Reflection;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using NUnit.Framework;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterWave 의 스포너 동시 시작·StartDelay 시차·완료·중단·Finished 발행을 확인한다 (B4 완료 기준 2·5, B5 보정 2·3).
    /// 웨이브·스포너 행은 이 클래스가 직접 만든다 (실제 WaveTable 수치 조정과 무관). 팩토리·위치 구현도 대신한다 (소환 수만 센다).
    /// </summary>
    public sealed class TestMonsterWave : IMonsterFactory, ISpawnZoneProvider
    {
        // 203 (1마리, StartDelay 0) + 204 (8마리, StartDelay 2, 1초 간격)
        private static readonly WaveDef Wave103 = CreateWaveDef(103, 0f,
            CreateSpawnerDef(203, 1, 0f, 0f),
            CreateSpawnerDef(204, 8, 2f, 1f));

        private int spawnCount;

        [SetUp]
        public void SetUp()
        {
            spawnCount = 0;
        }

        private MonsterWave CreateWave(WaveDef waveDef)
        {
            return new MonsterWave(waveDef, this, this);
        }

        // Def 생성자가 internal 이라 리플렉션으로 DataManager Build 단계와 같은 생성자를 부른다. Monster 는 Link 하지 않는다 (대역 팩토리는 안 씀)
        private static WaveSpawnerDef CreateSpawnerDef(int id, int count, float startDelay, float spawnInterval)
        {
            var dto = new WaveSpawnerDto { Id = id, MonsterId = 1002, Count = count, StartDelay = startDelay, SpawnInterval = spawnInterval, SpawnPattern = "Surround" };
            return (WaveSpawnerDef)Activator.CreateInstance(typeof(WaveSpawnerDef), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { dto, new DataLoadReport() }, null);
        }

        // LinkWaves 대신 Spawners 를 직접 채운다 (internal set)
        private static WaveDef CreateWaveDef(int id, float startTime, params WaveSpawnerDef[] spawners)
        {
            var dto = new WaveDto { Id = id, StartTime = startTime, SpawnList = string.Join(";", spawners.Select(spawner => spawner.Id)) };
            var wave = (WaveDef)Activator.CreateInstance(typeof(WaveDef), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { dto, new DataLoadReport() }, null);
            typeof(WaveDef).GetProperty(nameof(WaveDef.Spawners)).SetValue(wave, spawners);
            return wave;
        }

        [Test]
        public void Create_DoesNotSpawnUntilTick()
        {
            MonsterWave wave = CreateWave(Wave103);

            Assert.AreEqual(0, spawnCount);
            Assert.IsFalse(wave.IsFinished);
        }

        [Test]
        public void AllSpawnersStartTogether_OffsetOnlyByStartDelay()
        {
            MonsterWave wave = CreateWave(Wave103);

            wave.Tick(0f);
            Assert.AreEqual(1, spawnCount, "203 은 바로");
            wave.Tick(1.5f);
            Assert.AreEqual(1, spawnCount, "204 는 2초 전엔 없음");
            wave.Tick(0.5f);
            Assert.AreEqual(2, spawnCount, "204 첫 소환은 2초");
        }

        [Test]
        public void AllSpawnersDone_IsFinished()
        {
            MonsterWave wave = CreateWave(Wave103);

            wave.Tick(8.5f);  // 203 1마리 + 204 2~8초 7마리
            Assert.AreEqual(8, spawnCount);
            Assert.IsFalse(wave.IsFinished);

            wave.Tick(0.5f);  // 204 마지막 9초
            Assert.AreEqual(9, spawnCount);
            Assert.IsTrue(wave.IsFinished);
        }

        [Test]
        public void Stop_CancelsRemainingSpawns()
        {
            MonsterWave wave = CreateWave(Wave103);
            wave.Tick(0f);

            wave.Stop();
            wave.Tick(100f);

            Assert.AreEqual(1, spawnCount);
            Assert.IsTrue(wave.IsFinished);
        }

        [Test]
        public void LastSpawnerDone_RaisesFinishedOnce()
        {
            MonsterWave wave = CreateWave(Wave103);
            int finishedCount = 0;
            wave.Finished += _ => finishedCount++;

            wave.Tick(0f);    // 203 끝 (204 는 남음)
            Assert.AreEqual(0, finishedCount);
            wave.Tick(9f);    // 204 마지막 9초
            Assert.AreEqual(1, finishedCount);

            wave.Tick(100f);
            Assert.AreEqual(1, finishedCount, "끝난 뒤 Tick 은 다시 발행하지 않음");
        }

        [Test]
        public void Stop_DoesNotRaiseFinished()
        {
            MonsterWave wave = CreateWave(Wave103);
            int finishedCount = 0;
            wave.Finished += _ => finishedCount++;
            wave.Tick(0f);

            wave.Stop();
            wave.Tick(100f);

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
            position = Vector3.zero;
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
