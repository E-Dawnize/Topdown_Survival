using Game.Config;
using UnityEngine;

namespace Game.Model
{
    public enum PlayerState
    {
        Idle,
        Moving,
        Dashing
    }

    /// <summary>
    /// 玩家的纯数据。只认 Vector2 的 (x, z)，不碰 Transform。
    /// 生命上限与移速走 StatBlock：道具与 buff 以后只能改这里，不许直接改最终值。
    /// 冲刺相关的数字还是构造时定死的普通字段，I1-2 的冲刺机制本身不经过属性系统。
    /// </summary>
    public sealed class PlayerModel
    {
        /// <summary>
        /// 玩家的受击体积（米）。玩家预制体是半径 0.5 的胶囊，敌人的近战判定与投射物命中都用它。
        /// 以前敌人的伤害只看中心点距离，站在身体边上也会被“看不见的攻击”打到；判定统一按体积算。
        /// </summary>
        public const float BodyRadius = 0.5f;

        /// <summary>玩家打出去的伤害与暴击默认值。角色表还没有这几列（只有 BaseDamage 是武器表的事）。</summary>
        const float DefaultAttackBonus = 0f;
        const float DefaultCritChance = 0.05f;
        const float DefaultCritDamage = 1.5f;
        const float DefaultAttackSpeed = 0f;

        /// <summary>拾取范围，GDD 5.1：经验与心的吸附半径 2.5 米。写在 StatBlock 里，拾取道具与 buff 才能改它。</summary>
        const float DefaultPickupRadius = 2.5f;

        /// <summary>全屏吸取没有配置时的兜底冷却（秒）。正式装配从 Balance.json 读 ExperienceCollectCooldown。</summary>
        public const float DefaultExperienceCollectCooldown = 12f;

        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 DashDirection;
        public float Facing;

        public PlayerState State;

        public readonly StatBlock Stats;

        /// <summary>当前血量。掉到 0 以下只做判定，别的地方不许拿它当"还活着"的唯一依据。</summary>
        public float HP;

        public readonly float DashSpeed;
        public readonly float DashDuration;
        public readonly float DashCooldown;
        public readonly float ExperienceCollectCooldown;
        public readonly float ArenaHalfExtent;

        public float DashTimer;
        public float DashCooldownTimer;

        /// <summary>
        /// 全屏吸取经验（E/RB）的剩余冷却。为 0 时才能再次使用；
        /// 只有真的吸到经验才进入冷却，屏幕上没经验球时按了不会白白浪费一次。
        /// </summary>
        public float ExperienceCollectTimer;

        /// <summary>碰撞伤害的结算冷却。RunSimulation 每步推进，0.5 秒对贴身敌人结算一次。</summary>
        public float ContactTimer;

        public PlayerModel(float maxHp, float moveSpeed, float dashSpeed, float dashDuration, float dashCooldown,
            float arenaHalfExtent, float experienceCollectCooldown = DefaultExperienceCollectCooldown)
        {
            Stats = new StatBlock();
            Stats.SetBase(StatType.MaxHP, maxHp);
            Stats.SetBase(StatType.MoveSpeed, moveSpeed);
            Stats.SetBase(StatType.Attack, DefaultAttackBonus);
            Stats.SetBase(StatType.CritChance, DefaultCritChance);
            Stats.SetBase(StatType.CritDamage, DefaultCritDamage);
            Stats.SetBase(StatType.AttackSpeed, DefaultAttackSpeed);
            Stats.SetBase(StatType.PickupRadius, DefaultPickupRadius);

            HP = maxHp;
            DashSpeed = dashSpeed;
            DashDuration = dashDuration;
            DashCooldown = dashCooldown;
            ExperienceCollectCooldown = experienceCollectCooldown > 0.01f
                ? experienceCollectCooldown
                : DefaultExperienceCollectCooldown;
            ArenaHalfExtent = arenaHalfExtent;
        }

        /// <summary>生命上限。读 StatBlock，装备与 buff 改了 MaxHP 之后 HUD 与 HpRatio 自动跟上。</summary>
        public float MaxHP => Stats[StatType.MaxHP];

        public float MoveSpeed => Stats[StatType.MoveSpeed];

        /// <summary>朝向的 XZ 单位向量。表现层与模拟层共用，避免 Core 依赖 Views 的转换工具。</summary>
        public Vector2 Forward => new Vector2(Mathf.Cos(Facing), Mathf.Sin(Facing));

        public bool IsDead => HP <= 0f;
        public float HpRatio => MaxHP > 0f ? Mathf.Clamp01(HP / MaxHP) : 0f;
        public bool IsDashing => DashTimer > 0f;
        public bool IsInvulnerable => DashTimer > 0f;
        public bool CanDash => DashCooldownTimer <= 0f && DashTimer <= 0f;

        public bool CanCollectExperience => ExperienceCollectTimer <= 0f;
    }
}
