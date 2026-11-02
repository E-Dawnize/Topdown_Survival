using System;
using Game.Config;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 一件武器在运行时的那一份数据：表里的参数 + 冷却 + 行为策略。
    /// 行为类不许直接翻 WeaponDef 取参数，一律从这里读——
    /// 以后"单武器独立加成"（比如只给这把弓加弹道数）只需要改这一处，不用改每个行为。
    /// </summary>
    public sealed class WeaponModel
    {
        /// <summary>
        /// 攻速加成被减到 -100% 以下时 1 + 攻速 会变成 0 或负数，间隔就成了 inf / 负值。
        /// 这种数据不该出现，但夹一下比事后排查 NaN 便宜；0.01 也意味着"最快 1% 间隔"，不会真的一帧无限开火。
        /// </summary>
        const float MinSpeedDivisor = 0.01f;

        /// <summary>没开过火时的哨兵值，避免用 0 当“刚开火”。</summary>
        const float NeverFiredAge = 999f;

        public readonly WeaponDef Def;
        public readonly IWeaponBehaviour Behaviour;
        public readonly WeaponUpgradeRules Rules;

        float _cooldownRemaining;
        float _shotAge = NeverFiredAge;

        public WeaponModel(WeaponDef def, IWeaponBehaviour behaviour, WeaponUpgradeRules rules = null)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Behaviour = behaviour ?? throw new ArgumentNullException(nameof(behaviour));
            Rules = rules ?? WeaponUpgradeRules.Default;
        }

        public string Id => Def.Id;

        /// <summary>1..Rules.MaxLevel。升级走 UpgradeLevel，直接改字段会让伤害与冷却不同步。</summary>
        public int Level { get; private set; } = 1;

        public bool CanLevelUp => Level < Rules.MaxLevel;

        public float BaseDamage => Def.BaseDamage * Rules.DamageMultiplier(Level);

        /// <summary>表里的基础间隔再乘等级成长。实际间隔还会被攻速加成除，见 Fire。</summary>
        public float Cooldown => Def.Cooldown * Rules.CooldownMultiplier(Level);

        public float Range => Def.Range;

        /// <summary>
        /// 最近一次真正出手时的有效射程。近战的表现层要按它画扇形，否则射程道具加了以后
        /// 弧线还是基础射程，会出现「看着没砍到却掉血」。远程也顺手写一份，调试层可以直接读。
        /// </summary>
        public float LastRange { get; set; }

        /// <summary>
        /// 近战伤害窗口的起止秒数（表里 Params[0] / Params[1]）。
        /// 模拟的伤害判定与表现层的弧线都读这一份：两处各写一个 0.08 迟早会对不上。
        /// 表里写反时在这里交换，调用方拿到的永远是 start &lt;= end。
        /// </summary>
        public float MeleeWindowStart
        {
            get
            {
                float start = Def.GetParam(0, 0.08f);
                float end = Def.GetParam(1, 0.22f);
                return start <= end ? start : end;
            }
        }

        public float MeleeWindowEnd
        {
            get
            {
                float start = Def.GetParam(0, 0.08f);
                float end = Def.GetParam(1, 0.22f);
                return start <= end ? end : start;
            }
        }

        /// <summary>一次开火打几发。表里填 0 或负数没有意义，行为那边按 1 处理。</summary>
        public int Projectiles => Def.Projectiles + Rules.BonusProjectiles(Level);

        /// <summary>能命中几个目标（1 = 只命中第一个，2 = 穿过第一个再打第二个）。</summary>
        public int Pierce => Def.Pierce + Rules.BonusPierce(Level);

        /// <summary>升 1 级，返回是否真的升了。满级返回 false，调用方不用自己判。</summary>
        public bool UpgradeLevel()
        {
            if (!CanLevelUp) return false;
            Level++;
            return true;
        }

        public float ProjectileSpeed => Def.ProjectileSpeed;

        /// <summary>
        /// 远程是相邻两发弹道的夹角，整束按 (发数-1)×SpreadDeg 展开；
        /// 近战没有"多弹道"，这个值当扇形半角用（表里 45 ⇒ 90° 扇角，与技术方案 8.3 一致）。
        /// 两种武器共用一个字段，含义按 Kind 分开解释，所以行为类各自读、各自写注释。
        /// </summary>
        public float SpreadDeg => Def.SpreadDeg;

        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>冷却到点。用 &lt;= 判：递减到 0 就算好了，不给浮点留 1e-8 的"永远差一点"。</summary>
        public bool IsReady => _cooldownRemaining <= 0f;

        /// <summary>这次装填一共打出去几次。调试层与单测靠它数开火次数，不用去数已经飞走的投射物。</summary>
        public int ShotsFired { get; private set; }

        /// <summary>
        /// 距离最近一次开火过了多少模拟秒。视图层晚于武器创建时用它补播第一次攻击：
        /// 升级期间刚拿到的武器可能在 WeaponVisual 挂上之前就出手了，只比较 ShotsFired 会把那一刀吞掉。
        /// 没开过火时是一个很大的哨兵值，刚开火是 0。
        /// </summary>
        public float ShotAge => _shotAge;

        /// <summary>
        /// 最近一次真正瞄准/挥砍的方向（世界 x,z）。纯表现层读它来转武器模型：
        /// 远程自动索敌可能不朝着玩家移动方向，只让武器架跟玩家转会让弓口和实际弹道对不上。
        /// 默认 zero 表示还没有开过火，表现层退回父节点朝向。
        /// </summary>
        public Vector2 AimDirection { get; set; }

        /// <summary>当前锁定的目标位置（世界 x,z），没有目标时 HasTarget 为 false。表现层的瞄准圈读它。</summary>
        public Vector2 TargetPosition { get; private set; }

        /// <summary>目标身体半径。瞄准圈的内圈按它收拢，对大体型怪不会缩进身体里。</summary>
        public float TargetRadius { get; private set; } = 0.4f;

        public bool HasTarget { get; private set; }

        public void SetTarget(Vector2 position, float radius)
        {
            TargetPosition = position;
            TargetRadius = radius > 0.05f ? radius : 0.4f;
            HasTarget = true;
        }

        public void ClearTarget()
        {
            HasTarget = false;
        }

        public void Tick(float dt)
        {
            if (_shotAge < NeverFiredAge)
            {
                _shotAge += dt;
                if (_shotAge > NeverFiredAge) _shotAge = NeverFiredAge;
            }

            if (_cooldownRemaining <= 0f) return;

            _cooldownRemaining -= dt;
            if (_cooldownRemaining < 0f) _cooldownRemaining = 0f;
        }

        /// <summary>
        /// 真的打出东西了才调，射程内没有目标时不许调：
        /// "先打空枪再等 CD"会让玩家边追边打时永远开不出火，射程外的空枪也不该进日志。
        /// 间隔 = 基础间隔 / (1 + 攻速加成)，攻速翻倍就是间隔减半。
        /// </summary>
        public void Fire(in ShooterStats stats)
        {
            float divisor = 1f + stats.AttackSpeed;
            if (divisor < MinSpeedDivisor) divisor = MinSpeedDivisor;

            _cooldownRemaining = Cooldown / divisor;
            _shotAge = 0f;
            ShotsFired++;
        }
    }

    /// <summary>
    /// 武器行为策略。加一把新武器只加一个实现类并把 BehaviourType 填成它（技术方案 8.4），
    /// 所以这里只给"每步推进一次"这一个入口，行为自己要的状态（挥砍计时、已命中集合）由实现类自己持有。
    /// facing 必须是单位向量：扇形判定直接拿它算夹角，不在这里再归一化一次。
    /// </summary>
    public interface IWeaponBehaviour
    {
        void Tick(WeaponModel weapon, Vector2 shooterPosition, Vector2 facing, in ShooterStats stats, float dt);
    }
}
