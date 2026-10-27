using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Behaviour;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    /// <summary>
    /// 테스트 전용. 웨이브(B4)가 생기기 전에 MonsterSpawner 를 손으로 돌려 본다.
    /// 숫자패드 1~8 = spawnerIds 0~7번 스포너 실행. Inspector 메뉴로 전체 실행·전체 중단.
    /// </summary>
    /// <remarks>
    /// 소환은 ServiceLocator 의 IMonsterFactory(씬에 MonsterPool 이 있으면 풀), 위치는 ISpawnZoneProvider(기본 링)가 한다.
    /// 이 클래스는 입력과 실행 중 스포너 목록의 Tick 만 맡는다.
    /// </remarks>
    public sealed class MonsterSpawnerLab : MonoBehaviour
    {
        [Header("실행할 스포너")]
        [Tooltip("WaveSpawnerTable Id. 0~7번이 숫자패드 1~8 에 대응")]
        [SerializeField] private int[] spawnerIds = { 201, 205, 206, 212 };

        private static readonly Key[] RunKeys =
            { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8 };

        // ── 실행 중 ──
        private readonly List<MonsterSpawner> runningSpawners = new List<MonsterSpawner>();

        /// <summary>눌린 키의 스포너를 시작하고, 실행 중 스포너의 시간을 진행한다</summary>
        private void Update()
        {
            RunPressedSpawners();
            TickRunningSpawners();
        }

        /// <summary>숫자패드 1~8 중 이번 프레임 눌린 키의 번호로 스포너를 시작한다. 배열에 없는 번호는 무시한다</summary>
        private void RunPressedSpawners()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < RunKeys.Length; i++)
            {
                bool isPressed = keyboard[RunKeys[i]].wasPressedThisFrame;
                bool hasSpawnerId = i < spawnerIds.Length;
                if (isPressed && hasSpawnerId) Run(spawnerIds[i]);
            }
        }

        /// <summary>실행 중 스포너에 Time.deltaTime 을 넘기고, 끝난 스포너는 목록에서 뺀다</summary>
        private void TickRunningSpawners()
        {
            foreach (MonsterSpawner spawner in runningSpawners) spawner.Tick(Time.deltaTime);
            runningSpawners.RemoveAll(spawner => spawner.IsFinished);
        }

        /// <summary>
        /// 스포너 한 줄을 새로 시작한다. 같은 Id 를 여러 번 누르면 그만큼 따로 실행된다.
        /// </summary>
        /// <param name="spawnerId">WaveSpawnerTable Id</param>
        public void Run(int spawnerId)
        {
            var spawner = new MonsterSpawner(
                DataManager.WaveSpawners.Get(spawnerId),
                ServiceLocator.Get<IMonsterFactory>(),
                ServiceLocator.Get<ISpawnZoneProvider>());
            runningSpawners.Add(spawner);
            Debug.Log($"[MonsterSpawnerLab] 스포너 {spawnerId} 시작 (실행 중 {runningSpawners.Count}개)", this);
        }

        /// <summary>spawnerIds 의 스포너를 한꺼번에 시작한다 (웨이브 1개 흉내)</summary>
        [ContextMenu("전체 실행")]
        public void RunAll()
        {
            foreach (int spawnerId in spawnerIds) Run(spawnerId);
        }

        /// <summary>실행 중 스포너의 남은 소환을 모두 취소한다. 이미 나온 몬스터는 그대로 둔다</summary>
        [ContextMenu("전체 중단")]
        public void StopAll()
        {
            foreach (MonsterSpawner spawner in runningSpawners) spawner.Stop();
            runningSpawners.Clear();
        }
    }
}
