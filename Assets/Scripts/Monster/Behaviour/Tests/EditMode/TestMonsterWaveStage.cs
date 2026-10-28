using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterWaveStage 의 웨이브 시작 시각·시작 프레임·앞당김 없음·완료·중단·Finished 발행을 확인한다 (B4 완료 기준 1·3·4·5, B5 보정 2·3).
    /// 스테이지 행은 실제 WaveStageTable(DataManager)에서 읽고, 팩토리·위치 구현은 이 클래스가 대신한다 (소환 수만 센다).
    /// </summary>
    /// <remarks>
    /// 스테이지 1 = 웨이브 101 (0초, 201: 6마리 1초 간격) · 102 (10초, 202: 8마리 1초 간격) · 103 (30초, 203: 1마리 + 204: 8마리 2초 뒤).
    /// </remarks>
    public sealed class TestMonsterWaveStage : IMonsterFactory, ISpawnZoneProvider
    {
        private const int Stage1 = 1;
        private const int Stage1TotalCount = 6 + 8 + 1 + 8;

        private int spawnCount;

        [SetUp]
        public void SetUp()
        {
            spawnCount = 0;
        }

        private MonsterWaveStage CreateStage(int stageId)
        {
            // DataManager 첫 로드 때 프리팹 없음(Weapon·Monster 1001·1004) 오류 로그가 난다. 이 테스트와 무관.
            // SetUp 에서 켜면 테스트 본문에서 다시 꺼지므로 로드하는 이 자리에서 켠다
            LogAssert.ignoreFailingMessages = true;
            return new MonsterWaveStage(DataManager.WaveStages.Get(stageId), this, this);
        }

        private static void TickRepeatedly(MonsterWaveStage stage, float deltaTime, int frames)
        {
            for (int frame = 0; frame < frames; frame++) stage.Tick(deltaTime);
        }

        [Test]
        public void WaveStartFrame_NoSpawn_SpawnsFromNextTick()
        {
            MonsterWaveStage stage = CreateStage(Stage1);

            stage.Tick(0f);  // 101 시작
            Assert.AreEqual(0, spawnCount);
            stage.Tick(0f);
            Assert.AreEqual(1, spawnCount);
        }

        [Test]
        public void EachWaveStartsAtItsStartTime()
        {
            MonsterWaveStage stage = CreateStage(Stage1);

            TickRepeatedly(stage, 0.5f, 20);  // 10초: 102 시작 프레임
            Assert.AreEqual(6, spawnCount);
            stage.Tick(0f);
            Assert.AreEqual(7, spawnCount, "102 첫 소환은 다음 Tick");

            TickRepeatedly(stage, 0.5f, 40);  // 30초: 103 시작 프레임
            Assert.AreEqual(14, spawnCount);
            stage.Tick(0f);
            Assert.AreEqual(15, spawnCount, "103 의 203 은 다음 Tick");
        }

        [Test]
        public void EarlyFinishedWave_DoesNotPullNextWaveForward()
        {
            MonsterWaveStage stage = CreateStage(Stage1);

            stage.Tick(0f);
            TickRepeatedly(stage, 0.5f, 19);  // 9.5초
            stage.Tick(0.4f);                 // 9.9초

            Assert.AreEqual(6, spawnCount, "101 은 5초에 끝났지만 102 는 10초 전엔 없음");
            Assert.IsFalse(stage.IsFinished);
        }

        [Test]
        public void LongTick_StartsAllDueWavesTogether()
        {
            MonsterWaveStage stage = CreateStage(Stage1);

            stage.Tick(30f);  // 101·102·103 함께 시작
            Assert.AreEqual(0, spawnCount);
            stage.Tick(0f);
            Assert.AreEqual(3, spawnCount, "201·202·203 첫 소환, 204 는 2초 뒤");
        }

        [Test]
        public void AllWavesDone_IsFinished()
        {
            MonsterWaveStage stage = CreateStage(Stage1);

            stage.Tick(30f);  // 웨이브 3개 시작
            Assert.IsFalse(stage.IsFinished);
            stage.Tick(8.5f);
            Assert.IsFalse(stage.IsFinished, "204 마지막(9초) 전");

            stage.Tick(0.5f);
            Assert.AreEqual(Stage1TotalCount, spawnCount);
            Assert.IsTrue(stage.IsFinished);
        }

        [Test]
        public void Stop_CancelsRunningAndNotStartedWaves()
        {
            MonsterWaveStage stage = CreateStage(Stage1);
            stage.Tick(0f);
            stage.Tick(0f);

            stage.Stop();
            stage.Tick(100f);
            stage.Tick(100f);

            Assert.AreEqual(1, spawnCount);
            Assert.IsTrue(stage.IsFinished);
        }

        [Test]
        public void LastWaveDone_RaisesFinishedOnce_ThroughSpawnerAndWaveChain()
        {
            MonsterWaveStage stage = CreateStage(Stage1);
            int finishedCount = 0;
            stage.Finished += _ => finishedCount++;

            TickRepeatedly(stage, 0.5f, 20);  // 10초: 101 은 5초에 끝났지만 102·103 이 남음
            Assert.AreEqual(0, finishedCount, "웨이브를 다 시작하기 전엔 발행하지 않음");

            TickRepeatedly(stage, 0.5f, 80);  // 50초: 103 의 204 까지 끝
            Assert.AreEqual(Stage1TotalCount, spawnCount);
            Assert.AreEqual(1, finishedCount);

            TickRepeatedly(stage, 0.5f, 10);
            Assert.AreEqual(1, finishedCount, "끝난 뒤 Tick 은 다시 발행하지 않음");
        }

        [Test]
        public void Stop_DoesNotRaiseFinished()
        {
            MonsterWaveStage stage = CreateStage(Stage1);
            int finishedCount = 0;
            stage.Finished += _ => finishedCount++;
            stage.Tick(0f);
            stage.Tick(0f);

            stage.Stop();
            TickRepeatedly(stage, 0.5f, 100);

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
