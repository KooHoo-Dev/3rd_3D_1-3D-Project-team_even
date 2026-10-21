using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>
    /// Input System 에서 입력을 읽어 IPlayerInputSource 로 노출한다. 이동·카메라 로직은 전혀 모른다.
    /// 액션은 코드로 만든다 (InputSystem_Actions 에는 구르기가 없어서 에셋을 건드리지 않기 위해).
    /// 키 변경은 아래 바인딩 문자열만 고치면 된다.
    /// 일시정지·강화 선택·게임오버에서는 입력을 끊고 커서를 풀어준다.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour, IPlayerInputSource
    {
        [Tooltip("게임패드 오른쪽 스틱을 마우스 픽셀 단위로 환산할 때의 초당 픽셀 (0.15 감도 기준 600 = 초당 90도)")]
        [SerializeField] private float stickLookPixelsPerSecond = 600f;
        [SerializeField] private bool lockCursor = true;

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction rollAction;
        private InputAction sprintAction;

        private bool jumpBuffered;
        private bool rollBuffered;
        private bool inputEnabled = true;

        public bool InputEnabled => inputEnabled;

        public Vector2 Move => inputEnabled ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;

        public Vector2 Look
        {
            get
            {
                if (inputEnabled == false) return Vector2.zero;
                var value = lookAction.ReadValue<Vector2>();
                // 마우스 delta 는 이미 "프레임당 이동량", 스틱은 -1~1 값이라 시간을 곱해 환산
                return lookAction.activeControl?.device is Gamepad ? value * (stickLookPixelsPerSecond * Time.unscaledDeltaTime) : value;
            }
        }

        public bool SprintHeld => inputEnabled && sprintAction.IsPressed();

        public bool ConsumeJump() { bool v = jumpBuffered; jumpBuffered = false; return v; }
        public bool ConsumeRoll() { bool v = rollBuffered; rollBuffered = false; return v; }

        private void Awake()
        {
            moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddBinding("<Gamepad>/leftStick");

            lookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            lookAction.AddBinding("<Mouse>/delta");
            lookAction.AddBinding("<Gamepad>/rightStick");

            jumpAction = new InputAction("Jump", InputActionType.Button);
            jumpAction.AddBinding("<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");

            rollAction = new InputAction("Roll", InputActionType.Button);
            rollAction.AddBinding("<Keyboard>/leftCtrl");
            rollAction.AddBinding("<Gamepad>/buttonEast");

            sprintAction = new InputAction("Sprint", InputActionType.Button);
            sprintAction.AddBinding("<Keyboard>/leftShift");
            sprintAction.AddBinding("<Gamepad>/leftStickPress");

            jumpAction.performed += OnJump;
            rollAction.performed += OnRoll;
        }

        private void OnEnable()
        {
            moveAction.Enable();
            lookAction.Enable();
            jumpAction.Enable();
            rollAction.Enable();
            sprintAction.Enable();
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            SetInputEnabled(true);
        }

        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            moveAction.Disable();
            lookAction.Disable();
            jumpAction.Disable();
            rollAction.Disable();
            sprintAction.Disable();
            SetCursorLocked(false);
        }

        private void OnDestroy()
        {
            jumpAction.performed -= OnJump;
            rollAction.performed -= OnRoll;
            moveAction.Dispose();
            lookAction.Dispose();
            jumpAction.Dispose();
            rollAction.Dispose();
            sprintAction.Dispose();
        }

        private void OnJump(InputAction.CallbackContext _) { if (inputEnabled) jumpBuffered = true; }
        private void OnRoll(InputAction.CallbackContext _) { if (inputEnabled) rollBuffered = true; }

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            bool playable = evt.Current != GameState.Paused && evt.Current != GameState.Reward && evt.Current != GameState.GameOver;
            SetInputEnabled(playable);
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
            if (value == false) { jumpBuffered = false; rollBuffered = false; }
            SetCursorLocked(value);
        }

        private void SetCursorLocked(bool locked)
        {
            if (lockCursor == false) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
