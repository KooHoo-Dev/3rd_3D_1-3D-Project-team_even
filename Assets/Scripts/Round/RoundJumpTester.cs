using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Round
{
    /// <summary>
    /// 테스트 전용. F5~F9 를 누르면 지금 라운드의 몬스터를 모두 정리하고 1~5 라운드를 바로 시작한다.
    /// RunDirector 가 진행 중일 때만 동작하고, RoundSequencer 의 공개 메서드만 쓴다.
    /// </summary>
    /// <remarks>
    /// RunDirector.CurrentRound 는 바뀌지 않는다. 점프한 라운드가 끝나면 다음 라운드는 RunDirector 의 원래 횟수 + 1 로 이어진다.
    /// BossViewTester(F5~F8), PlaytestAnalyzer HUD(F8·F9)와 키가 겹친다. 같은 씬에서 함께 켜지 않는다.
    /// </remarks>
    public sealed class RoundJumpTester : MonoBehaviour
    {
        [SerializeField] private RunDirector runDirector;
        [SerializeField] private RoundSequencer roundSequencer;

        private void Awake()
        {
            if (runDirector == null) runDirector = FindAnyObjectByType<RunDirector>();
            if (roundSequencer == null) roundSequencer = FindAnyObjectByType<RoundSequencer>();
        }

        private void Update()
        {
            int roundNumber = GetPressedRoundNumber();
            if (roundNumber == 0) return;

            JumpToRound(roundNumber);
        }

        /// <summary>이번 프레임에 누른 F 키를 라운드 번호로 바꾼다.</summary>
        /// <returns>F5~F9 = 1~5, 아무것도 안 눌렀으면 0</returns>
        private static int GetPressedRoundNumber()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0;

            if (keyboard.f5Key.wasPressedThisFrame) return 1;
            if (keyboard.f6Key.wasPressedThisFrame) return 2;
            if (keyboard.f7Key.wasPressedThisFrame) return 3;
            if (keyboard.f8Key.wasPressedThisFrame) return 4;
            if (keyboard.f9Key.wasPressedThisFrame) return 5;
            return 0;
        }

        /// <summary>
        /// 지금 라운드를 멈추고 정리 이벤트로 몬스터·보급을 치운 뒤, 같은 프레임에 roundNumber 라운드를 시작한다.
        /// RunDirector 의 라운드 전환(정리 → RoundStartedEvent → StartRound)과 같은 순서다.
        /// </summary>
        /// <param name="roundNumber">시작할 라운드 번호 (1~5). RoundSequencer 가 웨이브 라운드로 순환해 고른다</param>
        private void JumpToRound(int roundNumber)
        {
            if (runDirector.IsRunning == false)
            {
                Debug.LogWarning("[RoundJumpTester] 런이 진행 중일 때만 라운드를 바꿀 수 있습니다 (게임오버·시작 전 제외).", this);
                return;
            }

            roundSequencer.StopRound();
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
            if (roundSequencer.CanStartRound() == false) return;

            var def = DataManager.GetRound(1);
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(roundNumber, def));
            roundSequencer.StartRound(def, roundNumber);
            Debug.Log($"[RoundJumpTester] {roundNumber}라운드로 이동", this);
        }
    }
}
