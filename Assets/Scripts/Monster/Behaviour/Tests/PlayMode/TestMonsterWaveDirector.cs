using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterWaveDirector 의 스테이지 시작 시각·남은 소환 유지·중단·라운드 번호·timeScale 0·스테이지별 마감 전 전멸 특수 웨이브를 확인한다 (B5 완료 기준 5~11).
    /// 실제 MonsterPool·실제 테이블(DataManager)을 쓰고, 소환 위치만 이 클래스가 대신한다 (원점). 소환 수는 MonsterSpawnedEvent 로 센다.
    /// </summary>
    /// <remarks>
    /// Time.captureDeltaTime = 0.5 라서 1프레임 = 라운드 0.5초다.
    /// 정식 테이블(버전 2, 55초 단위) 라운드 1 = 스테이지 1 (0초, 29마리, 약 30초에 소환 끝) · 2 (55초, 23마리, 약 85초) · 3 (110초, 34마리 = 엘리트 2 포함, 약 135.5초)
    /// · 4 (165~195초, 보스 대기 17마리, 약 189초) · ClearBonus 7·8·9 (마감 46·101·156초, 각 7마리, 시작 0.5초 뒤 첫 스폰, 약 2.5초에 소환 끝).
    /// 몬스터는 스스로 죽지 않으므로, 전멸은 이 클래스가 소환된 몬스터마다 MonsterDiedEvent 를 발행해 흉내 낸다.
    /// </remarks>
    public sealed class TestMonsterWaveDirector : ISpawnZoneProvider
    {
        private const float SecondsPerFrame = 0.5f;
        private const int Stage1Count = 7 + 8 + 3 + 8 + 3;
        private const int Stage2Count = 2 + 3 + 9 + 2 + 1 + 1 + 5;
        private const int Stage3Count = 2 + 6 + 8 + 3 + 1 + 6 + 1 + 1 + 4 + 2;
        private const int Stage4Count = 9 + 3 + 1 + 2 + 2;
        private const int ClearBonusCount = 4 + 3;
        private const int MainStagesCount = Stage1Count + Stage2Count + Stage3Count;

        private MonsterWaveDirector director;
        private readonly List<GameObject> spawnedMonsters = new List<GameObject>();
        private readonly List<MonsterSpawnedEvent> spawnEvents = new List<MonsterSpawnedEvent>();

        [SetUp]
        public void SetUp()
        {
            spawnedMonsters.Clear();
            spawnEvents.Clear();
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

        private void OnMonsterSpawned(MonsterSpawnedEvent e)
        {
            spawnedMonsters.Add(e.Instance);
            spawnEvents.Add(e);
        }

        // 지금까지 소환된 몬스터 전부의 사망을 알린다 (플레이어가 전멸시킨 상황)
        private void KillAllSpawned()
        {
            foreach (MonsterSpawnedEvent spawned in spawnEvents)
                EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(spawned.Def, Vector3.zero, spawned.Instance));
        }

        private int SpawnCount => spawnedMonsters.Count;

        private bool IsRoundRunning => (bool)typeof(MonsterWaveDirector)
            .GetField("isRoundRunning", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(director);

        // 이번 라운드 Normal 스테이지 목록 (StartRound 뒤에만 의미 있음). 마지막 스테이지를 빼서 "마감 뒤 Normal 없음" 라운드를 만든다
        private List<WaveStageDef> RoundStages => (List<WaveStageDef>)typeof(MonsterWaveDirector)
            .GetField("roundStages", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(director);

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
            int frames = Mathf.RoundToInt(seconds / Time.captureDeltaTime);
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
            LogAssert.Expect(LogType.Log, new Regex("스테이지 4 시작"));
            LogAssert.Expect(LogType.Log, new Regex("군중전 소환 끝"));

            director.StartRound(1);
            yield return WaitRoundSeconds(50f);
            Assert.AreEqual(Stage1Count, SpawnCount, "55초 전엔 스테이지 1 몫만");

            yield return WaitRoundSeconds(10f);   // 60초
            Assert.Greater(SpawnCount, Stage1Count, "스테이지 2 시작");

            yield return WaitRoundSeconds(45f);   // 105초
            Assert.AreEqual(Stage1Count + Stage2Count, SpawnCount, "110초 전엔 스테이지 2 몫까지");

            yield return WaitRoundSeconds(10f);   // 115초
            Assert.Greater(SpawnCount, Stage1Count + Stage2Count, "스테이지 3 시작");

            yield return WaitRoundSeconds(45f);   // 160초
            Assert.AreEqual(MainStagesCount, SpawnCount, "본 웨이브 끝, 살아 있는 몬스터가 있어 특수 웨이브 없음");
            Assert.IsTrue(IsRoundRunning);

            yield return WaitRoundSeconds(40f);   // 200초
            Assert.AreEqual(MainStagesCount + Stage4Count, SpawnCount, "보스 대기 스테이지까지 남은 소환을 끝까지 냄");
            Assert.IsFalse(IsRoundRunning, "다 소환했고 모든 마감이 지나 군중전 소환 끝");
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
            yield return WaitRoundSeconds(45f);
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
            yield return WaitRoundSeconds(43f);   // 라운드 45초
            Assert.AreEqual(Stage1Count, SpawnCount, "멈춘 동안 라운드 시간도 멈춰 스테이지 2 는 아직");
        }

        // ── 마감 전 전멸 특수 웨이브 ──

        [UnityTest]
        public IEnumerator AllKilledBeforeStageDeadline_StartsClearBonusOnceAfterDelay()
        {
            yield return CreateDirector();
            LogAssert.Expect(LogType.Log, new Regex("특수 웨이브"));
            director.StartRound(1);
            yield return WaitRoundSeconds(35f);
            Assert.AreEqual(Stage1Count, SpawnCount, "스테이지 1 소환 끝");

            KillAllSpawned();
            yield return WaitRoundSeconds(0.5f);  // 특수 웨이브 시작 프레임 (첫 Tick 은 다음 프레임)
            Assert.AreEqual(Stage1Count, SpawnCount, "특수 웨이브 첫 스폰은 시작 0.5초 뒤");

            yield return WaitRoundSeconds(4f);    // 39.5초
            Assert.AreEqual(Stage1Count + ClearBonusCount, SpawnCount, "마감 46초 전 전멸 → 특수 웨이브 7마리");

            KillAllSpawned();
            yield return WaitRoundSeconds(9f);    // 48.5초
            Assert.AreEqual(Stage1Count + ClearBonusCount, SpawnCount, "같은 마감의 특수 웨이브는 1번, 다음 마감 것은 스테이지 2 전이라 아직");
        }

        [UnityTest]
        public IEnumerator AllKilledBeforeEachStageDeadline_StartsClearBonusPerStage()
        {
            yield return CreateDirector();
            LogAssert.Expect(LogType.Log, new Regex("마감 46초 전 전멸"));
            LogAssert.Expect(LogType.Log, new Regex("마감 101초 전 전멸"));
            director.StartRound(1);
            yield return WaitRoundSeconds(35f);

            KillAllSpawned();
            yield return WaitRoundSeconds(5f);    // 40초
            Assert.AreEqual(Stage1Count + ClearBonusCount, SpawnCount, "스테이지 1 특수 웨이브");

            KillAllSpawned();
            yield return WaitRoundSeconds(50f);   // 90초
            Assert.AreEqual(Stage1Count + ClearBonusCount + Stage2Count, SpawnCount, "스테이지 2 소환 끝");

            KillAllSpawned();
            yield return WaitRoundSeconds(5f);    // 95초
            Assert.AreEqual(Stage1Count + Stage2Count + ClearBonusCount * 2, SpawnCount, "스테이지 2 특수 웨이브");
        }

        [UnityTest]
        public IEnumerator AllKilledAfterLastSpawn_NoNormalStageAfterDeadline_StartsClearBonus()
        {
            yield return CreateDirector();
            LogAssert.Expect(LogType.Log, new Regex("마감 156초 전 전멸"));
            LogAssert.Expect(LogType.Log, new Regex("군중전 소환 끝"));
            director.StartRound(1);
            RoundStages.RemoveAt(RoundStages.Count - 1);  // 보스 대기 스테이지(165초)를 빼서 마감 뒤 Normal 이 없는 라운드로

            yield return WaitRoundSeconds(140f);
            Assert.AreEqual(MainStagesCount, SpawnCount, "본 웨이브 소환 끝");
            Assert.IsTrue(IsRoundRunning, "마지막 소환이 끝나도 마감 전엔 라운드 진행 유지");

            KillAllSpawned();
            yield return WaitRoundSeconds(5f);    // 145초
            Assert.AreEqual(MainStagesCount + ClearBonusCount, SpawnCount, "마감 156초 전 전멸 → 특수 웨이브 7마리");
            Assert.IsFalse(IsRoundRunning, "특수 웨이브 소환이 끝나면 군중전 소환 끝");
        }

        [UnityTest]
        public IEnumerator AllKilledAfterDeadline_NoClearBonus()
        {
            yield return CreateDirector();
            director.StartRound(1);
            yield return WaitRoundSeconds(47f);

            KillAllSpawned();
            yield return WaitRoundSeconds(4f);    // 51초
            Assert.AreEqual(Stage1Count, SpawnCount, "46초 마감 뒤 전멸은 특수 웨이브 없음");
        }

        [UnityTest]
        public IEnumerator AllKilledWhileSpawning_DoesNotStartClearBonus()
        {
            yield return CreateDirector();
            director.StartRound(1);
            yield return WaitRoundSeconds(10f);   // 스테이지 1 소환 중

            KillAllSpawned();
            yield return WaitRoundSeconds(35f);   // 45초 (마감 46초 전)
            Assert.AreEqual(Stage1Count, SpawnCount, "소환 중인 스테이지가 있으면 특수 웨이브 없음");
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
