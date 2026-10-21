using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>
    /// 테스트 전용: F1 = 1인칭, F3 = 3인칭 전환을 요청한다. (숫자 1~4 는 무기 교체 키라 F 키로 옮김) (CameraModeRequestedEvent 발행 → CameraModeSwitcher 가 처리)
    /// 씬의 아무 오브젝트에 붙이면 동작하고, 테스트가 끝나면 컴포넌트만 제거하면 된다.
    /// </summary>
    public class CameraModeDebugInput : MonoBehaviour
    {
        [Tooltip("-1 = ConfigTable 전환 시간, 0 = 즉시")]
        [SerializeField] private float transitionTime = -1f;
        [SerializeField] private bool showOverlay = true;

        private CameraModeSwitcher switcher;
        private CameraMode lastRequested = CameraMode.FirstPerson;

        private void Awake()
        {
            switcher = FindFirstObjectByType<CameraModeSwitcher>();
            if (switcher == null)
                Debug.LogWarning("[CameraModeDebugInput] 씬에 CameraModeSwitcher 가 없습니다. 요청해도 전환되지 않습니다.");
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f1Key.wasPressedThisFrame)
                Request(CameraMode.FirstPerson);
            else if (keyboard.f3Key.wasPressedThisFrame)
                Request(CameraMode.ThirdPerson);
        }

        private void Request(CameraMode mode)
        {
            lastRequested = mode;
            EventBus<CameraModeRequestedEvent>.Publish(new CameraModeRequestedEvent(mode, transitionTime));
            Debug.Log($"[CameraModeDebugInput] 요청: {mode}");
        }

        private void OnGUI()
        {
            if (showOverlay == false) return;

            string state = switcher == null
                ? "Switcher 없음"
                : $"{switcher.TargetMode}{(switcher.IsTransitioning ? " (전환 중)" : "")}";
            GUI.Label(new Rect(10, 10, 400, 24), $"[F1] 1인칭   [F3] 3인칭   현재: {state}   마지막 요청: {lastRequested}");
        }
    }
}
