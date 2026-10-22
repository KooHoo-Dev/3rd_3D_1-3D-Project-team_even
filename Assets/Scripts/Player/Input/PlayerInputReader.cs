using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>
    /// Input System 에서 입력을 읽어 IPlayerInputSource 로 노출한다. 이동·카메라 로직은 전혀 모른다.
    /// 액션은 코드로 만든다 (InputSystem_Actions 에는 구르기가 없어서 에셋을 건드리지 않기 위해).
    /// 일시정지·강화 선택·게임오버에서는 입력을 끊고 커서를 풀어준다.
    ///
    /// 키 설정(리바인딩) 준비
    ///  - 모든 액션은 한 개의 InputActionMap("Player") 에 들어 있다 → <see cref="Actions"/> 로 꺼내 리바인딩 UI 에 넘기면 된다.
    ///  - 바꾼 키는 <see cref="SaveBindingOverrides"/> 로 PlayerPrefs 에 저장, 다음 실행 때 Awake 에서 자동으로 불러온다.
    ///  - 기본 키는 아래 Awake 의 바인딩 문자열.
    ///
    /// 기본 키
    ///  이동 WASD · 시점 마우스 · 점프/벽넘기 Space · 구르기 LCtrl · 달리기 LShift
    ///  사격 좌클릭 · 조준 우클릭 · 재장전 R · 수류탄 G · 회복 V · 무기 1~4
    /// </summary>
    public class PlayerInputReader : MonoBehaviour, IPlayerInputSource
    {
        public const string BindingPrefsKey = "PlayerInputBindings";

        [Tooltip("게임패드 오른쪽 스틱을 마우스 픽셀 단위로 환산할 때의 초당 픽셀 (0.15 감도 기준 600 = 초당 90도)")]
        [SerializeField] private float stickLookPixelsPerSecond = 600f;
        [SerializeField] private bool lockCursor = true;

        private InputActionMap map;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private InputAction fireAction;
        private InputAction aimAction;

        // "한 번 누름" 액션들. 눌린 순간 버퍼에 기록, Consume 에서 꺼낸다
        private ButtonBuffer jump, roll, reload, grenade, heal;
        private readonly ButtonBuffer[] weaponSlots = new ButtonBuffer[4];

        private bool inputEnabled = true;

        public bool InputEnabled => inputEnabled;

        /// <summary>리바인딩 UI 용. 액션 이름: Move, Look, Jump, Roll, Sprint, Fire, Aim, Reload, Grenade, Heal, Weapon1~4</summary>
        public InputActionMap Actions => map;

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
        public bool FireHeld => inputEnabled && fireAction.IsPressed();
        public bool AimHeld => inputEnabled && aimAction.IsPressed();

        public bool ConsumeJump() => jump.Consume();
        public bool ConsumeRoll() => roll.Consume();
        public bool ConsumeReload() => reload.Consume();
        public bool ConsumeGrenade() => grenade.Consume();
        public bool ConsumeHeal() => heal.Consume();

        public bool ConsumeWeaponSlot(out int slot)
        {
            slot = -1;
            for (int i = 0; i < weaponSlots.Length; i++)
                if (weaponSlots[i].Consume()) slot = i; // 같은 프레임에 여러 개면 마지막 것
            return slot >= 0;
        }

        private void Awake()
        {
            map = new InputActionMap("Player");

            moveAction = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddBinding("<Gamepad>/leftStick");

            lookAction = map.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            lookAction.AddBinding("<Mouse>/delta");
            lookAction.AddBinding("<Gamepad>/rightStick");

            sprintAction = AddButton("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            fireAction = AddButton("Fire", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            aimAction = AddButton("Aim", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");

            jump = new ButtonBuffer(this, AddButton("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth"));
            roll = new ButtonBuffer(this, AddButton("Roll", "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast"));
            reload = new ButtonBuffer(this, AddButton("Reload", "<Keyboard>/r", "<Gamepad>/buttonWest"));
            grenade = new ButtonBuffer(this, AddButton("Grenade", "<Keyboard>/g", "<Gamepad>/rightShoulder"));
            heal = new ButtonBuffer(this, AddButton("Heal", "<Keyboard>/v", "<Gamepad>/leftShoulder"));

            // 무기 슬롯: 키보드 1~4, 게임패드 십자키 위·오른쪽·아래·왼쪽
            string[] pads = { "<Gamepad>/dpad/up", "<Gamepad>/dpad/right", "<Gamepad>/dpad/down", "<Gamepad>/dpad/left" };
            for (int i = 0; i < weaponSlots.Length; i++)
                weaponSlots[i] = new ButtonBuffer(this, AddButton($"Weapon{i + 1}", $"<Keyboard>/{i + 1}", pads[i]));

            LoadBindingOverrides();
        }

        private InputAction AddButton(string name, string keyboardOrMouse, string gamepad)
        {
            var action = map.AddAction(name, InputActionType.Button);
            action.AddBinding(keyboardOrMouse);
            action.AddBinding(gamepad);
            return action;
        }

        private void OnEnable()
        {
            map.Enable();
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            SetInputEnabled(true);
        }

        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            map.Disable();
            SetCursorLocked(false);
        }

        private void OnDestroy()
        {
            jump.Dispose(); roll.Dispose(); reload.Dispose(); grenade.Dispose(); heal.Dispose();
            foreach (var slot in weaponSlots) slot.Dispose();
            map.Dispose();
        }

        // ───────────────────────── 키 설정 저장/불러오기 ─────────────────────────

        /// <summary>현재 바인딩 변경 사항을 PlayerPrefs 에 저장 (리바인딩 UI 에서 호출)</summary>
        public void SaveBindingOverrides()
        {
            PlayerPrefs.SetString(BindingPrefsKey, map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public void LoadBindingOverrides()
        {
            string json = PlayerPrefs.GetString(BindingPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json) == false) map.LoadBindingOverridesFromJson(json);
        }

        /// <summary>모든 키를 기본값으로 되돌리고 저장된 설정도 지운다</summary>
        public void ResetBindingOverrides()
        {
            map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(BindingPrefsKey);
        }

        // ───────────────────────── 상태 ─────────────────────────

        private void OnGameStateChanged(GameStateChangedEvent evt)
        {
            bool playable = evt.Current != GameState.Paused && evt.Current != GameState.Reward && evt.Current != GameState.GameOver;
            SetInputEnabled(playable);
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
            if (value == false)
            {
                jump.Clear(); roll.Clear(); reload.Clear(); grenade.Clear(); heal.Clear();
                foreach (var slot in weaponSlots) slot.Clear();
            }
            SetCursorLocked(value);
        }

        private void SetCursorLocked(bool locked)
        {
            if (lockCursor == false) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>버튼이 눌린 순간을 기억했다가 Consume 에서 한 번만 돌려준다</summary>
        private sealed class ButtonBuffer
        {
            private readonly PlayerInputReader owner;
            private readonly InputAction action;
            private bool pressed;
    
            public ButtonBuffer(PlayerInputReader owner, InputAction action)
            {
                this.owner = owner;
                this.action = action;
                action.performed += OnPerformed;
            }

            private void OnPerformed(InputAction.CallbackContext _) { if (owner.inputEnabled) pressed = true; }

            public bool Consume() { bool v = pressed; pressed = false; return v; }
            public void Clear() => pressed = false;
            public void Dispose() => action.performed -= OnPerformed;
        }
    }
}
