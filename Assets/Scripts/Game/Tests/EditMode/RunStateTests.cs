using Game.Model;
using NUnit.Framework;

namespace Game.Tests
{
    public class RunStateTests
    {
        [Test]
        public void StartsAtLevelOne()
        {
            RunState run = TestContent.NewRun();

            Assert.AreEqual(1, run.Level);
            Assert.AreEqual(0, run.Xp);
            Assert.AreEqual(0, run.PendingLevelUps);
            Assert.AreEqual(10, run.MaxLevel);
            Assert.AreEqual(5, run.XpToNext);
        }

        [Test]
        public void ExperienceCarriesAcrossLevels()
        {
            RunState run = TestContent.NewRun();

            run.AddExperience(5);
            Assert.AreEqual(2, run.Level);
            Assert.AreEqual(0, run.Xp);
            Assert.AreEqual(1, run.PendingLevelUps);

            // 2→3 需要 6，3→4 需要 7，4→5 需要 8；20 点只够升到 4 级，剩 7 点
            run.AddExperience(20);
            Assert.AreEqual(4, run.Level);
            Assert.AreEqual(7, run.Xp);
            Assert.AreEqual(3, run.PendingLevelUps, "一次拾取跨两级，应该排队 3 次（包含第一次）");
        }

        [Test]
        public void ConsumePendingUpgradeOneAtATime()
        {
            RunState run = TestContent.NewRun();
            run.AddExperience(5);
            run.AddExperience(6);

            Assert.AreEqual(2, run.PendingLevelUps);
            Assert.IsTrue(run.ConsumePendingLevelUp());
            Assert.AreEqual(1, run.PendingLevelUps);
            Assert.IsTrue(run.ConsumePendingLevelUp());
            Assert.AreEqual(0, run.PendingLevelUps);
            Assert.IsFalse(run.ConsumePendingLevelUp());
        }

        [Test]
        public void MaxLevelStopsAccumulating()
        {
            RunState run = TestContent.NewRun();
            run.AddExperience(100000);

            Assert.AreEqual(run.MaxLevel, run.Level);
            Assert.AreEqual(0, run.Xp);
            Assert.AreEqual(0f, run.XpRatio);
            Assert.IsTrue(run.IsMaxLevel);
        }

        [Test]
        public void KillStatsCountEliteAndBoss()
        {
            RunState run = TestContent.NewRun();

            run.AddKill(false, false);
            run.AddKill(true, false);
            run.AddKill(false, true);

            Assert.AreEqual(3, run.Kills);
            Assert.AreEqual(1, run.EliteKills);
            Assert.AreEqual(1, run.BossKills);
        }

        [Test]
        public void HpRatioAndDeath()
        {
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);

            player.HP = 25f;
            Assert.AreEqual(0.25f, player.HpRatio, 0.001f);
            Assert.IsFalse(player.IsDead);

            player.HP = 0f;
            Assert.IsTrue(player.IsDead);
        }
    }
}
