using Game.Config;
using Game.Model;

namespace Game.Core.Events
{
    // 单位身份（UnitId / UnitKind / UnitIdAllocator）在 Game.Model。

    // ---- 战斗 ----

    /// <summary>玩家侧范围伤害的表现类型。只用于表现层选颜色/节奏，不参与结算。</summary>
    public enum CombatAreaEffectKind
    {
        Contact,
        Explosion,
        Chain,
        Orbit,
        Thorns
    }

    /// <summary>
    /// 玩家侧一次范围伤害已经结算完。以前碰撞伤害、爆炸、链电、环绕刀、反伤都是“怪突然掉血”，
    /// 视图层收不到任何位置/半径信息；这个事件让每一种范围伤害都有一个可见的环。
    /// </summary>
    public readonly struct CombatAreaEffect
    {
        public readonly UnitId Source;
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;
        public readonly CombatAreaEffectKind Kind;

        public CombatAreaEffect(UnitId source, float x, float z, float radius, CombatAreaEffectKind kind)
        {
            Source = source;
            X = x;
            Z = z;
            Radius = radius;
            Kind = kind;
        }
    }

    /// <summary>
    /// 敌人开始起手。表现层用它画预警环/蓄力姿态；模拟层不依赖这个事件。
    /// Angle 是从敌人指向玩家的世界方向（弧度），Duration 是起手总时长。
    /// </summary>
    public readonly struct EnemyAttackStarted
    {
        public readonly UnitId Attacker;
        public readonly float X;
        public readonly float Z;
        public readonly float Angle;
        public readonly float Duration;
        public readonly EnemyAttackKind Kind;

        public EnemyAttackStarted(UnitId attacker, float x, float z, float angle, float duration,
            EnemyAttackKind kind)
        {
            Attacker = attacker;
            X = x;
            Z = z;
            Angle = angle;
            Duration = duration;
            Kind = kind;
        }
    }

    /// <summary>
    /// 敌人起手结束、真正出手。近战/冲锋用它画红色扇形，远程用它画枪口闪光；
    /// 伤害是否命中仍由模拟层决定，表现层只负责把“这一下打出来了”画清楚。
    /// </summary>
    public readonly struct EnemyAttackStrike
    {
        public readonly UnitId Attacker;
        public readonly float X;
        public readonly float Z;
        public readonly float Angle;
        public readonly float Reach;
        public readonly float HalfAngleDeg;
        public readonly EnemyAttackKind Kind;

        public EnemyAttackStrike(UnitId attacker, float x, float z, float angle, float reach,
            float halfAngleDeg, EnemyAttackKind kind)
        {
            Attacker = attacker;
            X = x;
            Z = z;
            Angle = angle;
            Reach = reach;
            HalfAngleDeg = halfAngleDeg;
            Kind = kind;
        }
    }

    public readonly struct UnitDamaged
    {
        public readonly UnitId Target;
        public readonly UnitId Source;
        public readonly float Amount;
        public readonly bool IsCrit;
        public readonly float RemainingHp;

        /// <summary>伤害来自 buff 的连锁效果。BuffSystem 不再对这类伤害二次触发，避免无限递归。</summary>
        public readonly bool FromEffect;

        /// <summary>命中时目标的位置。飘字与击杀反应要用；清理阶段移除模型后事件里就拿不到了。</summary>
        public readonly float X;
        public readonly float Z;

        public UnitDamaged(UnitId target, UnitId source, float amount, bool isCrit, float remainingHp,
            bool fromEffect = false, float x = 0f, float z = 0f)
        {
            Target = target;
            Source = source;
            Amount = amount;
            IsCrit = isCrit;
            RemainingHp = remainingHp;
            FromEffect = fromEffect;
            X = x;
            Z = z;
        }
    }

    public readonly struct UnitDied
    {
        public readonly UnitId Unit;
        public readonly UnitId Killer;
        public readonly float X;
        public readonly float Z;

        /// <summary>是不是精英/BOSS。结算统计与击杀反馈要用，清理阶段移除模型后事件里就拿不到这些信息了。</summary>
        public readonly bool IsElite;

        public readonly bool IsBoss;

        public UnitDied(UnitId unit, UnitId killer, float x, float z, bool isElite = false, bool isBoss = false)
        {
            Unit = unit;
            Killer = killer;
            X = x;
            Z = z;
            IsElite = isElite;
            IsBoss = isBoss;
        }
    }

    // ---- 成长 ----

