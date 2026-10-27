using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player.Tester
{
    /// <summary>
    /// 카메라 흔들림 테스트 (보스 없이). 씬에 두고 Play.
    ///  - F9  보스 등장: BossAppearedEvent 발행 → 플레이어 자동 흔들림 (shakeOnBossAppear)
    ///  - F10 보스 걸음 한 번: 플레이어 앞 footstepDistance 에서 CameraShakeRequestedEvent (거리 감쇠)
    ///  - F11 보스가 걷는 중 켜기/끄기: footstepInterval 마다 걸음 흔들림 (보스 애니메이션 이벤트 대신)
    ///  - F12 강한 한 방 (Impact)
    /// 흔들리는 동안 조준점(화면 중앙 광선)이 움직이지 않는지, 1·3인칭·전환·스코프에서 튀지 않는지 본다
    /// </summary>
    public sealed class CameraShakeTester : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [Tooltip("걸음 진원까지 거리(m)")]
        [SerializeField] private float footstepDistance = 12f;
        [Tooltip("걸음 흔들림 감쇠 반경(m). 이 거리 밖에서는 흔들리지 않는다")]
        [SerializeField] private float footstepRadius = 40f;
        [Tooltip("걷는 중 걸음 간격(초)")]
        [SerializeField] private float footstepInterval = 0.9f;

        private bool walking;
        private float nextStepTime;
        private GUIStyle style;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f9Key.wasPressedThisFrame) EventBus<BossAppearedEvent>.Publish(new BossAppearedEvent(null, null));
            if (keyboard.f10Key.wasPressedThisFrame) Footstep();
            if (keyboard.f11Key.wasPressedThisFrame)
            {
                walking = walking == false;
                nextStepTime = Time.time;
            }
            if (keyboard.f12Key.wasPressedThisFrame) EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(CameraShakePresets.Impact));

            if (walking && Time.time >= nextStepTime)
            {
                nextStepTime = Time.time + footstepInterval;
                Footstep();
            }
        }

        private void Footstep()
        {
            Vector3 origin = player != null ? player.transform.position + player.Forward * footstepDistance : transform.position;
            var settings = CameraShakePresets.BossFootstep.At(origin, footstepRadius);
            EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(settings));
        }

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            float y = Screen.height - 60f;
            GUI.Label(new Rect(10f, y, 900f, 24f), "[F9] 보스 등장  [F10] 걸음  [F11] 걷기 켜기/끄기  [F12] 강한 한 방", style);
            string status = player == null ? "Player 없음"
                : $"걷는 중 {walking}  흔들림 {(player.IsShaking ? "있음" : "없음")}  배율 {player.ShakeScale:0.00}  시점 {player.CurrentViewMode}";
            GUI.Label(new Rect(10f, y + 22f, 900f, 24f), status, style);
        }
    }
}
