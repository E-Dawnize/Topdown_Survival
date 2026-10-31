using Game.Config;
using Game.Model;

namespace Game.Systems
{
    /// <summary>一条当前生效的阵营羁绊。Tier 1 = 2 件效果，Tier 2 = 4 件效果（GDD 7.2）。</summary>
    public readonly struct ActiveBond
    {
        public readonly CompanionFaction Faction;

        /// <summary>队伍里这个阵营的旅伴数量。重复购买升的是等级，不增加数量。</summary>
        public readonly int Count;

        /// <summary>0 = 没触发，1 = 2 件，2 = 4 件。</summary>
        public readonly int Tier;

        public ActiveBond(CompanionFaction faction, int count, int tier)
        {
            Faction = faction;
            Count = count;
            Tier = tier;
        }

        public bool IsActive => Tier > 0;
    }

    /// <summary>
    /// GDD 7.2 的羁绊数值表。这里只放"加成是什么"，什么时候生效、加在谁身上、怎么回滚归 CompanionSystem。
    ///
    /// 全部加成走 StatBlock.Add，Source 由调用方给（一条羁绊一个源对象）：
    /// 数量掉到阈值以下时 CompanionSystem 用 RemoveBySource 一次退干净，不会留下半条加成。
    ///
    /// 两种 ModOp 的选择不是随手写的：
    ///   - 攻击力、攻速、弹道数的 StatBlock 基线是 0，它们的值本身就是"加成比例/额外发数"，
    ///     用 Percent 会乘在 0 上变成 0，所以按百分点平加（Flat）；
    ///   - 生命上限、移速有非零基线，"全队 +12%"要的是 ×1.12，所以用 Percent。
    ///
    /// 4 件效果的两条附加项没有搬进来：
    ///   - 幽灵 4 件的"攻击穿透 +1 目标"：属性表（GDD 5.1）里没有穿透这一项，加它要新开一个属性并改武器管线；
    ///   - 玩偶 4 件的"每 15 秒 1 层护盾"：护盾是一整套吸收机制（伤害管线 + 计时 + 上限），不是一条属性。
    /// 这两条留给 I3-2（旅伴差异化 + 4 阵营）；本阶段能通过 StatBlock 表达的加成一条不少。
    /// </summary>
    public static class CompanionBonds
    {
        /// <summary>2 件效果的触发数量。GDD 7.2 只有 2 件与 4 件两档。</summary>
        public const int FirstTierCount = 2;

        /// <summary>4 件效果的触发数量，也是 GDD 7 的"一局最多同时拥有 4 个"。</summary>
        public const int SecondTierCount = 4;

        public static int TierOf(int count)
        {
            if (count >= SecondTierCount) return 2;
            if (count >= FirstTierCount) return 1;
            return 0;
        }

        /// <summary>
        /// 把某个阵营某一档的加成写进一份属性表。tier 2 是替换而不是叠加：
        /// GDD 4 件写的是"全队攻速 +25%"，不是"+10% 再 +25%"。
        /// </summary>
        public static void Apply(StatBlock stats, CompanionFaction faction, int tier, object source)
        {
            if (stats == null || tier <= 0) return;

            bool second = tier >= 2;

            switch (faction)
            {
                case CompanionFaction.Machine:
                    stats.Add(new StatModifier(StatType.AttackSpeed, second ? 0.25f : 0.10f, ModOp.Flat, source));
                    if (second) stats.Add(new StatModifier(StatType.ProjectileCount, 1f, ModOp.Flat, source));
                    break;

                case CompanionFaction.Ghost:
                    stats.Add(new StatModifier(StatType.Attack, second ? 0.35f : 0.12f, ModOp.Flat, source));
                    break;

                case CompanionFaction.Beast:
                    stats.Add(new StatModifier(StatType.MoveSpeed, second ? 0.25f : 0.12f, ModOp.Percent, source));
                    if (second) stats.Add(new StatModifier(StatType.CritChance, 0.15f, ModOp.Flat, source));
                    break;

                case CompanionFaction.Doll:
                    stats.Add(new StatModifier(StatType.MaxHP, second ? 0.35f : 0.15f, ModOp.Percent, source));
                    break;
            }
        }
    }
}
