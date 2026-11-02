using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// 군중전부터 보상 선택 완료까지 한 라운드의 순서를 실행한다.
    /// 군중전이 끝나는 조건은 RoundDef.Type 이 정한다: CrowdToBoss = 보스 시각 → 보스전 → 보스 처치 ·
    /// Annihilation = 누적 처치 목표 달성 (PhaseTime 에 소환만 끝남) · AnnihilationToBoss = 섬멸 목표 달성 → 보스전 → 보스 처치 ·
    /// Survival = PhaseTime 생존. 모두 Reward(강화 선택)로 끝난다 (최종 보스는 게임 클리어).
    /// 웨이브 편성은 라운드 시작마다 RoundDef.WaveRounds 후보에서 무작위로 고른다 (같은 출발에서 앞 라운드가 쓴 편성 제외).
    /// </summary>
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

        // ── 섬멸 목표 ──
        private int killCount;              // 이번 라운드 군중전 누적 처치 수 (GrantReward 사망만, 종류 무관)
        private int killTarget;             // 이번 라운드 목표 처치 수 (마리, 고른 편성으로 계산). 섬멸 유형이 아니면 0
        private bool reinforcementEnded;    // 섬멸전 PhaseTime 이 지나 소환을 끝냈는지

        // ── 웨이브 편성 ──
        private int waveRound;                                       // 이번 라운드가 쓰는 편성 (WaveStageTable.RoundNo)
        private readonly List<int> usedWaveRounds = new List<int>(); // 이번 출발(1라운드부터)에서 이미 쓴 편성

        public bool IsRunning => isRoundRunning;
        public float ElapsedSeconds => elapsedSeconds;
        // 목표 HUD 가 매 프레임 읽는 값
        public RoundDef CurrentRoundDef => roundDef;
        public int RoundNumber => roundNumber;
        public GameState Phase => phase;
        public int KillCount => killCount;
        public int KillTarget => killTarget;
        public int WaveRound => waveRound;
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
            EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);
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
        /// 웨이브 편성을 고르고 섬멸 목표를 정한 뒤 소환을 시작한다. 처치 수는 첫 소환(StartRound 안 0초 스테이지)보다 먼저 0으로 되돌린다.
        /// </summary>
        /// <param name="def">유형·웨이브 편성 후보·보스·보급 시각을 정하는 라운드 데이터. null 이면 시작하지 않는다</param>
        /// <param name="roundNumber">RunDirector 의 진행 라운드 번호 (1부터, 계속 늘어남). 1 이면 새 출발이라 쓴 편성 기록을 비운다</param>
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
            killCount = 0;
            reinforcementEnded = false;
            if (roundNumber == 1) usedWaveRounds.Clear();
            waveRound = PickWaveRound(def);
            usedWaveRounds.Add(waveRound);
            killTarget = def.IsAnnihilation ? DataManager.GetKillTarget(def, waveRound) : 0;
            isRoundRunning = true;
            ChangePhase(GameState.CrowdBattle);
            if (isRoundRunning == false) return;

            Debug.Log($"[RoundSequencer] 진행 {roundNumber}라운드 ({def.Type}) → 웨이브 편성 {waveRound} (후보 {string.Join(";", def.WaveRounds)}, 목표 처치 {killTarget})", this);
            monsterWaveDirector.StartRound(waveRound);
            UpdateSupplyStart();
        }

        /// <summary>
        /// 이번 라운드의 웨이브 편성을 후보(def.WaveRounds) 중 무작위로 고른다. 이번 출발에서 앞 라운드가 쓴 편성은 뺀다.
        /// 후보가 모두 쓰였으면 (마지막 행 재사용 등) 전체 후보에서 고른다.
        /// </summary>
        /// <param name="def">후보 목록을 가진 라운드 데이터</param>
        /// <returns>WaveStageTable.RoundNo</returns>
        private int PickWaveRound(RoundDef def)
        {
            var unusedWaveRounds = def.WaveRounds.Where(candidate => usedWaveRounds.Contains(candidate) == false).ToList();
            var pool = unusedWaveRounds.Count > 0 ? unusedWaveRounds : def.WaveRounds.ToList();
            return pool[UnityEngine.Random.Range(0, pool.Count)];
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
            if (keyboard != null && keyboard.f12Key.wasPressedThisFrame) SkipToBoss();
#endif

            switch (phase)
            {
                case GameState.CrowdBattle:
                    elapsedSeconds += Time.deltaTime;
                    UpdateCrowdBattle();
                    break;
                case GameState.BossIntro:
                    if (cameraReady == false) return;
                    if (bossStarted == false)
                    {
                        bossStarted = true;
                        bossEncounter.Begin(roundDef.Boss, roundDef.BossId);
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

        /// <summary>
        /// 군중전 한 프레임: 라운드 유형별 종료 조건을 보고, 아직 끝나지 않았으면 보급 시작 시각을 확인한다.
        /// Update 가 경과 시간을 늘린 뒤 부른다.
        /// </summary>
        private void UpdateCrowdBattle()
        {
            switch (roundDef.Type)
            {
                case RoundType.CrowdToBoss:
                    UpdateCrowdToBoss();
                    break;
                case RoundType.Annihilation:
                case RoundType.AnnihilationToBoss:
                    UpdateAnnihilation();
                    break;
                case RoundType.Survival:
                    UpdateSurvival();
                    break;
            }
        }

        /// <summary>기존 군중전: 보스 등장 시각이 되면 보스 등장으로 넘긴다</summary>
        private void UpdateCrowdToBoss()
        {
            bool isBossTime = elapsedSeconds >= bossSpawnTime;
            if (isBossTime)
            {
                BeginBossIntro();
                return;
            }
            UpdateSupplyStart();
        }

        /// <summary>
        /// 섬멸전: 누적 처치가 목표에 닿으면 남은 소환을 버리고, Annihilation 은 보스 없이 보상 단계로,
        /// AnnihilationToBoss 는 보스 등장(기존 보스 라운드와 같은 경고·카메라 전환·등장 연출)으로 간다.
        /// PhaseTime 이 지나도 목표 미달이면 소환만 끝낸다 (남은 적을 잡아 목표를 채움, 실패 전이 없음).
        /// </summary>
        private void UpdateAnnihilation()
        {
            bool hasReachedKillTarget = killCount >= killTarget;
            bool isReinforcementTimeOver = elapsedSeconds >= roundDef.PhaseTime;
            if (hasReachedKillTarget && roundDef.HasBoss)
            {
                BeginBossIntro();
                return;
            }
            if (hasReachedKillTarget)
            {
                BeginRewardWithoutBoss();
                return;
            }
            if (isReinforcementTimeOver && reinforcementEnded == false) EndReinforcement();

            UpdateSupplyStart();
        }

        /// <summary>생존전: PhaseTime 을 버티면 보스 없이 보상 단계로 간다. 처치 수·남은 적은 보지 않는다</summary>
        private void UpdateSurvival()
        {
            bool hasSurvived = elapsedSeconds >= roundDef.PhaseTime;
            if (hasSurvived)
            {
                BeginRewardWithoutBoss();
                return;
            }
            UpdateSupplyStart();
        }

        /// <summary>섬멸전 소환 시간 끝: 남은 소환만 취소한다. 이미 나온 적과 보급 투하는 그대로 둔다</summary>
        private void EndReinforcement()
        {
            reinforcementEnded = true;
            monsterWaveDirector.StopRound();
            Debug.Log($"[RoundSequencer] 진행 {roundNumber}라운드 증원 종료 (처치 {killCount} / {killTarget})", this);
        }

        /// <summary>
        /// 보스 없는 라운드 클리어 → 남은 소환을 취소하고 보상 단계로 간다. 보스 경고·카메라 전환·보스 생성은 하지 않는다.
        /// 필드에 남은 적·보급은 강화 선택 뒤 RunDirector 의 RoundCleanupEvent 가 치운다 (보상 단계는 시간이 멈춰 있음).
        /// </summary>
        private void BeginRewardWithoutBoss()
        {
            monsterWaveDirector.StopRound();
            BeginReward();
        }

        /// <summary>테스트용 (F12·BossCallTester): 보스 라운드의 군중전 중이면 보스 시각을 기다리지 않고 바로 보스 등장으로 넘긴다</summary>
        /// <returns>넘겼으면 true. 라운드가 멈췄거나, 군중전이 아니거나, 보스 없는 라운드면 false</returns>
        public bool SkipToBoss()
        {
            if (isRoundRunning == false || phase != GameState.CrowdBattle) return false;
            if (roundDef.HasBoss == false) return false;
            BeginBossIntro();
            return true;
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

        /// <summary>라운드 클리어(보스 처치·섬멸 목표·생존 시간) → 보상 단계. 보급 투하를 멈춘다 (필드의 보급은 라운드 정리 때 제거)</summary>
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

        /// <summary>
        /// 군중전 중 처치를 센다. 종류는 가리지 않는다. GrantReward = false(자폭 성공·보스 등장/라운드 정리)는 플레이어 처치가 아니라 세지 않는다.
        /// 보상 단계 이후·라운드 밖의 사망은 세지 않는다.
        /// </summary>
        private void OnMonsterDied(MonsterDiedEvent e)
        {
            bool isCrowdBattleRunning = isRoundRunning && phase == GameState.CrowdBattle;
            if (isCrowdBattleRunning == false) return;
            if (e.GrantReward == false) return;
            killCount++;
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
            EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
            StopRound();
        }
    }
}
