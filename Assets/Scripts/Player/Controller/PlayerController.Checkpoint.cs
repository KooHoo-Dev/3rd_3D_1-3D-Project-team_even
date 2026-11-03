using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    public partial class PlayerController
    {
        public readonly struct CheckpointState
        {
            internal readonly Vector3 Position;
            internal readonly float Yaw;
            internal readonly float Pitch;
            internal readonly int Lives;
            internal readonly int BaseMaxLives;
            internal readonly int BonusMaxLives;
            internal readonly int Grenades;
            internal readonly int HealPacks;
            internal readonly GunSlot.CheckpointState Weapons;

            internal CheckpointState(Vector3 position, float yaw, float pitch, int lives, int baseMaxLives, int bonusMaxLives, int grenades, int healPacks, GunSlot.CheckpointState weapons)
            {
                Position = position;
                Yaw = yaw;
                Pitch = pitch;
                Lives = lives;
                BaseMaxLives = baseMaxLives;
                BonusMaxLives = bonusMaxLives;
                Grenades = grenades;
                HealPacks = healPacks;
                Weapons = weapons;
            }
        }

        /// <summary>체력·스태미나를 제외한 라운드 시작 상태를 값으로 보관한다.</summary>
        public CheckpointState CaptureCheckpoint()
        {
            return new CheckpointState(rb.position, lookYaw, lookPitch, lives, baseMaxLives, bonusMaxLives, items != null ? items.Grenades : 0, items != null ? items.HealPacks : 0, gunSlot != null ? gunSlot.CaptureCheckpoint() : null);
        }

        /// <summary>강화 복원 후 호출한다. 사망·임시 동작을 풀고 체력·스태미나는 새 최대치로 채운다.</summary>
        public void RestoreCheckpoint(CheckpointState state)
        {
            EndExternalControl();
            SetCinematic(false);
            ClearVelocitySources();
            StopAllShakes();
            tremorSpread = 0f;
            Teleport(state.Position);
            transform.position = state.Position;
            SetRotationImmediate(state.Yaw);
            lookPitch = state.Pitch;
            jumpRequestedTime = float.NegativeInfinity;
            rollReadyTime = Time.time;
            lastStaminaUseTime = float.NegativeInfinity;
            baseMaxLives = state.BaseMaxLives;
            bonusMaxLives = state.BonusMaxLives;
            lives = Mathf.Clamp(state.Lives, 1, MaxLives);
            RefreshUpgradeStats(false);
            if (items != null) items.RestoreCheckpoint(state.Grenades, state.HealPacks);
            if (gunSlot != null && state.Weapons != null) gunSlot.RestoreCheckpoint(state.Weapons);
            ResetLife(false);
            SyncRigRotationNow();
            SetPlayerTrackingInfo();
        }
    }
}
