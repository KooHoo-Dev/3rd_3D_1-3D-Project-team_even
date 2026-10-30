using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using NUnit.Framework;

namespace HybridShooter.Core.Tests
{
    /// <summary>WeaponUnlockLedger: 실제 WeaponTable 로 해금 순서·저장을 검사한다 (테스트 전용 PlayerPrefs 키)</summary>
    public sealed class TestWeaponUnlockLedger
    {
        private const string Key = "Test.Unlock.Weapons";
        private WeaponUnlockLedger ledger;
        private readonly List<WeaponDef> published = new List<WeaponDef>();

        [SetUp]
        public void SetUp()
        {
            ledger = new WeaponUnlockLedger(Key);
            ledger.ResetAll();
            EventBus<WeaponUnlockedEvent>.Subscribe(OnUnlocked);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<WeaponUnlockedEvent>.Unsubscribe(OnUnlocked);
            ledger.ResetAll();
            published.Clear();
        }

        [Test]
        public void DefaultWeapons_AreAlwaysUnlocked()
        {
            foreach (var def in DataManager.Weapons.Where(WeaponUnlockLedger.IsAlwaysUnlocked))
                Assert.IsTrue(ledger.IsUnlocked(def.Id), def.Name);
            foreach (var def in DataManager.Weapons.Where(w => WeaponUnlockLedger.IsAlwaysUnlocked(w) == false))
                Assert.IsFalse(ledger.IsUnlocked(def.Id), def.Name);
        }

        [Test]
        public void PistolAndAssaultRifle_AreDefault_OthersLoadoutLocked()
        {
            var defaults = DataManager.Weapons.Where(w => w.Category != WeaponCategory.Supply && w.UnlockOrder == 0).Select(w => w.Id).ToArray();
            CollectionAssert.AreEquivalent(new[] { 2002, 2003 }, defaults, "기본 무기 = 돌격소총(2002) · 권총(2003)");
        }

        [Test]
        public void UnlockNext_FollowsUnlockOrder_ThenStops()
        {
            var expected = DataManager.Weapons
                .Where(w => WeaponUnlockLedger.IsAlwaysUnlocked(w) == false)
                .OrderBy(w => w.UnlockOrder).ThenBy(w => w.Id)
                .ToList();

            foreach (var def in expected)
            {
                Assert.AreSame(def, ledger.UnlockNext());
                Assert.IsTrue(ledger.IsUnlocked(def.Id));
            }
            Assert.IsNull(ledger.UnlockNext(), "모두 해금되면 더 없음");
            CollectionAssert.AreEqual(expected, published, "해금마다 WeaponUnlockedEvent 1번");
        }

        [Test]
        public void Unlocks_PersistAcrossInstances()
        {
            var first = ledger.UnlockNext();
            Assume.That(first, Is.Not.Null);

            var reloaded = new WeaponUnlockLedger(Key);
            Assert.IsTrue(reloaded.IsUnlocked(first.Id));

            reloaded.ResetAll();
            Assert.IsFalse(new WeaponUnlockLedger(Key).IsUnlocked(first.Id));
        }

        private void OnUnlocked(WeaponUnlockedEvent e) => published.Add(e.Weapon);
    }
}
