using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class DamageResolverTests
    {
        const float Tolerance = 1e-4f;

        static DamageInfo Hit(float amount, bool isCrit = false, DamageFlags flags = DamageFlags.Dodgeable)
        {
            return new DamageInfo(amount, DamageType.Physical, new UnitId(7), new Vector2(1f, 0f), 0f, isCrit, flags);
        }

        static DamageTarget Target(float hp, float armor = 0f, float dodge = 0f, bool invulnerable = false)
        {
            return new DamageTarget(hp, armor, dodge, invulnerable);
        }

        [Test]
        public void DodgeSucceedsWhenRollBelowChance()
        {
            // 闪避 50%，骰到 0.30 → 闪开，一点血都不掉
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, dodge: 0.5f), 0.30f);

            Assert.IsTrue(result.Dodged);
            Assert.IsFalse(result.Blocked);
            Assert.IsFalse(result.Killed);
            Assert.AreEqual(0f, result.Applied, 0f);
            Assert.AreEqual(100f, result.RemainingHp, 0f);
        }

        [Test]
        public void DodgeFailsWhenRollAtOrAboveChance()
        {
            // 骰到 0.50 与闪避率相等算命中，否则 100% 闪避要靠浮点运气
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, dodge: 0.5f), 0.50f);

            Assert.IsFalse(result.Dodged);
            Assert.AreEqual(20f, result.Applied, Tolerance);
            Assert.AreEqual(80f, result.RemainingHp, Tolerance);
        }

        [Test]
        public void NonDodgeableHitIgnoresDodge()
        {
            // DoT / 环境伤害带 Flags.None，满闪避也照吃
            DamageResult result = DamageResolver.Resolve(Hit(20f, flags: DamageFlags.None), Target(100f, dodge: 1f), 0f);

            Assert.IsFalse(result.Dodged);
            Assert.AreEqual(20f, result.Applied, Tolerance);
        }

        [Test]
        public void ZeroArmorTakesFullDamage()
        {
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, armor: 0f), 1f);

            Assert.AreEqual(20f, result.Applied, Tolerance);
            Assert.AreEqual(80f, result.RemainingHp, Tolerance);
            Assert.IsFalse(result.Killed);
        }

        [Test]
        public void FiftyArmorHalvesDamage()
        {
            // 减伤 = 50 / (50 + 50) = 0.5，20 点伤害打进去只剩 10
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, armor: 50f), 1f);

            Assert.AreEqual(10f, result.Applied, Tolerance);
            Assert.AreEqual(90f, result.RemainingHp, Tolerance);
        }

        [Test]
        public void HundredFiftyArmorCutsThreeQuarters()
        {
            // 减伤 = 150 / (150 + 50) = 0.75，收益递减但堆越高越接近但不到 100%
            DamageResult result = DamageResolver.Resolve(Hit(40f), Target(100f, armor: 150f), 1f);

            Assert.AreEqual(10f, result.Applied, Tolerance);
            Assert.AreEqual(90f, result.RemainingHp, Tolerance);
        }

        [Test]
        public void NegativeArmorDoesNotAmplifyDamage()
        {
            // 负护甲按 0 处理，不能变成易伤
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, armor: -30f), 1f);

            Assert.AreEqual(20f, result.Applied, Tolerance);
        }

        [Test]
        public void ArmorBelowMinusFiftyIsStillZeroReduction()
        {
            // -60 / (-60 + 50) = 6，直接 clamp 到 [0,1] 会得到 1，变成满减伤。负护甲必须先归零
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, armor: -60f), 1f);

            Assert.AreEqual(20f, result.Applied, Tolerance);
        }

        [Test]
        public void CritAmountIsAppliedAsIs()
        {
            // 暴击在开火时已经乘好 critDmg（30 × 1.5 = 45）写进 Amount，Resolve 不再乘、不再掷骰
            DamageResult result = DamageResolver.Resolve(Hit(45f, isCrit: true), Target(100f), 1f);

            Assert.AreEqual(45f, result.Applied, Tolerance, "暴击不该在结算时被再乘一次");
            Assert.AreEqual(55f, result.RemainingHp, Tolerance);
        }

        [Test]
        public void InvulnerableTargetBlocksDamage()
        {
            // 冲刺 i-frame 期间挨打：Blocked，不掉血、不算闪避
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, invulnerable: true), 0f);

            Assert.IsTrue(result.Blocked);
            Assert.IsFalse(result.Dodged);
            Assert.IsFalse(result.Killed);
            Assert.AreEqual(0f, result.Applied, 0f);
            Assert.AreEqual(100f, result.RemainingHp, 0f);
        }

        [Test]
        public void DodgeIsCheckedBeforeInvulnerability()
        {
            // 顺序固定：两者同时成立时报告闪避，保证管线判定顺序可测
            DamageResult result = DamageResolver.Resolve(Hit(20f), Target(100f, dodge: 1f, invulnerable: true), 0f);

            Assert.IsTrue(result.Dodged);
            Assert.IsFalse(result.Blocked);
        }

        [Test]
        public void OverkillIsCappedAtRemainingHp()
        {
            // 100 伤害打在 30 血上：剩 0 血、判死，实际扣血按 30 记
            DamageResult result = DamageResolver.Resolve(Hit(100f), Target(30f), 1f);

            Assert.IsTrue(result.Killed);
            Assert.AreEqual(0f, result.RemainingHp, 0f, "血不能为负");
            Assert.AreEqual(30f, result.Applied, Tolerance);
        }

        [Test]
        public void DamageExactlyEqualHpKills()
        {
            DamageResult result = DamageResolver.Resolve(Hit(30f), Target(30f), 1f);

            Assert.IsTrue(result.Killed);
            Assert.AreEqual(0f, result.RemainingHp, 0f);
        }

        [Test]
        public void TrueDamageBypassesArmorButStillRespectsDodge()
        {
            var trueHit = new DamageInfo(20f, DamageType.True, new UnitId(7), Vector2.right, 0f, false,
                DamageFlags.None);
            DamageResult armored = DamageResolver.Resolve(trueHit, Target(100f, armor: 50f), 0f);
            Assert.AreEqual(20f, armored.Applied, Tolerance, "真实伤害不应被 50 护甲减半");

            var dodgeableTrueHit = new DamageInfo(20f, DamageType.True, new UnitId(7), Vector2.right, 0f, false,
                DamageFlags.Dodgeable);
            DamageResult dodged = DamageResolver.Resolve(dodgeableTrueHit, Target(100f, dodge: 0.5f), 0.1f);
            Assert.IsTrue(dodged.Dodged, "真实伤害绕护甲，但不应该绕闪避");
        }

        [Test]
        public void DamageServiceRoutesPlayerThroughTheSamePipeline()
        {
            var bus = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
            var events = new List<PlayerDamaged>();
            bus.Subscribe<PlayerDamaged>(events.Add);

            DamageResult result = DamageService.Apply(player,
                new DamageInfo(20f, DamageType.Physical, new UnitId(7, UnitKind.Enemy), Vector2.right, 0f,
                    false, DamageFlags.Dodgeable), 1f, bus);
            bus.Flush();

            Assert.AreEqual(80f, result.RemainingHp, Tolerance);
            Assert.AreEqual(80f, player.HP, Tolerance);
            Assert.AreEqual(1, events.Count, "玩家受伤仍然要发 PlayerDamaged，HUD/Buff 不需要改订阅");
            Assert.AreEqual(20f, events[0].Amount, Tolerance);
            Assert.AreEqual(new UnitId(7, UnitKind.Enemy), player.LastDamageSource);
        }

        [Test]
        public void DamageServiceRoutesCompanionAndPublishesUnitDamaged()
        {
            var bus = new EventBus();
            var events = new List<UnitDamaged>();
            bus.Subscribe<UnitDamaged>(events.Add);
            var companion = new CompanionModel(new CompanionDef
            {
                Id = "test_companion", Name = "测试旅伴", MaxHP = 40f, WeaponId = "Bow"
            }, new WeaponDef { Id = "Bow", Name = "弓", Kind = WeaponKind.Ranged }, 0,
                new UnitId(9, UnitKind.Companion), 6f);

            DamageResult result = DamageService.Apply(companion,
                new DamageInfo(15f, DamageType.Physical, UnitId.Player, Vector2.right, 0f, false,
                    DamageFlags.Dodgeable), 1f, bus);
            bus.Flush();

            Assert.AreEqual(25f, companion.HP, Tolerance);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(UnitKind.Companion, events[0].Target.Kind);
            Assert.AreEqual(companion.UnitId, events[0].Target);
            Assert.AreEqual(UnitId.Player, companion.LastDamageSource);
        }

        [Test]
        public void AlreadyDeadTargetIsNotKilledTwice()
        {
            // 已经 0 血再挨一刀：不重复判死，否则死亡事件会入队两次、掉落翻倍
            DamageResult result = DamageResolver.Resolve(Hit(50f), Target(0f), 1f);

            Assert.IsFalse(result.Killed);
            Assert.IsFalse(result.Dodged);
            Assert.IsFalse(result.Blocked);
            Assert.AreEqual(0f, result.Applied, 0f);
            Assert.AreEqual(0f, result.RemainingHp, 0f);
        }
    }
}
