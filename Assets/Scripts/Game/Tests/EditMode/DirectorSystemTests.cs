using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 刷怪导演：时间轴切段、同屏上限、精英倍率、BOSS 出场/召唤/狂暴。
    /// </summary>
    public class DirectorSystemTests
    {
        const float ArenaHalf = 67f;

        static (DirectorSystem Director, EnemySystem Enemies, EventBus Bus) Build(
            IReadOnlyList<DirectorDef> directors = null)
        {
            var config = TestContent.Config();
            var bus = new EventBus();
            var player = new PlayerModel(100000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var enemies = new EnemySystem(player, ArenaHalf, new DeterministicRandom(TestRun.Seed), null, bus);
            var director = new DirectorSystem(enemies, config.Enemies,
                directors ?? config.Directors, new DeterministicRandom(TestRun.Seed), config, bus);
            director.Begin();
            return (director, enemies, bus);
        }

        [Test]
        public void TimelineMovesThreatAndSpawnsWithinCap()
        {
            var (director, enemies, _) = Build();

            Assert.AreEqual(1, director.CurrentThreat);

            for (int i = 0; i < 60 * 4; i++) director.Tick(1f / 60f, Vector2.zero);
            Assert.AreEqual(1, director.CurrentThreat);
            Assert.LessOrEqual(enemies.AliveCount, 20);

            for (int i = 0; i < 60 * 2; i++) director.Tick(1f / 60f, Vector2.zero);
            Assert.AreEqual(2, director.CurrentThreat);
            Assert.GreaterOrEqual(director.HighestTier, 2);
            Assert.LessOrEqual(enemies.AliveCount, 30);
        }

        [Test]
        public void EliteChanceScalesHpDamageXpAndSize()
        {
            var config = TestContent.Config();
            var custom = new List<DirectorDef>
            {
                new DirectorDef
                {
                    Id = "Only", StartTime = 0, EndTime = 2, Threat = 1, SpawnInterval = 1,
                    SpawnCountMin = 1, SpawnCountMax = 1, EnemyPool = new[] { "Crawler" },
                    EliteChance = 1f, ConcurrentCap = 5, HpScale = 1, DamageScale = 1, SpeedScale = 1,
                    BossId = ""
                }
            };

            var (director, enemies, _) = Build(custom);
            director.Tick(0.1f, Vector2.zero);   // 0 秒刷第一批

            EnemyModel spawned = null;
            for (int slot = 0; slot < enemies.SlotCapacity; slot++)
            {
                if (enemies.Get(slot) != null) spawned = enemies.Get(slot);
            }

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsElite);
            Assert.AreEqual(12f * 3.2f, spawned.MaxHP, 1e-4f);
            Assert.AreEqual(6f * 1.5f, spawned.ContactDamage, 1e-4f);
            Assert.AreEqual(1 * 4, spawned.XpValue);
            Assert.Greater(spawned.VisualScale, 1f);
        }

        [Test]
        public void BossSpawnsWithFinalPhaseScalingAndCallsAdds()
        {
            var (director, enemies, bus) = Build();
            var enraged = new List<BossEnraged>();
            bus.Subscribe<BossEnraged>(enraged.Add);

            Assert.IsTrue(director.BeginBoss(Vector2.zero));
            Assert.IsTrue(director.BossActive);
            Assert.AreEqual("缝线巨偶", director.BossName);
            Assert.AreEqual(100f * 1.5f, director.BossMaxHp, 1e-4f);
            Assert.IsTrue(enemies.TryGetSlot(director.BossUnitId, out int bossSlot));

            EnemyModel boss = enemies.Get(bossSlot);
            Assert.IsTrue(boss.IsBoss);
            Assert.AreEqual(2.5f, boss.VisualScale, 1e-4f);

            // 2 秒后召唤一批小怪
            int before = enemies.AliveCount;
            for (int i = 0; i < 60 * 2 + 2; i++) director.Tick(1f / 60f, Vector2.zero);
            Assert.Greater(enemies.AliveCount, before);

            // 半血狂暴：只触发一次，伤害/移速提高
            float speed = boss.Speed;
            float damage = boss.ContactDamage;
            boss.HP = boss.MaxHP * 0.49f;
            director.Tick(1f / 60f, Vector2.zero);
            bus.Flush();

            Assert.AreEqual(1, enraged.Count);
            Assert.Greater(boss.Speed, speed);
            Assert.Greater(boss.ContactDamage, damage);

            director.Tick(1f / 60f, Vector2.zero);
            bus.Flush();
            Assert.AreEqual(1, enraged.Count, "狂暴只能触发一次");
        }

        [Test]
        public void BossPropertiesAllReadTheSameBossFightState()
        {
            var (director, _, _) = Build();

            Assert.IsTrue(director.BeginBoss(Vector2.zero));
            BossFight boss = director.BossFight;

            Assert.IsTrue(boss.Active);
            Assert.AreEqual(boss.Active, director.BossActive);
            Assert.AreEqual(boss.Name, director.BossName);
            Assert.AreEqual(boss.UnitId, director.BossUnitId);
            Assert.AreEqual(boss.MaxHp, director.BossMaxHp, 1e-4f);
            Assert.AreEqual(boss.HpRatio, director.BossHpRatio, 1e-6f);
            Assert.AreEqual(boss.FightSeconds, director.BossFightSeconds, 1e-6f);

            director.NotifyBossDefeated();

            Assert.IsTrue(boss.Defeated);
            Assert.AreEqual(boss.Defeated, director.BossDefeated);
            Assert.AreEqual(boss.Active, director.BossActive);
        }

        [Test]
        public void TimelineAlgorithmUsesNarrowEliteScalingSettings()
        {
            var config = TestContent.Config();
            var custom = new List<DirectorDef>
            {
                new DirectorDef
                {
                    Id = "Only", StartTime = 0, EndTime = 2, Threat = 1, SpawnInterval = 1,
                    SpawnCountMin = 1, SpawnCountMax = 1, EnemyPool = new[] { "Crawler" },
                    EliteChance = 1f, ConcurrentCap = 5, HpScale = 1, DamageScale = 1, SpeedScale = 1,
                    BossId = ""
                }
            };
            var settings = new DirectorSettings(custom, new EliteScaling(7f, 2f, 1.5f, 9f, 2f));

            var bus = new EventBus();
            var player = new PlayerModel(100000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var enemies = new EnemySystem(player, ArenaHalf, new DeterministicRandom(TestRun.Seed), null, bus);
            DirectorSystem director = new DirectorSystem(enemies, config.Enemies, settings,
                new DeterministicRandom(TestRun.Seed), bus);
            director.Begin();
            director.Tick(0.1f, Vector2.zero);

            EnemyModel spawned = null;
            for (int slot = 0; slot < enemies.SlotCapacity; slot++)
            {
                if (enemies.Get(slot) != null) spawned = enemies.Get(slot);
            }

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsElite);
            Assert.AreEqual(12f * 7f, spawned.MaxHP, 1e-4f, "精英血量只读 EliteScaling.Hp");
            Assert.AreEqual(6f * 2f, spawned.ContactDamage, 1e-4f, "精英伤害只读 EliteScaling.Damage");
            Assert.AreEqual(1 * 9, spawned.XpValue, "精英经验只读 EliteScaling.Xp");
            Assert.AreEqual(2f, spawned.VisualScale, 1e-4f, "精英体型只读 EliteScaling.VisualScale");
        }
    }
}
