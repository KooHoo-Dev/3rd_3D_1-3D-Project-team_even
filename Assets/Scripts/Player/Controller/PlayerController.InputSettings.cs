using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>키 배치</summary>
    public partial class PlayerController
    {
        public class InputPlayerSettings
        {
            public Key jumpKey = Key.Space;
            public Key sprintKey = Key.LeftShift;
            public Key RollKey = Key.LeftCtrl;
            public Key frontKey = Key.W;
            public Key backKey = Key.S;
            public Key leftKey = Key.A;
            public Key rightKey = Key.D;
            public Key viewToggleKey = Key.F1;
            public Key cursorToggleKey = Key.Escape;
            public Key reloadKey = Key.R;
            public Key fireModeKey = Key.B;
            public Key grenadeKey = Key.G;
            public Key healKey = Key.V;
            public Key[] slotKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
            /// <summary>테스트용: 주무기를 예비 총으로 교체</summary>
            public Key debugSwapPrimaryKey = Key.T;


            public InputPlayerSettings() { }

            public InputPlayerSettings(Key jump, Key sprint, Key roll, Key front, Key back, Key left, Key right, Key viewToggle = Key.F1)
            {
                viewToggleKey = viewToggle;
                jumpKey = jump;
                sprintKey = sprint;
                RollKey = roll;
                frontKey = front;
                backKey = back;
                leftKey = left;
                rightKey = right;
            }

        }
    }
}
