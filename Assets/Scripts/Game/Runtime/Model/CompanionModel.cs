using Game.Config;
using Game.Core.Events;
using UnityEngine;

namespace Game.Model
{
    /// <summary>
    /// 旅伴的纯数据。和 EnemyModel 一样只认 Vector2 的 (x, z)，不碰 Transform。
    ///
    /// 等级效果走 StatBlock，Source 就是模型自己：升一级只留一条修正（先 RemoveBySource 再 Add），
    /// 所以"买第二次、第三次"不会把历史等级叠成几条互相加成的修正。
    /// 羁绊加成也写进同一份 StatBlock，但 Source 是羁绊自己的源对象，清羁绊时不会碰等级修正。
    ///
    /// 旅伴当前不受伤：I2 这一阶段敌人只对玩家结算接触伤害，旅伴既不被敌人推也不掉血，
    /// HP 与 MaxHP 先按 CompanionDef.MaxHP 准备好，接敌人对旅伴的伤害时直接用。
    /// </summary>
    public sealed class CompanionModel
    {
        /// <summary>GDD 7.3：重复购买升 1 级，上限 3 级。</summary>
        public const int MaxLevel = 3;

        /// <summary>每级伤害 +40%（GDD 7.3）。攻击力在 StatBlock 里的值本身就是加成比例，所以按百分点平加。</summary>
        const float DamagePerLevel = 0.4f;

        /// <summary>每级间隔 −8%（GDD 7.3）。间隔 = 基础 / (1 + 攻速)，见 SetLevel 里的换算。</summary>
        const float IntervalFactorPerLevel = 0.92f;

        /// <summary>暴击基线。角色表还没有这几列，先与 PlayerModel 的默认值保持一致，旅伴的暴击才不会凭空变成 0。</summary>
        const float DefaultCritChance = 0.05f;
        const float DefaultCritDamage = 1.5f;

        public readonly CompanionDef Def;

        /// <summary>CompanionDef.WeaponId 指向的那把武器。每步由 CompanionSystem 交给这个旅伴自己的 WeaponSystem。</summary>
        public readonly WeaponDef Weapon;

        public readonly StatBlock Stats;

        /// <summary>队伍槽位（0..MaxCompanions-1）。环绕角度与 UnitId 都按它算，所以旅伴离队后槽位可以被下一个复用。</summary>
        public readonly int Slot;

        /// <summary>旅伴自己的 id。与玩家（1）和敌人（槽位号 + 1）都不重叠，见 CompanionSystem.UnitIdOf。</summary>
        public readonly UnitId UnitId;

        public Vector2 Position;
        public Vector2 PrevPosition;

        /// <summary>当前血量。旅伴这一阶段不会掉血，字段先备好，等敌人能打旅伴时再接。</summary>
        public float HP;

        /// <summary>1..MaxLevel。重复购买一次 +1，到 MaxLevel 后不能再买。</summary>
        public int Level { get; private set; }

        public CompanionModel(CompanionDef def, WeaponDef weapon, int slot, UnitId unitId, float followSpeed)
        {
            Def = def;
            Weapon = weapon;
            Slot = slot;
            UnitId = unitId;

            Stats = new StatBlock();

            // 基线只写表里真有的数：MaxHP 来自 CompanionDef，移速表里没有列，由 CompanionSystem 给一个常量。
            // 攻击/攻速基线是 0（它们的值本身就是"加成比例"），暴击沿用玩家默认值。
            Stats.SetBase(StatType.MaxHP, def.MaxHP);
            Stats.SetBase(StatType.MoveSpeed, followSpeed);
            Stats.SetBase(StatType.Attack, 0f);
            Stats.SetBase(StatType.AttackSpeed, 0f);
            Stats.SetBase(StatType.CritChance, DefaultCritChance);
            Stats.SetBase(StatType.CritDamage, DefaultCritDamage);

            HP = def.MaxHP;
            SetLevel(1);
        }

        /// <summary>阵营。羁绊只按 CompanionDef 的这一列统计，代码里不认具体是哪个旅伴。</summary>
        public CompanionFaction Faction => Def.Faction;

        public float MaxHP => Stats[StatType.MaxHP];

        public float MoveSpeed => Stats[StatType.MoveSpeed];

        public bool IsAlive => HP > 0f;

        /// <summary>能否再买一次。满级后商店里的这一格就不能买了。</summary>
        public bool CanLevelUp => Level < MaxLevel;

        /// <summary>
        /// 设定等级并重算等级修正。夹在 1..MaxLevel：低于 1 当 1（新建），高于 3 当 3（买不了第 4 次）。
        /// 先把上一次的等级修正整个退掉再加，升到 3 级时属性里只有一条 3 级修正，没有 1、2 级的残影。
        /// </summary>
        public void SetLevel(int level)
        {
            Level = level < 1 ? 1 : level > MaxLevel ? MaxLevel : level;

            Stats.RemoveBySource(this);
            int steps = Level - 1;
            if (steps <= 0) return;

            // 伤害：+40% 一级，攻击力是加成比例，按百分点平加
            Stats.Add(new StatModifier(StatType.Attack, DamagePerLevel * steps, ModOp.Flat, this));

            // 间隔：每级乘 0.92。武器那边算的是 间隔 = 基础 / (1 + 攻速)，
            // 所以 1 / 0.92^k − 1 才是"间隔乘 0.92^k"对应的攻速值；基线 0 上只能平加，不能乘。
            float attackSpeed = 1f / Mathf.Pow(IntervalFactorPerLevel, steps) - 1f;
            Stats.Add(new StatModifier(StatType.AttackSpeed, attackSpeed, ModOp.Flat, this));
        }
    }
}
