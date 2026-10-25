using System.Collections.Generic;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player.Tester
{
    /// <summary>
    /// 보스전 시점 전환 테스트 (코어·보스 없이). 씬에 두고 Play.
    ///  - F5 BossIntro / F6 BossBattle / F7 Reward / F8 CrowdBattle 을 GameStateChangedEvent 로 발행
    ///  - CameraModeChangedEvent 를 받을 때마다 모드·완료 여부·마지막 상태 발행 후 걸린 시간을 콘솔과 화면 좌상단에 출력
    ///  - 보스 대신 "BossIntro 이후 IsComplete 를 받았는지"를 표시한다 (받기 전에는 등장 공격 금지)
    /// </summary>
    public sealed class BossViewTester : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private int maxLines = 8;

        private readonly List<string> lines = new List<string>();
        private GameState state = GameState.CrowdBattle;
        private float stateTime;
        private bool waitingEntrance;
        private GUIStyle style;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
        }

        private void OnEnable() => EventBus<CameraModeChangedEvent>.Subscribe(OnCameraModeChanged);
        private void OnDisable() => EventBus<CameraModeChangedEvent>.Unsubscribe(OnCameraModeChanged);

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f5Key.wasPressedThisFrame) Publish(GameState.BossIntro);
            if (keyboard.f6Key.wasPressedThisFrame) Publish(GameState.BossBattle);
            if (keyboard.f7Key.wasPressedThisFrame) Publish(GameState.Reward);
            if (keyboard.f8Key.wasPressedThisFrame) Publish(GameState.CrowdBattle);
        }

        private void Publish(GameState next)
        {
            var previous = state;
            state = next;
            stateTime = Time.time;
            waitingEntrance = next == GameState.BossIntro;
            Log($"GameState {previous} → {next}");
            EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(previous, next));
        }

        private void OnCameraModeChanged(CameraModeChangedEvent e)
        {
            float elapsed = Time.time - stateTime;
            Log($"CameraModeChanged {e.Mode} {(e.IsComplete ? "완료" : "시작")} (+{elapsed:0.00}s)");
            if (waitingEntrance && e.IsComplete && e.Mode == CameraMode.ThirdPerson)
            {
                waitingEntrance = false;
                Log($"→ 보스 등장 공격 가능 (BossIntro 후 {elapsed:0.00}s)");
            }
        }

        private void Log(string message)
        {
            Debug.Log($"[BossViewTester] {message}", this);
            lines.Add(message);
            if (lines.Count > maxLines) lines.RemoveAt(0);
        }

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            float y = 10f;
            string status = player == null ? "Player 없음"
                : $"상태 {state}  시점 {player.CurrentViewMode}{(player.CameraChanging ? " (전환 중)" : "")}  잠금 {player.IsViewLocked}  보스카메라 {player.IsBossView}";
            GUI.Label(new Rect(10f, y, 900f, 24f), "[F5] BossIntro  [F6] BossBattle  [F7] Reward  [F8] CrowdBattle", style);
            GUI.Label(new Rect(10f, y += 22f, 900f, 24f), status, style);
            if (waitingEntrance) GUI.Label(new Rect(10f, y += 22f, 900f, 24f), "등장 공격 대기 중 (전환 완료 전)", style);
            foreach (var line in lines) GUI.Label(new Rect(10f, y += 22f, 900f, 24f), line, style);
        }
    }
}
