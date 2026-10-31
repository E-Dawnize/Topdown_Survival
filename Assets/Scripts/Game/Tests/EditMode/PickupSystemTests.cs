using System;
using System.Collections.Generic;
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
    /// 掉落与磁吸（I2-3）。磁吸是一段"距离 → 速度 → 位移"的连续过程，所以这里逐步推进
    /// PickupSystem.Tick 而不是整局跑几万步：错在哪一步、错多少米，断言消息里能直接看出来。
    /// 随机流用固定值桩：0.99 只掉金币，0 必掉心，掉落种类因此是确定的。
    /// </summary>
    public class PickupSystemTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void LootOutsideMagnetRadiusStaysPut()
        {
            PickupSystem pickups = Build(out _, out RunState run, out _, heartRoll: 0.99f);

            pickups.DropEnemyLoot(new Vector2(10f, 0f));
            Advance(pickups, 120);   // 2 秒

            Assert.AreEqual(1, pickups.Count, "0.99 的骰子只掉金币");
            Assert.AreEqual(new Vector2(10f, 0f), PositionOf(pickups, 0), "远了就不吸：掉落物原地待着");
            Assert.IsFalse(Magnetized(pickups, 0));
            Assert.AreEqual(0, run.Coins);
        }

        [Test]
        public void CoinInsideMagnetRadiusMovesCloserEveryStepThenIsCollected()
        {
            PickupSystem pickups = Build(out _, out RunState run, out EventBus bus, heartRoll: 0.99f);

            var picked = new List<CoinPicked>();
            var changed = new List<CoinChanged>();
            bus.Subscribe<CoinPicked>(picked.Add);
            bus.Subscribe<CoinChanged>(changed.Add);

            pickups.DropEnemyLoot(new Vector2(2.2f, 0f));   // 默认吸附半径 2.5
            Assert.AreEqual(1, pickups.Count);

            float previous = PositionOf(pickups, 0).magnitude;
            int steps = 0;
            while (pickups.Count > 0 && steps < 240)
            {
                steps++;
                pickups.Tick(Dt);

                if (pickups.Count == 0) break;

                float distance = PositionOf(pickups, 0).magnitude;
                Assert.Less(distance, previous, "第 " + steps + " 步没有靠近玩家（" + distance.ToString("0.000") +
                                               " vs " + previous.ToString("0.000") + "）");
                Assert.IsTrue(Magnetized(pickups, 0), "进了吸附半径就该处在磁吸状态");
                previous = distance;
            }

            Assert.AreEqual(0, pickups.Count, "飞到手就该消失，而不是留在场上");
            Assert.Less(steps, 240, "2.2 米吸了 " + steps + " 步还没到");
            Assert.AreEqual(1, run.Coins, "一枚金币价值 1");

            bus.Flush();
            Assert.AreEqual(1, picked.Count, "拾取要发 CoinPicked");
            Assert.AreEqual(1, picked[0].Value);
            Assert.AreEqual(1, changed.Count, "拾取也要发 CoinChanged");
            Assert.AreEqual(1, changed[0].Current);
            Assert.AreEqual(1, changed[0].Delta);
        }

        [Test]
        public void MagnetRadiusComesFromThePickupRadiusStat()
        {
            PickupSystem pickups = Build(out PlayerModel player, out _, out _, heartRoll: 0.99f);

            pickups.DropEnemyLoot(new Vector2(5f, 0f));
            Advance(pickups, 30);

            Assert.IsFalse(Magnetized(pickups, 0), "5 米超出默认的 2.5 米吸附半径");

            // 拾取范围道具以后加的就是这条修正，磁吸半径必须跟着走
            var source = new object();
            player.Stats.Add(new StatModifier(StatType.PickupRadius, 3f, ModOp.Flat, source));

            pickups.Tick(Dt);
            Assert.IsTrue(Magnetized(pickups, 0), "吸收半径变成 5.5 米后，5 米处的金币该被吸住");

            player.Stats.RemoveBySource(source);
        }

        [Test]
        public void HeartHealsFiveButNeverPastMaxHp()
        {
            PickupSystem pickups = Build(out PlayerModel player, out _, out _, heartRoll: 0f);

            player.HP = 98f;
            pickups.DropEnemyLoot(new Vector2(0.3f, 0f));   // 0 的骰子：金币 + 心，都落在拾取半径内
            pickups.Tick(Dt);

            Assert.AreEqual(0, pickups.Count, "贴身的两件都该结算");
            Assert.AreEqual(100f, player.HP, 1e-4f, "回 5 血不能把 98 推到 103，必须 clamp 到 MaxHP");

            // 血不满时一颗心回满 5 点
            player.HP = 50f;
            pickups.DropEnemyLoot(new Vector2(0.3f, 0f));
            pickups.Tick(Dt);
            Assert.AreEqual(55f, player.HP, 1e-4f);
        }

        [Test]
        public void CapacityDropsTheOldestPickup()
        {
            PickupSystem pickups = Build(out _, out _, out _, heartRoll: 0.99f);

            const int Spawned = PickupSystem.Capacity + 10;

            // 全部扔在场子另一头（离玩家远），保证不会被磁吸走
            for (int i = 0; i < Spawned; i++)
            {
                pickups.DropEnemyLoot(new Vector2(100f + i, 100f));
            }

            Assert.AreEqual(PickupSystem.Capacity, pickups.Count, "上限就是容量，满了不再涨");

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < pickups.Count; i++)
            {
                Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                minX = Mathf.Min(minX, snapshot.Position.x);
                maxX = Mathf.Max(maxX, snapshot.Position.x);
            }

            Assert.AreEqual(110f, minX, 1e-3f, "最旧的 10 个被顶掉，场上最早的是第 11 个");
            Assert.AreEqual(100f + Spawned - 1, maxX, 1e-3f, "最新掉的那个一定还在");
        }

        [Test]
        public void EnemyDeathsDropOneCoinEachThroughTheSimulation()
        {
            const float ArenaHalf = 67f;

            var bus = new EventBus();
            var player = new PlayerModel(1000000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var run = new RunState();
            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(AppRoot.DefaultWorldSeed),
                AppRoot.PlayerUnit, 64, bus);
            weapons.AddWeapon(new WeaponDef
            {
                Id = "Bow", Name = "弓箭", Kind = WeaponKind.Ranged, BaseDamage = 100f, Cooldown = 0.1f,
                Range = 8f, Projectiles = 1, SpreadDeg = 0f, BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 30f, Pierce = 1, Params = new float[0]
            });

            // 只掉金币（0.99 的骰子），金币收支才对得上账
            var pickups = new PickupSystem(player, run, bus, new FixedRandom(0.99f));

            var kills = 0;
            bus.Subscribe<UnitDied>(_ => kills++);

            var simulation = new RunSimulation(player, new PlayerController(player, bus), enemies, weapons, bus,
                null, null, pickups);

            var crawler = new EnemyDef
            {
                Id = "Crawler", Name = "爬虫", Behaviour = EnemyBehaviourType.Chase,
                HP = 12f, Damage = 6f, Speed = 3f, AttackRange = 0.6f, AttackInterval = 1f,
                CoinDrop = 1, BodyRadius = 0.35f
            };

            // 7 米：还在弓箭 8 米射程内，但比 2.5 米吸附半径远得多，
            // 死之前那 0.5 秒的追击（3 米/秒）跑不进吸附范围，金币应该都留在地上
            const int Spawned = 5;
            for (int i = 0; i < Spawned; i++)
            {
                enemies.Spawn(crawler, new Vector2(7f, -1f + i * 0.5f));
            }

            var input = new InputSnapshot();
            for (int step = 0; step < 300; step++)
            {
                simulation.Tick(Dt, ref input);
            }

            Assert.AreEqual(Spawned, kills, "5 个 12 血的爬虫在 100 伤害的箭下应该全死");

            // 掉在 7 米外（超出 2.5 米吸附半径），所以应该都还在地上
            int coinsOnGround = 0;
            for (int i = 0; i < pickups.Count; i++)
            {
                Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                Assert.AreEqual(PickupKind.Coin, snapshot.Kind);
                coinsOnGround++;
            }

            Assert.AreEqual(Spawned, coinsOnGround, "每死一只怪掉 1 枚金币");
            Assert.AreEqual(Spawned, run.Coins + coinsOnGround, "金币总数 = 击杀数，没有凭空的币也没有丢的币");
        }

        [Test]
        public void HeartDropsAboutAQuarterOfTheTime()
        {
            // 25% 的判定只能靠统计：3000 次掉落在同一套固定种子上是确定的，
            // 容差按二项分布取 3 个百分点（σ 约 0.8 个百分点）。
            const int Rounds = 10;
            const int DropsPerRound = 300;   // 每轮 300 金币 + 约 75 心，不会碰到 512 的容量上限

            int hearts = 0;
            const int TotalDrops = Rounds * DropsPerRound;

            for (int round = 0; round < Rounds; round++)
            {
                // 每轮换一个 PickupSystem：容量是固定的 512，不换就没法统计整局的掉落率
                var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
                var run = new RunState();
                var pickups = new PickupSystem(player, run, null,
                    new DeterministicRandom(0x9E3779B97F4A7C15ul + (ulong)round));

                for (int i = 0; i < DropsPerRound; i++)
                {
                    pickups.DropEnemyLoot(new Vector2(100f, 100f));   // 远处，不会被捡走
                }

                for (int i = 0; i < pickups.Count; i++)
                {
                    Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                    if (snapshot.Kind == PickupKind.Heart) hearts++;
                }
            }

            // 分母是"掉了多少次"，不是"场上还剩多少件"：心是额外掉的一件，算进去会把比例压到 0.8 倍
            float rate = (float)hearts / TotalDrops;
            TestContext.Progress.WriteLine("[掉落] " + TotalDrops + " 次掉落里 " + hearts + " 颗心 = " +
                                          (rate * 100f).ToString("0.00") + "%（目标 25%）");
            Assert.LessOrEqual(Mathf.Abs(rate - PickupSystem.HeartDropChance), 0.03f,
                "心的掉落率 " + (rate * 100f).ToString("0.00") + "%，与 25% 差得太多");
        }

        [Test]
        public void TickDoesNotAllocateInSteadyState()
        {
            PickupSystem pickups = Build(out _, out _, out _, heartRoll: 0.99f);

            const int Count = 200;
            for (int i = 0; i < Count; i++)
            {
                pickups.DropEnemyLoot(new Vector2(100f, 100f + i));
            }

            // 预热：JIT 与 Mathf 的静态初始化这类一次性开销不该记在磁吸循环头上
            Advance(pickups, 600);

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            Advance(pickups, 600);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.AreEqual(Count, pickups.Count, "这批掉落物都在远处，不该被吸走");
            Assert.AreEqual(0L, allocated, "600 步磁吸判定分配了 " + allocated + " 字节（" + Count + " 个掉落物）");
        }

        // ---- 装配 ----

        static PickupSystem Build(out PlayerModel player, out RunState run, out EventBus bus, float heartRoll)
        {
            bus = new EventBus();
            player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            run = new RunState();
            return new PickupSystem(player, run, bus, new FixedRandom(heartRoll));
        }

        static void Advance(PickupSystem pickups, int steps)
        {
            for (int i = 0; i < steps; i++) pickups.Tick(Dt);
        }

        static Vector2 PositionOf(PickupSystem pickups, int ordinal)
        {
            Assert.IsTrue(pickups.TryGetActive(ordinal, out PickupSystem.Snapshot snapshot), "第 " + ordinal + " 个掉落物不存在");
            return snapshot.Position;
        }

        static bool Magnetized(PickupSystem pickups, int ordinal)
        {
            Assert.IsTrue(pickups.TryGetActive(ordinal, out PickupSystem.Snapshot snapshot), "第 " + ordinal + " 个掉落物不存在");
            return snapshot.Magnetized;
        }

        /// <summary>固定值桩：掉落种类与磁吸判定都不该依赖"这次骰子掷出多少"，只有心是概率。</summary>
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
