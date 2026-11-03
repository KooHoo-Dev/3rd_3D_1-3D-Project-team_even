using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using NUnit.Framework;

namespace HybridShooter.Core.Tests
{
    /// <summary>WeaponUnlockLedger: 실제 WeaponTable 로 해금 순서·라운드별 해금·저장하지 않음(실행 단위)을 검사한다</summary>
    public sealed class TestWeaponUnlockLedger
    {
        private WeaponUnlockLedger ledger;
        private readonly List<WeaponDef> published = new List<WeaponDef>();

        [SetUp]
        public void SetUp()
        {
            ledger = new WeaponUnlockLedger();
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
        public void PistolRiflesAndSmg_AreDefault_OthersLoadoutLocked()
        {
            var defaults = DataManager.Weapons.Where(w => w.Category != WeaponCategory.Supply && w.UnlockOrder == 0).Select(w => w.Id).ToArray();
            CollectionAssert.AreEquivalent(new[] { 2001, 2002, 2003, 2004 }, defaults, "기본 무기 = 라이플(2001) · 돌격소총(2002) · 권총(2003) · 기관단총(2004)");
        }

        [Test]
        public void UnlockForRound_Round1Shotgun_Round2Sniper()
        {
            CollectionAssert.AreEqual(new[] { 2005 }, ledger.UnlockForRound(1).Select(w => w.Id), "1라운드 클리어 = 산탄총");
            Assert.IsFalse(ledger.IsUnlocked(2006));
            CollectionAssert.IsEmpty(ledger.UnlockForRound(1), "같은 라운드를 다시 깨도 더 없음");
            CollectionAssert.AreEqual(new[] { 2006 }, ledger.UnlockForRound(2).Select(w => w.Id), "2라운드 클리어 = 저격소총");
            CollectionAssert.AreEqual(new[] { 2005, 2006 }, published.Select(w => w.Id), "해금마다 WeaponUnlockedEvent 1번");
        }

        [Test]
        public void UnlockForRound_LaterRound_FillsMissedEarlierUnlocks()
        {
            CollectionAssert.AreEqual(new[] { 2005, 2006 }, ledger.UnlockForRound(3).Select(w => w.Id));
        }

        [Test]
        public void StageClearedEvent_UnlocksByRoundNumber()
        {
            ServiceLocator.TryGet<IWeaponUnlocks>(out var previous);
            ServiceLocator.Register<IWeaponUnlocks>(ledger);
            ledger.BindEvents();
            try
            {
                EventBus<StageClearedEvent>.Publish(new StageClearedEvent(2));
                Assert.IsTrue(ledger.IsUnlocked(2005));
                Assert.IsTrue(ledger.IsUnlocked(2006));
            }
            finally
            {
                ledger.UnbindEvents();
                ServiceLocator.Unregister<IWeaponUnlocks>(ledger);
                if (previous != null) ServiceLocator.Register(previous);
            }
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
        public void Unlocks_AreNotSaved_NewLedgerStartsWithDefaultsOnly()
        {
            var first = ledger.UnlockNext();
            Assume.That(first, Is.Not.Null);
            Assert.IsTrue(ledger.IsUnlocked(first.Id));

            Assert.IsFalse(new WeaponUnlockLedger().IsUnlocked(first.Id), "앱 재실행 = 새 Ledger → 해금이 남지 않는다");
        }

        private void OnUnlocked(WeaponUnlockedEvent e) => published.Add(e.Weapon);
    }
}
