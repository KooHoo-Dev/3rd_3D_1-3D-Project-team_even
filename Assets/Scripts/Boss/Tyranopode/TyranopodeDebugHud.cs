using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// Tyranopode 테스트용 화면 (Test_Tyranopode 씬). F1 로 열고 닫는다 (열면 마우스 커서가 풀린다).
    ///  - 상태·페이즈·체력·부위 체력·현재 패턴·난이도 보정 값 표시
    ///  - 패턴 강제, 부위 파괴, 페이즈 2, 그로기, 등장 연출 다시 보기, 플레이어 무적, 시간 배속, 3인칭 시점
    /// 에디터·개발 빌드에서만 동작한다
    /// </summary>
    public sealed class TyranopodeDebugHud : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 찾는다 (라운드가 나중에 만든 보스도)")]
        [SerializeField] private TyranopodeController boss;
        [SerializeField] private bool visible = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const float Width = 330f;
        private const int IntroCrowd = 40;
        private Vector2 scroll;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
                Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = visible;
            }
            if (boss == null) boss = FindAnyObjectByType<TyranopodeController>();
        }

        private void OnGUI()
        {
            if (visible == false) return;
            GUILayout.BeginArea(new Rect(10f, 10f, Width, Screen.height - 20f), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<b>Tyranopode 디버그</b> (F1 닫기)", Rich);

            if (boss == null || boss.Def == null)
            {
                GUILayout.Label("보스 없음 (초기화 전)");
                DrawCommon();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            var core = boss.Boss;
            GUILayout.Label($"상태 {boss.StateName} · 페이즈 {(boss.IsPhase2 ? 2 : 1)} · 무적 {core.IsInvulnerable}");
            GUILayout.Label($"체력 {core.CurrentHp}/{core.MaxHp} ({core.HpRatio:P0}) · 부서진 부위 {boss.BrokenCount}");
            GUILayout.Label($"현재 패턴 {boss.CurrentAttackName}");
            GUILayout.Label($"난이도 보정 {boss.Struggle:0.00} (공격 간격 ×{boss.AssistDelayMultiplier:0.00})");
            if (boss.ShieldHp > 0f) GUILayout.Label($"보호막 {boss.ShieldHp:0}/{boss.ShieldMax:0}");
            var minions = boss.Minions;
            GUILayout.Label($"소환 자리 버림: NavMesh 없음 {minions.RejectedNoNavMesh} · 길 끊김 {minions.RejectedDisconnected} · 크게 돎 {minions.RejectedDetour}");
            foreach (var (type, hp, max, broken) in boss.PartStatus())
                GUILayout.Label(broken ? $"  {type}: 파괴됨" : $"  {type}: {hp}/{max}");

            GUILayout.Space(6f);
            GUILayout.Label("<b>다음 패턴</b>", Rich);
            Row(("촉수", () => boss.DebugForce<TentacleSwipeAttack>()), ("물기", () => boss.DebugForce<BiteAttack>()), ("내려찍기", () => boss.DebugForce<JumpSmashAttack>()));
            Row(("돌진", () => boss.DebugForce<ChargeAttack>()), ("침", () => boss.DebugForce<SpitAttack>()), ("알", () => boss.DebugForce<EggThrowAttack>()));
            Row(("땅울림", () => boss.DebugForce<TremorAttack>()), ("포효 소환", boss.DebugSummon), ("촉수 폭풍", boss.DebugStorm));

            GUILayout.Label("<b>부위 파괴</b>", Rich);
            Row(("앞 촉수", () => boss.DebugBreak(TyranopodePartType.FrontTentacle)), ("다리", () => boss.DebugBreak(TyranopodePartType.LegTentacle)));
            Row(("주머니", () => boss.DebugBreak(TyranopodePartType.Sac)), ("머리", () => boss.DebugBreak(TyranopodePartType.Head)));

            GUILayout.Label("<b>흐름</b>", Rich);
            Row(("페이즈 2", boss.DebugPhase2), ("그로기", boss.DebugGroggy), ("보호막", boss.DebugShield));
            Row(("최후 연출", boss.DebugFinale));
            // 몬스터를 흩어 놓고 지금 자리에 다시 착지 (흡수 → 보호막까지)
            Row(("등장 연출", () => boss.DebugReplayIntro(false, IntroCrowd)), ("짧은 등장", () => boss.DebugReplayIntro(true, IntroCrowd)));
            DrawCommon();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCommon()
        {
            GUILayout.Label("<b>플레이어·시간</b>", Rich);
            if (ServiceLocator.TryGet(out IPlayerTarget target) && target is PlayerController player)
            {
                bool invincible = GUILayout.Toggle(player.DebugInvincible, " 플레이어 무적");
                if (invincible != player.DebugInvincible) player.DebugInvincible = invincible;
            }
            Row(("×0.5", () => Time.timeScale = 0.5f), ("×1", () => Time.timeScale = 1f), ("×2", () => Time.timeScale = 2f));
            Row(("3인칭", () => EventBus<CameraModeRequestedEvent>.Publish(new CameraModeRequestedEvent(CameraMode.ThirdPerson, 0.5f))),
                ("1인칭", () => EventBus<CameraModeRequestedEvent>.Publish(new CameraModeRequestedEvent(CameraMode.FirstPerson, 0.5f))));
        }

        private static void Row(params (string label, System.Action action)[] buttons)
        {
            GUILayout.BeginHorizontal();
            foreach (var (label, action) in buttons)
                if (GUILayout.Button(label)) action();
            GUILayout.EndHorizontal();
        }

        private static GUIStyle rich;
        private static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true };
#endif
    }
}
