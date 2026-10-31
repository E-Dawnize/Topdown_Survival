using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 商店逻辑（I2-4）：抽卡权重、保底、去重、价格、刷新、买入。
    ///
    /// 道具表不用真实配置：Item.json 现在只有 4 件（一种品质一件），既装不满 6 格也验不了去重，
    /// 所以这里用一张形状相同、数量够的合成表（Items）。真实配置装不满 6 格的行为另有一条用例说明。
    ///
    /// 抽卡用自己的种子固定：商店抽卡本身走 runRng，测试里换成独立实例才不会被别的系统拽走序列。
    /// </summary>
    public class ShopControllerTests
    {
        const float Dt = 1f / 60f;
        const ulong Seed = 0x51A0C0DE1B2C3D4Eul;

        [Test]
        public void WeightsMatchTheGddEndpoints()
        {
            ShopController.WeightsFor(0f, out float common, out float rare, out float epic, out float legendary);

            Assert.AreEqual(0.55f, common, 1e-5f, "幸运 0：白 55%");
            Assert.AreEqual(0.30f, rare, 1e-5f, "幸运 0：蓝 30%");
            Assert.AreEqual(0.12f, epic, 1e-5f, "幸运 0：紫 12%");
            Assert.AreEqual(0.03f, legendary, 1e-5f, "幸运 0：金 3%");

            ShopController.WeightsFor(50f, out common, out rare, out epic, out legendary);

            Assert.AreEqual(0.30f, common, 1e-5f, "幸运 50：白 30%");
            Assert.AreEqual(0.40f, rare, 1e-5f, "幸运 50：蓝 40%");
            Assert.AreEqual(0.22f, epic, 1e-5f, "幸运 50：紫 22%");
            Assert.AreEqual(0.08f, legendary, 1e-5f, "幸运 50：金 8%");

            // 中点插值 + 权重之和仍是 1，插值不需要再归一化
            ShopController.WeightsFor(25f, out common, out rare, out epic, out legendary);
            Assert.AreEqual(0.425f, common, 1e-5f);
            Assert.AreEqual(0.35f, rare, 1e-5f);
            Assert.AreEqual(0.17f, epic, 1e-5f);
            Assert.AreEqual(0.055f, legendary, 1e-5f);
            Assert.AreEqual(1f, common + rare + epic + legendary, 1e-5f);

            // 超过 50 点幸运按 50 那一列算，权重不会跑出表外
            ShopController.WeightsFor(200f, out common, out _, out _, out _);
            Assert.AreEqual(0.30f, common, 1e-5f);
        }

        [Test]
        public void RarityDistributionMatchesWeightsAtLuckZero()
        {
            AssertDistribution(0f);
        }

        [Test]
        public void RarityDistributionMatchesWeightsAtLuckFifty()
        {
            AssertDistribution(50f);
        }

        [Test]
        public void PityForcesEpicAfterEightDryDraws()
        {
            ShopController shop = Build(out _, out _, out _);

            int streak = 0;            // 连续没出紫以上的次数
            int forced = 0;            // 被保底救出来的次数
            const int Shops = 2000;

            for (int i = 0; i < Shops; i++)
            {
                shop.OpenShop(1);

                for (int slot = 0; slot < ShopController.SlotCount; slot++)
                {
                    ItemDef def = shop.Slot(slot).Item;
                    Assert.IsNotNull(def, "每格都该抽到一件道具");

                    if (def.Rarity == ItemRarity.Epic || def.Rarity == ItemRarity.Legendary)
                    {
                        if (streak >= ShopController.PityThreshold) forced++;
                        streak = 0;
                        continue;
                    }

                    streak++;
                    Assert.LessOrEqual(streak, ShopController.PityThreshold,
                        "连续 " + streak + " 次没出紫以上，保底没生效（第 " + i + " 间商店第 " + slot + " 格）");
                }
            }

            Assert.Greater(forced, 0, Shops * ShopController.SlotCount + " 抽里一次保底都没触发，这条用例等于没跑");
            TestContext.Progress.WriteLine("[保底] " + Shops * ShopController.SlotCount + " 抽里触发 " + forced + " 次");
        }

        [Test]
        public void PriceFollowsBasePriceAndWaveGrowth()
        {
            var table = new Dictionary<string, ItemDef>
            {
                ["WoolGlove"] = new ItemDef
                {
                    Id = "WoolGlove", Name = "毛线手套", Rarity = ItemRarity.Common, BasePrice = 25,
                    MaxStack = 3, StatA = StatType.Regen, ValueA = -2f
                }
            };

            ShopController shop = Build(out _, out _, out _, table);

            shop.OpenShop(1);
            for (int i = 0; i < ShopController.SlotCount; i++)
            {
                Assert.AreEqual(25, shop.Slot(i).Price, "第 1 波不打折也不涨价");
            }

            shop.OpenShop(3);
            for (int i = 0; i < ShopController.SlotCount; i++)
            {
                Assert.AreEqual(30, shop.Slot(i).Price, "25 × (1 + 0.1 × 2) = 30");
            }

            // 半值向上取整：45 × 1.1 = 49.5 → 50（Mathf.RoundToInt 会往偶数走成 48）
            var neck = new ItemDef { Id = "IronNeck", Name = "铁皮护颈", BasePrice = 45 };
            Assert.AreEqual(45, shop.PriceOf(neck, 1));
            Assert.AreEqual(50, shop.PriceOf(neck, 2));
            Assert.AreEqual(54, shop.PriceOf(neck, 3));

            var sign = new ItemDef { Id = "InfiniteSign", Name = "无限路牌", BasePrice = 140 };
            Assert.AreEqual(140, shop.PriceOf(sign, 1));
            Assert.AreEqual(196, shop.PriceOf(sign, 5), "140 × (1 + 0.1 × 4) = 196");
        }

        [Test]
        public void SlotsAreDistinctWithinOneShop()
        {
            ShopController shop = Build(out _, out _, out _, Items(2));

            for (int i = 0; i < 200; i++)
            {
                shop.OpenShop(1 + i % 3);

                var ids = new List<string>(ShopController.SlotCount);
                for (int slot = 0; slot < ShopController.SlotCount; slot++)
                {
                    string id = shop.Slot(slot).Item.Id;
                    Assert.IsFalse(ids.Contains(id), "第 " + i + " 间商店的 " + slot + " 格重复了 " + id +
                                                     "（本轮：" + string.Join(",", ids) + "）");
                    ids.Add(id);
                }
            }
        }

        [Test]
        public void SmallItemTableStillFillsSixSlotsWithDuplicates()
        {
            // Item.json 现在只有 4 件（一种品质一件），凑不出 6 格不重复。
            // 这时候宁可重复也不要留空格：空着的格子会让玩家以为"这一波商店只有 4 件货"。
            // 顺带一个只能等 I3 补表才能消掉的现象：去重优先于品质，所以 4 件的表里
            // 每一手都会把紫和金摆出来（先在抽到的品质里找没用过的，找不到就退到别的品质），
            // 紫金不再是"抽到的"，而是"凑出来的"。表补到每种品质 6 件以上就回到按权重抽。
            ShopController shop = Build(out _, out _, out _, Items(1));

            shop.OpenShop(1);

            for (int slot = 0; slot < ShopController.SlotCount; slot++)
            {
                Assert.IsNotNull(shop.Slot(slot).Item, "第 " + slot + " 格空着：表太小也该用重复填满");
            }
        }

        [Test]
        public void FirstRefreshIsFreeThenCostsTenCoins()
        {
            ShopController shop = Build(out _, out RunState run, out _, coins: 100);

            shop.OpenShop(1);
            Assert.AreEqual(0, shop.RefreshPriceNow, "每波第一次刷新免费");

            Assert.IsTrue(shop.TryRefresh());
            Assert.AreEqual(100, run.Coins, "免费刷新不该扣钱");
            Assert.AreEqual(0, shop.Spent);

            Assert.AreEqual(ShopController.RefreshPrice, shop.RefreshPriceNow, "第二次开始收 10 金币");
            Assert.IsTrue(shop.TryRefresh());
            Assert.AreEqual(90, run.Coins);
            Assert.AreEqual(10, shop.Spent);

            // 下一间商店：免费刷新回来，本间花费清零
            shop.OpenShop(2);
            Assert.AreEqual(0, shop.RefreshPriceNow);
            Assert.AreEqual(0, shop.Spent);
        }

        [Test]
        public void RefreshFailsWhenCoinsAreShort()
        {
            ShopController shop = Build(out _, out RunState run, out _, coins: 15);

            shop.OpenShop(1);
            Assert.IsTrue(shop.TryRefresh(), "第一次免费，钱再少也能刷");

            run.TrySpend(run.Coins - 5);   // 只剩 5 金币
            Assert.IsFalse(shop.TryRefresh(), "不够 10 金币就不该刷");
            Assert.AreEqual(5, run.Coins, "刷新失败不该扣钱");
            Assert.AreEqual(0, shop.Spent);
        }

        [Test]
        public void CommandsDoNothingOutsideAnOpenedShop()
        {
            ShopController shop = Build(out _, out RunState run, out _, coins: 100);

            Assert.IsFalse(shop.TryBuy(0), "还没开商店就没东西可买");
            Assert.IsFalse(shop.TryRefresh(), "还没开商店也不该能刷");
            Assert.AreEqual(100, run.Coins);

            shop.OpenShop(1);
            shop.Close();
            Assert.IsFalse(shop.TryBuy(0), "关掉的商店不能买上一间的货");
            Assert.IsFalse(shop.TryRefresh());
            Assert.IsTrue(shop.Slot(0).IsEmpty, "关商店要清空格子");
            Assert.AreEqual(100, run.Coins);
        }

        [Test]
        public void BuyingSpendsCoinsAppliesStatsAndEmptiesTheSlot()
        {
            var table = new Dictionary<string, ItemDef>
            {
                ["WoolGlove"] = new ItemDef
                {
                    Id = "WoolGlove", Name = "毛线手套", Rarity = ItemRarity.Common, BasePrice = 25,
                    MaxStack = 3, StatA = StatType.MaxHP, ValueA = 10f
                }
            };

            ShopController shop = Build(out PlayerModel player, out RunState run, out EventBus bus, table, coins: 100);
            shop.OpenShop(1);

            var changed = new List<CoinChanged>();
            bus.Subscribe<CoinChanged>(changed.Add);

            Assert.IsTrue(shop.TryBuy(0));
            bus.Flush();

            Assert.AreEqual(75, run.Coins, "买入按格子上的价格扣钱");
            Assert.AreEqual(25, shop.Spent, "本间商店的花费要累计起来给 ShopClosed 用");
            Assert.AreEqual(110f, player.MaxHP, 1e-4f, "属性必须通过 StatBlock.Add 生效");
            Assert.AreEqual(1, shop.Owned.Count);
            Assert.AreEqual(1, shop.StackOf("WoolGlove"));

            Assert.IsTrue(shop.Slot(0).IsEmpty, "买走以后这一格清空");
            Assert.IsFalse(shop.TryBuy(0), "同一格不能重复购买");

            Assert.AreEqual(1, changed.Count, "扣钱要发 CoinChanged，HUD 靠它刷新");
            Assert.AreEqual(75, changed[0].Current);
            Assert.AreEqual(-25, changed[0].Delta);

            // 同名道具的第二层：另一个格子（这张表里只有这一件），属性再叠一层
            Assert.IsTrue(shop.TryBuy(1));
            Assert.AreEqual(120f, player.MaxHP, 1e-4f, "同名道具按层叠加");
            Assert.AreEqual(2, shop.StackOf("WoolGlove"));
            Assert.AreEqual(50, shop.Spent);
        }

        [Test]
        public void BuyingRespectsMaxStack()
        {
            // MaxStack = 1 的单件表：6 格都是它，买过一件之后其余 5 格也买不了
            var table = new Dictionary<string, ItemDef>
            {
                ["WoolGlove"] = new ItemDef
                {
                    Id = "WoolGlove", Name = "毛线手套", Rarity = ItemRarity.Common, BasePrice = 25,
                    MaxStack = 1, StatA = StatType.MaxHP, ValueA = 10f
                }
            };

            ShopController shop = Build(out PlayerModel player, out RunState run, out _, table, coins: 100);
            shop.OpenShop(1);

            Assert.IsTrue(shop.TryBuy(0));
            Assert.IsFalse(shop.TryBuy(1), "叠满 MaxStack 后这件道具再出现也买不了");
            Assert.AreEqual(1, shop.StackOf("WoolGlove"));
            Assert.AreEqual(110f, player.MaxHP, 1e-4f, "没买成就不该叠属性");
            Assert.AreEqual(75, run.Coins, "没买成就不该扣钱");
            Assert.IsFalse(shop.Slot(1).IsEmpty, "没买成的格子还留着道具");
        }

        [Test]
        public void BuyingWithoutEnoughCoinsChangesNothing()
        {
            ShopController shop = Build(out PlayerModel player, out RunState run, out _, coins: 10);
            shop.OpenShop(1);

            Assert.IsFalse(shop.TryBuy(0));
            Assert.AreEqual(10, run.Coins);
            Assert.AreEqual(0, shop.Spent);
            Assert.IsFalse(shop.Slot(0).IsEmpty, "买不起时格子不该被清空");
            Assert.AreEqual(0, shop.Owned.Count);
            Assert.AreEqual(100f, player.MaxHP, 1e-4f);
        }

        [Test]
        public void RunControllerOpensShopAndReportsActualSpend()
        {
            var bus = new EventBus();
            var player = new PlayerModel(1000000f, 5f, 15f, 0.15f, 2f, 67f);
            var run = new RunState();
            var shop = new ShopController(player, run, Items(6), bus, new DeterministicRandom(Seed), 0.1f);
            var controller = new RunController(player, run, Waves(2f), bus, Seed, "Scavenger", shop);

            var opened = new List<ShopOpened>();
            var closed = new List<ShopClosed>();
            bus.Subscribe<ShopOpened>(opened.Add);
            bus.Subscribe<ShopClosed>(closed.Add);

            controller.StartRun();
            bus.Flush();

            int guard = 0;
            while (controller.Phase != RunPhase.Shop && guard++ < 1000)
            {
                controller.Tick(Dt);
                bus.Flush();
            }

            Assert.AreEqual(RunPhase.Shop, controller.Phase, "一波打完该进商店");
            Assert.AreEqual(1, opened.Count);
            Assert.IsTrue(shop.IsOpen, "进商店时 RunController 要把商店开起来");
            Assert.IsFalse(shop.Slot(0).IsEmpty, "开好的商店应该有候选格");

            int coinsBefore = run.Coins;
            int price = shop.Slot(0).Price;
            Assert.IsTrue(shop.TryBuy(0));
            bus.Flush();

            Assert.IsTrue(controller.CloseShop());
            bus.Flush();

            Assert.AreEqual(price, closed[0].CoinsSpent, "ShopClosed 报的是实际花费，不由调用方输入");
            Assert.AreEqual(coinsBefore - price, run.Coins);
            Assert.IsFalse(shop.IsOpen, "关商店以后商店要收起来");
            Assert.IsFalse(shop.TryBuy(0), "离开商店就买不了上一间的货");
        }

        [Test]
        public void StartRunResetsPity()
        {
            var bus = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            var run = new RunState();

            // 0.2 的骰子每次都落进白色（55%），保底计数每抽加一，所以 6 抽之后必然是 6
            var shop = new ShopController(player, run, Items(2), bus, new FixedRandom(0.2f), 0.1f);
            var controller = new RunController(player, run, Waves(2f), bus, Seed, "Scavenger", shop);

            shop.OpenShop(1);
            Assert.AreEqual(ShopController.SlotCount, shop.Pity, "6 抽都落在白色，保底计数该是 6");

            controller.StartRun();
            Assert.AreEqual(0, shop.Pity, "换一局必须把上一局的保底计数清零");
            Assert.IsFalse(shop.IsOpen);
        }

        // ---- 装配 ----

        /// <summary>
        /// 每个品质 count 件、形状与 Item.json 一致（属性 + 品质 + 基准价 + MaxStack）。
        /// 价格 25、MaxStack 3、属性 +10 生命上限：买入效果与价格一眼能对上。
        /// </summary>
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
                    table[id] = new ItemDef
                    {
                        Id = id, Name = id, Description = "", Rarity = rarity, BasePrice = 25,
                        Category = ItemCategory.Passive, StatA = StatType.MaxHP, ValueA = 10f,
                        StatB = StatType.Unknown, ValueB = 0f, MaxStack = 3, EffectType = "",
                        EffectParams = new float[0]
                    };
                }
            }

            return table;
        }

        static ShopController Build(out PlayerModel player, out RunState run, out EventBus bus,
            IReadOnlyDictionary<string, ItemDef> items = null, float luck = 0f, int coins = 1000)
        {
            bus = new EventBus();
            player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            player.Stats.SetBase(StatType.Luck, luck);
            run = new RunState();
            run.AddCoins(coins);
            return new ShopController(player, run, items ?? Items(6), bus, new DeterministicRandom(Seed), 0.1f);
        }

        /// <summary>照抄 Wave.json 的三波，只把时长改短：奖励与上限不动，够进商店就行。</summary>
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
                    SpawnCountMin = 2, SpawnCountMax = 3, EnemyPool = new[] { "Crawler", "Charger", "Thrower" },
                    ConcurrentCap = 100, CoinReward = 110, BossId = ""
                },
            };
        }

        /// <summary>
        /// 10000 间商店 × 6 格 = 60000 次抽卡，逐品质比对权重表，容差 2 个百分点。
        ///
        /// 每间商店开之前清一次保底计数：保底会把"紫以上"的实际占比从 15% 抬到约 19.5%
        /// （连空 8 次的概率有 0.85^8 ≈ 27%），一条不掐掉保底的分布断言测的就不是权重表了。
        /// 每间只抽 6 次，够不到 8 连空，所以清一次就够。
        /// </summary>
        void AssertDistribution(float luck)
        {
            ShopController shop = Build(out _, out _, out _, Items(6), luck);

            ShopController.WeightsFor(luck, out float common, out float rare, out float epic, out float legendary);
            float[] expected = { common, rare, epic, legendary };
            string[] names = { "白", "蓝", "紫", "金" };
            int[] counts = new int[4];

            const int Shops = 10000;
            int draws = 0;

            for (int i = 0; i < Shops; i++)
            {
                shop.ResetForNewRun();
                shop.OpenShop(1);

                for (int slot = 0; slot < ShopController.SlotCount; slot++)
                {
                    Assert.IsNotNull(shop.Slot(slot).Item, "每格都该抽到一件道具");
                    counts[(int)shop.Slot(slot).Item.Rarity]++;
                    draws++;
                }
            }

            for (int i = 0; i < expected.Length; i++)
            {
                float actual = (float)counts[i] / draws;
                TestContext.Progress.WriteLine("[幸运 " + luck + "] " + names[i] + " 实测 " +
                                              (actual * 100f).ToString("0.00") + "%（权重 " +
                                              (expected[i] * 100f).ToString("0.0") + "%）");
                Assert.LessOrEqual(Mathf.Abs(actual - expected[i]), 0.02f,
                    "幸运 " + luck + " 时" + names[i] + "品质实测 " + (actual * 100f).ToString("0.00") +
                    "%，与权重 " + (expected[i] * 100f).ToString("0.0") + "% 相差超过 2 个百分点");
            }
        }

        /// <summary>固定值桩：抽品质只掷一次骰子，钉死它就能让品质走向确定。</summary>
        sealed class FixedRandom : IRandom01
        {
            readonly float _value;

            public FixedRandom(float value)
            {
                _value = value;
            }

            public float Next01() => _value;
        }
    }
}
