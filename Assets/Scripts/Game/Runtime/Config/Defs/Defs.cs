using System;

namespace Game.Config
{
    // 所有 *Def 的字段名必须和 Excel 表头完全一致，导表脚本不会做名字映射。

    public sealed class ConfigManifest
    {
        public string version;
        public string exportedAt;
        public string[] tables;
    }

    public sealed class CharacterDef
    {
        public string Id;
        public string Name;
        public string Description;
        public float BaseHP;
        public float BaseMoveSpeed;
        public float BaseAttackInterval;
        public float BaseDamage;
        public float BaseRange;
        public string StartingWeaponId;
        public string TraitType;
    }

    public sealed class WeaponDef
    {
        public string Id;
        public string Name;
        public string Description;
        public WeaponKind Kind;
        public float BaseDamage;
        public float Cooldown;
        public float Range;
        public int Projectiles;
        public float SpreadDeg;
        public string BehaviourType;
        public float ProjectileSpeed;
        public int Pierce;
        public float[] Params;

        public float GetParam(int index, float fallback)
        {
            return Params != null && index >= 0 && index < Params.Length ? Params[index] : fallback;
        }
    }

    public sealed class CompanionDef
    {
        public string Id;
        public string Name;
        public string Description;
        public CompanionFaction Faction;
        public string WeaponId;
        public float FollowRadius;
        public float MaxHP;

        /// <summary>旅伴行为。Follow 保留旧版环绕；Aggro/Kiter/Guard 会主动接敌。</summary>
        public CompanionAI AI;

        /// <summary>主动接敌的搜索半径（米）。Follow 不使用。</summary>
        public float AggroRadius;

        /// <summary>想和敌人保持的距离（米）。近战约 1.3–1.6，远程约 5–6。</summary>
        public float PreferredRange;

        /// <summary>离玩家的最大活动距离（米）。超过就回位，防止追怪追到天边。</summary>
        public float LeashDistance;
    }

    public sealed class EnemyDef
    {
        public string Id;
        public string Name;

        /// <summary>用哪个预制体。新怪种可以共用已有几何体，靠颜色与缩放区分，不必每个 Id 都做一份资源。</summary>
        public string PrefabId;

        public EnemyBehaviourType Behaviour;

        /// <summary>威胁等级。1 是杂兵，2–3 是中期，4–5 是后期；Boss 用 9。只用于 HUD 与刷怪品质展示。</summary>
        public int Tier;

        public float HP;
        public float Damage;
        public float Speed;
        public float AttackRange;
        public float AttackInterval;

        /// <summary>一只怪掉多少经验。金币已从这一版移除，这一列是唯一的经济出口。</summary>
        public int XpValue;

        public float BodyRadius;

        /// <summary>视觉缩放。判定半径按 BodyRadius × Scale 算，所以大怪真的更难躲。</summary>
        public float Scale;
    }

    /// <summary>
    /// 刷怪导演的一段。时间轴从 StartTime 到 EndTime，期间用这一段的间隔、数量、怪种池与成长倍率。
    /// 最后一段的 EndTime 就是 BOSS 出场时间。
    /// </summary>
    public sealed class DirectorDef
    {
        public string Id;
        public float StartTime;
        public float EndTime;

        /// <summary>威胁等级，1 起。HUD 显示"威胁 Lv N"，也是设计文档里难度曲线的刻度。</summary>
        public int Threat;

        public float SpawnInterval;
        public int SpawnCountMin;
        public int SpawnCountMax;
        public string[] EnemyPool;
        public float EliteChance;
        public int ConcurrentCap;

        /// <summary>本段敌人在基础值上的倍率。时间越往后越高，不靠"波次结束"跳变。</summary>
        public float HpScale;
        public float DamageScale;
        public float SpeedScale;

        public string BossId;
        public float BossAddInterval;
        public int BossAddCount;

        public bool HasBoss => !string.IsNullOrEmpty(BossId);
    }

