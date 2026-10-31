using System.Collections.Generic;
using System.Diagnostics;
using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 三波全流程：从第一波打到最后一波，走的是 RunSimulation.Tick —— RunLoop 每帧跑的那同一段顺序代码。
    ///
    /// 波次表照抄 Wave.json（3 波 × 180 秒，每波上限 40 / 70 / 100），所以这条测试要跑三万多步，
    /// 是本仓库最慢的一条：每波实际时长与配置差多少、同屏数量有没有突破上限、事件顺序对不对，
    /// 都在这一条里一次验完。
    ///
    /// 玩家生命调到很大：默认 100 血在四十个爬虫面前撑不过十秒，"跑满三波"就无从谈起。
    /// 中途死亡的流程另有一条用例（PlayerDiesMidWave...）。
    ///
    /// 没有商店 UI，所以一进商店就从外面调 CloseShop —— 商店 UI 以后调的是同一个入口。
    /// </summary>
    public class WaveFlowTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;

        /// <summary>玩家生命开到这么大，三波里怎么挨打都不会死。</summary>
        const float UnkillableHp = 1000000f;

        static readonly ulong WorldSeed = AppRoot.DefaultWorldSeed;

        [Test]
        public void ThreeWavesRunForTheirConfiguredDurations()
        {
            RunSimulation simulation = Build(out Recorder recorder, out EnemySystem enemies, out RunController run,
                out RunState state, UnkillableHp);

            var input = new InputSnapshot();
            var stopwatch = Stopwatch.StartNew();

            const int StepsPerWave = 10800;                 // 180 秒 × 60Hz
            const int MaxSteps = 3 * StepsPerWave + 1200;   // 两间商店各几步 + 余量

            int step = 0;
            while (recorder.EndedAt < 0 && step < MaxSteps)
            {
                step++;
                recorder.Step = step;

                simulation.Tick(Dt, ref input);

                // 同屏上限：本步刷完以后活着的不该超过当前这一波的上限。
                // 用 if + Fail 而不是 LessOrEqual：这条每步都跑，消息字符串不该每步都拼一个。
                WaveDef wave = run.CurrentWave;
                if (enemies.AliveCount > wave.ConcurrentCap)
                {
                    Assert.Fail("第 " + step + " 步同屏 " + enemies.AliveCount + " 个，超过第 " + wave.Index +
                                " 波的上限 " + wave.ConcurrentCap);
                }

                // 没有商店 UI：进商店就关掉，相当于按 F4 跳过
                if (run.Phase == RunPhase.Shop) run.CloseShop();
            }

            stopwatch.Stop();
            TestContext.Progress.WriteLine("[三波全流程] " + step + " 个模拟步，" + stopwatch.ElapsedMilliseconds +
                                          " ms，共 " + (step * Dt / 60f).ToString("0.00") + " 分钟游戏时间");

            Assert.GreaterOrEqual(recorder.EndedAt, 0, "三波没打完就到了步数上限，步数 " + step);
            Assert.IsTrue(recorder.Victory, "打完最后一波算通关");
            Assert.AreEqual(3, state.WaveIndex, "波次索引停在配置里的最后一波");
            Assert.AreEqual(3, state.WavesTotal);
            Assert.AreEqual(60 + 80 + 110, state.Coins, "三波的金币奖励都要结算");

            // 每波实际时长与 Wave.json 的 Duration 差 ≤ 2 秒
            for (int index = 1; index <= 3; index++)
            {
                Assert.IsTrue(recorder.ClearedAt.ContainsKey(index), "第 " + index + " 波没打完");
                int started = recorder.StartedAt[index];
                int cleared = recorder.ClearedAt[index];
                float measured = (cleared - started) * Dt;

                TestContext.Progress.WriteLine("[第 " + index + " 波] " + (cleared - started) + " 步 = " +
                                              measured.ToString("0.000") + " 秒（配置 180 秒）");

                Assert.LessOrEqual(Mathf.Abs(measured - 180f), 2f,
                    "第 " + index + " 波实际跑了 " + measured.ToString("0.00") + " 秒，配置是 180 秒");
            }

            CollectionAssert.AreEqual(new[]
            {
                "RunStarted", "WaveStarted(1)", "WaveCleared(1)", "ShopOpened(1)", "ShopClosed(0)",
                "WaveStarted(2)", "WaveCleared(2)", "ShopOpened(2)", "ShopClosed(0)",
                "WaveStarted(3)", "WaveCleared(3)", "RunEnded(win)",
            }, recorder.Order, recorder.Joined);
        }

        [Test]
        public void PlayerDiesMidWaveAndNoFurtherWaveStarts()
        {
            RunSimulation simulation = Build(out Recorder recorder, out _, out RunController run, out RunState state,
                30f);   // 30 血：几个爬虫几秒就打空

            var input = new InputSnapshot();
            int step = 0;
            const int MaxSteps = 3 * 10800;

            while (run.Phase != RunPhase.GameOver && step < MaxSteps)
            {
                step++;
                recorder.Step = step;

                simulation.Tick(Dt, ref input);
                if (run.Phase == RunPhase.Shop) run.CloseShop();
            }

            Assert.AreEqual(RunPhase.GameOver, run.Phase, "玩家死了一局就该收尾");
            Assert.IsFalse(recorder.Victory, "中途死亡不能算通关");
            Assert.AreEqual(1, state.WaveIndex, "死在第 1 波，索引不该往前走");

            // 死亡以后一条 WaveStarted 都不该再出现，也没有清波与商店
            CollectionAssert.AreEqual(new[] { "RunStarted", "WaveStarted(1)", "RunEnded(lose)" }, recorder.Order,
                recorder.Joined);
        }

        // ---- 装配 ----

        /// <summary>整套装配：玩家、控制器、敌人、弓箭、局状态机、刷怪系统、一步模拟。</summary>
        static RunSimulation Build(out Recorder recorder, out EnemySystem enemies, out RunController run,
            out RunState state, float playerHp)
        {
            var events = new EventBus();
            var player = new PlayerModel(playerHp, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var playerController = new PlayerController(player, events);

            // 刷怪与战斗共用一条流（I2-2 的要求）：同一种子 + 同一输入 = 同一次刷怪与暴击
            var combatRandom = new DeterministicRandom(WorldSeed);
            enemies = new EnemySystem(player, ArenaHalf, combatRandom);

            var weapons = new WeaponSystem(enemies, combatRandom, AppRoot.PlayerUnit, 256, events);
            weapons.AddWeapon(BowDef());

            state = new RunState();
            List<WaveDef> waves = ConfigWaves();
            run = new RunController(player, state, waves, events, WorldSeed, "Scavenger");
            var waveController = new WaveController(enemies, EnemyTable(), combatRandom);

            recorder = new Recorder(events);

            var simulation = new RunSimulation(player, playerController, enemies, weapons, events, run, waveController);
            run.StartRun();
            return simulation;
        }

        /// <summary>照抄 Wave.json：3 波各 180 秒，间隔 1.2 / 1.0 / 0.9，上限 40 / 70 / 100。</summary>
        static List<WaveDef> ConfigWaves()
        {
            return new List<WaveDef>
            {
                new WaveDef
                {
                    Id = "W1", Index = 1, Duration = 180f, SpawnInterval = 1.2f,
                    SpawnCountMin = 1, SpawnCountMax = 2, EnemyPool = new[] { "Crawler" },
                    ConcurrentCap = 40, CoinReward = 60, BossId = ""
                },
                new WaveDef
                {
                    Id = "W2", Index = 2, Duration = 180f, SpawnInterval = 1.0f,
                    SpawnCountMin = 2, SpawnCountMax = 3, EnemyPool = new[] { "Crawler", "Thrower" },
                    ConcurrentCap = 70, CoinReward = 80, BossId = ""
                },
                new WaveDef
                {
                    Id = "W3", Index = 3, Duration = 180f, SpawnInterval = 0.9f,
                    SpawnCountMin = 2, SpawnCountMax = 3,
                    EnemyPool = new[] { "Crawler", "Charger", "Thrower" },
                    ConcurrentCap = 100, CoinReward = 110, BossId = ""
                },
            };
        }

        /// <summary>照抄 Enemy.json 的三个怪种。测试不读 StreamingAssets。</summary>
        static Dictionary<string, EnemyDef> EnemyTable()
        {
            return new Dictionary<string, EnemyDef>
            {
                ["Crawler"] = new EnemyDef
                {
                    Id = "Crawler", Name = "爬虫", Behaviour = EnemyBehaviourType.Chase,
                    HP = 12f, Damage = 6f, Speed = 3f, AttackRange = 0.6f, AttackInterval = 1f,
                    CoinDrop = 1, BodyRadius = 0.35f
                },
                ["Charger"] = new EnemyDef
                {
                    Id = "Charger", Name = "冲锋兵", Behaviour = EnemyBehaviourType.Charge,
                    HP = 18f, Damage = 10f, Speed = 2.5f, AttackRange = 4f, AttackInterval = 3f,
                    CoinDrop = 2, BodyRadius = 0.4f
                },
                ["Thrower"] = new EnemyDef
                {
                    Id = "Thrower", Name = "投手", Behaviour = EnemyBehaviourType.Ranged,
                    HP = 15f, Damage = 8f, Speed = 2f, AttackRange = 5f, AttackInterval = 2.5f,
                    CoinDrop = 2, BodyRadius = 0.35f
                },
            };
        }

        /// <summary>照抄 Weapon.json 的 Bow：玩家得有武器，不然这一局只有挨打。</summary>
        static WeaponDef BowDef()
        {
            return new WeaponDef
            {
                Id = "Bow", Name = "弓箭", Kind = WeaponKind.Ranged,
                BaseDamage = 8f, Cooldown = 0.85f, Range = 8f,
                Projectiles = 1, SpreadDeg = 0f,
                BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 18f, Pierce = 1, Params = new float[0]
            };
        }

        /// <summary>
        /// 按收到顺序记事件，并带上"第几步收到的"。事件在 RunSimulation.Tick 末尾分发，
        /// 所以 Step 就是这一步的序号——每波时长靠 WaveStarted 与 WaveCleared 的步数差算出来。
        /// </summary>
        sealed class Recorder
        {
            public int Step;
            public readonly List<string> Order = new List<string>();
            public readonly Dictionary<int, int> StartedAt = new Dictionary<int, int>();
            public readonly Dictionary<int, int> ClearedAt = new Dictionary<int, int>();
            public int EndedAt = -1;
            public bool Victory;

            public Recorder(IEventBus bus)
            {
                bus.Subscribe<RunStarted>(_ => Order.Add("RunStarted"));
                bus.Subscribe<WaveStarted>(e =>
                {
                    StartedAt[e.Index] = Step;
                    Order.Add("WaveStarted(" + e.Index + ")");
                });
                bus.Subscribe<WaveCleared>(e =>
                {
                    ClearedAt[e.Index] = Step;
                    Order.Add("WaveCleared(" + e.Index + ")");
                });
                bus.Subscribe<ShopOpened>(e => Order.Add("ShopOpened(" + e.WaveIndex + ")"));
                bus.Subscribe<ShopClosed>(e => Order.Add("ShopClosed(" + e.CoinsSpent + ")"));
                bus.Subscribe<RunEnded>(e =>
                {
                    EndedAt = Step;
                    Victory = e.Victory;
                    Order.Add(e.Victory ? "RunEnded(win)" : "RunEnded(lose)");
                });
            }

            public string Joined => string.Join(" | ", Order);
        }
    }
}
