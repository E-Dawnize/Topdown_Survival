using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 重开一局的状态干净性（I2-10 的"中途死亡重开三次状态正确"）。
    ///
    /// 正式的重开是场景重载（见 ResultView / UnitySceneFlow）：新场景就是新的 PlayerModel、RunState、
    /// 事件总线与视图，天然没有残留。这里验的是另一条更严的口径：在同一个进程、同一批对象上重开，
    /// 跨局状态也必须全部回到开局值。两条都过，才说明"上一局的东西不会漏进下一局"不是靠场景兜住的。
    ///
    /// 跨局状态有四处：RunState 的金币与波次、ShopController 的保底与已买道具、
    /// 买过的道具在 PlayerModel.Stats 上留下的修正、RunController 的击杀数。
    /// </summary>
    public class RunRestartTests
    {
        const float Dt = 1f / 60f;
        const ulong Seed = 0x0BADF00D12345678ul;

        [Test]
        public void ThreeRestartsLeaveNoCrossRunState()
        {
            var bus = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            var state = new RunState();
            var shop = new ShopController(player, state, Items(1), bus, new DeterministicRandom(Seed), 0.1f);
            var run = new RunController(player, state, Waves(0.5f), bus, Seed, "Scavenger", shop);

            float baseMaxHp = player.MaxHP;

            // 第 0 轮是首局，1..3 是三次重开
            for (int restart = 0; restart <= 3; restart++)
            {
                // 场景重载会重建玩家；这里手动补血只是为了在同一个对象上接着跑下一轮
                player.HP = player.MaxHP;

                run.StartRun();
                bus.Flush();

                Assert.AreEqual(0, state.Coins, "第 " + restart + " 次重开：金币必须从 0 开始");
                Assert.AreEqual(1, state.WaveIndex, "第 " + restart + " 次重开：从第 1 波开始");
                Assert.AreEqual(0, shop.Pity, "第 " + restart + " 次重开：保底计数不跨局");
                Assert.AreEqual(0, run.Kills, "第 " + restart + " 次重开：击杀数不跨局");
                Assert.AreEqual(0, shop.Owned.Count, "第 " + restart + " 次重开：已买道具清空");
                Assert.AreEqual(baseMaxHp, player.MaxHP, 1e-4f, "第 " + restart + " 次重开：买过的道具不能把属性带进新局");

                // 打穿第一波 → 进商店 → 买一件 +10 生命上限的道具
                Advance(run, bus, 0.6f);
                Assert.AreEqual(RunPhase.Shop, run.Phase, "第 " + restart + " 局第一波没打完");

                Assert.Greater(state.Coins, 0, "第一波奖励要进金币，否则下面的买入无从谈起");
                int index = FirstPurchasable(shop);
                Assert.GreaterOrEqual(index, 0, "金币够却一件都买不了");
                Assert.IsTrue(shop.TryBuy(index), "买入应该成功");
                Assert.Greater(player.MaxHP, baseMaxHp, "买了加生命上限的道具，属性应该立刻生效");

                // 关掉商店回到战斗，然后被打死 → 结算
                Assert.IsTrue(run.CloseShop());
                bus.Flush();

                player.HP = 0f;
                Advance(run, bus, 0.1f);

                Assert.AreEqual(RunPhase.GameOver, run.Phase, "第 " + restart + " 局没有正常收尾");
            }

            // 三轮下来（每轮都赚了钱、买了一件），下一次重开依旧是干净的开局值
            player.HP = player.MaxHP;
            run.StartRun();
            bus.Flush();

            Assert.AreEqual(0, state.Coins);
            Assert.AreEqual(1, state.WaveIndex);
            Assert.AreEqual(0, shop.Pity);
            Assert.AreEqual(0, shop.Owned.Count);
            Assert.AreEqual(baseMaxHp, player.MaxHP, 1e-4f);
        }

        /// <summary>
        /// 单独钉一条：ResetForNewRun 要把买过的道具在属性上的修正退掉。
        /// 漏掉这一步时金币会归零、属性却还是叠加后的——"重开一局状态干净"就只剩下半句。
        /// </summary>
        [Test]
        public void ResetForNewRunRollsBackPurchasedStats()
        {
            var bus = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            var state = new RunState();
            state.AddCoins(100);
            var shop = new ShopController(player, state, Items(1), bus, new DeterministicRandom(Seed), 0.1f);

            float baseMaxHp = player.MaxHP;

            shop.OpenShop(1);
            int index = FirstPurchasable(shop);
            Assert.GreaterOrEqual(index, 0);
            Assert.IsTrue(shop.TryBuy(index));
            Assert.AreEqual(baseMaxHp + 10f, player.MaxHP, 1e-4f, "道具是 +10 生命上限");
            Assert.AreEqual(1, shop.Owned.Count);

            shop.ResetForNewRun();

            Assert.AreEqual(baseMaxHp, player.MaxHP, 1e-4f, "重置后属性要回到基准");
            Assert.AreEqual(0, shop.Owned.Count);
            Assert.IsFalse(shop.IsOpen, "重置后不该还开着一间商店");
        }

        /// <summary>保底计数只在换局时归零：它跨商店累计（I2-4 的口径），但不跨局。</summary>
        [Test]
        public void ResetForNewRunZeroesPity()
        {
            var bus = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            var state = new RunState();
            state.AddCoins(100);

            // 全白表：每次抽都是"没出紫以上"，保底计数一定累加，不靠运气
            var commonOnly = new Dictionary<string, ItemDef> { ["Common_0"] = Item(ItemRarity.Common, 0) };
            var shop = new ShopController(player, state, commonOnly, bus, new DeterministicRandom(Seed), 0.1f);

            shop.OpenShop(1);
            Assert.Greater(shop.Pity, 0, "连抽 6 次白装，保底计数应该累加");

            shop.ResetForNewRun();
            Assert.AreEqual(0, shop.Pity, "换局后保底计数归零");
        }

        // ---- 装配 ----

        static void Advance(RunController run, IEventBus bus, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                run.Tick(Dt);
                bus.Flush();
            }
        }

        static int FirstPurchasable(ShopController shop)
        {
            for (int i = 0; i < ShopController.SlotCount; i++)
            {
                if (shop.CanBuy(i)) return i;
            }

            return -1;
        }

        /// <summary>形状与 Item.json 一致：+10 生命上限、基准价 25、MaxStack 3。每个品质 count 件。</summary>
        static Dictionary<string, ItemDef> Items(int count)
        {
            var table = new Dictionary<string, ItemDef>();

            foreach (ItemRarity rarity in new[]
                     {
                         ItemRarity.Common, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary
                     })
            {
                for (int i = 0; i < count; i++)
                {
                    string id = rarity + "_" + i;
                    table[id] = Item(rarity, i);
                }
            }

            return table;
        }

        static ItemDef Item(ItemRarity rarity, int index)
        {
            string id = rarity + "_" + index;
            return new ItemDef
            {
                Id = id, Name = id, Description = "", Rarity = rarity, BasePrice = 25,
                Category = ItemCategory.Passive, StatA = StatType.MaxHP, ValueA = 10f,
                StatB = StatType.Unknown, ValueB = 0f, MaxStack = 3, EffectType = "",
                EffectParams = new float[0]
            };
        }

        /// <summary>照抄 Wave.json 的三波，只把时长改短：够打完一波进商店就行。</summary>
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
    }
}
