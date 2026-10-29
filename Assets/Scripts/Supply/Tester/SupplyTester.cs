using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Supply.Tester
{
    /// <summary>
    /// 보급 시스템 인게임 확인용 (SupplyPackTestscene).
    ///  - 1 키: 보급 투하 (SupplyTable 가중치 랜덤)
    ///  - 투하 위치·플레이어와의 XZ 거리를 콘솔에 출력
    ///  - 화면 우상단에 다음 자동 투하까지 남은 시간 / 필드 보급 수 표시
    /// </summary>
    public sealed class SupplyTester : MonoBehaviour
    {
        [SerializeField] private SupplyDropper dropper;
        [SerializeField] private bool showStatus = true;

        private GUIStyle statusStyle;

        private void Awake()
        {
            if (dropper == null) dropper = FindAnyObjectByType<SupplyDropper>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || dropper == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                Drop();
        }

        private void OnGUI()
        {
            if (showStatus == false || dropper == null) return;
            statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperRight };

            float next = dropper.TimeUntilNextDrop;
            string nextText = float.IsInfinity(next) ? "자동 투하 꺼짐" : $"다음 자동 투하 {next:0.0}초";
            string limitText = dropper.IsAtLimit ? " (최대 — 투하 중지)" : string.Empty;
            string text = $"{nextText}\n필드 보급 {dropper.ActiveCrates.Count} / {dropper.MaxActiveSupplies}{limitText}";
            GUI.Label(new Rect(Screen.width - 520f, 20f, 500f, 80f), text, statusStyle);
        }

        private void Drop()
        {
            var crate = dropper.DropRandom();
            if (crate == null) return;

            string distance = "-";
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null)
            {
                Vector3 offset = crate.LandingPosition - player.Root.position;
                offset.y = 0f;
                distance = $"{offset.magnitude:0.0}m";
            }
            Debug.Log($"[SupplyTester] {crate.Def.DisplayName} x{crate.Def.AmountMin}~{crate.Def.AmountMax} 투하 → 착지 지점 {crate.LandingPosition}, 플레이어와 XZ 거리 {distance}, 낙하 높이 {crate.transform.position.y - crate.LandingPosition.y:0.0}m");
        }
    }
}
