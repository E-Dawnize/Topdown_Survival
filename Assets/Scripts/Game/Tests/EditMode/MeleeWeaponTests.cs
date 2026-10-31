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
    /// 砍刀（I1-8）：扇形判定、伤害窗口、单次挥砍去重、攻速换 DPS。
    /// 武器表里 Params = [0.08, 0.22] 是伤害窗口的起止秒数，All 用例都按 1/60 固定步长走，
    /// 步数与窗口对得上：0.08 秒落到第 5 步，0.22 秒落到第 14 步。
    /// </summary>
    public class MeleeWeaponTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;
        const float BodyRadius = 0.3f;
        const float SwordDamage = 15f;
        const float SwordCooldown = 0.6f;
        const float SwordRange = 2.2f;

        /// <summary>表里 SpreadDeg = 45 是扇形半角，也就是 90° 扇角。</summary>
        const float SwordHalfAngle = 45f;

        static readonly ShooterStats NoCrit = new ShooterStats(0f, 0f, 1.5f, 0f);

        static WeaponDef SwordDef()
        {
            return new WeaponDef
            {
                Id = "Sword",
                Name = "砍刀",
                Kind = WeaponKind.Melee,
                BaseDamage = SwordDamage,
                Cooldown = SwordCooldown,
                Range = SwordRange,
                Projectiles = 1,
                SpreadDeg = SwordHalfAngle,
                BehaviourType = WeaponSystem.MeleeBehaviourType,
                ProjectileSpeed = 0f,
                Pierce = 0,
                Params = new[] { 0.08f, 0.22f }
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

        /// <summary>固定值随机桩：0.99 永远不会暴击（暴击率 0 时更是如此）。</summary>
        sealed class FixedRandom : IRandom01
        {
            readonly float _value;

            public FixedRandom(float value)
            {
                _value = value;
            }

            public float Next01() => _value;
        }

        static Vector2 AtAngle(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians) * distance, Mathf.Sin(radians) * distance);
        }

        static void Step(WeaponSystem weapons, EnemySystem enemies, in ShooterStats stats, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                enemies.Grid.Rebuild(Vector2.zero, enemies);
                weapons.Tick(Vector2.zero, Vector2.right, stats, Dt);
            }
        }

        static EnemySystem NewEnemies()
        {
            return new EnemySystem(new PlayerModel(1000f, 5f, 15f, 0.15f, 2f, ArenaHalf), ArenaHalf);
        }

        static WeaponSystem NewWeapons(EnemySystem enemies)
        {
            return new WeaponSystem(enemies, new FixedRandom(0.99f), new UnitId(1));
        }

        [Test]
        public void EnemyInsideFanTakesDamageAndOutsideFanDoesNot()
        {
            // 扇形 90°（朝向 ±45°）：30° 的那个该掉血，60° 的那个不该
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(SwordDef());
            int inside = enemies.Spawn(DummyDef(), AtAngle(30f, 1.5f));
            int outside = enemies.Spawn(DummyDef(), AtAngle(60f, 1.5f));

            // 0.3 秒足够走完 0.08 ~ 0.22 的伤害窗口
            Step(weapons, enemies, NoCrit, 18);

            Assert.AreEqual(1000f - SwordDamage, enemies.Get(inside).HP, 1e-3f, "角度内应该吃一次挥砍");
            Assert.AreEqual(1000f, enemies.Get(outside).HP, 1e-3f, "角度外一刀都不该中");
        }

        [Test]
        public void DamageOnlyLandsInsideTheSwingWindow()
        {
            // 起手那一 tick 不算伤害：判定绑的是挥砍窗口，不是"触发冷却的那一瞬间"
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel sword = weapons.AddWeapon(SwordDef());
            int slot = enemies.Spawn(DummyDef(), new Vector2(1f, 0f));

            Step(weapons, enemies, NoCrit, 1);
            Assert.AreEqual(1, sword.ShotsFired, "第一 tick 就该起手挥砍");
            Assert.AreEqual(1000f, enemies.Get(slot).HP, 1e-3f, "刚起手时伤害窗口还没开");

            // 再走 5 步 = 0.083 秒，刚进 0.08 秒窗口
            Step(weapons, enemies, NoCrit, 5);
            Assert.AreEqual(1000f - SwordDamage, enemies.Get(slot).HP, 1e-3f, "进入窗口后应该结算");
        }

        [Test]
        public void OneSwingHitsTheSameEnemyOnlyOnce()
        {
            // 同一次挥砍里敌人一直站在扇形内，0.08~0.22 的窗口有六七步，只能掉一次血
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(SwordDef());
            int slot = enemies.Spawn(DummyDef(), new Vector2(1f, 0f));

            Step(weapons, enemies, NoCrit, 30);   // 0.5 秒：一次挥砍走完，第二次还没到
            Assert.AreEqual(1000f - SwordDamage, enemies.Get(slot).HP, 1e-3f);

            Step(weapons, enemies, NoCrit, 3);    // 到 0.55 秒，仍在 0.6 秒冷却内
            Assert.AreEqual(1000f - SwordDamage, enemies.Get(slot).HP, 1e-3f, "冷却没到不该有第二刀");
        }

        [Test]
        public void NoSwingWhenTargetIsOutsideRadius()
        {
            // 2.5 米超出 2.2 米半径：不空挥，也不吃冷却
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel sword = weapons.AddWeapon(SwordDef());
            int slot = enemies.Spawn(DummyDef(), new Vector2(2.5f, 0f));

            Step(weapons, enemies, NoCrit, 60);

            Assert.AreEqual(0, sword.ShotsFired);
            Assert.AreEqual(1000f, enemies.Get(slot).HP, 1e-3f);
        }

        [Test]
        public void NoSwingWhenTargetIsBehindTheFan()
        {
            // 背后 180° 有敌人也不该空挥：扇形里没活目标就不出手
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            WeaponModel sword = weapons.AddWeapon(SwordDef());
            enemies.Spawn(DummyDef(), new Vector2(-1f, 0f));

            Step(weapons, enemies, NoCrit, 60);

            Assert.AreEqual(0, sword.ShotsFired);
        }

        [Test]
        public void DoublingAttackSpeedDoublesDamageOverTheSameDuration()
        {
            // 攻速加成 +100% → 间隔 0.6 / 2 = 0.3 秒，6 秒内出手次数翻倍，总伤害也应该翻倍。
            // 靶子血量给足，别让"打死了"干扰统计；只比伤害总量，不测耗时（Stopwatch 不在单测里用）。
            const int steps = 360;   // 6 秒

            float slow = TotalDamageOver(steps, 0f);
            float fast = TotalDamageOver(steps, 1f);

            Assert.Greater(slow, 0f, "基线应该真的打出了伤害");

            float ratio = fast / slow;
            Assert.IsTrue(ratio > 1.8f && ratio < 2.2f,
                "攻速翻倍后同样时长的总伤害应在 2 倍的 ±10% 内，实际 " + ratio + "（慢 " + slow + " / 快 " + fast + "）");
        }

        static float TotalDamageOver(int steps, float attackSpeed)
        {
            EnemySystem enemies = NewEnemies();
            WeaponSystem weapons = NewWeapons(enemies);
            weapons.AddWeapon(SwordDef());
            int slot = enemies.Spawn(DummyDef(100000f), new Vector2(1f, 0f));

            Step(weapons, enemies, new ShooterStats(0f, 0f, 1.5f, attackSpeed), steps);

            return 100000f - enemies.Get(slot).HP;
        }
    }
}
