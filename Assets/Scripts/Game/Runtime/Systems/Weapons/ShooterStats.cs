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

        public ShooterStats(float attackBonus, float critChance, float critDamage, float attackSpeed)
        {
            AttackBonus = attackBonus;
            CritChance = critChance;
            CritDamage = critDamage;
            AttackSpeed = attackSpeed;
        }
    }
}
