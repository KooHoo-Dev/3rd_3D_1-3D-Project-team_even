#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using HybridShooter.Boss;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Round;
using HybridShooter.Weapon;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.UI
{
    /// <summary>
    /// 게임 흐름 디버그 창 (에디터·개발 빌드 전용, IMGUI). GameFlowUiBootstrap 이 자동으로 붙인다.
    /// toggleKey(기본 `) 로 열고 닫는다. 열면 커서를 풀어 버튼을 누를 수 있다 (닫으면 ESC 로 다시 잠근다).
    ///  - 상태: GameState · 라운드 · 무기 해금 목록 · 다음 해금
    ///  - 해금: 다음 1개 / 모두 / 초기화(기본 무기만)  · 저장된 무기 선택 지우기
    ///  - 흐름: 보스 즉시 처치(스테이지 클리어, 보스가 나와 있을 때) · 강제 게임 오버 · 무기 선택 화면 열기 · 재시작(Ready)
    ///  - 무기: 보급 무기 지급(순환) · 지금 슬롯 총 탄약 채우기
    /// 보스 등장 전이면 F12(RoundSequencer) 로 보스를 먼저 부른다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowDebugPanel : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.Backquote;

        private RunDirector runDirector;
        private GunSlot gunSlot;
        private bool visible;
        private string message = "";
        private Vector2 scroll;
        private Rect window = new Rect(20f, 80f, 360f, 560f);

        private void Awake()
        {
            runDirector = FindAnyObjectByType<RunDirector>();
            gunSlot = FindAnyObjectByType<GunSlot>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || keyboard[toggleKey].wasPressedThisFrame == false) return;
            visible = !visible;
            if (visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnGUI()
        {
            if (visible == false) return;
            window = GUILayout.Window(GetInstanceID(), window, DrawWindow, "게임 흐름 디버그 (` 닫기)");
        }

        private void DrawWindow(int id)
        {
            var ledger = Ledger;
            GUILayout.Label($"상태: {(runDirector != null ? runDirector.CurrentState.ToString() : "-")} · 라운드 {(runDirector != null ? runDirector.CurrentRound : 0)}" +
                            $" · 진행 {(runDirector != null && runDirector.IsRunning ? "O" : "X")}");
            bool held = ServiceLocator.TryGet<IRoundStartGate>(out var gate) && gate.HoldsStart;
            GUILayout.Label($"출발 대기(무기 선택): {(held ? "붙잡는 중" : "없음")}");

            GUILayout.Space(6f);
            GUILayout.Label("── 무기 해금 ──");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(130f));
            GUILayout.Label(UnlockList());
            GUILayout.EndScrollView();
            if (ledger != null)
            {
                var next = ledger.PeekNext();
                GUILayout.Label($"다음 해금: {(next != null ? next.Name : "모두 해금됨")}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("다음 1개 해금")) Say(ledger.UnlockNext() is { } d ? $"해금: {d.Name}" : "더 해금할 무기가 없습니다");
                if (GUILayout.Button("모두 해금"))
                {
                    int count = 0;
                    while (ledger.UnlockNext() != null) count++;
                    Say($"{count}개 해금");
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("해금 초기화 (기본 무기만)")) { ledger.ResetAll(); Say("해금 기록을 지웠습니다"); }
            }
            else GUILayout.Label("WeaponUnlockLedger 가 등록되지 않았습니다");
            if (GUILayout.Button("저장된 무기 선택 지우기"))
            {
                foreach (int slot in GunSlot.LoadoutSlots) PlayerPrefs.DeleteKey(LoadoutPanel.PrefsKeyPrefix + slot);
                PlayerPrefs.Save();
                Say("Loadout.Slot* 를 지웠습니다");
            }

            GUILayout.Space(6f);
            GUILayout.Label("── 흐름 ──");
            if (GUILayout.Button("보스 즉시 처치 (스테이지 클리어)")) KillBoss();
            if (GUILayout.Button("강제 게임 오버"))
            {
                EventBus<PlayerFinalDeathEvent>.Publish(new PlayerFinalDeathEvent());
                Say("PlayerFinalDeathEvent 발행");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("무기 선택 열기"))
            {
                var panel = FindAnyObjectByType<LoadoutPanel>();
                if (panel != null) panel.Open();
                Say(panel != null ? "무기 선택 화면을 열었습니다" : "LoadoutPanel 이 없습니다");
            }
            if (GUILayout.Button("재시작 (Ready)"))
                Say(runDirector != null && runDirector.ReturnToReady() ? "Ready 로 돌아감" : "게임 오버 상태에서만 됩니다");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("── 무기 ──");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("보급 무기 지급")) Say(gunSlot != null && gunSlot.DebugCycleSupplyWeapon() ? $"보급 칸: {gunSlot.SupplyWeaponId}" : "지급 실패");
            if (GUILayout.Button("지금 총 탄약 채우기"))
            {
                if (gunSlot != null && gunSlot.CurrentGun != null) gunSlot.CurrentGun.RefillAmmo();
                Say("탄약을 채웠습니다");
            }
            GUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(message) == false)
            {
                GUILayout.Space(6f);
                GUILayout.Label(message);
            }
            GUI.DragWindow();
        }

        private static WeaponUnlockLedger Ledger =>
            ServiceLocator.TryGet<IWeaponUnlocks>(out var unlocks) ? unlocks as WeaponUnlockLedger : null;

        private static string UnlockList()
        {
            var sb = new StringBuilder();
            foreach (var def in DataManager.Weapons)
            {
                bool unlocked = GunSlot.IsUnlocked(def.Id);
                sb.Append(unlocked ? "● " : "○ ").Append(def.Name).Append(" (").Append(def.Category);
                if (WeaponUnlockLedger.IsAlwaysUnlocked(def) == false) sb.Append(" · 순서 ").Append(def.UnlockOrder);
                sb.Append(")\n");
            }
            return sb.ToString().TrimEnd();
        }

        private void KillBoss()
        {
            var boss = FindAnyObjectByType<BossController>();
            if (boss == null)
            {
                Say("보스가 없습니다 (군중전 중이면 F12 로 보스를 부르세요)");
                return;
            }
            ((IDamageable)boss).TakeDamage(new DamageInfo(1000000, DamageKind.Bullet, Faction.Player, gameObject, boss.transform.position, Vector3.forward));
            Say("보스에게 큰 피해를 줬습니다");
        }

        private void Say(string text)
        {
            message = text;
            Debug.Log("[GameFlowDebug] " + text);
        }
    }
}
#endif
