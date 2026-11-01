namespace Game.Config
{
    public enum DamageType
    {
        Physical,
        Fire,
        Curse,
        True
    }

    public enum WeaponKind
    {
        Ranged,
        Melee
    }

    public enum CompanionFaction
    {
        None,
        Machine,
        Ghost,
        Beast,
        Doll
    }

    /// <summary>旅伴 AI。Follow 是旧版环绕跟随；其余三种会主动找敌人。</summary>
    public enum CompanionAI
    {
        /// <summary>只跟在玩家身边，不主动接敌。</summary>
        Follow,

        /// <summary>冲向最近的敌人，贴到 PreferredRange 内开火/挥砍。</summary>
        Aggro,

        /// <summary>保持距离：敌人近了往后退，远了再靠近。</summary>
        Kiter,

        /// <summary>只打靠近玩家的敌人，不追远目标。</summary>
        Guard
    }

    /// <summary>构筑卡与敌人的稀有度。数值只影响抽卡权重与表现，不直接进伤害公式。</summary>
    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    /// <summary>构筑卡的四条主干。升级时抽到的是其中一类，同类之间共享槽位/叠层规则。</summary>
    public enum UpgradeCategory
    {
        /// <summary>新增或升级一把武器。玩家最多同时持有 WeaponSlots 把。</summary>
        Weapon,

        /// <summary>招募或升级一个旅伴。队伍上限由 CompanionSystem 管。</summary>
        Companion,

        /// <summary>直接写进 StatBlock 的永久数值。按 MaxStacks 叠层。</summary>
        Stat,

        /// <summary>带触发条件的 buff 道具。每个道具实例独立结算层数。</summary>
        Buff
    }

    /// <summary>属性修正方式。运行时映射到 StatBlock 的 ModOp，配置层不引用 Model 枚举。</summary>
    public enum StatOp
    {
        /// <summary>平加。攻击力、攻速、暴击率这类"本身就是加成比例"的属性用它。</summary>
        Flat,

        /// <summary>百分比相加后乘一次。生命上限、移速这类有非零基线的属性用它。</summary>
        Percent,

        /// <summary>独立乘区。倍数写 1.2 表示 ×1.2。</summary>
        Multiply
    }

    public enum EnemyBehaviourType
    {
        Chase,
        Charge,
        Ranged,
        Split,
        Shield,
        EliteSummoner,
        EliteDischarger,
        Boss
    }

    /// <summary>
    /// buff 的触发时机。Stat 类型只在获得时写一次属性；
    /// 其余几种由 BuffSystem 在对应事件或每个固定步里结算。
    /// </summary>
    public enum BuffEffectType
    {
        None,

        /// <summary>只写 BuffDef 上的 StatA/StatB，不参与触发。</summary>
        Stat,

        /// <summary>每击杀一个敌人，叠一层攻击速度。</summary>
        OnKillAttackSpeed,

        /// <summary>每击杀一个敌人，回复固定生命。</summary>
        OnKillHeal,

        /// <summary>每受到一次伤害，叠一层护甲。</summary>
        OnHurtArmor,

        /// <summary>生命低于阈值时获得攻击力加成，回到阈值以上移除。</summary>
        LowHpAttack,

        /// <summary>每隔 Interval 秒回复固定生命。</summary>
        PeriodicHeal,

        /// <summary>击杀敌人时在尸体位置引发范围爆炸。</summary>
        OnKillExplode,

        /// <summary>命中敌人时在目标位置引发范围爆炸（有冷却）。</summary>
        OnHitExplode,

        /// <summary>命中时向附近敌人弹射闪电（有冷却）。</summary>
        OnHitChain,

        /// <summary>每隔 Interval 秒对玩家周围敌人造成伤害（环绕刀片）。</summary>
        OrbitDamage,

        /// <summary>玩家/旅伴造成伤害时按比例回复生命。</summary>
        Lifesteal,

        /// <summary>玩家受击时对周围敌人造成反伤。</summary>
        OnHurtThorns
    }

    public enum StatType
    {
        Unknown,
        MaxHP,
        Regen,
        Armor,
        Dodge,
        MoveSpeed,
        Attack,
        AttackSpeed,
        CritChance,
        CritDamage,
        ProjectileCount,
        Range,
        PickupRadius,
        Luck,
        Treasure,
        ContactDamage
    }
}
