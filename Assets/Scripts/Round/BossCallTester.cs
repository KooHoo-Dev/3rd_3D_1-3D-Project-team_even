using System.Collections;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Round
{
    /// <summary>
    /// 테스트 전용 (보스 등장 사운드·연출 확인). stage1Key·stage2Key 를 누르면 지금 라운드를 정리하고
    /// 1라운드(stage1Key) 또는 마지막 라운드(stage2Key, 최종 보스)를 시작한 뒤, 군중전을 건너뛰고 바로 보스를 부른다.
    /// 보스는 RoundTable 의 그 라운드 BossId 로 정해진다 (1 = 클리페오사우루스, 2 = 티라노포드 = 마지막 라운드).
    /// RunDirector 가 진행 중일 때만 동작하고, RoundSequencer 의 공개 메서드만 쓴다 (RoundJumpTester 와 같은 순서).
    /// </summary>
    /// <remarks>
    /// 라운드를 시작한 프레임에 바로 보스를 부르면 군중전 시점 전환과 겹칠 수 있어 bossDelay 만큼 기다렸다 부른다.
    /// RunDirector.CurrentRound 는 바뀌지 않는다. 기본 키 F10·F11 은 다른 테스터와 겹치지 않는다.
    /// </remarks>
    public sealed class BossCallTester : MonoBehaviour
    {
        [SerializeField] private RunDirector runDirector;
        [SerializeField] private RoundSequencer roundSequencer;
        [Tooltip("1스테이지 보스 (1라운드)")]
        [SerializeField] private Key stage1Key = Key.F10;
        [Tooltip("최종 보스 (RoundTable 마지막 라운드)")]
        [SerializeField] private Key stage2Key = Key.F11;
        [Tooltip("라운드를 시작하고 보스를 부르기까지 기다리는 시간(초)")]
        [SerializeField, Min(0f)] private float bossDelay = 0.5f;

        private Coroutine calling;

        private void Awake()
        {
            if (runDirector == null) runDirector = FindAnyObjectByType<RunDirector>();
            if (roundSequencer == null) roundSequencer = FindAnyObjectByType<RoundSequencer>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[stage1Key].wasPressedThisFrame) CallBoss(1);
            else if (keyboard[stage2Key].wasPressedThisFrame) CallBoss(DataManager.Rounds.All.Count);
        }

        /// <param name="stage">시작할 라운드 번호. 이 라운드의 RoundTable.BossId 가 나온다</param>
        private void CallBoss(int stage)
        {
            if (runDirector == null || roundSequencer == null)
            {
                Debug.LogWarning("[BossCallTester] 씬에 RunDirector·RoundSequencer 가 없습니다.", this);
                return;
            }
            if (runDirector.IsRunning == false)
            {
                Debug.LogWarning("[BossCallTester] 런이 진행 중일 때만 보스를 부를 수 있습니다 (게임오버·시작 전 제외).", this);
                return;
            }

            if (calling != null) StopCoroutine(calling);
            calling = StartCoroutine(CallBossRoutine(stage));
        }

        /// <summary>정리 → 라운드 시작 → bossDelay 뒤 보스 등장</summary>
        private IEnumerator CallBossRoutine(int stage)
        {
            roundSequencer.StopRound();
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
            if (roundSequencer.CanStartRound() == false) yield break;

            var def = DataManager.GetRound(stage);
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(stage, def));
            roundSequencer.StartRound(def, stage);

            yield return new WaitForSeconds(bossDelay);
            calling = null;

            if (roundSequencer.SkipToBoss()) Debug.Log($"[BossCallTester] {stage}스테이지 보스 호출 (BossId {def.BossId})", this);
            else Debug.LogWarning($"[BossCallTester] 군중전이 아니거나 보스 없는 라운드({def.Type})라 보스를 부르지 못했습니다.", this);
        }
    }
}
