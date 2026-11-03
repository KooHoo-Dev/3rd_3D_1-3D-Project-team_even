using System.Collections;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Round
{
    /// <summary>
    /// 라운드 반복·최종 사망·재시작을 관리한다. 진행 횟수(CurrentRound)의 RoundTable 행으로 라운드를 시작한다
    /// (유형·웨이브 편성·보스는 그 행이 정함, 마지막 행 이후는 마지막 행 재사용).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RoundSequencer))]
    public sealed class RunDirector : MonoBehaviour
    {
        [SerializeField] private RoundSequencer roundSequencer;
        [SerializeField] private bool startOnPlay = true;

        private bool isRunning;
        private bool nextRoundPending;
        private bool gameOverPending;
        private bool simulationPaused;
        private float playTimeScale = 1f;

        public int CurrentRound { get; private set; }
        public GameState CurrentState { get; private set; } = GameState.Ready;
        public bool IsRunning => isRunning;

        private void Awake()
        {
            if (roundSequencer == null) roundSequencer = GetComponent<RoundSequencer>();
        }

        private void OnEnable()
        {
            roundSequencer.PhaseChanged += OnPhaseChanged;
            roundSequencer.Completed += OnRoundCompleted;
            EventBus<PlayerFinalDeathEvent>.Subscribe(OnPlayerFinalDeath);
        }

        private IEnumerator Start()
        {
            // 플레이어의 시작 위치와 각 파트의 Start 초기화가 끝난 뒤 시작한다.
            yield return null;
            // 무기 선택 화면(IRoundStartGate)이 붙잡고 있으면 그 화면의 [출발]이 StartRun 을 부른다
            if (startOnPlay && IsStartHeld == false) StartRun();
        }

        /// <summary>시작을 붙잡는 화면이 있는지 (등록되지 않았으면 false)</summary>
        private static bool IsStartHeld => ServiceLocator.TryGet<IRoundStartGate>(out var gate) && gate.HoldsStart;

        [ContextMenu("Start Run")]
        public void StartRun()
        {
            if (isActiveAndEnabled == false || isRunning || CurrentState != GameState.Ready) return;
            CurrentRound = 0;
            BeginNextRound();
        }

        /// <summary>게임 오버 → 처음부터 바로 다시 시작 (무기 선택 화면을 거치지 않음)</summary>
        [ContextMenu("Restart Run")]
        public void RestartRun()
        {
            if (ReturnToReady()) BeginNextRound();
        }

        /// <summary>
        /// 게임 오버 → 처음부터 재시작 준비만 하고 Ready 에서 멈춘다 (전부 정리·1인칭 복귀).
        /// 무기 선택 화면(LoadoutPanel)이 GameOver → Ready 를 보고 다시 열리고, [출발]에서 StartRun 을 부른다.
        /// </summary>
        [ContextMenu("Return To Ready")]
        public bool ReturnToReady()
        {
            if (isActiveAndEnabled == false || CurrentState != GameState.GameOver) return false;

            roundSequencer.StopRound();
            nextRoundPending = false;
            gameOverPending = false;
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(true));
            CurrentRound = 0;
            ChangeState(GameState.Ready);
            EventBus<CameraModeRequestedEvent>.Publish(new CameraModeRequestedEvent(CameraMode.FirstPerson, 0f));
            return true;
        }

        private void BeginNextRound()
        {
            var def = DataManager.GetRound(CurrentRound + 1);
            if (def == null || roundSequencer.CanStartRound() == false) return;

            isRunning = true;
            CurrentRound++;
            // Round 는 진행 횟수다. def 는 그 횟수의 RoundTable 행 (유형·웨이브 편성·보스)
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(CurrentRound, def));
            if (isRunning) roundSequencer.StartRound(def, CurrentRound);
        }

        private void OnPhaseChanged(GameState phase)
        {
            if (isRunning) ChangeState(phase);
        }

        private void OnRoundCompleted()
        {
            if (isRunning && CurrentState == GameState.Reward) nextRoundPending = true;
        }

        private void OnPlayerFinalDeath(PlayerFinalDeathEvent e)
        {
            if (isRunning == false) return;

            isRunning = false;
            nextRoundPending = false;
            gameOverPending = true;
            roundSequencer.StopRound();
        }

        private void LateUpdate()
        {
            // 같은 프레임의 보상 완료와 최종 사망은 최종 사망을 우선한다.
            if (gameOverPending)
            {
                gameOverPending = false;
                ChangeState(GameState.GameOver);
                EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
                EventBus<GameOverEvent>.Publish(new GameOverEvent(CurrentRound));
                return;
            }

            if (nextRoundPending == false || isRunning == false) return;
            // 보상 뒤 무기 선택 화면이 열려 있으면 Reward 상태(시간 정지)로 기다린다
            if (IsStartHeld) return;
            nextRoundPending = false;
            roundSequencer.StopRound();
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
            if (isRunning) BeginNextRound();
        }

        private void ChangeState(GameState state)
        {
            if (CurrentState == state) return;
            var previous = CurrentState;
            CurrentState = state;
            SetSimulationPaused(state == GameState.Reward || state == GameState.GameOver);
            EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(previous, state));
            // Reward 는 라운드 클리어(보스 처치 · 섬멸 목표 · 생존 시간)로만 들어온다 (RoundSequencer) → 스테이지 클리어. 보상 화면이 열린 뒤에 알린다
            if (state == GameState.Reward) EventBus<StageClearedEvent>.Publish(new StageClearedEvent(CurrentRound));
        }

        private void SetSimulationPaused(bool paused)
        {
            if (simulationPaused == paused) return;
            if (paused) playTimeScale = Time.timeScale;
            Time.timeScale = paused ? 0f : playTimeScale;
            simulationPaused = paused;
        }

        private void OnDisable()
        {
            roundSequencer.PhaseChanged -= OnPhaseChanged;
            roundSequencer.Completed -= OnRoundCompleted;
            EventBus<PlayerFinalDeathEvent>.Unsubscribe(OnPlayerFinalDeath);
            bool needsCleanup = isRunning || gameOverPending;
            isRunning = false;
            nextRoundPending = false;
            gameOverPending = false;
            roundSequencer.StopRound();
            if (needsCleanup) EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
            ChangeState(GameState.Ready);
            SetSimulationPaused(false);
        }
    }
}
