using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Player
{
    /// <summary>
    /// EventBus 알림을 받아 1인칭 ↔ 3인칭을 부드럽게 전환한다. 카메라 위치 계산은 PlayerCameraRig 가 하고,
    /// 이 클래스는 "언제, 얼마나 걸려서" 만 책임진다.
    ///
    ///  받는 이벤트
    ///   - GameStateChangedEvent: BossIntro → 3인칭, Ready/CrowdBattle → 1인칭 (BL-022)
    ///   - CameraModeRequestedEvent: 직접 요청 (연출·디버그)
    ///  발행하는 이벤트
    ///   - CameraModeChangedEvent(mode, false) 전환 시작, (mode, true) 전환 완료  → 보스가 완료를 기다렸다가 등장 공격 시작
    /// </summary>
    public class CameraModeSwitcher : MonoBehaviour
    {
        [SerializeField] private PlayerCameraRig rig;
        [SerializeField] private CameraMode startMode = CameraMode.FirstPerson;
        [Tooltip("ConfigTable 에 CameraTransitionTime 이 없을 때 쓰는 전환 시간(초)")]
        [SerializeField] private float fallbackTransitionTime = 1.5f;
        [Tooltip("전환 진행도 → 보간 곡선")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("1인칭에서 숨길 몸체 (선택)")]
        [Tooltip("1인칭이 되면 그림자만 남기고 숨긴다. 카메라가 몸 안에 있어 보이는 것을 막음")]
        [SerializeField] private Renderer[] bodyRenderers;
        [Tooltip("블렌드가 이 값 미만이면 몸체를 숨김")]
        [SerializeField, Range(0f, 1f)] private float hideBodyBelowBlend = 0.15f;

        private float linear;          // 선형 진행도 0 = 1인칭, 1 = 3인칭
        private float target;
        private float duration;
        private bool transitioning;
        private CameraMode targetMode;
        private bool bodyHidden;

        public CameraMode TargetMode => targetMode;
        public bool IsTransitioning => transitioning;

        private void Awake()
        {
            targetMode = startMode;
            linear = target = startMode == CameraMode.ThirdPerson ? 1f : 0f;
        }

        private void OnEnable()
        {
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<CameraModeRequestedEvent>.Subscribe(OnModeRequested);
            if (rig != null) { rig.SetMode(targetMode); Apply(); }
        }

        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<CameraModeRequestedEvent>.Unsubscribe(OnModeRequested);
        }

        /// <summary>duration &lt; 0: ConfigTable 값, 0: 즉시</summary>
        public void SetMode(CameraMode mode, float transitionTime = -1f)
        {
            if (mode == targetMode) return;

            targetMode = mode;
            target = mode == CameraMode.ThirdPerson ? 1f : 0f;
            duration = transitionTime < 0f ? ConfigTransitionTime() : transitionTime;
            rig.SetMode(mode);

            EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(mode, false));

            if (duration <= 0f)
            {
                linear = target;
                Apply();
                Complete();
                return;
            }
            transitioning = true;
        }

        private void Update()
        {
            if (transitioning == false) return;

            // 전환 도중에 반대로 요청돼도 현재 진행도에서 곧바로 되돌아간다
            linear = Mathf.MoveTowards(linear, target, Time.unscaledDeltaTime / duration);
            Apply();
            if (Mathf.Approximately(linear, target)) Complete();
        }

        private void Complete()
        {
            transitioning = false;
            EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(targetMode, true));
        }

        private void Apply()
        {
            float blend = ease.Evaluate(linear);
            rig.SetBlend(blend);
            SetBodyHidden(blend < hideBodyBelowBlend);
        }

        private void SetBodyHidden(bool hidden)
        {
            if (hidden == bodyHidden || bodyRenderers == null) return;
            bodyHidden = hidden;
            var mode = hidden ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in bodyRenderers)
                if (r != null) r.shadowCastingMode = mode;
        }

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            switch (evt.Current)
            {
                case GameState.BossIntro:
                    SetMode(CameraMode.ThirdPerson);
                    break;
                case GameState.Ready:
                    SetMode(CameraMode.FirstPerson, 0f);   // 재시작은 즉시 원위치
                    break;
                case GameState.CrowdBattle:
                    SetMode(CameraMode.FirstPerson);
                    break;
            }
        }

        private void OnModeRequested(CameraModeRequestedEvent evt) => SetMode(evt.Mode, evt.Duration);

        private float ConfigTransitionTime()
        {
            var config = DataManager.Config;
            return config.Has(GameConfig.CameraTransitionTime)
                ? config.GetFloat(GameConfig.CameraTransitionTime, fallbackTransitionTime)
                : fallbackTransitionTime;
        }
    }
}
