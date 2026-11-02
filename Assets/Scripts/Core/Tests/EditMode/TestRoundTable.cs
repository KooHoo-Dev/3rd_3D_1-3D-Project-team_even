using System.Linq;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using NUnit.Framework;

namespace HybridShooter.Core.Tests
{
    /// <summary>
    /// 실제 RoundTable.tsv 를 읽어 라운드 유형 배치·최종 보스 규칙·섬멸 목표 범위를 확인한다.
    /// </summary>
    public sealed class TestRoundTable
    {
        [OneTimeSetUp]
        public void LoadTables()
        {
            DataManager.Load(DataManager.DefaultTableFolder);
        }

        [Test]
        public void Load_HasNoRoundTableErrors()
        {
            var roundErrors = DataManager.LastReport.Errors.Where(error => error.Contains(RoundDef.Table)).ToList();
            Assert.IsEmpty(roundErrors, string.Join("\n", roundErrors));
        }

        [Test]
        public void Rounds_FollowAgreedOrder()
        {
            var types = DataManager.Rounds.All.Select(round => round.Type).ToArray();
            CollectionAssert.AreEqual(new[] { RoundType.CrowdToBoss, RoundType.Annihilation, RoundType.CrowdToBoss, RoundType.Survival, RoundType.CrowdToBoss }, types);
            Assert.AreEqual(1, DataManager.GetRound(1).WaveRound, "R1 현행 유지");
            Assert.AreEqual(2, DataManager.GetRound(3).WaveRound, "R3 = 기존 R2 웨이브");
            Assert.AreEqual(150f, DataManager.GetRound(2).PhaseTime);
            Assert.AreEqual(180f, DataManager.GetRound(4).PhaseTime);
        }

        [Test]
        public void TyranopodeOnlyInLastRound()
        {
            var rounds = DataManager.Rounds.All;
            int finalBossId = DataManager.Tyranopode.BossId;
            Assert.AreEqual(finalBossId, rounds[rounds.Count - 1].BossId);
            for (int i = 0; i < rounds.Count - 1; i++)
                Assert.AreNotEqual(finalBossId, rounds[i].BossId, $"{rounds[i].Round} 라운드");
        }

        [Test]
        public void AnnihilationKillTarget_Is90To95PercentOfRequiredSpawns()
        {
            var annihilation = DataManager.GetRound(2);
            int requiredSpawns = CountSpawnsBefore(annihilation.WaveRound, annihilation.PhaseTime);
            float ratio = (float)annihilation.KillTarget / requiredSpawns;
            Assert.That(ratio, Is.InRange(0.9f, 0.95f), $"KillTarget {annihilation.KillTarget} / 필수 소환 {requiredSpawns}");
            Assert.That(requiredSpawns - annihilation.KillTarget, Is.GreaterThanOrEqualTo(10));
        }

        // 자폭형 포함 전체 필수 소환 수 (DataManager 검증은 자폭형을 뺀 수로 따로 본다)
        private static int CountSpawnsBefore(int waveRound, float seconds)
        {
            int count = 0;
            foreach (var stage in DataManager.WaveStages.Where(row => row.RoundNo == waveRound && row.Kind == WaveStageKind.Normal))
            foreach (var wave in stage.Waves)
            foreach (var spawner in wave.Spawners)
            for (int order = 0; order < spawner.Count; order++)
                if (stage.StartTime + wave.StartTime + spawner.StartDelay + spawner.SpawnInterval * order < seconds) count++;
            return count;
        }
    }
}
