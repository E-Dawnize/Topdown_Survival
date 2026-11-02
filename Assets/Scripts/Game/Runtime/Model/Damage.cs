using System;
using Game.Config;
using UnityEngine;

namespace Game.Model
{
    /// <summary>命中附带的行为开关。只列当前管线真的会用到的，不加"以后可能用"的位。</summary>
    [Flags]
    public enum DamageFlags
    {
        None = 0,

        /// <summary>可以被闪避。环境伤害、DoT 一般不带这一位。</summary>
        Dodgeable = 1 << 0,
    }

    /// <summary>
    /// 一次命中的输入。暴击在开火瞬间算好并写进 IsCrit，命中时不再掷骰——
    /// 一发穿透箭打三个目标如果各自暴击，伤害数字会前后矛盾。
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly DamageType Type;
        public readonly UnitId Source;
        public readonly Vector2 Direction;
        public readonly float Knockback;
        public readonly bool IsCrit;
        public readonly DamageFlags Flags;

        public DamageInfo(float amount, DamageType type, UnitId source, Vector2 direction, float knockback, bool isCrit,
            DamageFlags flags)
        {
            Amount = amount;
            Type = type;
            Source = source;
            Direction = direction;
            Knockback = knockback;
            IsCrit = isCrit;
            Flags = flags;
        }
    }

    /// <summary>受击方的即时状态快照。Resolve 只读它，血量由调用方拿 RemainingHp 回写。</summary>
    public readonly struct DamageTarget
    {
        public readonly float Hp;
        public readonly float Armor;
        public readonly float Dodge;
        public readonly bool Invulnerable;

        public DamageTarget(float hp, float armor, float dodge, bool invulnerable)
        {
            Hp = hp;
            Armor = armor;
            Dodge = dodge;
            Invulnerable = invulnerable;
        }
    }

    public readonly struct DamageResult
    {
        /// <summary>实际扣掉的血。目标血不够时按剩余血算，调用方回写 RemainingHp 后血量不会为负。</summary>
        public readonly float Applied;

        public readonly bool Dodged;

        /// <summary>被无敌帧挡下（冲刺 i-frame），与闪避是两回事：闪避是概率，这是状态。</summary>
        public readonly bool Blocked;

        public readonly bool Killed;

        public readonly float RemainingHp;

        public DamageResult(float applied, bool dodged, bool blocked, bool killed, float remainingHp)
        {
            Applied = applied;
            Dodged = dodged;
            Blocked = blocked;
            Killed = killed;
            RemainingHp = remainingHp;
        }
    }

    /// <summary>
    /// 可受伤单位的只读契约。玩家、敌人、旅伴都实现这一份，伤害写回和存活判断不再各写一套。
    /// 这里只暴露伤害结算需要的最小信息，不把事件总线、视图或系统引用带进 Model。
    /// </summary>
    public interface IDamageable
    {
        UnitId Id { get; }
        UnitKind Kind { get; }
        bool IsAlive { get; }
        float Hp { get; }
        float MaxHp { get; }
        Vector2 Position { get; }

        /// <summary>只负责算伤害、写回生命和记录最后来源；事件由 DamageService 统一发布。</summary>
        DamageResult ApplyDamage(in DamageInfo damage, float dodgeRoll);
    }

    /// <summary>
    /// 伤害结算。纯静态、不读时间、不掷随机，所以同样的输入永远同样的输出，单测能直接钉死数值。
    /// 闪避的骰子由调用方用 runRng 掷好再传进来：随机流归 RunLoop 一条线管，这里不许自己开流，
    /// 否则"固定种子 + 固定输入 = 相同结果"就不成立了。
    /// 结算顺序：闪避 → 无敌帧 → 护甲减伤 → 扣血 → 死亡判定。
    /// 真实伤害只绕过护甲，不绕过闪避/无敌帧。
    /// </summary>
    public static class DamageResolver
    {
        /// <summary>护甲减伤 = armor / (armor + 50)，50 点是"减伤一半"，不设上限衰减曲线。</summary>
        const float ArmorConstant = 50f;

        public static DamageResult Resolve(in DamageInfo damage, in DamageTarget target, float dodgeRoll)
        {
            // 已经 0 血还挨打时不再判死，否则死亡事件会被重复入队、掉落会翻倍
            if (target.Hp <= 0f || damage.Amount <= 0f)
            {
                return new DamageResult(0f, false, false, false, Mathf.Max(0f, target.Hp));
            }

            // roll 由调用方用 runRng 掷好传进来，∈ [0,1)；恰好相等算命中，边界不留给浮点巧合
            if ((damage.Flags & DamageFlags.Dodgeable) != 0 && dodgeRoll < target.Dodge)
                return new DamageResult(0f, true, false, false, target.Hp);

            if (target.Invulnerable) return new DamageResult(0f, false, true, false, target.Hp);

            float afterArmor = damage.Type == DamageType.True
                ? damage.Amount
                : damage.Amount * (1f - ArmorReduction(target.Armor));

            float remaining = target.Hp - afterArmor;
            bool killed = remaining <= 0f;
            if (remaining < 0f) remaining = 0f;   // 血不能为负，HUD 与 HpRatio 都按这个前提写

            return new DamageResult(target.Hp - remaining, false, false, killed, remaining);
        }

        /// <summary>负护甲按 0 处理。先 clamp 再除：armor 小于 -50 时 armor/(armor+50) 会变成正数，clamp 到 1 就成了满减伤。</summary>
        static float ArmorReduction(float armor)
        {
            if (armor <= 0f) return 0f;
            return armor / (armor + ArmorConstant);
        }
    }
}
