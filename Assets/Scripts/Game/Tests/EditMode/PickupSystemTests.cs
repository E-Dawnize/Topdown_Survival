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
    /// 掉落与磁吸。经验是这一版唯一的经济：击杀掉经验，拾取写 RunState 并排队升级。
    /// 随机流用固定值桩：0.99 只掉经验，0 必掉心。
    /// </summary>
    public class PickupSystemTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void LootOutsideMagnetRadiusStaysPut()
        {
            PickupSystem pickups = Build(out _, out RunState run, out _, heartRoll: 0.99f);

            pickups.DropEnemyLoot(new Vector2(10f, 0f), 3);
            Advance(pickups, 120);

            Assert.AreEqual(1, pickups.Count, "0.99 的骰子只掉经验");
            Assert.AreEqual(new Vector2(10f, 0f), PositionOf(pickups, 0));
            Assert.IsFalse(Magnetized(pickups, 0));
            Assert.AreEqual(0, run.Xp);
        }

        [Test]
        public void ExperienceInsideMagnetRadiusIsCollectedAndLevelsUp()
        {
            PickupSystem pickups = Build(out _, out RunState run, out EventBus bus, heartRoll: 0.99f);

            var gained = new List<ExperienceGained>();
            bus.Subscribe<ExperienceGained>(gained.Add);

            pickups.DropEnemyLoot(new Vector2(2.2f, 0f), 5);
            Assert.AreEqual(1, pickups.Count);

            float previous = PositionOf(pickups, 0).magnitude;
            int steps = 0;
            while (pickups.Count > 0 && steps < 240)
            {
                steps++;
                pickups.Tick(Dt);
                if (pickups.Count == 0) break;

                float distance = PositionOf(pickups, 0).magnitude;
                Assert.Less(distance, previous, "第 " + steps + " 步没有靠近玩家");
                Assert.IsTrue(Magnetized(pickups, 0));
                previous = distance;
            }

            Assert.AreEqual(0, pickups.Count);
            Assert.Less(steps, 240);
            Assert.AreEqual(2, run.Level, "5 点经验刚好升到 2 级");
            Assert.AreEqual(1, run.PendingLevelUps);

            bus.Flush();
            Assert.AreEqual(1, gained.Count);
            Assert.AreEqual(5, gained[0].Amount);
            Assert.AreEqual(2, gained[0].Level);
            Assert.AreEqual(1, gained[0].PendingLevelUps);
        }

        [Test]
        public void MagnetRadiusComesFromThePickupRadiusStat()
        {
            PickupSystem pickups = Build(out PlayerModel player, out _, out _, heartRoll: 0.99f);

            pickups.DropEnemyLoot(new Vector2(5f, 0f), 1);
            Advance(pickups, 30);
            Assert.IsFalse(Magnetized(pickups, 0), "5 米超出默认的 2.5 米吸附半径");

            var source = new object();
            player.Stats.Add(new StatModifier(StatType.PickupRadius, 3f, ModOp.Flat, source));

            pickups.Tick(Dt);
            Assert.IsTrue(Magnetized(pickups, 0), "吸收半径变成 5.5 米后，5 米处的经验该被吸住");

            player.Stats.RemoveBySource(source);
        }

        [Test]
        public void HeartHealsEightButNeverPastMaxHp()
        {
            PickupSystem pickups = Build(out PlayerModel player, out _, out _, heartRoll: 0f, heartHeal: 8f);

            player.HP = 98f;
            pickups.DropEnemyLoot(new Vector2(0.3f, 0f), 1);
            pickups.Tick(Dt);

            Assert.AreEqual(0, pickups.Count);
            Assert.AreEqual(100f, player.HP, 1e-4f, "回 8 血不能把 98 推到 106");

            player.HP = 50f;
            pickups.DropEnemyLoot(new Vector2(0.3f, 0f), 1);
            pickups.Tick(Dt);
            Assert.AreEqual(58f, player.HP, 1e-4f);
        }

        [Test]
        public void CapacityDropsTheOldestPickup()
        {
            PickupSystem pickups = Build(out _, out _, out _, heartRoll: 0.99f);

            const int Spawned = PickupSystem.Capacity + 10;
            for (int i = 0; i < Spawned; i++)
            {
                pickups.DropEnemyLoot(new Vector2(100f + i, 100f), 1);
            }

            Assert.AreEqual(PickupSystem.Capacity, pickups.Count);

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < pickups.Count; i++)
            {
                Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                minX = Mathf.Min(minX, snapshot.Position.x);
                maxX = Mathf.Max(maxX, snapshot.Position.x);
            }

            Assert.AreEqual(110f, minX, 1e-3f, "最旧的 10 个被顶掉");
            Assert.AreEqual(100f + Spawned - 1, maxX, 1e-3f);
        }

        [Test]
        public void EnemyDeathsDropExperienceThroughTheSimulation()
        {
            const float ArenaHalf = 67f;

            var bus = new EventBus();
            var player = new PlayerModel(1000000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            RunState run = TestContent.NewRun();
            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(AppRoot.DefaultWorldSeed),
                AppRoot.PlayerUnit, 64, bus, WeaponUpgradeRules.Default);
            weapons.AddWeapon(new WeaponDef
            {
                Id = "Bow", Name = "弓箭", Kind = WeaponKind.Ranged, BaseDamage = 100f, Cooldown = 0.1f,
                Range = 8f, Projectiles = 1, SpreadDeg = 0f, BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 30f, Pierce = 1, Params = new float[0]
            });

            var pickups = new PickupSystem(player, run, bus, new FixedRandom(0.99f));

            var kills = 0;
            bus.Subscribe<UnitDied>(_ => kills++);

            var simulation = new RunSimulation(player, new PlayerController(player, bus), enemies, weapons, bus,
                null, null, pickups);

            EnemyDef crawler = TestContent.Enemy("Crawler");
            const int Spawned = 5;
            for (int i = 0; i < Spawned; i++)
            {
                enemies.Spawn(crawler, new Vector2(7f, -1f + i * 0.5f));
            }

            var input = new InputSnapshot();
            for (int step = 0; step < 300; step++) simulation.Tick(Dt, ref input);

            Assert.AreEqual(Spawned, kills);

            int xpOnGround = 0;
            for (int i = 0; i < pickups.Count; i++)
            {
                Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                Assert.AreEqual(PickupKind.Experience, snapshot.Kind);
                xpOnGround += snapshot.Value;
            }

            Assert.AreEqual(Spawned, xpOnGround, "每只 Crawler 掉 1 点经验");
        }

        [Test]
        public void HeartDropsAboutConfiguredChance()
        {
            const int DropsPerRound = 300;
            const int Rounds = 10;
            const float Expected = 0.09f;

            int hearts = 0;
            const int TotalDrops = Rounds * DropsPerRound;

            for (int round = 0; round < Rounds; round++)
            {
                var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
                RunState run = TestContent.NewRun();
                var pickups = new PickupSystem(player, run, null,
                    new DeterministicRandom(0x9E3779B97F4A7C15ul + (ulong)round), Expected, 8f);

                for (int i = 0; i < DropsPerRound; i++)
                {
                    pickups.DropEnemyLoot(new Vector2(100f, 100f), 1);
                }

                for (int i = 0; i < pickups.Count; i++)
                {
                    Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                    if (snapshot.Kind == PickupKind.Heart) hearts++;
                }
            }

            float rate = (float)hearts / TotalDrops;
            TestContext.Progress.WriteLine("[掉落] " + TotalDrops + " 次掉落里 " + hearts + " 颗心 = " +
                                          (rate * 100f).ToString("0.00") + "%（目标 9%）");
            Assert.LessOrEqual(Mathf.Abs(rate - Expected), 0.03f);
        }

        [Test]
        public void CollectAllExperienceTakesEveryOrbButLeavesHearts()
        {
            PickupSystem pickups = Build(out _, out RunState run, out _, heartRoll: 0f);

            pickups.DropEnemyLoot(new Vector2(100f, 0f), 5);
            pickups.DropEnemyLoot(new Vector2(-50f, 0f), 3);
            Assert.AreEqual(4, pickups.Count, "两枚经验 + 两颗心");

            int collected = pickups.CollectAllExperience(out int totalExperience);

            Assert.AreEqual(2, collected);
            Assert.AreEqual(8, totalExperience);
            Assert.AreEqual(8, run.TotalXp, "全屏吸取要把经验直接结算进 RunState");
            Assert.AreEqual(2, pickups.Count, "心不能被全屏吸取带走，仍在地上");

            for (int i = 0; i < pickups.Count; i++)
            {
                Assert.IsTrue(pickups.TryGetActive(i, out PickupSystem.Snapshot snapshot));
                Assert.AreEqual(PickupKind.Heart, snapshot.Kind);
            }
        }

        [Test]
        public void ExperienceTierFollowsEnemyQuality()
        {
            Assert.AreEqual(PickupTier.Common, PickupSystem.TierOf(NewEnemy(TierDef(1))));
            Assert.AreEqual(PickupTier.Uncommon, PickupSystem.TierOf(NewEnemy(TierDef(2))));
            Assert.AreEqual(PickupTier.Rare, PickupSystem.TierOf(NewEnemy(TierDef(4))));
            Assert.AreEqual(PickupTier.Elite, PickupSystem.TierOf(NewEnemy(TierDef(3),
                new EnemySpawnOptions(1f, 1f, 1f, 1.35f, 4f, true, false))));
            Assert.AreEqual(PickupTier.Boss, PickupSystem.TierOf(NewEnemy(TierDef(9),
                new EnemySpawnOptions(1f, 1f, 1f, 1f, 1f, false, true))));
        }

        static EnemyDef TierDef(int tier)
        {
            return new EnemyDef
            {
                Id = "tier_" + tier, Name = "品阶靶", Behaviour = EnemyBehaviourType.Chase, Tier = tier,
                HP = 1f, Damage = 0f, Speed = 0f, AttackRange = 1f, AttackInterval = 1f,
                XpValue = tier, BodyRadius = 0.3f, Scale = 1f
            };
        }

        static EnemyModel NewEnemy(EnemyDef def, EnemySpawnOptions? options = null)
        {
            return new EnemyModel(def, Vector2.zero, new UnitId(100), options ?? EnemySpawnOptions.Default);
        }

        [Test]
        public void TickDoesNotAllocateInSteadyState()
        {
            PickupSystem pickups = Build(out _, out _, out _, heartRoll: 0.99f);

            const int Count = 200;
            for (int i = 0; i < Count; i++) pickups.DropEnemyLoot(new Vector2(100f, 100f + i), 1);

            Advance(pickups, 600);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Advance(pickups, 600);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(Count, pickups.Count);
            Assert.AreEqual(0L, allocated);
        }

        static PickupSystem Build(out PlayerModel player, out RunState run, out EventBus bus, float heartRoll,
            float heartHeal = 8f)
        {
            bus = new EventBus();
            player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            run = TestContent.NewRun();
            return new PickupSystem(player, run, bus, new FixedRandom(heartRoll), 0.09f, heartHeal);
        }

        static void Advance(PickupSystem pickups, int steps)
        {
            for (int i = 0; i < steps; i++) pickups.Tick(Dt);
        }

        static Vector2 PositionOf(PickupSystem pickups, int ordinal)
        {
            Assert.IsTrue(pickups.TryGetActive(ordinal, out PickupSystem.Snapshot snapshot));
            return snapshot.Position;
        }

        static bool Magnetized(PickupSystem pickups, int ordinal)
        {
            Assert.IsTrue(pickups.TryGetActive(ordinal, out PickupSystem.Snapshot snapshot));
            return snapshot.Magnetized;
        }

        sealed class FixedRandom : IRandom01
        {
            readonly float _value;
            public FixedRandom(float value) { _value = value; }
            public float Next01() => _value;
        }
    }
}
