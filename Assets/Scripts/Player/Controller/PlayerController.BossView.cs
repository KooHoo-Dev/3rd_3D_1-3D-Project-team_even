using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 보스전 시점 (BL-022, BL-023).
    ///  - GameState.BossIntro: 3인칭으로 자동 전환 + 보스전 카메라(멀고 높게, FOV 추가) + F1 잠금
    ///  - 보스전이 끝나고 Ready / CrowdBattle: 1인칭으로 복귀 + 잠금 해제 (BossBattle·Reward 동안은 그대로)
    ///  - CameraModeRequestedEvent: 연출·디버그용 요청 (Duration &lt; 0 = ConfigTable.CameraTransitionTime, 0 = 즉시)
    ///  - BossAppearedEvent: 보스에 IBossCameraProfile 이 있으면 보스전 카메라 거리를 그 배율로 (큰 보스가 화면에 다 들어오게)
    /// 전환 시작·완료는 SwitchViewMode 가 CameraModeChangedEvent 로 알린다 → 보스는 IsComplete 를 받고 등장 공격
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>보스전 시점 고정 중 (F1 전환 막힘)</summary>
        public bool IsViewLocked => viewLocked;
        /// <summary>보스전 카메라를 쓰는 중</summary>
        public bool IsBossView => bossView;

        /// <summary>3인칭 FOV. 보스전 카메라가 켜진 만큼 bossFovAdd 를 더한다</summary>
        private float TpFov => tpFov + bossFovAdd * bossBlend;
        /// <summary>3인칭 카메라 팔 기본 위치. 어깨 시점 ↔ 보스전 카메라 사이를 bossBlend 로 섞는다</summary>
        private Vector3 TpArmHome => Vector3.Lerp(tpArmHomePos, bossArmPosition * bossCameraScale, bossBlend);
        private const float BossCameraScaleSpeed = 0.4f; // 배율이 초당 바뀌는 양

        /// <summary>ConfigTable 값이 있으면 인스펙터 값 대신 쓴다</summary>
        private void LoadBossViewConfig()
        {
            var config = DataManager.Config;
            if (config == null) return;
            if (config.Has(GameConfig.CameraTransitionTime)) autoTransitionTime = config.GetFloat(GameConfig.CameraTransitionTime, autoTransitionTime);
            if (config.Has(GameConfig.BossCameraFovAdd)) bossFovAdd = config.GetFloat(GameConfig.BossCameraFovAdd, bossFovAdd);
        }

        private void SubscribeViewEvents()
        {
            ServiceLocator.Register<IPlayerTarget>(this);
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<CameraModeRequestedEvent>.Subscribe(OnCameraModeRequested);
            EventBus<BossAppearedEvent>.Subscribe(OnBossAppearedCamera);
            SubscribeShakeEvents();
        }

        private void UnsubscribeViewEvents()
        {
            ServiceLocator.Unregister<IPlayerTarget>(this);
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<CameraModeRequestedEvent>.Unsubscribe(OnCameraModeRequested);
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppearedCamera);
            UnsubscribeShakeEvents();
        }

        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            SetFlowInputBlocked(e.Current == GameState.Ready || e.Current == GameState.Reward || e.Current == GameState.GameOver || e.Current == GameState.Paused);
            if (e.Current == GameState.BossIntro) EnterBossView();
            else if (e.Current == GameState.Ready || e.Current == GameState.CrowdBattle) ExitBossView();
        }

        private void OnCameraModeRequested(CameraModeRequestedEvent e)
        {
            var mode = e.Mode == CameraMode.FirstPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson;
            SwitchViewMode(mode, e.Duration < 0f ? autoTransitionTime : e.Duration);
        }

        private void OnBossAppearedCamera(BossAppearedEvent e)
        {
            bossCameraScaleTarget = e.Instance != null && e.Instance.TryGetComponent(out IBossCameraProfile profile)
                ? profile.CameraDistanceScale
                : 1f;
        }

        private void EnterBossView()
        {
            viewLocked = true;
            SetBossFraming(true, autoTransitionTime);
            SwitchViewMode(ViewMode.ThirdPerson, autoTransitionTime);
        }

        /// <summary>보스전을 거친 경우에만 1인칭으로 돌린다 (처음 시작할 때 시점은 건드리지 않는다)</summary>
        private void ExitBossView()
        {
            if (bossView == false && viewLocked == false) return;
            viewLocked = false;
            bossCameraScaleTarget = 1f;
            SetBossFraming(false, autoTransitionTime);
            SwitchViewMode(ViewMode.FirstPerson, autoTransitionTime);
        }

        /// <summary>
        /// 보스전 카메라 켜기/끄기. 3인칭 카메라가 안 보일 때(1인칭)는 바로 바꾸고,
        /// 보이는 중이면 duration 동안 섞는다. 끌 때는 1인칭 전환이 끝난 뒤에 되돌린다 (UpdateBossFraming)
        /// </summary>
        private void SetBossFraming(bool on, float duration)
        {
            bossView = on;
            bossBlendTarget = on ? 1f : 0f;
            bossBlendSpeed = duration > 0f ? 1f / duration : float.PositiveInfinity;

            bool tpHidden = viewMode == ViewMode.FirstPerson && CameraChanging == false;
            if (on && tpHidden) SnapBossFraming();
        }

        private void SnapBossFraming()
        {
            bossBlend = bossBlendTarget;
            tpArmDistance = Mathf.Max(0f, -TpArmHome.z); // 벽에 막혀 있으면 UpdateCameraArm 이 바로 당긴다
        }

        /// <summary>LateUpdate: bossBlend 를 목표로. 1인칭으로 돌아가는 연출 중에는 출발점(3인칭 카메라)이 흔들리지 않게 멈춘다</summary>
        private void UpdateBossFraming(float dt)
        {
            bossCameraScale = Mathf.MoveTowards(bossCameraScale, bossCameraScaleTarget, BossCameraScaleSpeed * dt);
            if (Mathf.Approximately(bossBlend, bossBlendTarget)) return;
            if (CameraChanging && transitionTarget == ViewMode.FirstPerson) return;
            if (viewMode == ViewMode.FirstPerson && CameraChanging == false)
            {
                SnapBossFraming();
                return;
            }
            bossBlend = Mathf.MoveTowards(bossBlend, bossBlendTarget, bossBlendSpeed * dt);
        }
    }
}
