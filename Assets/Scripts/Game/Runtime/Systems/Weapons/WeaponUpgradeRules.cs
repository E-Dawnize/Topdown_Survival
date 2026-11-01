using System;
using Game.Config;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 单把武器的成长规则。数值从 Balance 表读，不藏在代码里：
    /// 每级伤害倍率、每级冷却乘数、几级加弹道、几级加穿透、等级上限。
    ///
    /// 这组规则同时被 WeaponModel（实际开火）与 BuildController（卡片文案、能不能再升）读，
    /// 两处共用一份，不会出现"卡上写 Lv5、实际只能升到 Lv3"。
    /// </summary>
    public sealed class WeaponUpgradeRules
    {
        const int DefaultMaxLevel = 5;
        const float DefaultDamagePerLevel = 0.30f;
        const float DefaultCooldownPerLevel = 0.92f;
        const int DefaultProjectileMilestone = 3;
        const int DefaultPierceMilestone = 4;

        public readonly int MaxLevel;
        public readonly float DamagePerLevel;
        public readonly float CooldownPerLevel;
        public readonly int ProjectileMilestone;
        public readonly int PierceMilestone;

        public WeaponUpgradeRules(int maxLevel, float damagePerLevel, float cooldownPerLevel,
            int projectileMilestone, int pierceMilestone)
        {
            MaxLevel = Math.Max(1, maxLevel);
            DamagePerLevel = Math.Max(0f, damagePerLevel);
            CooldownPerLevel = cooldownPerLevel <= 0f ? 1f : cooldownPerLevel;
            ProjectileMilestone = projectileMilestone;
            PierceMilestone = pierceMilestone;
        }

        public static WeaponUpgradeRules FromConfig(ConfigDatabase config)
        {
            return new WeaponUpgradeRules(
                config.GetBalanceInt("WeaponMaxLevel", DefaultMaxLevel),
                config.GetBalance("WeaponDamagePerLevel", DefaultDamagePerLevel),
                config.GetBalance("WeaponCooldownPerLevel", DefaultCooldownPerLevel),
                config.GetBalanceInt("WeaponProjectileMilestone", DefaultProjectileMilestone),
                config.GetBalanceInt("WeaponPierceMilestone", DefaultPierceMilestone));
        }

        /// <summary>测试与"配置缺失"时的兜底，数值与 Balance 表默认值一致。</summary>
        public static readonly WeaponUpgradeRules Default = new WeaponUpgradeRules(
            DefaultMaxLevel, DefaultDamagePerLevel, DefaultCooldownPerLevel,
            DefaultProjectileMilestone, DefaultPierceMilestone);

        public float DamageMultiplier(int level)
        {
            int steps = ClampLevel(level) - 1;
            return 1f + DamagePerLevel * steps;
        }

        public float CooldownMultiplier(int level)
        {
            int steps = ClampLevel(level) - 1;
            return (float)Math.Pow(CooldownPerLevel, steps);
        }

        public int BonusProjectiles(int level)
        {
            return ProjectileMilestone > 1 && level >= ProjectileMilestone ? 1 : 0;
        }

        public int BonusPierce(int level)
        {
            return PierceMilestone > 1 && level >= PierceMilestone ? 1 : 0;
        }

        int ClampLevel(int level)
        {
            if (level < 1) return 1;
            return level > MaxLevel ? MaxLevel : level;
        }
    }
}
