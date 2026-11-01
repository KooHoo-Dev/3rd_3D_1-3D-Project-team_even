using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>
    /// 테스트 전용. 플레이어 사망 이벤트를 수동으로 발행해 몬스터가 멈추는지 확인한다.
    /// 경로: EventBus → MonsterController.OnPlayerDied → Brain.SetTarget(null) + mover.SetTarget(null) → CrowdController 전원 Idle.
    /// </summary>
    public sealed class TestPlayerDied : MonoBehaviour
    {
        [ContextMenu("Publish PlayerDiedEvent")]
        private void PublishPlayerDied() => EventBus<PlayerDiedEvent>.Publish(new PlayerDiedEvent());

        /// <summary>부활: MonsterController.OnPlayerRevived → 다음 Update 에서 대상을 다시 잡는다</summary>
        [ContextMenu("Publish PlayerRevivedEvent")]
        private void PublishPlayerRevived() => EventBus<PlayerRevivedEvent>.Publish(new PlayerRevivedEvent());
    }
}
