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
    /// 테스트 전용. 스테이지 전환(B5)이 생기기 전에 MonsterWaveStage 를 손으로 돌려 본다.
    /// F6~F8 = stageIds 0~2번 스테이지 실행. Inspector 메뉴로 스테이지 1 실행·전체 중단.
    /// </summary>
    /// <remarks>
    /// 소환은 ServiceLocator 의 IMonsterFactory(씬에 MonsterPool 이 있으면 풀), 위치는 ISpawnZoneProvider(기본 링)가 한다.
    /// 이 클래스는 입력과 실행 중 스테이지 목록의 Tick 만 맡는다. Time.deltaTime 을 넘기므로 timeScale 0 이면 멈춘다.
    /// </remarks>
    public sealed class WaveLab : MonoBehaviour
    {
        [Header("실행할 스테이지")]
        [Tooltip("WaveStageTable Id. 0~2번이 F6~F8 에 대응")]
        [SerializeField] private int[] stageIds = { 1, 2, 3 };

        private static readonly Key[] RunKeys =
            { Key.Digit8, Key.Digit9, Key.Digit0, };

        // ── 실행 중 ──
        private readonly List<MonsterWaveStage> runningStages = new List<MonsterWaveStage>();

        /// <summary>눌린 키의 스테이지를 시작하고, 실행 중 스테이지의 시간을 진행한다</summary>
        private void Update()
        {
            RunPressedStages();
            TickRunningStages();
        }

        /// <summary>F6~F8 중 이번 프레임 눌린 키의 번호로 스테이지를 시작한다. 배열에 없는 번호는 무시한다</summary>
        private void RunPressedStages()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < RunKeys.Length; i++)
            {
                bool isPressed = keyboard[RunKeys[i]].wasPressedThisFrame;
                bool hasStageId = i < stageIds.Length;
                if (isPressed && hasStageId) Run(stageIds[i]);
            }
        }

        /// <summary>실행 중 스테이지에 Time.deltaTime 을 넘기고, 끝난 스테이지는 목록에서 뺀다</summary>
        private void TickRunningStages()
        {
            foreach (MonsterWaveStage stage in runningStages) stage.Tick(Time.deltaTime);
            runningStages.RemoveAll(stage => stage.IsFinished);
        }

        /// <summary>
        /// 스테이지 하나를 새로 시작한다. 같은 Id 를 여러 번 누르면 그만큼 따로 실행된다.
        /// </summary>
        /// <param name="stageId">WaveStageTable Id</param>
        public void Run(int stageId)
        {
            var stage = new MonsterWaveStage(
                DataManager.WaveStages.Get(stageId),
                ServiceLocator.Get<IMonsterFactory>(),
                ServiceLocator.Get<ISpawnZoneProvider>());
            runningStages.Add(stage);
            Debug.Log($"[WaveLab] 스테이지 {stageId} 시작 (실행 중 {runningStages.Count}개)", this);
        }

        /// <summary>스테이지 1 을 시작한다</summary>
        [ContextMenu("스테이지 1 실행")]
        public void RunStage1() => Run(1);

        /// <summary>실행 중 스테이지의 남은 소환과 시작 안 한 웨이브를 모두 취소한다. 이미 나온 몬스터는 그대로 둔다</summary>
        [ContextMenu("전체 중단")]
        public void StopAll()
        {
            foreach (MonsterWaveStage stage in runningStages) stage.Stop();
            runningStages.Clear();
        }
    }
}
