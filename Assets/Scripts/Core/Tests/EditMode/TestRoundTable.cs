using System.Linq;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using NUnit.Framework;

namespace HybridShooter.Core.Tests
{
    /// <summary>
    /// 실제 RoundTable.tsv 를 읽어 3라운드 배치·편성 후보·최종 보스 규칙·섬멸 목표 계산을 확인한다.
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
            CollectionAssert.AreEqual(new[] { RoundType.CrowdToBoss, RoundType.Annihilation, RoundType.AnnihilationToBoss }, types);
            CollectionAssert.AreEqual(new[] { 1 }, DataManager.GetRound(1).WaveRounds, "R1 현행 유지");
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5 }, DataManager.GetRound(2).WaveRounds, "R2 = 2~5 편성 무작위");
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5 }, DataManager.GetRound(3).WaveRounds, "R3 = 2~5 편성 무작위 (R2 와 겹치지 않게는 RoundSequencer)");
            Assert.AreEqual(150f, DataManager.GetRound(2).PhaseTime);
            Assert.IsTrue(DataManager.GetRound(3).HasBoss, "R3 섬멸 후 보스");
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
        public void AnnihilationKillTarget_Is90PercentOfKillableSpawns_ForEveryCandidate()
        {
            foreach (var round in DataManager.Rounds.All.Where(row => row.IsAnnihilation))
            foreach (int waveRound in round.WaveRounds)
            {
                int killableSpawns = CountSpawnsBefore(waveRound, round.PhaseTime, includeSelfDestruct: false);
                int killTarget = DataManager.GetKillTarget(round, waveRound);
                Assert.AreEqual((int)(killableSpawns * 0.9f), killTarget, $"{round.Round} 라운드 편성 {waveRound}");
                Assert.That(killTarget, Is.LessThanOrEqualTo(CountSpawnsBefore(waveRound, round.PhaseTime, includeSelfDestruct: true)));
                Assert.That(killableSpawns - killTarget, Is.GreaterThanOrEqualTo(5));
            }
        }

        [Test]
        public void RoundBgmKeys_LinkToBgmSounds()
        {
            foreach (var round in DataManager.Rounds.All)
            {
                if (round.CrowdBgmKey != null)
                    Assert.IsNotNull(round.CrowdBgm, $"{round.Round} 라운드 CrowdBgm '{round.CrowdBgmKey}' 가 SoundTable 의 Bgm 키여야 함");
                if (round.BossBgmKey != null)
                    Assert.IsNotNull(round.BossBgm, $"{round.Round} 라운드 BossBgm '{round.BossBgmKey}' 가 SoundTable 의 Bgm 키여야 함");
                if (round.HasBoss == false)
                    Assert.IsNull(round.BossBgmKey, $"{round.Round} 라운드는 보스가 없어 BossBgm 을 비운다");
            }
        }

        // 필수 소환 수 (ClearBonus 제외). includeSelfDestruct = false 면 DataManager 의 처치 가능 수와 같은 기준
        private static int CountSpawnsBefore(int waveRound, float seconds, bool includeSelfDestruct)
        {
            int count = 0;
            foreach (var stage in DataManager.WaveStages.Where(row => row.RoundNo == waveRound && row.Kind == WaveStageKind.Normal))
            foreach (var wave in stage.Waves)
            foreach (var spawner in wave.Spawners)
            {
                bool isSelfDestruct = spawner.Monster != null && spawner.Monster.Role == MonsterRole.SelfDestruct;
                if (isSelfDestruct && includeSelfDestruct == false) continue;
                for (int order = 0; order < spawner.Count; order++)
                    if (stage.StartTime + wave.StartTime + spawner.StartDelay + spawner.SpawnInterval * order < seconds) count++;
            }
            return count;
        }
    }
}
