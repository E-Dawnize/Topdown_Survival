using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 收尾（I2-10）：死亡与通关都只发一次 RunEnded，摘要里的数字就是收尾那一刻 Model 的实际值。
    ///
    /// 只搭 RunController + 事件总线，不跑 RunSimulation：这里验的是"结束这一次发什么、带什么数字"，
    /// 每步跑哪些系统是 RunSimulationTests / WaveFlowTests 的事。摘要对不对靠"发出来的那份"与
    /// RunState / RunController 对照，而不是回头再算一遍——重算一遍就等于用同一个 bug 验自己。
    /// </summary>
    public class RunEndedTests
    {
        const float Dt = 1f / 60f;
        const ulong Seed = 0x10E2D00D5EED1234ul;

        [Test]
        public void PlayerDeathEndsTheRunOnceWithADefeatSummary()
        {
            RunController run = Build(out EventBus bus, out PlayerModel player, out RunState state,
                out Log log, duration: 180f);

            run.StartRun();
            bus.Flush();
            log.Clear();

            // 先打两个怪，摘要里的击杀数才有东西可对
            bus.Publish(new UnitDied(new UnitId(2), UnitId.None, 0f, 0f));
            bus.Publish(new UnitDied(new UnitId(3), UnitId.None, 0f, 0f));
            bus.Flush();

            Advance(run, bus, 2f);

            player.HP = 0f;   // 战斗中被打死
            Advance(run, bus, 0.1f);

            Assert.AreEqual(RunPhase.GameOver, run.Phase, "死了就该直接进结算");
            Assert.AreEqual(1, log.EndedCount, "RunEnded 只能发一次：" + log.Joined);
            Assert.IsFalse(log.LastVictory, "中途死亡不能算通关");

            RunSummary summary = log.LastSummary;
            Assert.AreEqual(2, summary.Kills, "摘要里的击杀数要等于 RunController 数到的击杀");
            Assert.AreEqual(run.Kills, summary.Kills);
            Assert.AreEqual(state.Coins, summary.Coins, "摘要里的金币要等于当时 RunState 的金币");
            Assert.AreEqual(0, summary.WavesCleared, "第一波还没打满，不能记成清了一波");
            Assert.AreEqual(Seed, summary.Seed, "种子要进摘要：复现一局靠它");
            Assert.Greater(summary.Duration, 2f, "存活时间要算到死亡那一刻，不能是 0");
            Assert.Less(summary.Duration, 2.5f, "存活时间不该多算进去死了以后的时间");

            // 结束之后每步都不该再发事件：不重复结算、不再开波、不再进商店
            log.Clear();
            Advance(run, bus, 5f);

            Assert.AreEqual(0, log.Order.Count, "结束态里不该再发任何流程事件：" + log.Joined);
            Assert.AreEqual(0, log.EndedCount, "RunEnded 不能重复");
        }

        [Test]
        public void LastWaveEndsTheRunWithAVictorySummary()
        {
            RunController run = Build(out EventBus bus, out _, out RunState state, out Log log, duration: 0.5f);

            run.StartRun();
            bus.Flush();

            // 三波一路打过去，每间商店都从外面关掉（相当于在商店面板上按 Esc）
            for (int wave = 1; wave <= 3; wave++)
            {
                Advance(run, bus, 0.6f);

                if (run.Phase != RunPhase.Shop) continue;

                run.CloseShop();
                bus.Flush();
            }

            Assert.AreEqual(RunPhase.GameOver, run.Phase);
            Assert.AreEqual(1, log.EndedCount, "通关也只发一次 RunEnded：" + log.Joined);
            Assert.IsTrue(log.LastVictory, "打完最后一波算通关");

            RunSummary summary = log.LastSummary;
            Assert.AreEqual(3, summary.WavesCleared, "三波都打完了");
            Assert.AreEqual(state.Coins, summary.Coins, "摘要里的金币要等于 Model 里的金币");
            Assert.AreEqual(60 + 80 + 110, summary.Coins, "三波奖励都要在摘要里");
            Assert.AreEqual(run.Kills, summary.Kills);
            Assert.AreEqual(3, run.WaveIndex, "波次停在配置里的最后一波");
            Assert.Greater(summary.Duration, 1.5f, "存活时间要覆盖三波");
        }

        /// <summary>
        /// 结算面板上的数字取自 RunEnded 的摘要，不在面板里重算。这条用例把"哪一行显示什么"钉住：
        /// 摘要一改，面板的四行会跟着变，不会出现"面板显示的金币和摘要不一样"。
        /// </summary>
        [Test]
        public void ResultLinesTakeTheirNumbersFromTheSummary()
        {
            var win = new RunSummary(wavesCleared: 3, kills: 128, coins: 250, duration: 754.6f, seed: Seed);
            ResultLines lines = ResultLines.From(win, victory: true, waveIndex: 3, wavesTotal: 3);

            Assert.AreEqual("通关", lines.Title);
            Assert.AreEqual("存活时间  12:34", lines.Duration, "754.6 秒 = 12 分 34 秒");
            Assert.AreEqual("波次  3 / 3", lines.Wave);
            Assert.AreEqual("金币  250", lines.Coins);
            Assert.AreEqual("击杀  128", lines.Kills);

            // 死在半路：那一波没打完，WavesCleared 数不到它，面板显示的是"打到第几波"
            var defeat = new RunSummary(wavesCleared: 1, kills: 7, coins: 12, duration: 65f, seed: Seed);
            ResultLines lost = ResultLines.From(defeat, victory: false, waveIndex: 2, wavesTotal: 3);

            Assert.AreEqual("阵亡", lost.Title);
            Assert.AreEqual("存活时间  1:05", lost.Duration);
            Assert.AreEqual("波次  2 / 3", lost.Wave);
            Assert.AreEqual("金币  12", lost.Coins);
            Assert.AreEqual("击杀  7", lost.Kills);
        }

        [Test]
        public void ClockFormatsMinutesAndNeverGoesNegative()
        {
            Assert.AreEqual("0:00", ResultLines.Clock(0f));
            Assert.AreEqual("0:05", ResultLines.Clock(5.9f), "秒数向下取整：还没到 6 秒就不显示 6");
            Assert.AreEqual("3:05", ResultLines.Clock(185f));
            Assert.AreEqual("59:59", ResultLines.Clock(3599.99f));
            Assert.AreEqual("0:00", ResultLines.Clock(-3f), "负时长按 0 算，不显示 -0:03");
        }

        // ---- 装配 ----

        /// <summary>照抄 Wave.json 的三波：只把时长改短，间隔 / 数量 / 上限 / 奖励都不动。</summary>
        static List<WaveDef> Waves(float duration)
        {
            return new List<WaveDef>
            {
                new WaveDef
                {
                    Id = "W1", Index = 1, Duration = duration, SpawnInterval = 1.2f,
                    SpawnCountMin = 1, SpawnCountMax = 2, EnemyPool = new[] { "Crawler" },
                    ConcurrentCap = 40, CoinReward = 60, BossId = ""
                },
                new WaveDef
                {
                    Id = "W2", Index = 2, Duration = duration, SpawnInterval = 1.0f,
                    SpawnCountMin = 2, SpawnCountMax = 3, EnemyPool = new[] { "Crawler", "Thrower" },
                    ConcurrentCap = 70, CoinReward = 80, BossId = ""
                },
                new WaveDef
                {
                    Id = "W3", Index = 3, Duration = duration, SpawnInterval = 0.9f,
                    SpawnCountMin = 2, SpawnCountMax = 3,
                    EnemyPool = new[] { "Crawler", "Charger", "Thrower" },
                    ConcurrentCap = 100, CoinReward = 110, BossId = ""
                },
            };
        }

        static RunController Build(out EventBus bus, out PlayerModel player, out RunState state, out Log log,
            float duration)
        {
            bus = new EventBus();
            player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            state = new RunState();

            var run = new RunController(player, state, Waves(duration), bus, Seed, "Scavenger");
            log = new Log(bus);
            return run;
        }

        /// <summary>每步末尾分发一次事件，与 RunSimulation.Tick 的节奏一致。</summary>
        static void Advance(RunController run, IEventBus bus, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                run.Tick(Dt);
                bus.Flush();
            }
        }

        sealed class Log
        {
            public readonly List<string> Order = new List<string>();
            public int EndedCount;
            public bool LastVictory;
            public RunSummary LastSummary;

            public Log(IEventBus bus)
            {
                bus.Subscribe<RunStarted>(_ => Order.Add("RunStarted"));
                bus.Subscribe<WaveStarted>(e => Order.Add("WaveStarted(" + e.Index + ")"));
                bus.Subscribe<WaveCleared>(e => Order.Add("WaveCleared(" + e.Index + ")"));
                bus.Subscribe<ShopOpened>(e => Order.Add("ShopOpened(" + e.WaveIndex + ")"));
                bus.Subscribe<ShopClosed>(e => Order.Add("ShopClosed(" + e.CoinsSpent + ")"));
                bus.Subscribe<RunEnded>(e =>
                {
                    EndedCount++;
                    LastVictory = e.Victory;
                    LastSummary = e.Summary;
                    Order.Add(e.Victory ? "RunEnded(win)" : "RunEnded(lose)");
                });
            }

            public string Joined => string.Join(" | ", Order);

            public void Clear()
            {
                Order.Clear();
                EndedCount = 0;
            }
        }
    }
}
