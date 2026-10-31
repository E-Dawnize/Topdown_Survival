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

        public readonly WeaponDef Def;
        public readonly IWeaponBehaviour Behaviour;

        float _cooldownRemaining;

        public WeaponModel(WeaponDef def, IWeaponBehaviour behaviour)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Behaviour = behaviour ?? throw new ArgumentNullException(nameof(behaviour));
        }

        public string Id => Def.Id;

        public float BaseDamage => Def.BaseDamage;

        /// <summary>表里的基础间隔。实际间隔还会被攻速加成除，见 Fire。</summary>
        public float Cooldown => Def.Cooldown;

        public float Range => Def.Range;

        /// <summary>一次开火打几发。表里填 0 或负数没有意义，行为那边按 1 处理。</summary>
        public int Projectiles => Def.Projectiles;

        /// <summary>能命中几个目标（1 = 只命中第一个，2 = 穿过第一个再打第二个）。</summary>
        public int Pierce => Def.Pierce;

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

        public void Tick(float dt)
        {
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

            _cooldownRemaining = Def.Cooldown / divisor;
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
