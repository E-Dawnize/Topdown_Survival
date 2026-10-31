using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// GDD 5.2 的伤害公式：
    /// 基础伤害 × (1 + 攻击力加成) × 暴击倍率 × 距离衰减 × (1 - 护甲减伤)。
    /// 最后一项由 DamageResolver 在结算时算，这里只算到"剥护甲之前"，
    /// 这样武器层不用重复实现一遍护甲曲线，改护甲常数只需要改一处。
    /// 全是纯函数、不掷骰：骰子和距离都由调用方传进来，同样的输入永远同样的输出，数值能单测钉死。
    /// </summary>
    public static class WeaponDamage
    {
        /// <summary>衰减结束的位置：射程的 120%。</summary>
        const float FalloffEnd = 1.2f;

        /// <summary>衰减下限 50%，超过 120% 之后一直是这个值，不会掉到 0——否则擦边命中和空过没有区别。</summary>
        const float FalloffFloor = 0.5f;

        /// <summary>
        /// 每点伤害换多少击退初速（米/秒）。武器表还没有击退列，先按伤害比例算：
        /// 8 点伤害的箭给 4 米/秒，与 EnemySystem 的衰减速率配起来退 0.3 米上下。
        /// </summary>
        const float KnockbackPerDamage = 0.5f;

        /// <summary>
        /// 单次命中与叠加后的击退初速上限。同一步里多发箭打中同一个身体时，
        /// 不封顶会把它推出半个场子，下一帧的分离力也会跟着抖。
        /// </summary>
        public const float MaxKnockbackSpeed = 6f;

        /// <summary>
        /// 距离衰减：射程 100% 内不衰减，从 100% 到 120% 线性降到 50%，再远保持 50%。
        /// 射程 &lt;= 0 时直接给下限：导表会拦掉这种数据，但公式里不能出现除零把伤害算成 NaN。
        /// </summary>
        public static float DistanceFalloff(float distance, float range)
        {
            if (range <= 0f) return FalloffFloor;

            float ratio = distance / range;
            if (ratio <= 1f) return 1f;
            if (ratio >= FalloffEnd) return FalloffFloor;

            // (ratio - 1) / 0.2 在 0..1 之间，正好从 1 落到 0.5
            return 1f - (ratio - 1f) / (FalloffEnd - 1f) * (1f - FalloffFloor);
        }

        /// <summary>暴击判定。roll 由调用方掷好，∈[0,1)；恰好相等算暴击，与闪避边界同一套约定。</summary>
        public static bool RollCrit(float roll, in ShooterStats stats) => roll < stats.CritChance;

        /// <summary>
        /// 击退初速。按伤害换算，大伤害推得远，符合直觉，也不用为了几个数去改表结构。
        /// 不掷骰：同样的伤害永远同样的击退，单测能把距离钉死。
        /// </summary>
        public static float KnockbackSpeed(float amount)
        {
            if (amount <= 0f) return 0f;

            float speed = amount * KnockbackPerDamage;
            return speed > MaxKnockbackSpeed ? MaxKnockbackSpeed : speed;
        }

        /// <summary>暴击倍率，不暴击是 1。</summary>
        public static float CritMultiplier(bool isCrit, in ShooterStats stats) => isCrit ? stats.CritDamage : 1f;

        /// <summary>剥护甲之前的伤害。距离衰减单独传，因为近战每个目标距离不同、远程在开火瞬间算一次。</summary>
        public static float Amount(float baseDamage, in ShooterStats stats, bool isCrit, float falloff)
        {
            return baseDamage * (1f + stats.AttackBonus) * CritMultiplier(isCrit, stats) * falloff;
        }

        /// <summary>
        /// 组装一次命中。暴击在开火瞬间就已经定好并写进 IsCrit，
        /// 命中时不再掷——一发穿透箭打三个目标如果各自暴击，三个伤害数字会自相矛盾。
        /// </summary>
        public static DamageInfo Compose(float amount, UnitId source, Vector2 direction, bool isCrit, float knockback)
        {
            // 两把武器在表里都没有伤害类型列（WeaponDef 里还没有这个字段），先统一物理；
            // 以后加列时只改这里的入参来源，行为类不用动。
            DamageType type = DamageType.Physical;

            DamageFlags flags = DamageFlags.Dodgeable | DamageFlags.TriggerHit;
            if (isCrit || amount >= HitFeedback.BigHitThreshold) flags |= DamageFlags.ShowNumber;

            return new DamageInfo(amount, type, source, direction, knockback, isCrit, flags);
        }

        /// <summary>
        /// 把一次命中写进敌人。只管扣血与冲量：存活标记与尸体清理归外部（EnemySystem.Remove 由清理阶段调），
        /// 武器在命中路径上顺手 Remove 会让"谁该看见尸体"变成隐式时序问题。
        /// 护甲、闪避从敌人的 StatBlock 现读，不缓存——以后敌人加 buff 就自动生效。
        /// </summary>
        public static DamageResult ApplyToEnemy(EnemyModel enemy, in DamageInfo damage, float dodgeRoll)
        {
            var target = new DamageTarget(
                enemy.HP,
                enemy.Stats[StatType.Armor],
                enemy.Stats[StatType.Dodge],
                false);   // 敌人暂时没有无敌帧状态；玩家冲刺的 i-frame 不走这条通路

            DamageResult result = DamageResolver.Resolve(damage, target, dodgeRoll);
            enemy.HP = result.RemainingHp;

            // 击杀归属在伤害结算这一刻才能确定，清理阶段拿不到，所以在这里落一笔
            if (result.Applied > 0f)
            {
                enemy.LastDamageSource = damage.Source;
                ApplyKnockback(enemy, damage);
            }
            return result;
        }

        /// <summary>
        /// 冲量按速度累加并封顶。闪避、无敌帧、已经死掉的目标都不改冲量：
        /// 只有真正掉了血的那一下才能把人推开，否则“明明闪了却被推一下”很难解释。
        /// </summary>
        static void ApplyKnockback(EnemyModel enemy, in DamageInfo damage)
        {
            if (damage.Knockback <= 0f) return;
            if (damage.Direction.sqrMagnitude < 1e-8f) return;

            Vector2 impulse = enemy.Knockback + damage.Direction.normalized * damage.Knockback;
            enemy.Knockback = impulse.sqrMagnitude > MaxKnockbackSpeed * MaxKnockbackSpeed
                ? impulse.normalized * MaxKnockbackSpeed
                : impulse;
        }
    }
}
