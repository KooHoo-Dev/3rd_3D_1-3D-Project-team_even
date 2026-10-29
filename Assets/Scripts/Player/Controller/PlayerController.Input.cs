using Study.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player
{
    /// <summary>키보드·마우스 입력 → 이동·무기·아이템 요청</summary>
    public partial class PlayerController
    {
        private bool flowInputBlocked;
        public bool IsGameplayInputBlocked => flowInputBlocked;

        private void InputHandle()
        {
            if (flowInputBlocked) return;
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
            if (SimpleInput.GetKey(keySettings.frontKey)) input.y += 1f;
            if (SimpleInput.GetKey(keySettings.backKey)) input.y -= 1f;
            if (SimpleInput.GetKey(keySettings.rightKey)) input.x += 1f;
            if (SimpleInput.GetKey(keySettings.leftKey)) input.x -= 1f;
            input = Vector2.ClampMagnitude(input, 1f); // 대각선이 더 빨라지지 않게

            // 로컬 입력 → 월드 방향
            Vector3 worldMove = InputToWorld(input);

            // 이동 + 달리기 (누르고 있는 동안). 조준 중에는 달리지 않는다
            bool sprint = SimpleInput.GetKey(keySettings.sprintKey) && (gunSlot == null || gunSlot.IsAiming == false);
            SetMoveInput(worldMove, sprint);

            // 점프
            if (SimpleInput.GetKeyDown(keySettings.jumpKey)) RequestJump();

            // 구르기: 이동 입력이 있으면 그 방향, 없으면 몸 정면
            if (SimpleInput.GetKeyDown(keySettings.RollKey))
            {
                Vector3 rollDir = worldMove.sqrMagnitude > 0.0001f ? worldMove : Forward;
                TryRoll(rollDir);
            }

            WeaponInputHandle();
            ItemInputHandle();
        }

        /// <summary>소모품 입력 → PlayerItems. G 수류탄, V 회복 팩. 커서가 풀려 있으면 무시</summary>
        private void ItemInputHandle()
        {
            if (items == null || Cursor.lockState != CursorLockMode.Locked) return;
            if (SimpleInput.GetKeyDown(keySettings.grenadeKey)) items.TryThrowGrenade();
            if (SimpleInput.GetKeyDown(keySettings.healKey)) items.TryUseHeal();
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

            if (SimpleInput.GetMouseButtonDown(SimpleInput.MouseButton.Left)) gunSlot.TriggerDown();
            if (SimpleInput.GetMouseButtonUp(SimpleInput.MouseButton.Left)) gunSlot.TriggerUp();
            // 조준은 우클릭을 누르고 있고 발이 땅에 닿아 있을 때만. 공중에 뜨면 풀리고, 누른 채 착지하면 다시 조준
            bool wantAim = SimpleInput.GetMouseButton(SimpleInput.MouseButton.Right) && IsOnGround;
            if (wantAim && gunSlot.IsAiming == false) gunSlot.AimDown();
            else if (wantAim == false && gunSlot.IsAiming) gunSlot.AimUp();

            if (SimpleInput.GetKeyDown(keySettings.reloadKey)) gunSlot.Reload();
            if (SimpleInput.GetKeyDown(keySettings.fireModeKey)) gunSlot.ToggleFireMode();

            for (int i = 0; i < keySettings.slotKeys.Length; i++)
                if (SimpleInput.GetKeyDown(keySettings.slotKeys[i])) gunSlot.Select(i);
            if (SimpleInput.GetKeyDown(keySettings.debugSwapPrimaryKey)) gunSlot.ReplacePrimaryWithNextReserve();

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