    /// <summary>升到这一级需要多少经验、给几张卡、稀有度是否上浮。</summary>
    public sealed class LevelDef
    {
        public string Id;

        /// <summary>当前等级。经验从 Level 升到 Level+1 需要 XpToNext。</summary>
        public int Level;

        public int XpToNext;

        /// <summary>这一级升级时给几个选项。</summary>
        public int OfferCount;

        /// <summary>稀有度上浮 0..1。越高越容易出紫/金，与幸运属性相加。</summary>
        public float RarityBonus;

        public string Note;
    }

    /// <summary>
    /// 构筑卡池的一行。Category 决定用哪个 Ref 列与哪套生效逻辑：
    ///   Weapon → WeaponId；Companion → CompanionId；Buff → BuffId；Stat → StatA/StatB。
    /// 同一行只允许一个主效果，避免"一张卡同时给武器和 20 点生命"这种无法解释的强度。
    /// </summary>
    public sealed class UpgradeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public UpgradeCategory Category;
        public Rarity Rarity;

        /// <summary>基础权重。同类同品质里越想要的卡写越高。</summary>
        public int Weight;

        public string WeaponId;
        public string CompanionId;
        public string BuffId;

        public StatType StatA;
        public float ValueA;
        public StatOp OpA;
        public StatType StatB;
        public float ValueB;
        public StatOp OpB;

        /// <summary>这张卡最多拿几次。武器与旅伴的等级上限由 Balance 管，这一列只对 Stat/Buff 生效。</summary>
        public int MaxStacks;

        /// <summary>构筑标签。组合效果按标签数量判定，例如 ranged / melee / explosive / chain。</summary>
        public string[] Tags;

        public float[] Params;

        public bool HasStatA => StatA != StatType.Unknown;
        public bool HasStatB => StatB != StatType.Unknown;

        public float GetParam(int index, float fallback)
        {
            return Params != null && index >= 0 && index < Params.Length ? Params[index] : fallback;
        }
    }

    /// <summary>
    /// buff 道具定义。获得时先写 StatA/StatB，再按 Effect 挂触发逻辑。
    /// MaxStacks 是触发叠层的上限（例如击杀叠攻速最多 20 层），不是"这张卡能买几次"；
    /// 后者由 UpgradeDef.MaxStacks 管。
    /// </summary>
    public sealed class BuffDef
    {
        public string Id;
        public string Name;
        public string Description;
        public BuffEffectType Effect;

        /// <summary>0 表示整局常驻。当前版本的构筑 buff 都是常驻，留给以后限时 buff。</summary>
        public float Duration;

        public int MaxStacks;
        public float Interval;
        public string Tags;

        public StatType StatA;
        public float ValueA;
        public StatOp OpA;
        public StatType StatB;
        public float ValueB;
        public StatOp OpB;

        public float[] Params;

        public bool HasStatA => StatA != StatType.Unknown;
        public bool HasStatB => StatB != StatType.Unknown;

        public float GetParam(int index, float fallback)
        {
            return Params != null && index >= 0 && index < Params.Length ? Params[index] : fallback;
        }
    }

    /// <summary>
    /// 组合效果：两类标签达到数量后，自动获得 BuffId 指向的 buff。
    /// 组合不占构筑槽位，是"拿了 A + B 之后多出来的一层"。
    /// </summary>
    public sealed class ComboDef
    {
        public string Id;
        public string Name;
        public string Description;

        /// <summary>条件 A 的标签名。空表示这个条件不参与判定。</summary>
        public string TagA;

        public int CountA;

        /// <summary>条件 B 的标签名。空表示只判定 A。</summary>
        public string TagB;

        public int CountB;

        /// <summary>满足条件后获得的 buff。Buff 表里必须有这一行。</summary>
        public string BuffId;
    }

    public sealed class BalanceDef
    {
        public string Id;
        public float Value;
        public string Note;
    }
}
