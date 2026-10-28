using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Supply;
using UnityEngine;

namespace HybridShooter.Round
{
    /// <summary>군중전부터 보상 선택 완료까지 한 라운드의 순서를 실행한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RoundBossEncounter))]
    public sealed class RoundSequencer : MonoBehaviour
    {
        private enum TimedSituation
        {
            StartSupplyDropCycle
        }

        [Serializable]
        private sealed class TimelineEntry
        {
            [Min(0f)] public float timeSeconds;
            public TimedSituation situation;
        }

        [Header("References")]
        [SerializeField] private MonsterWaveDirector monsterWaveDirector;
        [SerializeField] private SupplyDropper supplyDropper;
        [SerializeField] private RoundBossEncounter bossEncounter;

        [Header("Round Timeline")]
        [SerializeField] private List<TimelineEntry> timeline = new List<TimelineEntry>();

        private RoundDef roundDef;
        private GameState phase;
        private float elapsedSeconds;
        private float bossSpawnTime;
        private int nextTimelineIndex;
        private bool isRoundRunning;
        private bool cameraReady;
        private bool bossDefeated;
        private bool rewardFinished;
        private bool supplyStarted;

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

        public void StartRound(RoundDef def)
        {
            if (isRoundRunning || def == null) return;

            timeline.RemoveAll(entry => entry == null);
            timeline.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));
            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();

            roundDef = def;
            elapsedSeconds = 0f;
            nextTimelineIndex = 0;
            bossSpawnTime = def.RollBossSpawnTime(UnityEngine.Random.value);
            cameraReady = false;
            bossDefeated = false;
            rewardFinished = false;
            supplyStarted = false;
            isRoundRunning = true;
            ChangePhase(GameState.CrowdBattle);
            if (isRoundRunning == false) return;

            monsterWaveDirector.StartRound(1);
            ProcessTimeline();
        }

        public void StopRound()
        {
            isRoundRunning = false;
            cameraReady = false;
            bossDefeated = false;
            rewardFinished = false;
            if (monsterWaveDirector != null) monsterWaveDirector.StopRound();
            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();
            if (bossEncounter != null) bossEncounter.Stop();
        }

        private void Update()
        {
            if (isRoundRunning == false) return;

            switch (phase)
            {
                case GameState.CrowdBattle:
                    elapsedSeconds += Time.deltaTime;
                    if (elapsedSeconds >= bossSpawnTime) BeginBossIntro();
                    else ProcessTimeline();
                    break;
                case GameState.BossIntro:
                    if (cameraReady == false) return;
                    ChangePhase(GameState.BossBattle);
                    if (isRoundRunning) bossEncounter.Begin(roundDef.Boss);
                    break;
                case GameState.BossBattle:
                    if (bossDefeated) ChangePhase(GameState.Reward);
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

        private void ChangePhase(GameState next)
        {
            phase = next;
            PhaseChanged?.Invoke(next);
        }

        private void OnCameraModeChanged(CameraModeChangedEvent e)
        {
            if (isRoundRunning && phase == GameState.BossIntro && e.Mode == CameraMode.ThirdPerson && e.IsComplete) cameraReady = true;
        }

        private void OnBossDefeated()
        {
            if (isRoundRunning && phase == GameState.BossBattle) bossDefeated = true;
        }

        private void OnRewardSelectionFinished(RewardSelectionFinishedEvent e)
        {
            if (isRoundRunning && phase == GameState.Reward) rewardFinished = true;
        }

        private void ProcessTimeline()
        {
            if (timeline.Count == 0 && supplyStarted == false && elapsedSeconds >= roundDef.SupplyStartTime) StartSupplyDropCycle();
            while (isRoundRunning && nextTimelineIndex < timeline.Count && elapsedSeconds >= timeline[nextTimelineIndex].timeSeconds)
            {
                TimedSituation situation = timeline[nextTimelineIndex].situation;
                nextTimelineIndex++;
                if (situation == TimedSituation.StartSupplyDropCycle) StartSupplyDropCycle();
            }
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
            StopRound();
        }
    }
}
