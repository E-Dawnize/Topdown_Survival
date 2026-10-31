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
    /// 接入主循环之后的首次联调：玩家站着不动，50 个敌人围过来，走 600 个固定步长。
    /// 走的是 RunSimulation.Tick，也就是 RunLoop 每步跑的那同一段顺序代码，
    /// 所以这里绿了说明"玩家先动 → 敌人动 → 重建格子 → 武器结算 → 清理 → 分发事件"这条链是通的。
    /// </summary>
    public class RunSimulationTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;
        const int EnemyCount = 50;
        const int Steps = 600;   // 10 秒

        /// <summary>刷怪环 8~15 米，与 WaveController 用的是同一组半径。</summary>
        const float SpawnRadiusMin = WaveController.SpawnRadiusMin;
        const float SpawnRadiusMax = WaveController.SpawnRadiusMax;

        static readonly ulong WorldSeed = AppRoot.DefaultWorldSeed;

        /// <summary>照抄 Enemy.json 的三个怪种与 Weapon.json 的 Bow——测试不能读 StreamingAssets。</summary>
        static EnemyDef[] EnemyDefs()
        {
            return new[]
            {
                new EnemyDef
                {
                    Id = "Crawler", Name = "爬虫", Behaviour = EnemyBehaviourType.Chase,
                    HP = 12f, Damage = 6f, Speed = 3f, AttackRange = 0.6f, AttackInterval = 1f,
                    CoinDrop = 1, BodyRadius = 0.35f
                },
                new EnemyDef
                {
                    Id = "Charger", Name = "冲锋兵", Behaviour = EnemyBehaviourType.Charge,
                    HP = 18f, Damage = 10f, Speed = 2.5f, AttackRange = 4f, AttackInterval = 3f,
                    CoinDrop = 2, BodyRadius = 0.4f
                },
                new EnemyDef
                {
                    Id = "Thrower", Name = "投手", Behaviour = EnemyBehaviourType.Ranged,
                    HP = 15f, Damage = 8f, Speed = 2f, AttackRange = 5f, AttackInterval = 2.5f,
                    CoinDrop = 2, BodyRadius = 0.35f
                }
            };
        }

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

        /// <summary>整套装配：玩家、控制器、敌人系统、弓箭、一步模拟。返回时敌人已经刷好。</summary>
        static RunSimulation Build(out PlayerModel player, out EnemySystem enemies, out WeaponSystem weapons,
            out int[] spawnSlots)
        {
            var events = new EventBus();

            player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var controller = new PlayerController(player, events);

            enemies = new EnemySystem(player, ArenaHalf);
            weapons = new WeaponSystem(enemies, new DeterministicRandom(WorldSeed), AppRoot.PlayerUnit, 256, events);
            weapons.AddWeapon(BowDef());

            var simulation = new RunSimulation(player, controller, enemies, weapons, events);

            EnemyDef[] defs = EnemyDefs();
            var random = new DeterministicRandom(WorldSeed);
            var slots = new List<int>(EnemyCount);
            for (int i = 0; i < EnemyCount; i++)
            {
                float angle = random.Range(0f, Mathf.PI * 2f);
                float radius = random.Range(SpawnRadiusMin, SpawnRadiusMax);
                var offset = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                slots.Add(enemies.Spawn(defs[random.NextInt(defs.Length)], player.Position + offset));
            }

            spawnSlots = slots.ToArray();
            return simulation;
        }

        static void Run(RunSimulation simulation, int steps)
        {
            var input = new InputSnapshot();   // 全零：站着不动，不冲刺
            for (int i = 0; i < steps; i++)
            {
                simulation.Tick(Dt, ref input);
            }
        }

        [Test]
        public void StandingPlayerShootsArrowsAndTakesDamage()
        {
            RunSimulation simulation = Build(out PlayerModel player, out EnemySystem enemies, out WeaponSystem weapons,
                out int[] spawnSlots);
            float startHp = player.HP;

            Run(simulation, Steps);

            Assert.Greater(weapons.Weapons[0].ShotsFired, 0, "玩家站着也该自动射箭：射程内有目标就该开火");
            Assert.Less(player.HP, startHp, "50 个敌人围上来，玩家应该掉血");

            // 掉血的敌人可能已经被打死并从槽位里摘掉了，所以两种证据都算：
            // 活着但血少了，或者已经死了（Removed = 场上不再有 50 个）
            bool anyDamaged = false;
            for (int slot = 0; slot < enemies.SlotCapacity; slot++)
            {
                EnemyModel enemy = enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive) continue;
                if (enemy.HP < enemy.Def.HP) anyDamaged = true;
            }

            int killed = spawnSlots.Length - enemies.AliveCount;
            Assert.IsTrue(anyDamaged || killed > 0,
                "10 秒里至少该有一个敌人被箭打到：场上剩 " + enemies.AliveCount + " 个，一个都没掉血");
        }

        [Test]
        public void EnemiesStayInsideArenaWhileChasing()
        {
            RunSimulation simulation = Build(out _, out EnemySystem enemies, out _, out _);

            Run(simulation, Steps);

            for (int slot = 0; slot < enemies.SlotCapacity; slot++)
            {
                EnemyModel enemy = enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive) continue;

                Assert.LessOrEqual(Mathf.Abs(enemy.Position.x), ArenaHalf + 1e-3f,
                    "槽位 " + slot + " 的敌人从 x 方向跑出了场地");
                Assert.LessOrEqual(Mathf.Abs(enemy.Position.y), ArenaHalf + 1e-3f,
                    "槽位 " + slot + " 的敌人从 z 方向跑出了场地");
            }
        }

        [Test]
        public void EnemyRecordsPrevPositionForViewInterpolation()
        {
            // 渲染层靠 PrevPosition → Position 插值，模拟必须每步在移动前记一笔，
            // 否则视图只能读最终位置，固定步长的阶梯会直接写进 Transform
            RunSimulation simulation = Build(out _, out EnemySystem enemies, out _, out _);

            var input = new InputSnapshot();
            simulation.Tick(Dt, ref input);

            bool anyMoved = false;
            for (int slot = 0; slot < enemies.SlotCapacity; slot++)
            {
                EnemyModel enemy = enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive) continue;

                if ((enemy.Position - enemy.PrevPosition).sqrMagnitude > 1e-10f) anyMoved = true;
            }

            Assert.IsTrue(anyMoved, "第一步就该有敌人动了，PrevPosition 与 Position 不能总相等");
        }

        [Test]
        public void PlayerDeathIsPublishedOnceAndStopsSimulation()
        {
            var events = new EventBus();
            var player = new PlayerModel(10f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(WorldSeed), AppRoot.PlayerUnit, 16, events);
            var simulation = new RunSimulation(player, new PlayerController(player, events), enemies, weapons, events);

            // 贴脸的高伤害怪：攻击间隔 0.2 秒、伤害 50，第一步就能把 10 血打空
            var killer = new EnemyDef
            {
                Id = "test_killer", Name = "测试杀手", Behaviour = EnemyBehaviourType.Chase,
                HP = 100f, Damage = 50f, Speed = 3f, AttackRange = 1f, AttackInterval = 0.2f,
                CoinDrop = 0, BodyRadius = 0.35f
            };
            enemies.Spawn(killer, new Vector2(0.5f, 0f));

            int died = 0;
            events.Subscribe<PlayerDied>(_ => died++);

            var input = new InputSnapshot { Move = new Vector2(1f, 0f) };
            for (int i = 0; i < 120; i++)
            {
                simulation.Tick(Dt, ref input);
            }

            Assert.IsTrue(player.IsDead, "10 血挨 50 点接触伤害，第一步就该死");
            Assert.AreEqual(1, died, "PlayerDied 只能发一次");

            Vector2 deathPosition = player.Position;
            var stillPressing = new InputSnapshot { Move = new Vector2(1f, 0f) };
            for (int i = 0; i < 60; i++)
            {
                simulation.Tick(Dt, ref stillPressing);
            }

            Assert.AreEqual(deathPosition.x, player.Position.x, 1e-6f, "死后模拟不再推进，玩家不该再被输入挪动");
            Assert.AreEqual(deathPosition.y, player.Position.y, 1e-6f, "死后模拟不再推进，玩家不该再被输入挪动");
            Assert.AreEqual(1, died, "死了以后再走 60 步也不该重发死亡事件");
        }

        [Test]
        public void KillingBlowRemovesEnemyFromSystem()
        {
            // 打死以后由清理阶段 Remove：槽位被释放、存活数下降，不会在场上留下 200 个空对象
            var events = new EventBus();
            var player = new PlayerModel(1000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(WorldSeed), AppRoot.PlayerUnit, 256, events);
            weapons.AddWeapon(BowDef());

            var simulation = new RunSimulation(player, new PlayerController(player, events), enemies, weapons, events);

            // 12 血的小怪放在 3 米处：8 点伤害一箭不死，两箭（1.7 秒）必死
            EnemyDef crawler = EnemyDefs()[0];
            int slot = enemies.Spawn(crawler, new Vector2(3f, 0f));

            Run(simulation, Steps);

            Assert.IsNull(enemies.Get(slot), "打死的敌人必须从槽位里摘掉");
            Assert.AreEqual(0, enemies.AliveCount);
        }
        [Test]
        public void CompanionArrowsKillInTheSameStepAndTheKillBelongsToTheCompanion()
        {
            // 旅伴接进主循环之后的最小闭环：跟随与开火确实被 RunSimulation 推着走，
            // 而且击杀归属经过清理阶段的 UnitDied 后仍然记在旅伴头上（不是玩家）。
            var events = new EventBus();
            var player = new PlayerModel(1000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var enemies = new EnemySystem(player, ArenaHalf);

            // 玩家不给武器：这一发只可能是旅伴打的，击杀归属没有第二种解释
            var weapons = new WeaponSystem(enemies, new DeterministicRandom(WorldSeed), AppRoot.PlayerUnit, 256,
                events);

            var weaponTable = new Dictionary<string, WeaponDef> { ["Bow"] = BowDef() };
            var companions = new CompanionSystem(player, enemies, weaponTable, new DeterministicRandom(WorldSeed),
                events);
            CompanionModel companion = companions.Recruit(new CompanionDef
            {
                Id = "IronBox", Name = "铁盒", Faction = CompanionFaction.Machine, WeaponId = "Bow",
                FollowRadius = 1.6f, MaxHP = 40f
            });

            var simulation = new RunSimulation(player, new PlayerController(player, events), enemies, weapons, events,
                null, null, null, companions);

            // 16 血、不动的靶子放在射程内：弓一发 8 点，两发必死（掘到暴击则更快，结论一样）
            int slot = enemies.Spawn(new EnemyDef
            {
                Id = "dummy", Name = "测试靶", Behaviour = EnemyBehaviourType.Chase,
                HP = 16f, Damage = 0f, Speed = 0f, AttackRange = 1f, AttackInterval = 1f,
                CoinDrop = 0, BodyRadius = 0.3f
            }, new Vector2(4f, 0f));

            var died = new List<UnitDied>();
            events.Subscribe<UnitDied>(died.Add);

            Run(simulation, Steps);

            Assert.AreEqual(1, died.Count, "16 血的靶子应当被旅伴打死一只");
            Assert.AreEqual(EnemySystem.UnitIdOf(slot), died[0].Unit);
            Assert.AreEqual(companion.UnitId, died[0].Killer, "击杀应当归旅伴，不能算成玩家");
            Assert.AreNotEqual(AppRoot.PlayerUnit, died[0].Killer, "玩家根本没开火，不该出现在击杀归属里");
            Assert.IsNull(enemies.Get(slot), "清理阶段把尸体搞掉，与玩家击杀走同一条路径");
        }
    }
}
