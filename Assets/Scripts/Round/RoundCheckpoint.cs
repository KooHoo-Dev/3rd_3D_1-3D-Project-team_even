using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Services;
using HybridShooter.Player;

namespace HybridShooter.Round
{
    /// <summary>현재 씬의 라운드 시작 상태. 복원 중에도 원본 기록을 바꾸지 않아 여러 번 재시도할 수 있다.</summary>
    internal sealed class RoundCheckpoint
    {
        public int Round { get; }

        private readonly int balance;
        private readonly Dictionary<UpgradeType, UpgradeGrade[]> purchases = new Dictionary<UpgradeType, UpgradeGrade[]>();
        private readonly int[] unlockedWeapons;
        private readonly PlayerController player;
        private readonly PlayerController.CheckpointState playerState;

        public RoundCheckpoint(int round)
        {
            Round = round;
            if (ServiceLocator.TryGet<ICurrencyWallet>(out var wallet)) balance = wallet.Balance;
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades))
            {
                foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
                {
                    int count = upgrades.GetLevel(type);
                    if (count == 0) continue;
                    var grades = new UpgradeGrade[count];
                    for (int level = 1; level <= count; level++) grades[level - 1] = upgrades.GetGrade(type, level).GetValueOrDefault();
                    purchases.Add(type, grades);
                }
            }
            if (ServiceLocator.TryGet<IWeaponUnlocks>(out var unlocks))
            {
                var ids = new List<int>();
                foreach (var weapon in DataManager.Weapons)
                {
                    if (unlocks.IsUnlocked(weapon.Id)) ids.Add(weapon.Id);
                }
                unlockedWeapons = ids.ToArray();
            }
            if (ServiceLocator.TryGet<IPlayerTarget>(out var target) && target.Root != null) player = target.Root.GetComponentInParent<PlayerController>();
            if (player != null) playerState = player.CaptureCheckpoint();
        }

        public void Restore()
        {
            if (ServiceLocator.TryGet<ICurrencyWallet>(out var wallet)) wallet.ResetTo(balance);
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades)) upgrades.RestorePurchases(purchases);
            if (unlockedWeapons != null && ServiceLocator.TryGet<IWeaponUnlocks>(out var unlocks)) unlocks.RestoreUnlocked(unlockedWeapons);
            if (player != null) player.RestoreCheckpoint(playerState);
        }
    }
}
