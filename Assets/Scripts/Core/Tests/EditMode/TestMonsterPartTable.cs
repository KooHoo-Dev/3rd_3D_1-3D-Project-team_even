using System.Linq;
using HybridShooter.Core.Data;
using NUnit.Framework;

namespace HybridShooter.Core.Tests
{
    /// <summary>
    /// 실제 MonsterPartTable.tsv 를 읽어 일반 몬스터 부위(MonsterId)와 보스 부위(BossId)가 각자 주인에 붙는지 확인한다.
    /// </summary>
    public sealed class TestMonsterPartTable
    {
        [OneTimeSetUp]
        public void LoadTables()
        {
            DataManager.Load(DataManager.DefaultTableFolder);
        }

        [Test]
        public void Load_HasNoMonsterPartTableErrors()
        {
            var partErrors = DataManager.LastReport.Errors.Where(error => error.Contains("MonsterPartTable")).ToList();
            Assert.IsEmpty(partErrors, string.Join("\n", partErrors));
        }

        [Test]
        public void ClypeosaurusParts_LinkToBossTable()
        {
            var clypeosaurus = DataManager.Bosses.Get(1);
            Assert.AreEqual(1.5f, clypeosaurus.FindPart("Head").DamageMultiplier);
            Assert.AreEqual(2.5f, clypeosaurus.FindPart("Antenna").DamageMultiplier);
            Assert.IsNull(clypeosaurus.FindPart("Mouth"));
        }

        [Test]
        public void BossParts_AreNotAttachedToMonsters()
        {
            bool anyMonsterHasBossPart = DataManager.Monsters.Any(monster => monster.Parts.Any(part => part.BossId != 0));
            Assert.IsFalse(anyMonsterHasBossPart);
        }

        [Test]
        public void MonsterParts_KeepMultiplierAndExposure()
        {
            var letalobrachMouth = DataManager.Monsters.Get(2001).FindPart("Mouth");
            Assert.AreEqual(2f, letalobrachMouth.DamageMultiplier);
            Assert.AreEqual(PartExposure.WhileAttacking, letalobrachMouth.Exposure);
            Assert.AreEqual(1.5f, DataManager.Monsters.Get(2002).FindPart("Spine").DamageMultiplier);
            Assert.AreEqual(2f, DataManager.Monsters.Get(2003).FindPart("Gland").DamageMultiplier);
        }
    }
}
