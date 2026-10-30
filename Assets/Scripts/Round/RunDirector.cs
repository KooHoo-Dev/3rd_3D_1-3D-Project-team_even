using System.Collections;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Round
{
    /// <summary>라운드 반복·최종 사망·재시작을 관리한다. 진행 횟수와 관계없이 1라운드 RoundDef 를 재사용한다. 웨이브 라운드는 RoundSequencer 가 진행 횟수로 순환한다.</summary>
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
            if (startOnPlay) StartRun();
        }

        [ContextMenu("Start Run")]
        public void StartRun()
        {
            if (isActiveAndEnabled == false || isRunning || CurrentState != GameState.Ready) return;
            CurrentRound = 0;
            BeginNextRound();
        }

        [ContextMenu("Restart Run")]
        public void RestartRun()
        {
            if (isActiveAndEnabled == false || CurrentState != GameState.GameOver) return;

            roundSequencer.StopRound();
            nextRoundPending = false;
            gameOverPending = false;
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(true));
            CurrentRound = 0;
            ChangeState(GameState.Ready);
            EventBus<CameraModeRequestedEvent>.Publish(new CameraModeRequestedEvent(CameraMode.FirstPerson, 0f));
            BeginNextRound();
        }

        private void BeginNextRound()
        {
            var def = DataManager.GetRound(1);
            if (def == null || roundSequencer.CanStartRound() == false) return;

            isRunning = true;
            CurrentRound++;
            // Round는 진행 횟수다. RoundDef 는 항상 1이고, 웨이브 번호는 RoundSequencer 가 이 횟수로 순환해 고른다.
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
