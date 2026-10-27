using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterWave 의 스포너 동시 시작·StartDelay 시차·완료·중단·Finished 발행을 확인한다 (B4 완료 기준 2·5, B5 보정 2·3).
    /// 웨이브 행은 실제 WaveTable(DataManager)에서 읽고, 팩토리·위치 구현은 이 클래스가 대신한다 (소환 수만 센다).
    /// </summary>
    public sealed class TestMonsterWave : IMonsterFactory, ISpawnZoneProvider
    {
        private const int Wave103 = 103;  // 203 (1마리, StartDelay 0) + 204 (8마리, StartDelay 2, 1초 간격)

        private int spawnCount;

        [SetUp]
        public void SetUp()
        {
            spawnCount = 0;
        }

        private MonsterWave CreateWave(int waveId)
        {
            // DataManager 첫 로드 때 프리팹 없음(Weapon·Monster 1001·1004) 오류 로그가 난다. 이 테스트와 무관.
            // SetUp 에서 켜면 테스트 본문에서 다시 꺼지므로 로드하는 이 자리에서 켠다
            LogAssert.ignoreFailingMessages = true;
            return new MonsterWave(DataManager.Waves.Get(waveId), this, this);
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
