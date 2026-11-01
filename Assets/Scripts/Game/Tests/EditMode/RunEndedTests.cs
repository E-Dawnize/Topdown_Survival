using System.Collections.Generic;
using Game.Core.Events;
using Game.Model;
using Game.Views;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 收尾：死亡与击杀 BOSS 都只发一次 RunEnded，摘要数字取收尾那一刻的 Model。
    /// </summary>
    public class RunEndedTests
    {
        [Test]
        public void PlayerDeathEndsTheRunOnceWithADefeatSummary()
        {
            TestRun run = TestRun.Create(hp: 100f);

            var endings = new List<RunSummary>();
            run.Bus.Subscribe<RunEnded>(e => endings.Add(e.Summary));

            // 先记两个击杀，摘要里才有东西可对
            run.Bus.Publish(new UnitDied(new UnitId(2, UnitKind.Enemy), UnitId.None, 0f, 0f));
            run.Bus.Publish(new UnitDied(new UnitId(3, UnitKind.Enemy), UnitId.None, 0f, 0f));
            run.Bus.Flush();

            run.AdvanceSeconds(2f);
            run.Player.HP = 0f;
            run.Controller.Tick(TestRun.Dt);
            run.Bus.Flush();

            Assert.AreEqual(Game.Core.RunPhase.GameOver, run.Controller.Phase);
            Assert.AreEqual(1, endings.Count);
            Assert.AreEqual(2, endings[0].Kills);
            Assert.AreEqual(1, endings[0].Level);
            Assert.AreEqual(1, endings[0].Weapons);
            Assert.AreEqual(0, endings[0].Companions);
            Assert.AreEqual(TestRun.Seed, endings[0].Seed);
            Assert.Greater(endings[0].Duration, 2f);
            Assert.Less(endings[0].Duration, 2.5f);

            // 结束以后不再发流程事件
            int extra = 0;
            run.Bus.Subscribe<RunEnded>(_ => extra++);
            run.AdvanceSeconds(5f);
            Assert.AreEqual(0, extra);
        }

        [Test]
        public void BossKillEndsTheRunOnceWithAVictorySummary()
        {
            TestRun run = TestRun.Create();
            var endings = new List<RunEnded>();
            run.Bus.Subscribe<RunEnded>(endings.Add);

            run.AdvanceSeconds(10.2f);
            Assert.AreEqual(Game.Core.RunPhase.Boss, run.Controller.Phase);

            Assert.IsTrue(run.Enemies.TryGetSlot(run.Director.BossUnitId, out int slot));
            run.Enemies.Get(slot).HP = 0f;
            run.Step(2);
            run.Bus.Flush();

            Assert.AreEqual(1, endings.Count);
            Assert.IsTrue(endings[0].Victory, "胜负只认 RunEnded.Victory，不再看 Summary 里重复的 BossDefeated");
            Assert.GreaterOrEqual(endings[0].Summary.Kills, 1);
            Assert.GreaterOrEqual(endings[0].Summary.Duration, 10f);
        }

        [Test]
        public void ResultLinesTakeTheirNumbersFromTheSummary()
        {
            var win = Summary(level: 12, kills: 128, eliteKills: 9, duration: 754.6f, seed: 1ul,
                weapons: 4, companions: 3);
            ResultLines lines = ResultLines.From(win, victory: true);

            Assert.AreEqual("通关", lines.Title);
            Assert.AreEqual("存活时间  12:34", lines.Duration, "754.6 秒 = 12 分 34 秒");
            Assert.AreEqual("等级  12", lines.Level);
            Assert.AreEqual("击杀  128（精英 9）", lines.Kills);
            Assert.AreEqual("武器 4　旅伴 3", lines.Build);

            var loss = Summary(level: 7, kills: 41, eliteKills: 2, duration: 65f, seed: 2ul,
                weapons: 3, companions: 1);
            ResultLines lost = ResultLines.From(loss, victory: false);
            Assert.AreEqual("阵亡", lost.Title);
            Assert.AreEqual("存活时间  1:05", lost.Duration);
            Assert.AreEqual("等级  7", lost.Level);
        }

        static RunSummary Summary(int level, int kills, int eliteKills, float duration, ulong seed, int weapons,
            int companions)
        {
            return new RunSummary(level, kills, eliteKills, duration, seed, weapons, companions);
        }

        [Test]
        public void ClockFormatsMinutesAndNeverGoesNegative()
        {
            Assert.AreEqual("0:00", ResultLines.Clock(0f));
            Assert.AreEqual("0:05", ResultLines.Clock(5.9f));
            Assert.AreEqual("3:05", ResultLines.Clock(185f));
            Assert.AreEqual("59:59", ResultLines.Clock(3599.99f));
            Assert.AreEqual("0:00", ResultLines.Clock(-3f));
        }
    }
}
