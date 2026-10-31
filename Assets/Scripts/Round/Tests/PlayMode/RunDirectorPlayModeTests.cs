using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HybridShooter.Core.Battlefield;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Behaviour;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HybridShooter.Round.Tests
{
    // Round는 보급·보스와 함께 Assembly-CSharp에 있다. 테스트 asmdef에서는 리플렉션으로 실제 진행 코드를 호출한다.
    public sealed class RunDirectorPlayModeTests
    {
        private GameObject root;
        private TerrainData terrainData;
        private Component director;
        private Component sequencer;
        private Component encounter;
        private Component upgrades;
        private MonsterWaveDirector waves;
        private CurrencyWallet wallet;
        private UpgradeLedger ledger;
        private ICurrencyWallet previousWallet;
        private IUpgradeState previousUpgrades;
        private IPlayerTarget previousPlayer;
        private RoundFlowTestPlayer player;
        private object previousRounds;
        private object previousStages;
        private readonly List<RoundStartedEvent> starts = new List<RoundStartedEvent>();
        private readonly List<bool> cleanups = new List<bool>();
        private readonly List<int> stageClears = new List<int>();
        private bool completeCamera = true;
        private const string TestUnlockKey = "Test.RunDirector.Unlock.Weapons";
        // 실제 진행(PlayerPrefs Unlock.Weapons)을 건드리지 않도록 테스트 키의 해금 Ledger 를 등록한다
        private WeaponUnlockLedger unlocks;
        private IWeaponUnlocks previousUnlocks;

        private GameState State => Get<GameState>(director, "CurrentState");
        private int Round => Get<int>(director, "CurrentRound");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRounds = DataManager.Rounds;
            previousStages = DataManager.WaveStages;
            var report = new DataLoadReport();
            var dto = new RoundDto { Round = 1, BossId = 3001, BossSpawnTimeMin = 0.05f, BossSpawnTimeMax = 0.05f, SupplyStartTime = 60f, SupplyInterval = 45f, BossReward = 100 };
            var def = (RoundDef)Activator.CreateInstance(typeof(RoundDef), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { dto, report }, null);
            SetData("rounds", MakeTable(RoundDef.Table, 1, def, report));
            var stageDto = new WaveStageDto { Id = 1, RoundNo = 1, StartTime = 0f, EndTime = 180f, WaveList = "101" };
            var stage = (WaveStageDef)Activator.CreateInstance(typeof(WaveStageDef), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { stageDto, report }, null);
            SetData("waveStages", MakeTable(WaveStageDef.Table, 1, stage, report));

            ServiceLocator.TryGet(out previousWallet);
            ServiceLocator.TryGet(out previousUpgrades);
            ServiceLocator.TryGet(out previousPlayer);
            wallet = new CurrencyWallet();
            ledger = new UpgradeLedger();
            ServiceLocator.Register<ICurrencyWallet>(wallet);
            ServiceLocator.Register<IUpgradeState>(ledger);
            ServiceLocator.TryGet(out previousUnlocks);
            unlocks = new WeaponUnlockLedger(TestUnlockKey);
            unlocks.ResetAll();
            unlocks.BindEvents();
            ServiceLocator.Register<IWeaponUnlocks>(unlocks);

            root = new GameObject("RunDirectorTest");
            root.SetActive(false);
            player = root.AddComponent<RoundFlowTestPlayer>();
            ServiceLocator.Register<IPlayerTarget>(player);

            var waveRoot = new GameObject("Waves");
            waveRoot.transform.SetParent(root.transform);
            var pool = waveRoot.AddComponent<MonsterPool>();
            pool.enabled = false; // 빈 스테이지로 진행 연결만 검증한다. 실제 프리팹 예열·스폰은 기존 웨이브 테스트가 담당한다.
            waves = waveRoot.AddComponent<MonsterWaveDirector>();
            SetField(waves, "monsterPool", pool);

            director = root.AddComponent(RuntimeType("HybridShooter.Round.RunDirector"));
            sequencer = root.GetComponent(RuntimeType("HybridShooter.Round.RoundSequencer"));
            encounter = root.GetComponent(RuntimeType("HybridShooter.Round.RoundBossEncounter"));
            SetField(director, "startOnPlay", false);
            SetField(sequencer, "monsterWaveDirector", waves);

            var bossPrefab = new GameObject("TestBossPrefab");
            bossPrefab.transform.SetParent(root.transform);
            bossPrefab.SetActive(false);
            var boss = bossPrefab.AddComponent(RuntimeType("HybridShooter.Boss.BossController"));
            bossPrefab.GetComponent<NavMeshAgent>().enabled = false;
            ((Behaviour)bossPrefab.GetComponent(RuntimeType("HybridShooter.Boss.BossMovement"))).enabled = false;
            SetField(encounter, "bossPrefab", boss);
            bossPrefab.AddComponent<BoxCollider>();
            SetField(encounter, "spawnPointFinder", CreateSpawnPointFinder());

            root.AddComponent(RuntimeType("HybridShooter.Reward.KillRewardCollector"));
            upgrades = root.AddComponent(RuntimeType("HybridShooter.Reward.UpgradeManager"));
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Subscribe(OnCleanup);
            EventBus<GameStateChangedEvent>.Subscribe(OnStateChanged);
            EventBus<StageClearedEvent>.Subscribe(OnStageCleared);
            Time.timeScale = 1f;
            root.SetActive(true);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnCleanup);
            EventBus<GameStateChangedEvent>.Unsubscribe(OnStateChanged);
            EventBus<StageClearedEvent>.Unsubscribe(OnStageCleared);
            if (root != null) UnityEngine.Object.Destroy(root);
            if (terrainData != null) UnityEngine.Object.Destroy(terrainData);
            yield return null;
            SetData("rounds", previousRounds);
            SetData("waveStages", previousStages);
            ServiceLocator.Unregister<ICurrencyWallet>(wallet);
            ServiceLocator.Unregister<IUpgradeState>(ledger);
            ServiceLocator.Unregister<IPlayerTarget>(player);
            unlocks.UnbindEvents();
            unlocks.ResetAll();
            ServiceLocator.Unregister<IWeaponUnlocks>(unlocks);
            if (previousUnlocks != null && ReferenceEquals(previousUnlocks, unlocks) == false) ServiceLocator.Register(previousUnlocks);
            if (previousWallet != null) ServiceLocator.Register(previousWallet);
            if (previousUpgrades != null) ServiceLocator.Register(previousUpgrades);
            if (previousPlayer != null) ServiceLocator.Register(previousPlayer);
            Time.timeScale = 1f;
            starts.Clear();
            cleanups.Clear();
            stageClears.Clear();
            completeCamera = true;
        }

        [UnityTest]
        public IEnumerator StartRun_StartsWaveDirectly_NotificationsCannotRestartIt()
        {
            Call(director, "StartRun");
            Assert.AreEqual(GameState.CrowdBattle, State);
            Assert.AreEqual(1, Round);
            Assert.IsTrue(GetField<bool>(waves, "isRoundRunning"));
            SetField(waves, "roundElapsedTime", 17f);

            Call(director, "StartRun");
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(2, DataManager.GetRound(1)));
            Assert.AreEqual(17f, GetField<float>(waves, "roundElapsedTime"));
            Assert.AreEqual(1, GetField<int>(waves, "roundNo"));
            Assert.AreEqual(1, Round);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BossIntro_StopsWaves_AndWaitsForCameraCompletion()
        {
            completeCamera = false;
            Call(director, "StartRun");
            yield return WaitForState(GameState.BossIntro);
            Assert.IsFalse(GetField<bool>(waves, "isRoundRunning"));
            Assert.IsTrue(Get<bool>(sequencer, "IsRunning"));
            Assert.IsNull(GetField<Component>(encounter, "boss"));

            EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(CameraMode.ThirdPerson, false));
            yield return null;
            Assert.AreEqual(GameState.BossIntro, State);
            EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(CameraMode.ThirdPerson, true));
            yield return WaitForState(GameState.BossBattle);
            Assert.IsNotNull(GetField<Component>(encounter, "boss"));
        }

        [UnityTest]
        public IEnumerator BossIntro_PlacesBossInsideMapFacingPlayer()
        {
            Call(director, "StartRun");
            yield return WaitForState(GameState.BossBattle);

            Transform bossTransform = GetField<Component>(encounter, "boss").transform;
            Vector3 flatPosition = new Vector3(bossTransform.position.x, 0f, bossTransform.position.z);
            Vector3 flatDirectionToPlayer = -flatPosition.normalized;
            Assert.That(flatPosition.magnitude, Is.GreaterThan(0.1f), "생성 위치(원점) 그대로가 아니라 탐색기가 고른 위치");
            Assert.That(flatPosition.magnitude, Is.LessThanOrEqualTo(20f), "맵 반지름 20 안");
            Assert.That(Vector3.Dot(bossTransform.forward, flatDirectionToPlayer), Is.GreaterThan(0.99f), "플레이어를 바라봄");
        }

        [UnityTest]
        public IEnumerator Prepare_WithoutSpawnPointFinder_ReturnsFalse()
        {
            SetField(encounter, "spawnPointFinder", null);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SpawnPointFinder"));

            Assert.IsFalse((bool)Call(encounter, "Prepare"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RewardCompletion_PreservesUpgradesAndCurrency_RepeatsRoundOneOnce()
        {
            wallet.Add(5000);
            Assert.IsTrue(ledger.TryPurchase(UpgradeType.MagazineSize, wallet));
            wallet.ResetTo(73);
            Call(director, "StartRun");
            yield return DefeatBoss();
            Assert.AreEqual(173, wallet.Balance, "보스 자체 보상 데이터 없이 현재 라운드 보상 100만 지급");
            Assert.IsTrue(Get<bool>(upgrades, "IsOpen"));
            Assert.AreEqual(0f, Time.timeScale);

            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                if ((bool)Call(upgrades, "TryPurchase", type)) break;
            }
            Assert.IsTrue(Get<bool>(upgrades, "CanFinish"));
            int expectedBalance = wallet.Balance;
            int expectedLevel = ledger.GetLevel(UpgradeType.MagazineSize);
            Call(upgrades, "Finish");
            EventBus<RewardSelectionFinishedEvent>.Publish(new RewardSelectionFinishedEvent());
            yield return WaitForState(GameState.CrowdBattle);

            Assert.AreEqual(2, Round);
            Assert.AreEqual(2, starts.Count);
            Assert.AreEqual(1, starts[1].Def.Round);
            Assert.AreEqual(1, GetField<int>(waves, "roundNo"));
            Assert.AreEqual(expectedBalance, wallet.Balance);
            Assert.AreEqual(expectedLevel, ledger.GetLevel(UpgradeType.MagazineSize));
            Assert.AreEqual(new[] { false }, cleanups);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator FinalDeathDuringIntro_CancelsPendingBoss_RestartResetsRun()
        {
            completeCamera = false;
            wallet.Add(5000);
            Assert.IsTrue(ledger.TryPurchase(UpgradeType.MagazineSize, wallet));
            Call(director, "StartRun");
            yield return WaitForState(GameState.BossIntro);
            EventBus<PlayerDiedEvent>.Publish(new PlayerDiedEvent());
            yield return null;
            Assert.AreEqual(GameState.BossIntro, State, "부활 가능한 사망은 게임오버가 아님");

            EventBus<PlayerFinalDeathEvent>.Publish(new PlayerFinalDeathEvent());
            EventBus<PlayerFinalDeathEvent>.Publish(new PlayerFinalDeathEvent());
            yield return WaitForState(GameState.GameOver);
            EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(CameraMode.ThirdPerson, true));
            yield return null;
            Assert.IsNull(GetField<Component>(encounter, "boss"));
            Assert.IsFalse(Get<bool>(sequencer, "IsRunning"));

            Call(director, "RestartRun");
            Assert.AreEqual(GameState.CrowdBattle, State);
            Assert.AreEqual(1, Round);
            Assert.AreEqual(0, wallet.Balance);
            Assert.AreEqual(0, ledger.GetLevel(UpgradeType.MagazineSize));
            Assert.AreEqual(new[] { false, true }, cleanups);
            Assert.AreEqual(1, GetField<int>(waves, "roundNo"));
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator RewardCompletionAndFinalDeathTogether_GameOverWins()
        {
            Call(director, "StartRun");
            yield return DefeatBoss();
            EventBus<RewardSelectionFinishedEvent>.Publish(new RewardSelectionFinishedEvent());
            EventBus<PlayerFinalDeathEvent>.Publish(new PlayerFinalDeathEvent());
            yield return WaitForState(GameState.GameOver);
            Assert.AreEqual(1, Round);
            Assert.AreEqual(1, starts.Count);
            Assert.IsFalse(Get<bool>(upgrades, "IsOpen"));
            Assert.AreEqual(new[] { false }, cleanups);
        }

        [UnityTest]
        public IEnumerator DisableDuringReward_StopsExecutionAndRestoresPreviousSpeed()
        {
            Time.timeScale = 2f;
            Call(director, "StartRun");
            yield return DefeatBoss();
            Assert.AreEqual(0f, Time.timeScale);
            root.SetActive(false);
            Assert.AreEqual(2f, Time.timeScale);
            Assert.IsFalse(Get<bool>(sequencer, "IsRunning"));
            Assert.IsFalse(GetField<bool>(waves, "isRoundRunning"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BossDefeat_PublishesStageClearedOnce_AndUnlocksNextWeapon()
        {
            var next = unlocks.PeekNext();
            Call(director, "StartRun");
            yield return DefeatBoss();
            yield return null;
            Assert.AreEqual(new[] { 1 }, stageClears);
            if (next != null) Assert.IsTrue(unlocks.IsUnlocked(next.Id), "스테이지 클리어마다 다음 무기 하나 해금");

            // 테스터처럼 보스 처치 이벤트만 직접 보내면 해금되지 않는다
            var after = unlocks.PeekNext();
            EventBus<BossDefeatedEvent>.Publish(new BossDefeatedEvent(null, Vector3.zero));
            if (after != null) Assert.IsFalse(unlocks.IsUnlocked(after.Id));
            Assert.AreEqual(1, stageClears.Count);
        }

        [UnityTest]
        public IEnumerator ReturnToReady_ResetsRunAndWaitsForStart()
        {
            Assert.IsFalse((bool)Call(director, "ReturnToReady"), "게임 오버가 아니면 무시");
            Call(director, "StartRun");
            yield return null;
            EventBus<PlayerFinalDeathEvent>.Publish(new PlayerFinalDeathEvent());
            yield return WaitForState(GameState.GameOver);

            Assert.IsTrue((bool)Call(director, "ReturnToReady"));
            Assert.AreEqual(GameState.Ready, State);
            Assert.AreEqual(0, Round);
            Assert.IsFalse(Get<bool>(director, "IsRunning"));
            Assert.AreEqual(new[] { false, true }, cleanups);
            Assert.AreEqual(1f, Time.timeScale);
            yield return null;
            Assert.AreEqual(GameState.Ready, State, "출발(StartRun) 전에는 라운드를 시작하지 않는다");

            Call(director, "StartRun");
            Assert.AreEqual(GameState.CrowdBattle, State);
            Assert.AreEqual(1, Round);
        }

        [UnityTest]
        public IEnumerator StartGate_HoldsNextRoundInReward_UntilReleased()
        {
            var gate = new TestStartGate();
            ServiceLocator.Register<IRoundStartGate>(gate);
            try
            {
                Call(director, "StartRun");
                yield return DefeatBoss();
                gate.Holds = true;
                EventBus<RewardSelectionFinishedEvent>.Publish(new RewardSelectionFinishedEvent());
                for (int i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(GameState.Reward, State, "무기 선택 중에는 보상 단계(시간 정지)로 기다린다");
                Assert.AreEqual(1, Round);
                Assert.AreEqual(0f, Time.timeScale);
                Assert.AreEqual(0, cleanups.Count, "무기를 고르기 전에는 라운드 정리(탄약 초기화)를 하지 않는다");

                gate.Holds = false;
                yield return WaitForState(GameState.CrowdBattle);
                Assert.AreEqual(2, Round);
                Assert.AreEqual(new[] { false }, cleanups);
                Assert.AreEqual(1f, Time.timeScale);
            }
            finally
            {
                ServiceLocator.Unregister<IRoundStartGate>(gate);
            }
        }

        private sealed class TestStartGate : IRoundStartGate
        {
            public bool Holds;
            public bool HoldsStart => Holds;
        }

        // 평평한 64m 지형 위에 원점 중심 맵(반지름 20m)을 만드는 탐색기. 플레이어는 원점에 있다
        private SpawnPointFinder CreateSpawnPointFinder()
        {
            terrainData = new TerrainData { heightmapResolution = 65 };
            terrainData.size = new Vector3(64f, 20f, 64f);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.transform.SetParent(root.transform);
            terrainObject.transform.position = new Vector3(-32f, 0f, -32f);

            var finder = root.AddComponent<SpawnPointFinder>();
            SetField(finder, "terrain", terrainObject.GetComponent<Terrain>());
            SetField(finder, "mapCenter", root.transform);
            SetField(finder, "mapRadius", 20f);
            return finder;
        }

        private IEnumerator DefeatBoss()
        {
            yield return WaitForState(GameState.BossBattle);
            var boss = GetField<Component>(encounter, "boss");
            ((IDamageable)boss).TakeDamage(new DamageInfo(100000, DamageKind.Bullet, Faction.Player, root, Vector3.zero, Vector3.forward));
            yield return WaitForState(GameState.Reward);
        }

        private IEnumerator WaitForState(GameState expected)
        {
            for (int frame = 0; frame < 60 && State != expected; frame++) yield return null;
            Assert.AreEqual(expected, State);
        }

        private void OnRoundStarted(RoundStartedEvent e) => starts.Add(e);
        private void OnCleanup(RoundCleanupEvent e) => cleanups.Add(e.FullReset);
        private void OnStageCleared(StageClearedEvent e) => stageClears.Add(e.Round);

        private void OnStateChanged(GameStateChangedEvent e)
        {
            if (completeCamera && e.Current == GameState.BossIntro) EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(CameraMode.ThirdPerson, true));
        }

        private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        private static object Call(Component target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        private static T Get<T>(Component target, string property) => (T)target.GetType().GetProperty(property).GetValue(target);
        private static T GetField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void SetData(string name, object value) => typeof(DataManager).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

        private static DefTable<int, T> MakeTable<T>(string name, int key, T value, DataLoadReport report) where T : class
        {
            var table = (DefTable<int, T>)Activator.CreateInstance(typeof(DefTable<int, T>), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { name }, null);
            typeof(DefTable<int, T>).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(table, new object[] { key, value, report });
            return table;
        }
    }

    public sealed class RoundFlowTestPlayer : MonoBehaviour, IPlayerTarget, IDamageable
    {
        public Transform Root => transform;
        public Transform ViewPoint => transform;
        public PlayerTrackingInfo PlayerTrackingInfo { get; } = new PlayerTrackingInfo();
        public Faction Faction => Faction.Player;
        public bool IsAlive => true;
        public void TakeDamage(in DamageInfo info) { }
    }
}
