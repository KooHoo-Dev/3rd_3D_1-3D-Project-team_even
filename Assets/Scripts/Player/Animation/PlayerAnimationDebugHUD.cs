using System.Collections.Generic;
using System.Text;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>
    /// 애니메이션 디버그 표시 (F3 켜기/끄기). 화면 왼쪽 위에 레이어별 지금 상태 이름·전환 중인 상태·가중치,
    /// 주요 파라미터, 이동 상태를 띄운다 → 플레이 영상만 보고도 어느 상태에서 문제가 났는지 알 수 있게
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAnimationDebugHUD : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Key toggleKey = Key.F3;
        [SerializeField] private int fontSize = 16;

        private PlayerAnimationDriver driver;
        private PlayerController player;
        private AimIK aimIK;
        private PlayerBodyIK bodyIK;
        private PlayerAimIK playerAimIK;
        private PlayerFirstPersonIK fpIK;
        private Dictionary<int, string> stateNames;
        private readonly StringBuilder sb = new StringBuilder(512);
        private GUIStyle style;

        private void Awake()
        {
            driver = GetComponent<PlayerAnimationDriver>();
            player = GetComponent<PlayerController>();
            aimIK = GetComponentInChildren<AimIK>();
            bodyIK = GetComponent<PlayerBodyIK>();
            playerAimIK = GetComponent<PlayerAimIK>();
            fpIK = GetComponent<PlayerFirstPersonIK>();
            stateNames = new Dictionary<int, string>();
            foreach (string state in PlayerAnimNames.States.All) stateNames[Animator.StringToHash(state)] = state;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) visible = !visible;
        }

        private void OnGUI()
        {
            if (visible == false || driver == null || driver.Animator == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = fontSize, richText = true };

            Animator anim = driver.Animator;
            sb.Clear();
            sb.AppendLine($"<b>Animation</b>  (F3)   set: {driver.CurrentSet?.name ?? "-"}");
            for (int i = 0; i < anim.layerCount; i++)
            {
                AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(i);
                sb.Append($"[{i}] {anim.GetLayerName(i)} w{anim.GetLayerWeight(i):0.00}: {Name(current.shortNameHash)} {current.normalizedTime % 1f:0.00}");
                if (anim.IsInTransition(i)) sb.Append($"  → {Name(anim.GetNextAnimatorStateInfo(i).shortNameHash)}");
                sb.AppendLine();
            }

            sb.AppendLine($"MoveX {anim.GetFloat(PlayerAnimNames.Hash.MoveX):0.00}  MoveY {anim.GetFloat(PlayerAnimNames.Hash.MoveY):0.00}  " +
                          $"Scale {anim.GetFloat(PlayerAnimNames.Hash.MoveSpeedScale):0.00}  Moving {anim.GetBool(PlayerAnimNames.Hash.Moving)}  Sprint {anim.GetBool(PlayerAnimNames.Hash.Sprint)}");
            sb.AppendLine($"Grounded {anim.GetBool(PlayerAnimNames.Hash.Grounded)}  Falling {anim.GetBool(PlayerAnimNames.Hash.Falling)}  " +
                          $"LandType {anim.GetInteger(PlayerAnimNames.Hash.LandType)}");
            sb.AppendLine($"Aim {driver.AimWeight:0.00}  UpperPose state {driver.UpperPoseWeight:0.00} layer {driver.UpperPoseLayerWeight:0.00}  " +
                          $"AimIK w{(aimIK != null && aimIK.isActiveAndEnabled ? aimIK.solver.IKPositionWeight : 0f):0.00}  " +
                          $"ActionBlend {driver.ActionBlend:0.00}  ActionSpeed {anim.GetFloat(PlayerAnimNames.Hash.ActionSpeed):0.00}");
            sb.AppendLine($"Rolling {anim.GetBool(PlayerAnimNames.Hash.Rolling)}  Dodge ({anim.GetFloat(PlayerAnimNames.Hash.DodgeX):0.0},{anim.GetFloat(PlayerAnimNames.Hash.DodgeY):0.0}) x{anim.GetFloat(PlayerAnimNames.Hash.DodgeSpeed):0.00}  " +
                          $"Hit ({anim.GetFloat(PlayerAnimNames.Hash.HitX):0.0},{anim.GetFloat(PlayerAnimNames.Hash.HitY):0.0})  Dead {anim.GetBool(PlayerAnimNames.Hash.Dead)} dir {anim.GetInteger(PlayerAnimNames.Hash.DeathDir)}");
            if (bodyIK != null) sb.Append($"LeftHand IK w{bodyIK.LeftHandWeight:0.00}  Foot IK w{bodyIK.FootWeight:0.00}");
            if (playerAimIK != null) sb.Append($"  Muzzle err {playerAimIK.MuzzleError:0.0}°");
            if (fpIK != null) sb.Append($"  FP IK w{fpIK.Weight:0.00} wall {fpIK.WallPull * 100f:0}cm hand err {fpIK.HandPositionError:0.0}cm {fpIK.HandRotationError:0}° gun {fpIK.GunAngleError:0}°");
            if (bodyIK != null || playerAimIK != null) sb.AppendLine();
            if (player != null)
                sb.AppendLine($"speed {player.HorizontalSpeed:0.00} m/s  grounded {player.IsGrounded}  sliding {player.IsSliding}  " +
                              $"ground {(float.IsInfinity(player.GroundDistance) ? "-" : player.GroundDistance.ToString("0.00"))} m  vy {player.Velocity.y:0.00}");

            var content = new GUIContent(sb.ToString());
            Vector2 size = style.CalcSize(content);
            GUI.Box(new Rect(10, 10, size.x + 10, size.y + 6), content, style);
        }

        private string Name(int hash) => stateNames.TryGetValue(hash, out string name) ? name : hash.ToString();
    }
}