    /// <summary>经验拾取。HUD 与升级流程都靠它，不自己去读 PickupSystem。</summary>
    public readonly struct ExperienceGained
    {
        public readonly int Amount;
        public readonly int TotalXp;
        public readonly int Level;
        public readonly int Xp;
        public readonly int XpToNext;
        public readonly int PendingLevelUps;

        public ExperienceGained(int amount, int totalXp, int level, int xp, int xpToNext, int pendingLevelUps)
        {
            Amount = amount;
            TotalXp = totalXp;
            Level = level;
            Xp = xp;
            XpToNext = xpToNext;
            PendingLevelUps = pendingLevelUps;
        }
    }

    /// <summary>
    /// 全屏吸取经验已经结算。表现层画一个扩散环，HUD 弹一行提示；
    /// 枚数与总经验都带上，提示不用再回头去读 PickupSystem。
    /// </summary>
    public readonly struct ExperienceCollectPulse
    {
        public readonly float X;
        public readonly float Z;
        public readonly int Orbs;
        public readonly int TotalExperience;

        public ExperienceCollectPulse(float x, float z, int orbs, int totalExperience)
        {
            X = x;
            Z = z;
            Orbs = orbs;
            TotalExperience = totalExperience;
        }
    }

    /// <summary>玩家受到一次实际伤害（闪避/无敌帧挡下的不算）。buff 的受击触发挂在这里。</summary>
    public readonly struct PlayerDamaged
    {
        public readonly float Amount;
        public readonly float RemainingHp;
        public readonly float MaxHp;

        public PlayerDamaged(float amount, float remainingHp, float maxHp)
        {
            Amount = amount;
            RemainingHp = remainingHp;
            MaxHp = maxHp;
        }
    }

    public readonly struct LevelUpStarted
    {
        public readonly int Level;
        public readonly int OfferCount;

        public LevelUpStarted(int level, int offerCount)
        {
            Level = level;
            OfferCount = offerCount;
        }
    }

    /// <summary>重抽后选项换了，但升级轮次没变。UI 只刷新卡片，不重播开面板。</summary>
    public readonly struct UpgradeOptionsChanged
    {
        public readonly int Level;

        public UpgradeOptionsChanged(int level)
        {
            Level = level;
        }
    }

    public readonly struct LevelUpClosed
    {
        public readonly int Level;

        public LevelUpClosed(int level)
        {
            Level = level;
        }
    }

    /// <summary>组合效果触发。HUD/提示层用它做一次正反馈。</summary>
    public readonly struct ComboActivated
    {
        public readonly string ComboId;
        public readonly string Name;

        public ComboActivated(string comboId, string name)
        {
            ComboId = comboId;
            Name = name;
        }
    }

    /// <summary>武器/旅伴/数值/buff 的持有状态变了。HUD 只需知道"该重读构筑摘要了"。</summary>
    public readonly struct RunStarted
    {
        public readonly ulong Seed;
        public readonly string CharacterId;

        public RunStarted(ulong seed, string characterId)
        {
            Seed = seed;
            CharacterId = characterId;
        }
    }

    public readonly struct BossFightStarted
    {
        public readonly string BossId;
        public readonly string BossName;
        public readonly float MaxHp;

        public BossFightStarted(string bossId, string bossName, float maxHp)
        {
            BossId = bossId;
            BossName = bossName;
            MaxHp = maxHp;
        }
    }

    public readonly struct BossEnraged
    {
        public readonly string BossId;

        public BossEnraged(string bossId)
        {
            BossId = bossId;
        }
    }

    public readonly struct BossDefeated
    {
        public readonly string BossId;
        public readonly float FightSeconds;

        public BossDefeated(string bossId, float fightSeconds)
        {
            BossId = bossId;
            FightSeconds = fightSeconds;
        }
    }

    public readonly struct PlayerDashed
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Angle;

        public PlayerDashed(float x, float z, float angle)
        {
            X = x;
            Z = z;
            Angle = angle;
        }
    }

    public readonly struct PlayerDied
    {
        public readonly float X;
        public readonly float Z;

        public PlayerDied(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    public readonly struct RunSummary
    {
        public readonly int Level;
        public readonly int Kills;
        public readonly int EliteKills;
        public readonly float Duration;
        public readonly ulong Seed;
        public readonly int Weapons;
        public readonly int Companions;

        public RunSummary(int level, int kills, int eliteKills, float duration, ulong seed, int weapons,
            int companions)
        {
            Level = level;
            Kills = kills;
            EliteKills = eliteKills;
            Duration = duration;
            Seed = seed;
            Weapons = weapons;
            Companions = companions;
        }
    }

    public readonly struct RunEnded
    {
        public readonly RunSummary Summary;
        public readonly bool Victory;

        public RunEnded(in RunSummary summary, bool victory)
        {
            Summary = summary;
            Victory = victory;
        }
    }
}
