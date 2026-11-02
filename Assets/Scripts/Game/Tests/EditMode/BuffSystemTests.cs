using Game.Config;
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
    /// buff 道具的触发逻辑：击杀叠攻速/回血、受击叠甲、低血加攻、周期回复、换局回滚。
    /// buff 只认事件与 StatBlock，测试直接发布事件，不用跑整局。
    /// </summary>
    public class BuffSystemTests
    {
        static PlayerModel Player(float hp = 100f)
        {
            return new PlayerModel(hp, 5f, 15f, 0.15f, 2f, 67f);
        }

        [Test]
        public void KillStacksAttackSpeedUpToMax()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var buffs = new BuffSystem(player, bus);

            var def = new BuffDef
            {
                Id = "Frenzy", Name = "疯狂", Effect = BuffEffectType.OnKillAttackSpeed, MaxStacks = 3,
                Params = new[] { 0.02f }
            };
            buffs.Acquire(def);

            for (int i = 0; i < 5; i++)
            {
                bus.Publish(new UnitDied(new UnitId(2 + i, UnitKind.Enemy),
                    new UnitId(1, UnitKind.Player), 0f, 0f));
            }
            bus.Flush();

            Assert.AreEqual(0.06f, player.Stats[StatType.AttackSpeed], 1e-6f, "最多 3 层 ×2%");
            Assert.AreEqual(3, buffs.Buffs[0].TriggerStacks);
        }

        [Test]
        public void CompanionKillsAlsoTriggerOnKillBuffs()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var buffs = new BuffSystem(player, bus);
            buffs.Acquire(new BuffDef
            {
                Id = "Frenzy", Name = "疯狂", Effect = BuffEffectType.OnKillAttackSpeed, MaxStacks = 3,
                Params = new[] { 0.02f }
            });

            bus.Publish(new UnitDied(new UnitId(2, UnitKind.Enemy), new UnitId(1000, UnitKind.Companion), 0f, 0f));
            bus.Flush();

            Assert.AreEqual(0.02f, player.Stats[StatType.AttackSpeed], 1e-6f,
                "旅伴击杀也是玩家的击杀，否则羁绊/召唤流吃不到击杀型 buff");
        }

        [Test]
        public void OnHurtStacksArmor()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var buffs = new BuffSystem(player, bus);
            buffs.Acquire(new BuffDef
            {
                Id = "Scar", Name = "疤痕", Effect = BuffEffectType.OnHurtArmor, MaxStacks = 2,
                Params = new[] { 2f }
            });

            bus.Publish(new PlayerDamaged(5f, 95f, 100f));
            bus.Publish(new PlayerDamaged(5f, 90f, 100f));
            bus.Publish(new PlayerDamaged(5f, 85f, 100f));
            bus.Flush();

            Assert.AreEqual(4f, player.Stats[StatType.Armor], 1e-6f, "最多 2 层 ×2 护甲");
        }

        [Test]
        public void LowHpAttackTurnsOnAndOff()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            player.HP = 20f;
            var buffs = new BuffSystem(player, bus);
            buffs.Acquire(new BuffDef
            {
                Id = "LastStand", Name = "背水一战", Effect = BuffEffectType.LowHpAttack, MaxStacks = 1,
                Params = new[] { 0.35f, 0.35f }
            });

            buffs.Tick(1f / 60f);
            Assert.AreEqual(0.35f, player.Stats[StatType.Attack], 1e-6f);

            player.HP = 100f;
            buffs.Tick(1f / 60f);
            Assert.AreEqual(0f, player.Stats[StatType.Attack], 1e-6f, "回到阈值以上要撤掉，不能永久保留");
        }

        [Test]
        public void PeriodicHealCapsAtMaxHp()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            player.HP = 99f;
            var buffs = new BuffSystem(player, bus);
            buffs.Acquire(new BuffDef
            {
                Id = "Regen", Name = "再生", Effect = BuffEffectType.PeriodicHeal, MaxStacks = 1,
                Interval = 1f, Params = new[] { 2f }
            });

            buffs.Tick(1.1f);
            Assert.AreEqual(100f, player.HP, 1e-6f, "回血不能超过上限");
        }

        [Test]
        public void KillExplosionDamagesNearbyEnemies()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, 67f, null, null, bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            buffs.Acquire(new BuffDef
            {
                Id = "Boom", Name = "爆炸", Effect = BuffEffectType.OnKillExplode, MaxStacks = 1,
                Params = new[] { 4f, 10f }
            });

            EnemyModel dying = Spawn(enemies, new Vector2(0f, 0f), 100f);
            EnemyModel nearby = Spawn(enemies, new Vector2(1f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);
            dying.HP = 0f;

            bus.Publish(new UnitDied(dying.Id, AppRoot.PlayerUnit, 0f, 0f, false, false));
            bus.Flush();

            Assert.Less(nearby.HP, 100f, "击杀爆炸应该打到附近的敌人");
        }

        [Test]
        public void LifestealHealsOnDamage()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            player.HP = 50f;
            var enemies = new EnemySystem(player, 67f, null, null, bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            buffs.Acquire(new BuffDef
            {
                Id = "Vamp", Name = "吸血", Effect = BuffEffectType.Lifesteal, MaxStacks = 1,
                Params = new[] { 0.2f }
            });

            EnemyModel enemy = Spawn(enemies, new Vector2(1f, 0f), 100f);
            bus.Publish(new UnitDamaged(enemy.Id, AppRoot.PlayerUnit, 20f, false, 80f));
            bus.Flush();

            Assert.AreEqual(54f, player.HP, 1e-4f, "20 伤害 ×20% = 回 4 血");
        }

        [Test]
        public void OrbitDamageHitsEnemiesAroundPlayer()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, 67f, null, null, bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            buffs.Acquire(new BuffDef
            {
                Id = "Orbit", Name = "环绕刀", Effect = BuffEffectType.OrbitDamage, MaxStacks = 1,
                Interval = 0.5f, Params = new[] { 3f, 8f }
            });

            EnemyModel near = Spawn(enemies, new Vector2(1f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);
            buffs.Tick(0.6f);

            Assert.Less(near.HP, 100f);
        }

        [Test]
        public void ThornsDamagesEnemiesWhenPlayerIsHurt()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, 67f, null, null, bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            buffs.Acquire(new BuffDef
            {
                Id = "Thorns", Name = "荆棘", Effect = BuffEffectType.OnHurtThorns, MaxStacks = 1,
                Params = new[] { 3f, 8f }
            });

            EnemyModel attacker = Spawn(enemies, new Vector2(1f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);
            bus.Publish(new PlayerDamaged(5f, 95f, 100f));
            bus.Flush();

            Assert.Less(attacker.HP, 100f);
        }

        [Test]
        public void HurtEventStacksArmorAndTriggersThornsInTheSamePass()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, 67f, null, null, bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);

            // 反伤排在叠甲前面：合并遍历后，同一次受击仍然要同时处理两者（顺序不影响结果）
            buffs.Acquire(new BuffDef
            {
                Id = "Thorns", Name = "荆棘", Effect = BuffEffectType.OnHurtThorns, MaxStacks = 1,
                Params = new[] { 3f, 8f }
            });
            buffs.Acquire(new BuffDef
            {
                Id = "Scar", Name = "疤痕", Effect = BuffEffectType.OnHurtArmor, MaxStacks = 2,
                Params = new[] { 2f }
            });

            EnemyModel near = Spawn(enemies, new Vector2(1f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);

            bus.Publish(new PlayerDamaged(5f, 95f, 100f));
            bus.Flush();

            Assert.Less(near.HP, 100f, "同一次受击要同时触发反伤");
            Assert.AreEqual(2f, player.Stats[StatType.Armor], 1e-6f, "同一次受击也要叠一层护甲");
        }

        [Test]
        public void LongStepStillTicksEveryWholeInterval()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            player.HP = 10f;
            var buffs = new BuffSystem(player, bus);
            buffs.Acquire(new BuffDef
            {
                Id = "Regen", Name = "再生", Effect = BuffEffectType.PeriodicHeal, MaxStacks = 1,
                Interval = 1f, Params = new[] { 2f }
            });

            // 2.5 秒要结算 2 次（不是一次，也不是把整周丢掉），余下的 0.5 秒留给下一步
            buffs.Tick(2.5f);
            Assert.AreEqual(14f, player.HP, 1e-4f, "2.5 秒 / 1 秒间隔 = 两次回血");

            buffs.Tick(0.5f);
            Assert.AreEqual(16f, player.HP, 1e-4f, "余下的 0.5 秒在下一步凑满一个间隔");
        }

        static EnemyModel Spawn(EnemySystem enemies, Vector2 position, float hp)
        {
            int slot = enemies.Spawn(new EnemyDef
            {
                Id = "dummy", Name = "测试靶", PrefabId = "Crawler", Behaviour = EnemyBehaviourType.Chase,
                Tier = 1, HP = hp, Damage = 0f, Speed = 0f, AttackRange = 1f, AttackInterval = 1f,
                XpValue = 0, BodyRadius = 0.4f, Scale = 1f
            }, position);
            return enemies.Get(slot);
        }

        [Test]
        public void LifestealStillHealsOnKillingBlowAfterCleanup()
        {
            // 真实顺序：伤害事件入队 -> 清理阶段移除尸体 -> 最后 Flush。
            // 目标已经不在 EnemySystem 里，Lifesteal 不能因此被吞掉。
            var bus = new EventBus();
            PlayerModel player = Player();
            player.HP = 50f;
            var enemies = new EnemySystem(player, 67f, null, new UnitIdAllocator(2), bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            buffs.Acquire(new BuffDef
            {
                Id = "Vamp", Name = "吸血", Effect = BuffEffectType.Lifesteal, MaxStacks = 1,
                Params = new[] { 0.2f }
            });

            EnemyModel dying = Spawn(enemies, new Vector2(1f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);

            var damage = new DamageInfo(1000f, DamageType.Physical, AppRoot.PlayerUnit, Vector2.right, 0f,
                false, DamageFlags.Dodgeable);
            WeaponDamage.ApplyAndPublish(bus, dying, damage, 1f);
            Assert.IsTrue(dying.IsDead, "这一下应当击杀目标");

            Assert.IsTrue(enemies.TryGetSlot(dying.Id, out int slot));
            enemies.Remove(slot);
            bus.Flush();

            Assert.AreEqual(70f, player.HP, 1e-4f, "1000 点致命伤害按 20% 吸血应回 20 血，而不是因目标已移除而跳过");
        }

        [Test]
        public void OnHitChainUsesEventPositionOnKillingBlow()
        {
            // 击杀一击的 OnHit 也应该用事件里的命中位置，而不是回头查已经被清理的目标。
            var bus = new EventBus();
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, 67f, null, new UnitIdAllocator(2), bus);
            var buffs = new BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            buffs.Acquire(new BuffDef
            {
                Id = "Chain", Name = "导电", Effect = BuffEffectType.OnHitChain, MaxStacks = 1,
                Interval = 0.4f, Params = new[] { 4.5f, 10f, 3f }
            });

            EnemyModel dying = Spawn(enemies, new Vector2(1f, 0f), 100f);
            EnemyModel nearby = Spawn(enemies, new Vector2(2f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);

            var damage = new DamageInfo(1000f, DamageType.Physical, AppRoot.PlayerUnit, Vector2.right, 0f,
                false, DamageFlags.Dodgeable);
            WeaponDamage.ApplyAndPublish(bus, dying, damage, 1f);
            Assert.IsTrue(enemies.TryGetSlot(dying.Id, out int slot));
            enemies.Remove(slot);
            bus.Flush();

            Assert.Less(nearby.HP, 100f, "击杀一击的链电应该以事件位置为中心命中附近敌人");
        }

        [Test]
        public void ResetRemovesAllModifiers()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var buffs = new BuffSystem(player, bus);
            buffs.Acquire(new BuffDef
            {
                Id = "Frenzy", Name = "疯狂", Effect = BuffEffectType.OnKillAttackSpeed, MaxStacks = 3,
                Params = new[] { 0.02f }
            });
            buffs.Acquire(new BuffDef
            {
                Id = "Glass", Name = "玻璃大炮", Effect = BuffEffectType.Stat,
                StatA = StatType.Attack, ValueA = 0.25f, OpA = StatOp.Flat,
                StatB = StatType.MaxHP, ValueB = -0.15f, OpB = StatOp.Percent
            });

            bus.Publish(new UnitDied(new UnitId(2, UnitKind.Enemy), new UnitId(1, UnitKind.Player), 0f, 0f));
            bus.Flush();
            Assert.Greater(player.Stats[StatType.AttackSpeed], 0f);

            buffs.ResetForNewRun();

            Assert.AreEqual(0f, player.Stats[StatType.AttackSpeed], 1e-6f);
            Assert.AreEqual(0f, player.Stats[StatType.Attack], 1e-6f);
            Assert.AreEqual(100f, player.MaxHP, 1e-6f);
            Assert.AreEqual(0, buffs.Count);
        }
    }
}
