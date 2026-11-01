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
    /// 属性卡真正接进战斗的几条：射程、护甲、生命回复、碰撞伤害。
    /// 这些属性在 StatBlock 里加一条修正就该生效，不能只改面板数字。
    /// </summary>
    public class PlayerStatsIntegrationTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;

        [Test]
        public void RangeBonusExtendsEffectiveRange()
        {
            var stats = new ShooterStats(0f, 0f, 1.5f, 0f, 0, 0.12f);
            Assert.AreEqual(8.96f, stats.EffectiveRange(8f), 1e-4f);

            var negative = new ShooterStats(0f, 0f, 1.5f, 0f, 0, -0.99f);
            Assert.AreEqual(0.8f, negative.EffectiveRange(8f), 1e-4f, "极端负射程要留 10% 下限");
        }

        [Test]
        public void ArmorFromStatBlockReducesContactDamage()
        {
            var events = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            player.Stats.SetBase(StatType.Armor, 50f);

            var enemies = new EnemySystem(player, ArenaHalf, new DeterministicRandom(1ul), null, events);
            enemies.Spawn(new EnemyDef
            {
                Id = "crawler", Name = "爬虫", PrefabId = "Crawler", Behaviour = EnemyBehaviourType.Chase,
                Tier = 1, HP = 100f, Damage = 10f, Speed = 0f, AttackRange = 1f, AttackInterval = 1f,
                XpValue = 1, BodyRadius = 0.35f, Scale = 1f
            }, new Vector2(0.4f, 0f));
            enemies.Grid.Rebuild(Vector2.zero, enemies);

            // 近战现在有 0.32 秒起手预警，第 21 个固定步才真正结算。
            for (int i = 0; i < 21; i++) enemies.Tick(Vector2.zero, Dt);

            Assert.AreEqual(95f, player.HP, 1e-4f, "50 护甲把 10 点伤害减半，StatBlock 的 Armor 必须传进伤害管线");
        }

        [Test]
        public void RegenHealsOverSimulationSteps()
        {
            var events = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            player.HP = 50f;
            player.Stats.SetBase(StatType.Regen, 2f);

            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(2ul), AppRoot.PlayerUnit, 16, events);
            var simulation = new RunSimulation(player, new PlayerController(player, events), enemies, weapons, events);

            var input = new InputSnapshot();
            for (int i = 0; i < 60; i++) simulation.Tick(Dt, ref input);

            Assert.AreEqual(52f, player.HP, 1e-3f, "2 点/秒 × 1 秒 = 回 2 血");
        }

        [Test]
        public void ContactDamageHitsEnemiesCloseToPlayer()
        {
            var events = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            player.Stats.SetBase(StatType.ContactDamage, 20f);

            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(3ul), AppRoot.PlayerUnit, 16, events);
            var simulation = new RunSimulation(player, new PlayerController(player, events), enemies, weapons, events);

            EnemyDef tank = new EnemyDef
            {
                Id = "tank", Name = "靶", PrefabId = "Crawler", Behaviour = EnemyBehaviourType.Chase,
                Tier = 1, HP = 100f, Damage = 0f, Speed = 0f, AttackRange = 1f, AttackInterval = 1f,
                XpValue = 1, BodyRadius = 0.5f, Scale = 1f
            };
            int slot = enemies.Spawn(tank, new Vector2(0.5f, 0f));

            CombatAreaEffect? effect = null;
            events.Subscribe<CombatAreaEffect>(e => effect = e);

            var input = new InputSnapshot();
            simulation.Tick(Dt, ref input);

            Assert.Less(enemies.Get(slot).HP, 100f, "贴身敌人应当吃到碰撞伤害");
            Assert.Greater(player.ContactTimer, 0f, "结算后进入 0.5 秒冷却");
            Assert.IsTrue(effect.HasValue, "碰撞伤害也要发一条可见的范围表现事件");
            Assert.AreEqual(CombatAreaEffectKind.Contact, effect.Value.Kind);
            Assert.Greater(effect.Value.Radius, 0f);
        }
    }
}
