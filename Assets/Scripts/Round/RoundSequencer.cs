using System;
using System.Collections.Generic;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Supply;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace HybridShooter.Round
{
    /// <summary>군중전부터 보상 선택 완료까지 한 라운드의 순서를 실행한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RoundBossEncounter))]
    public sealed class RoundSequencer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MonsterWaveDirector monsterWaveDirector;
        [SerializeField] private SupplyDropper supplyDropper;
        [SerializeField] private RoundBossEncounter bossEncounter;

        private RoundDef roundDef;
        private int roundNumber;
        private GameState phase;
        private float elapsedSeconds;
        private float bossSpawnTime;
        private bool isRoundRunning;
        private bool cameraReady;
        private bool bossStarted;
        private bool bossDefeated;
        private bool rewardFinished;
        private bool supplyStarted;
        /// <summary>최종 보스 최후 연출이 시작됨 → 처치해도 보상 단계로 가지 않는다 (게임 클리어)</summary>
        private bool finaleStarted;

        public bool IsRunning => isRoundRunning;
        public float ElapsedSeconds => elapsedSeconds;
        public event Action<GameState> PhaseChanged;
        public event Action Completed;

        private void Awake()
        {
            if (monsterWaveDirector == null) monsterWaveDirector = FindAnyObjectByType<MonsterWaveDirector>();
            if (bossEncounter == null) bossEncounter = GetComponent<RoundBossEncounter>();
            if (bossEncounter == null) bossEncounter = gameObject.AddComponent<RoundBossEncounter>();
        }

        private void OnEnable()
        {
            bossEncounter.Defeated += OnBossDefeated;
            EventBus<CameraModeChangedEvent>.Subscribe(OnCameraModeChanged);
            EventBus<RewardSelectionFinishedEvent>.Subscribe(OnRewardSelectionFinished);
            EventBus<BossFinaleStartedEvent>.Subscribe(OnBossFinaleStarted);
        }

        public bool CanStartRound()
        {
            if (monsterWaveDirector == null || monsterWaveDirector.isActiveAndEnabled == false)
            {
                Debug.LogError("[RoundSequencer] 활성 MonsterWaveDirector를 연결해주세요.", this);
                return false;
            }
            return bossEncounter.Prepare();
        }

        /// <summary>
        /// 한 라운드를 군중전부터 시작한다. RunDirector 가 라운드마다 부른다.
        /// 웨이브는 진행 라운드 번호를 웨이브 데이터의 라운드 번호로 돌려 쓴다 (GetWaveRoundNumber).
        /// </summary>
        /// <param name="def">보스·보급 시각을 정하는 라운드 데이터. null 이면 시작하지 않는다</param>
        /// <param name="roundNumber">RunDirector 의 진행 라운드 번호 (1부터, 계속 늘어남)</param>
        public void StartRound(RoundDef def, int roundNumber)
        {
            if (isRoundRunning || def == null) return;

            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();

            roundDef = def;
            this.roundNumber = roundNumber;
            elapsedSeconds = 0f;
            bossSpawnTime = def.RollBossSpawnTime(UnityEngine.Random.value);
            cameraReady = false;
            bossStarted = false;
            bossDefeated = false;
            rewardFinished = false;
            supplyStarted = false;
            finaleStarted = false;
            isRoundRunning = true;
            ChangePhase(GameState.CrowdBattle);
            if (isRoundRunning == false) return;

            int waveRoundNumber = GetWaveRoundNumber(roundNumber);
            Debug.Log($"[RoundSequencer] 진행 {roundNumber}라운드 → 웨이브 라운드 {waveRoundNumber}", this);
            monsterWaveDirector.StartRound(waveRoundNumber);
            UpdateSupplyStart();
        }

        /// <summary>
        /// 진행 라운드 번호를 웨이브 라운드 번호로 바꾼다. WaveStageTable 에 있는 RoundNo 를 작은 순서로 돌려 쓴다 (모듈러 순환).
        /// 예: 웨이브 라운드가 1~5 이면 진행 1~5 → 1~5, 6 → 1, 7 → 2.
        /// </summary>
        /// <param name="roundNumber">진행 라운드 번호 (1부터)</param>
        /// <returns>MonsterWaveDirector.StartRound 에 넘길 RoundNo</returns>
        /// <exception cref="DivideByZeroException">WaveStageTable 에 스테이지가 하나도 없을 때</exception>
        private static int GetWaveRoundNumber(int roundNumber)
        {
            var waveRoundNumbers = new List<int>();
            foreach (WaveStageDef stage in DataManager.WaveStages)
            {
                if (waveRoundNumbers.Contains(stage.RoundNo)) continue;
                waveRoundNumbers.Add(stage.RoundNo);
            }
            waveRoundNumbers.Sort();

            int cycleIndex = (roundNumber - 1) % waveRoundNumbers.Count;
            return waveRoundNumbers[cycleIndex];
        }

        public void StopRound()
        {
            isRoundRunning = false;
            cameraReady = false;
            bossStarted = false;
            bossDefeated = false;
            rewardFinished = false;
            finaleStarted = false;
            if (monsterWaveDirector != null) monsterWaveDirector.StopRound();
            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();
            if (bossEncounter != null) bossEncounter.Stop();
        }

        private void Update()
        {
            if (isRoundRunning == false) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f12Key.wasPressedThisFrame && phase == GameState.CrowdBattle) BeginBossIntro();
#endif

            switch (phase)
            {
                case GameState.CrowdBattle:
                    elapsedSeconds += Time.deltaTime;
                    if (elapsedSeconds >= bossSpawnTime) BeginBossIntro();
                    else UpdateSupplyStart();
                    break;
                case GameState.BossIntro:
                    if (cameraReady == false) return;
                    if (bossStarted == false)
                    {
                        bossStarted = true;
                        // 보스는 진행 라운드 번호의 RoundTable 행으로 고른다 (roundDef 는 항상 1 라운드 행)
                        bossEncounter.Begin(roundDef.Boss, DataManager.GetRound(roundNumber)?.BossId ?? roundDef.BossId);
                    }
                    if (isRoundRunning == false) return;
                    if (bossDefeated) BeginReward();
                    else if (bossEncounter.IsEntranceComplete) BeginBossBattle();
                    break;
                case GameState.BossBattle:
                    if (bossDefeated) BeginReward();
                    break;
                case GameState.Reward:
                    if (rewardFinished == false) return;
                    isRoundRunning = false;
                    Completed?.Invoke();
                    break;
            }
        }

        private void BeginBossIntro()
        {
            monsterWaveDirector.StopRound();
            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();
            // 카메라 전환은 상태 변경 콜백 안에서 즉시 완료될 수도 있다.
            cameraReady = false;
            ChangePhase(GameState.BossIntro);
            if (isRoundRunning) EventBus<BossWarningEvent>.Publish(new BossWarningEvent(roundDef.Boss));
        }

        /// <summary>보스 등장 연출이 끝나면 보스전. 인트로 동안 멈췄던 보급을 한 간격 뒤부터 다시 투하한다</summary>
        private void BeginBossBattle()
        {
            ChangePhase(GameState.BossBattle);
            if (isRoundRunning && supplyDropper != null) supplyDropper.StartAutoDropCycle(roundDef.SupplyInterval, dropImmediately: false);
        }

        /// <summary>보스 처치 → 보상 단계. 보급 투하를 멈춘다 (필드의 보급은 라운드 정리 때 제거)</summary>
        private void BeginReward()
        {
            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();
            ChangePhase(GameState.Reward);
        }

        private void ChangePhase(GameState next)
        {
            phase = next;
            PhaseChanged?.Invoke(next);
        }

        private void OnCameraModeChanged(CameraModeChangedEvent e)
        {
            if (isRoundRunning && phase == GameState.BossIntro && e.Mode == CameraMode.ThirdPerson && e.IsComplete) cameraReady = true;
        }

        /// <summary>보스 처치 → 다음 Update 에서 보상 단계. 최종 보스 최후 연출 중이면 보상 없이 연출이 게임 클리어로 끝낸다</summary>
        private void OnBossDefeated()
        {
            if (finaleStarted) return;
            if (isRoundRunning && (phase == GameState.BossIntro || phase == GameState.BossBattle)) bossDefeated = true;
        }

        /// <summary>최종 보스 최후 연출 시작 → 보급 투하를 멈추고, 처치해도 보상 단계로 가지 않게 표시</summary>
        private void OnBossFinaleStarted(BossFinaleStartedEvent e)
        {
            if (isRoundRunning == false) return;
            finaleStarted = true;
            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();
        }

        private void OnRewardSelectionFinished(RewardSelectionFinishedEvent e)
        {
            if (isRoundRunning && phase == GameState.Reward) rewardFinished = true;
        }

        /// <summary>보급 시작 시각(RoundDef.SupplyStartTime, 라운드 시작 기준)이 되면 자동 투하를 한 번 켠다</summary>
        private void UpdateSupplyStart()
        {
            if (supplyStarted == false && elapsedSeconds >= roundDef.SupplyStartTime) StartSupplyDropCycle();
        }

        private void StartSupplyDropCycle()
        {
            supplyStarted = true;
            if (supplyDropper != null) supplyDropper.StartAutoDropCycle(roundDef.SupplyInterval);
        }

        private void OnDisable()
        {
            bossEncounter.Defeated -= OnBossDefeated;
            EventBus<CameraModeChangedEvent>.Unsubscribe(OnCameraModeChanged);
            EventBus<RewardSelectionFinishedEvent>.Unsubscribe(OnRewardSelectionFinished);
            EventBus<BossFinaleStartedEvent>.Unsubscribe(OnBossFinaleStarted);
            StopRound();
        }
    }
}
