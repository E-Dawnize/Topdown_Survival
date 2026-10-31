using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 局状态机的事件顺序。只测 RunController 一个对象：玩家、波次表、事件总线都是现搭的，
    /// 不经过 RunSimulation，这样"哪个阶段该发哪个事件"错的时候一眼能看出是状态机的问题。
    /// 波次表照抄 Wave.json 的三波（时长在这里改短，只为少跑几万步；结构、奖励、顺序都不动）。
    /// </summary>
    public class RunControllerTests
    {
        const float Dt = 1f / 60f;
        const ulong Seed = 0xA1B2C3D4E5F60718ul;
        const string CharacterId = "Scavenger";

        [Test]
        public void StartRunPublishesRunStartedThenFirstWave()
        {
            RunController run = Build(out EventBus bus, out _, out RunState state, out EventLog log);

            run.StartRun();
            bus.Flush();

            Assert.AreEqual(RunPhase.Battle, run.Phase);
            Assert.AreEqual(1, run.WaveIndex, "波次索引从 1 开始");
            Assert.AreEqual(3, run.WavesTotal, "总波数必须来自波次表：表里 3 条就是 3，不能是写死的 5");
            Assert.AreEqual(3, state.WavesTotal, "RunState 上的总波数也要跟着配置走，HUD 读的是它");
            CollectionAssert.AreEqual(new[] { "RunStarted", "WaveStarted(1)" }, log.Order, log.Joined);
        }

        [Test]
        public void TickInIdleDoesNothing()
        {
            RunController run = Build(out EventBus bus, out _, out _, out EventLog log);

            Advance(run, bus, 1f);

            Assert.AreEqual(RunPhase.Idle, run.Phase, "没调 StartRun 就不该自己开一局");
            Assert.AreEqual(0, log.Order.Count, log.Joined);
        }

        [Test]
        public void WaveClearOpensShopAndWaitsForCloseShop()
        {
            RunController run = Build(out EventBus bus, out _, out RunState state, out EventLog log, duration: 1f);

            run.StartRun();
            bus.Flush();
            log.Order.Clear();

            Advance(run, bus, 1.1f);

            Assert.AreEqual(RunPhase.Shop, run.Phase, "时长用完就该进商店");
            Assert.AreEqual("WaveCleared(1) | CoinChanged(60) | ShopOpened(1)", log.Joined,
                "清波先记奖励再开商店");
            Assert.AreEqual(60, state.Coins, "清波的金币奖励要记进 RunState");

            // 商店里继续跑不该发生任何事：下一波只能由外部显式调 CloseShop
            log.Order.Clear();
            Advance(run, bus, 5f);
            Assert.AreEqual(RunPhase.Shop, run.Phase);
            Assert.AreEqual(0, log.Order.Count, "商店态里 Tick 不该推进任何东西：" + log.Joined);

            Assert.IsTrue(run.CloseShop());
            bus.Flush();

            CollectionAssert.AreEqual(new[] { "ShopClosed(0)", "WaveStarted(2)" }, log.Order, log.Joined);
            Assert.AreEqual(2, run.WaveIndex);
            Assert.AreEqual(RunPhase.Battle, run.Phase);
        }

        [Test]
        public void LastWaveEndsTheRunWithoutOpeningAnotherShop()
        {
            RunController run = Build(out EventBus bus, out _, out RunState state, out EventLog log, duration: 0.5f);

            run.StartRun();
            bus.Flush();

            // 三波一路打过去，每间商店都从外面关掉（相当于没有 UI 时按 F4）
            for (int wave = 1; wave <= 3; wave++)
            {
                Advance(run, bus, 0.6f);

                if (run.Phase != RunPhase.Shop) continue;

                run.CloseShop();
                bus.Flush();
            }

            Assert.AreEqual(RunPhase.GameOver, run.Phase);
            Assert.AreEqual(3, run.WaveIndex, "最后一波就是配置里的最后一波，索引不能越界");
            Assert.AreEqual(60 + 80 + 110, state.Coins, "三波奖励都要累加进金币");

            CollectionAssert.AreEqual(new[]
            {
                "RunStarted", "WaveStarted(1)",
                "WaveCleared(1)", "CoinChanged(60)", "ShopOpened(1)", "ShopClosed(0)", "WaveStarted(2)",
                "WaveCleared(2)", "CoinChanged(140)", "ShopOpened(2)", "ShopClosed(0)", "WaveStarted(3)",
                "WaveCleared(3)", "CoinChanged(250)", "RunEnded(win)",
            }, log.Order, log.Joined);
        }

        [Test]
        public void PlayerDeathEndsTheRunWithoutClearingTheWave()
        {
            RunController run = Build(out EventBus bus, out PlayerModel player, out RunState state,
                out EventLog log, duration: 180f);

            run.StartRun();
            bus.Flush();
            Advance(run, bus, 2f);

            player.HP = 0f;   // 战斗里被打死
            Advance(run, bus, 0.1f);

            Assert.AreEqual(RunPhase.GameOver, run.Phase, "死了就该直接进结算，不该继续跑这一波");
            Assert.AreEqual(1, state.WaveIndex, "死在第一波，波次索引不该往前走");
            Assert.IsFalse(log.Order.Contains("WaveCleared(1)"), "还没打满 180 秒，不该发 WaveCleared：" + log.Joined);
            Assert.AreEqual("RunEnded(lose)", log.Order[log.Order.Count - 1], log.Joined);

            // 结束以后每一步都不该再发事件：RunEnded 只能有一次，WaveStarted 也不能再来
            log.Order.Clear();
            Advance(run, bus, 5f);
            Assert.AreEqual(0, log.Order.Count, "结束态里不该再发任何流程事件：" + log.Joined);
        }

        [Test]
        public void CloseShopOutsideShopIsIgnored()
        {
            RunController run = Build(out EventBus bus, out _, out _, out EventLog log);

            Assert.IsFalse(run.CloseShop(), "Idle 态没有商店可关");

            run.StartRun();
            bus.Flush();
            log.Order.Clear();

            Assert.IsFalse(run.CloseShop(), "战斗里调 CloseShop 不该凭空开出下一波");
            bus.Flush();
            Assert.AreEqual(0, log.Order.Count, log.Joined);
            Assert.AreEqual(1, run.WaveIndex);
        }

        [Test]
        public void StartRunAfterGameOverRestartsFromTheFirstWave()
        {
            RunController run = Build(out EventBus bus, out PlayerModel player, out _, out EventLog log,
                duration: 0.5f);

            run.StartRun();
            bus.Flush();
            player.HP = 0f;
            Advance(run, bus, 0.1f);
            Assert.AreEqual(RunPhase.GameOver, run.Phase);

            log.Order.Clear();
            run.StartRun();
            bus.Flush();

            Assert.AreEqual(RunPhase.Battle, run.Phase, "重开一局要能重新进战斗");
            Assert.AreEqual(1, run.WaveIndex, "重开从第一波开始");
            CollectionAssert.AreEqual(new[] { "RunStarted", "WaveStarted(1)" }, log.Order, log.Joined);
        }

        // ---- 装配 ----

        /// <summary>照抄 Wave.json 的三波：时长可变，间隔 / 数量 / 上限 / 奖励与表里一致。</summary>
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

        static RunController Build(out EventBus bus, out PlayerModel player, out RunState state,
            out EventLog log, float duration = 1f)
        {
            bus = new EventBus();
            player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            state = new RunState();

            var run = new RunController(player, state, Waves(duration), bus, Seed, CharacterId);
            log = new EventLog(bus);
            return run;
        }

        /// <summary>
        /// 跑若干秒的固定步长，每一步末尾分发一次事件。
        /// 真流程里 Flush 由 RunSimulation.Tick 统一做，这里照同一节奏来，
        /// 不然断言看到的顺序会混进"两条事件排在不同 Tick"的差别。
        /// </summary>
        static void Advance(RunController run, IEventBus bus, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                run.Tick(Dt);
                bus.Flush();
            }
        }

        /// <summary>把事件按收到顺序记成字符串：整条序列一起断言，比逐个 IsTrue 更容易看出错在哪一步。</summary>
        sealed class EventLog
        {
            public readonly List<string> Order = new List<string>();

            public EventLog(IEventBus bus)
            {
                bus.Subscribe<RunStarted>(_ => Order.Add("RunStarted"));
                bus.Subscribe<WaveStarted>(e => Order.Add("WaveStarted(" + e.Index + ")"));
                bus.Subscribe<WaveCleared>(e => Order.Add("WaveCleared(" + e.Index + ")"));
                bus.Subscribe<CoinChanged>(e => Order.Add("CoinChanged(" + e.Current + ")"));
                bus.Subscribe<ShopOpened>(e => Order.Add("ShopOpened(" + e.WaveIndex + ")"));
                bus.Subscribe<ShopClosed>(e => Order.Add("ShopClosed(" + e.CoinsSpent + ")"));
                bus.Subscribe<RunEnded>(e => Order.Add(e.Victory ? "RunEnded(win)" : "RunEnded(lose)"));
            }

            public string Joined => string.Join(" | ", Order);
        }
    }
}
