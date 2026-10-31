using Game.Config;
using Game.Core.Events;
using UnityEngine;

namespace Game.Model
{
    /// <summary>
    /// 敌人的纯数据。位置用 Vector2 的 (x, z)，不碰 Transform，也不认识网格与视图。
    /// 属性走 StatBlock：EnemyDef 里的数字只作为基线写进去，之后加 buff 才有一处可改。
    /// </summary>
    public sealed class EnemyModel
    {
        public readonly EnemyDef Def;
        public readonly StatBlock Stats;

        public Vector2 Position;
        public Vector2 Velocity;

        /// <summary>
        /// 击退冲量，单位是速度（米/秒），和 Velocity 是两份东西。
        /// Velocity 每步都由追击与分离力重算，把冲量写进去等于每帧清一次；
        /// 分开存以后，冲量按 EnemySystem 的速率自己衰减，追击的稳定性与它无关。
        /// </summary>
        public Vector2 Knockback;

        /// <summary>
        /// 本步移动前的位置。渲染层在 PrevPosition 与 Position 之间插值，
        /// 否则固定步长的阶梯位移落到 Transform 上就是一段一顿。
        /// </summary>
        public Vector2 PrevPosition;

        /// <summary>当前血量。掉到 0 以下由清理阶段（外部调 Remove）收走，模型自己不许改自己的存活标记。</summary>
        public float HP;

        /// <summary>
        /// 最后一次打到它的伤害来源。击杀归属只有在伤害结算那一刻才拿得到，
        /// 等清理阶段（外部调 Remove）再回头查已经晚了，所以这里顺手记一笔。
        /// </summary>
        public UnitId LastDamageSource;

        /// <summary>还在场上。Remove 之后置 false，之后这个槽位可能被新敌人复用。</summary>
        public bool IsAlive;

        /// <summary>距离下一次接触伤害的冷却。&lt;= 0 表示这一帧贴着玩家就能打。</summary>
        public float AttackTimer;

        public EnemyModel(EnemyDef def, Vector2 position)
        {
            Def = def;
            Stats = new StatBlock();

            // 基线只写 EnemyDef 里真有的字段：护甲/闪避这类敌人表里还没有的属性保持 0，
            // 以后要加就走同一条 StatBlock 通路，不会出现"某处直接改了 HP"的旁路。
            Stats.SetBase(StatType.MaxHP, def.HP);
            Stats.SetBase(StatType.MoveSpeed, def.Speed);
            Stats.SetBase(StatType.ContactDamage, def.Damage);

            Position = position;
            PrevPosition = position;   // 出生第一帧两边相等，视图不会从原点插值过来
            HP = Stats[StatType.MaxHP];
            IsAlive = true;
        }

        public bool IsDead => HP <= 0f;

        /// <summary>身体半径，分离力用它算两个敌人该保持多远。</summary>
        public float Radius => Def.BodyRadius;

        public float Speed => Stats[StatType.MoveSpeed];

        public float ContactDamage => Stats[StatType.ContactDamage];
    }
}
