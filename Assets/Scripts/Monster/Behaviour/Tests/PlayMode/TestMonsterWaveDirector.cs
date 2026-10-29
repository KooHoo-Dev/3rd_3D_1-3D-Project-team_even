using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterWaveDirector 의 스테이지 시작 시각·남은 소환 유지·중단·라운드 번호·timeScale 0 을 확인한다 (B5 완료 기준 5~11).
    /// 실제 MonsterPool·실제 테이블(DataManager)을 쓰고, 소환 위치만 이 클래스가 대신한다 (원점). 소환 수는 MonsterSpawnedEvent 로 센다.
    /// </summary>
    /// <remarks>
    /// Time.captureDeltaTime = 0.5 라서 1프레임 = 라운드 0.5초다. 라운드 1 = 스테이지 1 (0초, 23마리) · 2 (60초, 21마리) · 3 (120초, 52마리).
    /// 스테이지 1 은 약 40초, 2 는 약 96초, 3 은 약 165초에 소환이 끝난다.
    /// </remarks>
    public sealed class TestMonsterWaveDirector : ISpawnZoneProvider
    {
        private const float SecondsPerFrame = 0.5f;
        private const int Stage1Count = 6 + 8 + 1 + 8;
        private const int Stage2Count = 2 + 3 + 10 + 2 + 3 + 1;
        private const int Stage3Count = 2 + 12 + 16 + 4 + 16 + 2;

        private MonsterWaveDirector director;
        private readonly List<GameObject> spawnedMonsters = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            spawnedMonsters.Clear();
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            ServiceLocator.Register<ISpawnZoneProvider>(this);
            Time.captureDeltaTime = SecondsPerFrame;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            ServiceLocator.Unregister<ISpawnZoneProvider>(this);
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            // 반납된 몬스터는 풀 아래에 있으므로 몬스터를 먼저 지운다
            foreach (GameObject monster in spawnedMonsters.Distinct()) Object.DestroyImmediate(monster);
            spawnedMonsters.Clear();
            if (director != null) Object.DestroyImmediate(director.gameObject);
        }

        private void OnMonsterSpawned(MonsterSpawnedEvent e) => spawnedMonsters.Add(e.Instance);

        private int SpawnCount => spawnedMonsters.Count;

        private IEnumerator CreateDirector()
        {
            // DataManager 첫 로드 오류(프리팹 없음)와 NavMesh 없는 몬스터 경고는 이 테스트와 무관
            LogAssert.ignoreFailingMessages = true;
            var root = new GameObject("MonsterWaveDirector");
            MonsterPool pool = root.AddComponent<MonsterPool>();
            director = root.AddComponent<MonsterWaveDirector>();
            typeof(MonsterWaveDirector)
                .GetField("monsterPool", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, pool);
            yield return null;  // 풀 Start: 기본 Warmup
        }

        private static IEnumerator WaitRoundSeconds(float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / SecondsPerFrame);
            for (int frame = 0; frame < frames; frame++) yield return null;
        }

        // ── 완료 기준 5·6·7·10 ──

        [UnityTest]
        public IEnumerator StartRound1_StartsStagesAtTheirStartTime_AndFinishesAllSpawns()
        {
            yield return CreateDirector();
            LogAssert.Expect(LogType.Log, new Regex("스테이지 1 시작"));
            LogAssert.Expect(LogType.Log, new Regex("스테이지 2 시작"));
            LogAssert.Expect(LogType.Log, new Regex("스테이지 3 시작"));
            LogAssert.Expect(LogType.Log, new Regex("군중전 소환 끝"));

            director.StartRound(1);
            yield return WaitRoundSeconds(55f);
            Assert.AreEqual(Stage1Count, SpawnCount, "60초 전엔 스테이지 1 몫만");

            yield return WaitRoundSeconds(10f);   // 65초
            Assert.Greater(SpawnCount, Stage1Count, "스테이지 2 시작");

            yield return WaitRoundSeconds(50f);   // 115초
            Assert.AreEqual(Stage1Count + Stage2Count, SpawnCount, "120초 전엔 스테이지 2 몫까지");

            yield return WaitRoundSeconds(10f);   // 125초
            Assert.Greater(SpawnCount, Stage1Count + Stage2Count, "스테이지 3 시작");

            yield return WaitRoundSeconds(60f);   // 185초
            Assert.AreEqual(Stage1Count + Stage2Count + Stage3Count, SpawnCount, "모든 스테이지가 남은 소환을 끝까지 냄");
        }

        // ── 완료 기준 8 ──

        [UnityTest]
        public IEnumerator StopRound_CancelsRemainingSpawns_AndRestartBeginsAtZero()
        {
            yield return CreateDirector();
            director.StartRound(1);
            yield return WaitRoundSeconds(5f);

            director.StopRound();
            int countAtStop = SpawnCount;
            yield return WaitRoundSeconds(130f);
            Assert.AreEqual(countAtStop, SpawnCount, "Stop 뒤 소환 없음 (스테이지 2·3 도 시작 안 함)");

            director.StartRound(1);
            yield return WaitRoundSeconds(55f);
            Assert.AreEqual(countAtStop + Stage1Count, SpawnCount, "재시작은 0초부터 스테이지 1 전체");
        }

        [UnityTest]
        public IEnumerator RoundCleanupEvent_StopsRound()
        {
            yield return CreateDirector();
            director.StartRound(1);
            yield return WaitRoundSeconds(5f);

            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
            int countAtStop = SpawnCount;
            yield return WaitRoundSeconds(70f);

            Assert.AreEqual(countAtStop, SpawnCount);
        }

        [UnityTest]
        public IEnumerator StateNotifications_DoNotControlRound_DirectStopCancelsSpawns()
        {
            yield return CreateDirector();
            director.StartRound(1);
            yield return WaitRoundSeconds(2f);

            EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(GameState.CrowdBattle, GameState.Paused));
            int countAtPause = SpawnCount;
            yield return WaitRoundSeconds(2f);
            Assert.Greater(SpawnCount, countAtPause, "Paused 는 멈추지 않음");

            EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(GameState.CrowdBattle, GameState.BossIntro));
            yield return WaitRoundSeconds(2f);
            Assert.Greater(SpawnCount, countAtPause, "상태 알림은 시퀀서의 직접 호출을 대신하지 않음");
            director.StopRound();
            int countAtBossIntro = SpawnCount;
            yield return WaitRoundSeconds(70f);
            Assert.AreEqual(countAtBossIntro, SpawnCount);
        }

        // ── 완료 기준 9 ──

        [UnityTest]
        public IEnumerator RoundStartedNotification_DoesNotStartRound_DirectStartUsesRequestedData()
        {
            yield return CreateDirector();
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(2, null));
            yield return WaitRoundSeconds(10f);
            Assert.AreEqual(0, SpawnCount, "라운드 알림만으로는 소환하지 않음");

            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(1, null));
            yield return WaitRoundSeconds(2f);
            Assert.AreEqual(0, SpawnCount, "시퀀서의 직접 호출 전에 시작하지 않음");
            director.StartRound(1);
            yield return WaitRoundSeconds(10f);
            Assert.Greater(SpawnCount, 0, "라운드 1 은 시작");
        }

        // ── 완료 기준 11 ──

        [UnityTest]
        public IEnumerator TimeScaleZero_PausesRoundTimeAndSpawns()
        {
            yield return CreateDirector();
            director.StartRound(1);
            yield return WaitRoundSeconds(2f);

            Time.timeScale = 0f;
            int countAtPause = SpawnCount;
            yield return WaitRoundSeconds(100f);
            Assert.AreEqual(countAtPause, SpawnCount, "timeScale 0 동안 소환 없음");

            Time.timeScale = 1f;
            yield return WaitRoundSeconds(53f);   // 라운드 55초
            Assert.AreEqual(Stage1Count, SpawnCount, "멈춘 동안 라운드 시간도 멈춰 스테이지 2 는 아직");
        }

        // ── 군중 전멸 신호 (HordeCleared) ──

        private int hordeClearedCount;

        private void KillAllSpawned()
        {
            foreach (GameObject monster in spawnedMonsters.ToList())
                EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(null, Vector3.zero, monster));
        }

        [UnityTest]
        public IEnumerator HordeCleared_FiresOnce_WhenSpawnFinishedAndAllHordeDead()
        {
            yield return CreateDirector();
            hordeClearedCount = 0;
            director.HordeCleared += () => hordeClearedCount++;

            director.StartRound(1);
            yield return WaitRoundSeconds(190f);   // 소환 모두 끝남
            Assert.AreEqual(0, hordeClearedCount, "살아 있으면 신호 없음");

            KillAllSpawned();
            Assert.AreEqual(1, hordeClearedCount);
            KillAllSpawned();
            Assert.AreEqual(1, hordeClearedCount, "한 번만");
        }

        [UnityTest]
        public IEnumerator HordeCleared_DoesNotFire_WhenAllDeadButSpawnStillRunning()
        {
            yield return CreateDirector();
            hordeClearedCount = 0;
            director.HordeCleared += () => hordeClearedCount++;

            director.StartRound(1);
            yield return WaitRoundSeconds(10f);
            KillAllSpawned();
            yield return WaitRoundSeconds(2f);

            Assert.AreEqual(0, hordeClearedCount, "소환이 남았으면 전멸이 아님");
        }

        [UnityTest]
        public IEnumerator HordeCleared_DoesNotFire_AfterStopRound()
        {
            yield return CreateDirector();
            hordeClearedCount = 0;
            director.HordeCleared += () => hordeClearedCount++;

            director.StartRound(1);
            yield return WaitRoundSeconds(10f);
            director.StopRound();
            KillAllSpawned();
            yield return WaitRoundSeconds(2f);

            Assert.AreEqual(0, hordeClearedCount);
        }

        [UnityTest]
        public IEnumerator HordeCleared_IgnoresDeathOfMonsterNotSpawnedByDirector()
        {
            yield return CreateDirector();
            hordeClearedCount = 0;
            director.HordeCleared += () => hordeClearedCount++;
            var outsider = new GameObject("Outsider");

            director.StartRound(1);
            yield return WaitRoundSeconds(190f);
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(null, Vector3.zero, outsider));
            Assert.AreEqual(0, hordeClearedCount, "외부 몬스터 사망은 무시");

            Object.DestroyImmediate(outsider);
        }

        // ── ISpawnZoneProvider 대역 ──

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
