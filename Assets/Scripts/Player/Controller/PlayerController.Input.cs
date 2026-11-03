using Study.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>키보드·마우스 입력 → 이동·무기·아이템 요청</summary>
    public partial class PlayerController
    {
        private bool flowInputBlocked;
        private bool menuInputBlocked;
        /// <summary>게임 흐름(Ready·Reward·GameOver·Paused)·메뉴(출발 전 무기 선택 등)·연출(PlayerController.Cinematic) 때문에 입력이 막힘</summary>
        public bool IsGameplayInputBlocked => flowInputBlocked || menuInputBlocked || cinematicActive;

        private PlayerAbility allowedAbilities = PlayerAbility.All;
        /// <summary>입력으로 할 수 있는 행동 (기본 전부). 튜토리얼이 과목마다 좁힌다</summary>
        public PlayerAbility AllowedAbilities => allowedAbilities;

        public bool IsAllowed(PlayerAbility ability) => (allowedAbilities & ability) == ability;

        /// <summary>
        /// 입력으로 할 수 있는 행동을 정한다. 흐름·메뉴 차단(IsGameplayInputBlocked)과 별도라 서로 덮어쓰지 않는다.
        /// 막히는 순간 하던 이동·조준·사격은 놓는다 (이미 시작한 구르기·투척·회복은 끝까지 간다)
        /// </summary>
        public void SetAllowedAbilities(PlayerAbility abilities)
        {
            if (allowedAbilities == abilities) return;
            allowedAbilities = abilities;
            if (IsAllowed(PlayerAbility.Move) == false) SetMoveInput(Vector3.zero, false);
            if (gunSlot == null) return;
            if (IsAllowed(PlayerAbility.Fire) == false) gunSlot.TriggerUp();
            if (IsAllowed(PlayerAbility.Aim) == false && gunSlot.IsAiming) gunSlot.AimUp();
        }

        /// <summary>
        /// 메뉴 화면(출발 전 무기 선택 등)이 이동·사격·시선 입력을 막고 커서를 보인다.
        /// 게임 흐름 차단(GameStateChangedEvent)과 별도 플래그라 서로 덮어쓰지 않는다
        /// </summary>
        public void SetMenuInputBlocked(bool blocked)
        {
            if (menuInputBlocked == blocked) return;
            menuInputBlocked = blocked;
            ApplyInputBlock();
        }

        private void InputHandle()
        {
            if (IsGameplayInputBlocked) return;
            // 마우스 → 시선 회전.
            UpdateLook();
            SyncRigRotationNow();
            LockFacing(lookYaw);
            if(IsDead) return;
            // 0) 커서 잠금 해제/복귀 (좌클릭은 조준용으로 비워 둔다)
            if (SimpleInput.GetKeyDown(keySettings.cursorToggleKey))
                SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

            // 1) 시점 전환
            if (SimpleInput.GetKeyDown(keySettings.viewToggleKey)) ToggleViewMode();


            // 이동 키 → 로컬 입력 (x = 좌우, y = 앞뒤)
            Vector2 input = Vector2.zero;
            if (IsAllowed(PlayerAbility.Move))
            {
                if (SimpleInput.GetKey(keySettings.frontKey)) input.y += 1f;
                if (SimpleInput.GetKey(keySettings.backKey)) input.y -= 1f;
                if (SimpleInput.GetKey(keySettings.rightKey)) input.x += 1f;
                if (SimpleInput.GetKey(keySettings.leftKey)) input.x -= 1f;
            }
            input = Vector2.ClampMagnitude(input, 1f); // 대각선이 더 빨라지지 않게

            // 로컬 입력 → 월드 방향
            Vector3 worldMove = InputToWorld(input);

            // 이동 + 달리기 (누르고 있는 동안). 조준 중에는 달리지 않는다
            bool sprint = IsAllowed(PlayerAbility.Sprint) && SimpleInput.GetKey(keySettings.sprintKey) && (gunSlot == null || gunSlot.IsAiming == false);
            SetMoveInput(worldMove, sprint);

            // 점프
            if (IsAllowed(PlayerAbility.Jump) && SimpleInput.GetKeyDown(keySettings.jumpKey)) RequestJump();

            // 구르기: 이동 입력이 있으면 그 방향, 없으면 몸 정면
            if (IsAllowed(PlayerAbility.Roll) && SimpleInput.GetKeyDown(keySettings.RollKey))
            {
                Vector3 rollDir = worldMove.sqrMagnitude > 0.0001f ? worldMove : Forward;
                TryRoll(rollDir);
            }

            WeaponInputHandle();
            ItemInputHandle();
            InteractInputHandle();
        }

        /// <summary>상호작용 입력 → PlayerInteractor (F). 대상 판정·상태 조건은 PlayerInteractor 가 한다</summary>
        private void InteractInputHandle()
        {
            if (interactor == null || Cursor.lockState != CursorLockMode.Locked) return;
            if (IsAllowed(PlayerAbility.Interact) && SimpleInput.GetKeyDown(keySettings.interactKey)) interactor.TryInteract();
        }

        /// <summary>소모품 입력 → PlayerItems. G 수류탄, V 회복 팩. 커서가 풀려 있으면 무시</summary>
        private void ItemInputHandle()
        {
            if (items == null || Cursor.lockState != CursorLockMode.Locked) return;
            if (IsAllowed(PlayerAbility.Grenade) && SimpleInput.GetKeyDown(keySettings.grenadeKey)) items.TryThrowGrenade();
            if (IsAllowed(PlayerAbility.Heal) && SimpleInput.GetKeyDown(keySettings.healKey)) items.TryUseHeal();
        }

        /// <summary>
        /// 무기 입력 → GunSlot → 지금 든 총. 좌클릭 사격, 우클릭 조준, R 재장전, B 사격 모드, 1~4 / 휠 무기 교체.
        /// 커서가 풀려 있으면(Esc) 방아쇠·조준을 놓는다
        /// </summary>
        private void WeaponInputHandle()
        {
            if (gunSlot == null) return;
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                gunSlot.TriggerUp();
                gunSlot.AimUp();
                return;
            }

            if (IsAllowed(PlayerAbility.Fire))
            {
                if (SimpleInput.GetMouseButtonDown(SimpleInput.MouseButton.Left)) gunSlot.TriggerDown();
                if (SimpleInput.GetMouseButtonUp(SimpleInput.MouseButton.Left)) gunSlot.TriggerUp();
            }
            // 조준은 우클릭을 누르고 있고 발이 땅에 닿아 있을 때만. 공중에 뜨면 풀리고, 누른 채 착지하면 다시 조준
            bool wantAim = IsAllowed(PlayerAbility.Aim) && SimpleInput.GetMouseButton(SimpleInput.MouseButton.Right) && IsOnGround;
            if (wantAim && gunSlot.IsAiming == false) gunSlot.AimDown();
            else if (wantAim == false && gunSlot.IsAiming) gunSlot.AimUp();

            if (IsAllowed(PlayerAbility.Reload) && SimpleInput.GetKeyDown(keySettings.reloadKey)) gunSlot.Reload();
            if (IsAllowed(PlayerAbility.FireMode) && SimpleInput.GetKeyDown(keySettings.fireModeKey)) gunSlot.ToggleFireMode();

            if (IsAllowed(PlayerAbility.SwitchWeapon) == false) return;
            for (int i = 0; i < keySettings.slotKeys.Length; i++)
                if (SimpleInput.GetKeyDown(keySettings.slotKeys[i])) gunSlot.SelectByKey(i);
            // if (SimpleInput.GetKeyDown(keySettings.debugSwapPrimaryKey)) gunSlot.DebugCycleSupplyWeapon();

            float scroll = SimpleInput.GetMouseScrollDeltaRaw();
            if (scroll > 0f) gunSlot.SelectNext(-1);
            else if (scroll < 0f) gunSlot.SelectNext(1);
        }

        /// <summary>WASD 입력(x=좌우, y=앞뒤)을 시선 yaw 로 돌려 월드 수평 방향으로 바꾼다.
        /// 1인칭·3인칭 모두 "카메라 정면 = 플레이어 정면 = W"</summary>
        private Vector3 InputToWorld(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;
            return Quaternion.Euler(0f, lookYaw, 0f) * new Vector3(input.x, 0f, input.y);
        }

        /// <summary>마우스 이동량을 yaw / pitch 에 누적. 커서가 풀려 있으면 회전하지 않는다</summary>
        private void UpdateLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            // delta 는 이미 "이번 프레임 이동량" 이라 deltaTime 을 곱하지 않는다
            Vector2 delta = mouse.delta.ReadValue() * (mouseSensitivity * aimSensitivity);
            lookYaw = Mathf.Repeat(lookYaw + delta.x, 360f);
            float before = lookPitch;
            lookPitch = Mathf.Clamp(lookPitch + (invertY ? delta.y : -delta.y), minPitch, MaxPitch);
            lookInputPitchDelta = lookPitch - before;
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = locked == false;
        }

        private void SetFlowInputBlocked(bool blocked)
        {
            flowInputBlocked = blocked;
            ApplyInputBlock();
        }

        /// <summary>막히면 이동·총 동작을 멈추고 커서를 푼다. 둘 다 풀려야 커서를 다시 잠근다</summary>
        private void ApplyInputBlock()
        {
            bool blocked = IsGameplayInputBlocked;
            if (blocked)
            {
                SetMoveInput(Vector3.zero, false);
                if (gunSlot != null)
                {
                    if (gunSlot.CurrentGun != null) gunSlot.CurrentGun.CancelActions();
                }
            }
            SetCursorLocked(blocked == false);
        }
    }
}
