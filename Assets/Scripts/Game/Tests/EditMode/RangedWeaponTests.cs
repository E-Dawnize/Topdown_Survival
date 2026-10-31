using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 弓箭（I1-7）：射程开关火、冷却、穿透、弹道数与伤害公式。
    /// 每个用例都照主循环的顺序走：先 Grid.Rebuild（敌人不动，只更新快照），再武器系统一步。
    /// 步长固定 1/60，和 RunLoop 的固定步长一致，冷却与挥砍窗口的秒数才对得上。
    /// </summary>
    public class RangedWeaponTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;
        const float BodyRadius = 0.3f;
        const float BowDamage = 8f;
        const float BowCooldown = 0.85f;

        /// <summary>不暴击、无加成：一发箭的伤害正好等于表里的 8，断言能直接对表。</summary>
        static readonly ShooterStats NoCrit = new ShooterStats(0f, 0f, 1.5f, 0f);

        static WeaponDef BowDef(int pierce = 1, int projectiles = 1, float spreadDeg = 0f, float range = 8f)
        {
            return new WeaponDef
            {
                Id = "Bow",
                Name = "弓箭",
                Kind = WeaponKind.Ranged,
                BaseDamage = BowDamage,
                Cooldown = BowCooldown,
                Range = range,
                Projectiles = projectiles,
                SpreadDeg = spreadDeg,
                BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 18f,
                Pierce = pierce,
                Params = new float[0]
            };
        }

        static EnemyDef DummyDef(float hp = 1000f)
        {
            return new EnemyDef
            {
                Id = "test_dummy",
                Name = "测试靶",
                Behaviour = EnemyBehaviourType.Chase,
                HP = hp,
                Damage = 0f,
                Speed = 0f,
                AttackRange = 1f,
                AttackInterval = 1f,
                CoinDrop = 0,
                BodyRadius = BodyRadius
            };
        }

        /// <summary>固定值随机桩：暴击与闪避都读它，测试要的是"永远这个结果"。</summary>
        sealed class FixedRandom : IRandom01
        {
            readonly float _value;

            public FixedRandom(float value)
            {
                _value = value;
            }

            public float Next01() => _value;
        }

        static EnemySystem NewEnemies()
        {
            return new EnemySystem(new PlayerModel(1000f, 5f, 15f, 0.15f, 2f, ArenaHalf), ArenaHalf);
        }

        static WeaponSystem NewWeapons(EnemySystem enemies, float randomValue = 0.99f)
        {
            return new WeaponSystem(enemies, new FixedRandom(randomValue), new UnitId(1));
        }

        static void Step(WeaponSystem weapons, EnemySystem enemies, in ShooterStats stats, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                enemies.Grid.Rebuild(Vector2.zero, enemies);
                weapons.Tick(Vector2.zero, Vector2.right, stats, Dt);
            }
        }

        [Test]
        public void EnemyAtSevenPointNineMetersIsShot()
        {
            // 射程 8 米：7.9 米在射程内，一 tick 就该出膛一发
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel bow = weapons.AddWeapon(BowDef());
            enemies.Spawn(DummyDef(), new Vector2(7.9f, 0f));

            Step(weapons, enemies, NoCrit, 1);

            Assert.AreEqual(1, bow.ShotsFired);
            Assert.AreEqual(1, weapons.Projectiles.Count, "开一枪应该只生成一发投射物");
        }

        [Test]
        public void EnemyAtEightPointOneMetersIsNotShot()
        {
            // 8.1 米在射程外：不开火，投射物一个都不该有
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel bow = weapons.AddWeapon(BowDef());
            enemies.Spawn(DummyDef(), new Vector2(8.1f, 0f));

            Step(weapons, enemies, NoCrit, 30);

            Assert.AreEqual(0, bow.ShotsFired);
            Assert.AreEqual(0, weapons.Projectiles.Count);
        }

        [Test]
        public void OutOfRangeTargetDoesNotConsumeCooldown()
        {
            // 射程外那半秒不能白吃冷却：目标一进射程，下一 tick 就得开火，不是"再等 0.85 秒"
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel bow = weapons.AddWeapon(BowDef());
            int slot = enemies.Spawn(DummyDef(), new Vector2(8.1f, 0f));

            Step(weapons, enemies, NoCrit, 30);
            Assert.AreEqual(0, bow.ShotsFired);

            enemies.Get(slot).Position = new Vector2(5f, 0f);
            Step(weapons, enemies, NoCrit, 1);

            Assert.AreEqual(1, bow.ShotsFired, "进射程后的第一 tick 就该开火");
            Assert.AreEqual(0.85f, bow.CooldownRemaining, 1e-4f, "冷却从开火那一刻开始算，不该被空枪提前吃掉");
        }

        [Test]
        public void CooldownAllowsOnlyOneShotPerInterval()
        {
            // 0.833 秒（50 步）仍在 0.85 秒冷却里 → 只有一枪；1 秒（60 步）跨过冷却 → 第二枪
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel bow = weapons.AddWeapon(BowDef());
            enemies.Spawn(DummyDef(), new Vector2(3f, 0f));

            Step(weapons, enemies, NoCrit, 50);
            Assert.AreEqual(1, bow.ShotsFired, "冷却内只许开一枪");

            Step(weapons, enemies, NoCrit, 10);
            Assert.AreEqual(2, bow.ShotsFired, "跨过 0.85 秒冷却后开第二枪，且只开一枪");
        }

        [Test]
        public void PierceOneHitsOnlyTheFirstEnemy()
        {
            // 三个靶排在一条线上，穿透 1：箭只结算最前面那个
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(BowDef(pierce: 1));
            int first = enemies.Spawn(DummyDef(), new Vector2(2f, 0f));
            int second = enemies.Spawn(DummyDef(), new Vector2(4f, 0f));
            int third = enemies.Spawn(DummyDef(), new Vector2(6f, 0f));

            // 40 步 = 0.67 秒，箭以 18 米/秒飞满 8 米只需要 0.44 秒
            Step(weapons, enemies, NoCrit, 40);

            Assert.AreEqual(1000f - BowDamage, enemies.Get(first).HP, 1e-3f);
            Assert.AreEqual(1000f, enemies.Get(second).HP, 1e-3f, "穿透 1 不该打到第二个");
            Assert.AreEqual(1000f, enemies.Get(third).HP, 1e-3f);
        }

        [Test]
        public void PierceTwoHitsFirstTwoEnemiesWithSameDamage()
        {
            // 穿透 2：前两个各掉一发箭的血，第三个不动。被穿透的后续目标拿同一份伤害
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(BowDef(pierce: 2));
            int first = enemies.Spawn(DummyDef(), new Vector2(2f, 0f));
            int second = enemies.Spawn(DummyDef(), new Vector2(4f, 0f));
            int third = enemies.Spawn(DummyDef(), new Vector2(6f, 0f));

            Step(weapons, enemies, NoCrit, 40);

            Assert.AreEqual(1000f - BowDamage, enemies.Get(first).HP, 1e-3f);
            Assert.AreEqual(1000f - BowDamage, enemies.Get(second).HP, 1e-3f, "穿透的第二个目标应该吃满一发箭的伤害");
            Assert.AreEqual(1000f, enemies.Get(third).HP, 1e-3f, "穿透 2 不该打到第三个");
        }

        [Test]
        public void ThreeProjectilesSpawnInParallelWhenSpreadIsZero()
        {
            // 弹道数 3、SpreadDeg 0：一枪三发，方向完全一致
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(BowDef(projectiles: 3, spreadDeg: 0f));
            enemies.Spawn(DummyDef(), new Vector2(5f, 0f));

            Step(weapons, enemies, NoCrit, 1);

            Assert.AreEqual(3, weapons.Projectiles.Count);

            for (int i = 0; i < weapons.Projectiles.Count; i++)
            {
                Assert.IsTrue(weapons.Projectiles.TryGetActive(i, out ProjectileSystem.Snapshot shot));
                Assert.AreEqual(1f, shot.Direction.x, 1e-4f);
                Assert.AreEqual(0f, shot.Direction.y, 1e-4f);
            }
        }

        [Test]
        public void SpreadFansProjectilesSymmetricallyAroundAim()
        {
            // SpreadDeg 是相邻弹道的夹角：3 发 10° 铺成 -10° / 0° / +10°
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(BowDef(projectiles: 3, spreadDeg: 10f));
            enemies.Spawn(DummyDef(), new Vector2(5f, 0f));

            Step(weapons, enemies, NoCrit, 1);

            Assert.AreEqual(3, weapons.Projectiles.Count);
            Assert.IsTrue(weapons.Projectiles.TryGetActive(0, out ProjectileSystem.Snapshot left));
            Assert.IsTrue(weapons.Projectiles.TryGetActive(1, out ProjectileSystem.Snapshot centre));
            Assert.IsTrue(weapons.Projectiles.TryGetActive(2, out ProjectileSystem.Snapshot right));

            Assert.AreEqual(Mathf.Sin(-10f * Mathf.Deg2Rad), left.Direction.y, 1e-4f, "第一发偏在瞄准方向一侧");
            Assert.AreEqual(0f, centre.Direction.y, 1e-4f, "中间那发正对瞄准方向");
            Assert.AreEqual(Mathf.Sin(10f * Mathf.Deg2Rad), right.Direction.y, 1e-4f, "最后一发偏在另一侧");
        }

        [Test]
        public void DamageAppliesAttackBonusAndCritMultiplier()
        {
            // 8 × (1 + 0.2) × 2.0(暴击) × 1(距离) = 19.2
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies, randomValue: 0f);   // 骰到 0：暴击率 100% 时必暴
            weapons.AddWeapon(BowDef());
            int slot = enemies.Spawn(DummyDef(), new Vector2(3f, 0f));

            Step(weapons, enemies, new ShooterStats(0.2f, 1f, 2f, 0f), 40);

            Assert.AreEqual(1000f - 19.2f, enemies.Get(slot).HP, 1e-3f);
        }

        [Test]
        public void DistanceFalloffKeepsFullDamageInsideRangeAndHalvesAtOneTwentyPercent()
        {
            // GDD 5.2：射程 100% 内不衰减，到 120% 线性降到 50%，再远保持 50%
            Assert.AreEqual(1f, WeaponDamage.DistanceFalloff(0f, 8f), 1e-5f);
            Assert.AreEqual(1f, WeaponDamage.DistanceFalloff(8f, 8f), 1e-5f, "射程边界上仍是满伤害");
            Assert.AreEqual(0.75f, WeaponDamage.DistanceFalloff(8.8f, 8f), 1e-5f, "8.8 米正好在两段之间");
            Assert.AreEqual(0.5f, WeaponDamage.DistanceFalloff(9.6f, 8f), 1e-5f, "120% 射程降到 50%");
            Assert.AreEqual(0.5f, WeaponDamage.DistanceFalloff(20f, 8f), 1e-5f, "更远不会掉到 50% 以下");
            Assert.AreEqual(0.5f, WeaponDamage.DistanceFalloff(3f, 0f), 1e-5f, "射程为 0 是坏数据，但不能算出 NaN");
        }
    }
}
