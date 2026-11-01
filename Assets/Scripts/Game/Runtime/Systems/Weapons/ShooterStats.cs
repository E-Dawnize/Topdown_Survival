using Game.Config;
using Game.Model;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 开火者的加成快照，由调用方从 StatBlock 读好传进来。
    /// 武器系统只认这份只读结构，不持有单位引用——拿不到 MonoBehaviour、也拿不到全局单例，
    /// 所以武器逻辑能在 EditMode 里直接跑。
    /// </summary>
    public readonly struct ShooterStats
    {
        /// <summary>攻击力加成，0.2 表示 +20%，对应 GDD 5.2 的"1 + 攻击力加成"乘区。</summary>
        public readonly float AttackBonus;

        /// <summary>暴击率，0..1。StatBlock 已经把 CritChance 夹在 [0,1]，这里不再夹。</summary>
        public readonly float CritChance;

        /// <summary>暴击倍率，1.5 表示 150%。传 0 会打出 0 伤害，这是调用方的错，不做静默兜底。</summary>
        public readonly float CritDamage;

        /// <summary>攻速加成，0.5 表示 +50%，间隔 = 基础间隔 / (1 + 攻速加成)。</summary>
        public readonly float AttackSpeed;

        /// <summary>
        /// 在武器表给的弹道数上额外增加的几发（GDD 5.1 的"弹道数"，机械 4 件羁绊 +1）。
        /// 武器表里的 Projectiles 仍然是自己的基础发数，这里只加不乘：
        /// "单武器独立加成"的意思是武器自己的多弹道不会被全局属性覆盖掉。
        /// </summary>
        public readonly int BonusProjectiles;

        /// <summary>射程加成，0.12 表示 +12%。武器行为用 基础射程 ×(1+它)，不直接改 WeaponDef。</summary>
        public readonly float RangeBonus;

        public ShooterStats(float attackBonus, float critChance, float critDamage, float attackSpeed,
            int bonusProjectiles = 0, float rangeBonus = 0f)
        {
            AttackBonus = attackBonus;
            CritChance = critChance;
            CritDamage = critDamage;
            AttackSpeed = attackSpeed;
            BonusProjectiles = bonusProjectiles;
            RangeBonus = rangeBonus;
        }

        public float EffectiveRange(float baseRange)
        {
            float multiplier = 1f + RangeBonus;
            return multiplier < 0.1f ? baseRange * 0.1f : baseRange * multiplier;
        }

        /// <summary>
        /// 从一份属性表现读加成。玩家与旅伴的调用方都靠它构造，避免两处手写同样的六行读取：
        /// 以后属性表加一列“对武器有影响”的项，只需改这里（武器系统只认这份只读快照）。
        /// 弹道数是整数属性，与调用方原来的 Mathf.RoundToInt 一致（向下取整会让 0.9 的白送消失）。
        /// </summary>
        public static ShooterStats From(StatBlock stats)
        {
            if (stats == null) throw new System.ArgumentNullException(nameof(stats));

            return new ShooterStats(
                stats[StatType.Attack],
                stats[StatType.CritChance],
                stats[StatType.CritDamage],
                stats[StatType.AttackSpeed],
                Mathf.RoundToInt(stats[StatType.ProjectileCount]),
                stats[StatType.Range]);
        }
    }
}
