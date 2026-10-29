using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Behaviour;
using UnityEngine;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    /// <summary>
    /// 테스트 전용. 시퀀서 대신 웨이브 시작·중단을 직접 호출한다. 알림 이벤트는 다른 파트의 테스트용이다.
    /// Inspector 메뉴(ContextMenu)만 쓴다. F 키는 쓰지 않는다 (다른 Lab 과 겹침).
    /// </summary>
    /// <remarks>
    /// 이벤트는 다른 구독자(보상·사운드·플레이어 카메라 등)도 받는다. 씬에 그 컴포넌트가 있으면 함께 반응한다.
    /// 배속은 Time.timeScale 을 바꾼다. 라운드 180초를 4배속이면 실제 45초에 본다.
    /// </remarks>
    public sealed class WaveDirectorLab : MonoBehaviour
    {
        [SerializeField] private MonsterWaveDirector director;

        private void Awake()
        {
            if (director == null) director = FindAnyObjectByType<MonsterWaveDirector>();
        }

        /// <summary>MonsterWaveDirector의 1라운드를 직접 시작하고, 다른 파트에 시작을 알린다.</summary>
        [ContextMenu("라운드 1 시작")]
        public void StartRound1()
        {
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(1, null));
            if (director != null) director.StartRound(1);
            Debug.Log("[WaveDirectorLab] 라운드 1 시작 발행", this);
        }

        /// <summary>RoundCleanupEvent 를 발행한다. 남은 소환이 멈추고 몬스터는 스스로 반납된다</summary>
        [ContextMenu("라운드 정리")]
        public void CleanupRound()
        {
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false));
            Debug.Log("[WaveDirectorLab] 라운드 정리 발행", this);
        }

        /// <summary>시퀀서 대신 StopRound를 직접 호출하고 보스전 전이를 알린다.</summary>
        [ContextMenu("보스전 전이")]
        public void EnterBossIntro()
        {
            if (director != null) director.StopRound();
            EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(GameState.CrowdBattle, GameState.BossIntro));
            Debug.Log("[WaveDirectorLab] 보스전 전이 발행", this);
        }

        /// <summary>시간을 4배로 빠르게 한다</summary>
        [ContextMenu("배속 4x")]
        public void SetSpeed4x() => Time.timeScale = 4f;

        /// <summary>시간을 원래 속도로 되돌린다</summary>
        [ContextMenu("배속 1x")]
        public void SetSpeed1x() => Time.timeScale = 1f;
    }
}
