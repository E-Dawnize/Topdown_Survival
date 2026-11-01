using System;
using Game.Config;
using Game.Core.Events;
using UnityEngine;

namespace Game.Model
{
    /// <summary>
    /// 敌人的出手方式。由 EnemyDef.Behaviour 推导，不在配置里再加一列：
    /// 投手是远程、冲锋兵是冲锋，其余（含 BOSS）都是近战。
    /// </summary>
    public enum EnemyAttackKind
    {
        Melee,
        Charge,
        Ranged
    }

    /// <summary>
    /// 一次刷怪的强度参数。DirectorSystem 按时间轴算好倍率，EnemySystem 只负责把倍率写进模型：
    /// 刷怪品质（精英、后期高倍率）与普通刷怪走同一条构造路径，不另开一套敌人类。
    /// </summary>
    public readonly struct EnemySpawnOptions
    {
        public readonly float HpScale;
        public readonly float DamageScale;
        public readonly float SpeedScale;
        public readonly float VisualScale;
        public readonly float XpMultiplier;
        public readonly bool IsElite;
        public readonly bool IsBoss;

        public EnemySpawnOptions(float hpScale, float damageScale, float speedScale, float visualScale,
            float xpMultiplier, bool isElite, bool isBoss)
        {
            HpScale = hpScale;
            DamageScale = damageScale;
            SpeedScale = speedScale;
            VisualScale = visualScale;
            XpMultiplier = xpMultiplier;
            IsElite = isElite;
            IsBoss = isBoss;
        }

        public static readonly EnemySpawnOptions Default =
            new EnemySpawnOptions(1f, 1f, 1f, 1f, 1f, false, false);
    }

    /// <summary>
    /// 敌人的纯数据。位置用 Vector2 的 (x, z)，不碰 Transform，也不认识网格与视图。
    /// 属性走 StatBlock：EnemyDef 的数字乘上本段导演的倍率后只作为基线写进去。
    /// </summary>
    public sealed class EnemyModel
    {
        public readonly EnemyDef Def;
        public readonly StatBlock Stats;

        /// <summary>稳定实体 ID。槽位会被复用，这个 ID 不会；事件与投射物命中记录都用它。</summary>
        public readonly UnitId Id;

        /// <summary>这一只掉多少经验。精英按倍率放大，BOSS 由表里的 XpValue 直接给。</summary>
        public readonly int XpValue;

        /// <summary>视觉与判定共用的缩放。判定半径、视图尺寸都从这里来，不会出现"看着很大、打不到"。</summary>
        public readonly float VisualScale;

        public readonly bool IsElite;
        public readonly bool IsBoss;

        public Vector2 Position;
        public Vector2 Velocity;

        /// <summary>
        /// 击退冲量，单位是速度（米/秒），和 Velocity 是两份东西。
        /// Velocity 每步都由追击与分离力重算，把冲量写进去等于每帧清一次；
        /// 分开存以后，冲量按 EnemySystem 的速率自己衰减，追击的稳定性与它无关。
        /// </summary>
        public Vector2 Knockback;

        /// <summary>本步移动前的位置。渲染层在 PrevPosition 与 Position 之间插值。</summary>
        public Vector2 PrevPosition;

        /// <summary>当前血量。掉到 0 以下由清理阶段收走，模型自己不许改自己的存活标记。</summary>
        public float HP;

        /// <summary>最后一次打到它的伤害来源。清理阶段拿不到命中那一刻的归属，所以这里顺手记一笔。</summary>
        public UnitId LastDamageSource;

        /// <summary>还在场上。Remove 之后置 false，之后这个槽位可能被新敌人复用。</summary>
        public bool IsAlive;

        /// <summary>
        /// 距离下一次出手的剩余冷却。&lt;= 0 且进入攻击距离时开始起手；
        /// 不再代表“接触伤害现在就结算”——伤害只发生在起手结束的那一下。
        /// </summary>
        public float AttackTimer;

        /// <summary>起手剩余秒数。&gt; 0 表示正在蓄力，这一阶段不追击；归零的下一步结算命中/发射。</summary>
        public float AttackWindup;

        /// <summary>本次起手的总时长。表现层用它把预警环从大到小收拢，与模拟的出手时刻对齐。</summary>
        public float AttackWindupDuration;

        /// <summary>
        /// 起手时锁定的出手方向。锁定后不再追踪玩家，玩家侧移就能真正躲开；
        /// 不锁方向的话「看到预警再走」没有意义。
        /// </summary>
        public Vector2 AttackDirection;

        /// <summary>冲锋剩余秒数。&gt; 0 表示正在冲锋位移，这段时间不追击、不分离。</summary>
        public float ChargeTimer;

        /// <summary>本次冲锋是否已经撞到玩家。一次冲锋最多结算一次。</summary>
        public bool ChargeLanded;

        public bool IsWindingUp => AttackWindup > 0f;
        public bool IsCharging => ChargeTimer > 0f;

        public EnemyModel(EnemyDef def, Vector2 position, UnitId id, in EnemySpawnOptions options)
        {
            Def = def;
            Id = id;
            Stats = new StatBlock();

            Stats.SetBase(StatType.MaxHP, def.HP * options.HpScale);
            Stats.SetBase(StatType.MoveSpeed, def.Speed * options.SpeedScale);
            Stats.SetBase(StatType.ContactDamage, def.Damage * options.DamageScale);

            XpValue = Math.Max(0, Mathf.RoundToInt(def.XpValue * options.XpMultiplier));
            VisualScale = Math.Max(0.05f, def.Scale * options.VisualScale);
            IsElite = options.IsElite;
            IsBoss = options.IsBoss;

            Position = position;
            PrevPosition = position;   // 出生第一帧两边相等，视图不会从原点插值过来
            HP = MaxHP;
            IsAlive = true;
        }

        public bool IsDead => HP <= 0f;

        public float MaxHP => Stats[StatType.MaxHP];

        /// <summary>身体半径，分离力与命中判定用它算两个敌人该保持多远。</summary>
        public float Radius => Def.BodyRadius * VisualScale;

        public float Speed => Stats[StatType.MoveSpeed];

        public float ContactDamage => Stats[StatType.ContactDamage];

        /// <summary>按行为表推导的出手方式。表现层与 EnemySystem 共用，不各自 switch 一遍。</summary>
        public EnemyAttackKind AttackKind
        {
            get
            {
                switch (Def.Behaviour)
                {
                    case EnemyBehaviourType.Ranged: return EnemyAttackKind.Ranged;
                    case EnemyBehaviourType.Charge: return EnemyAttackKind.Charge;
                    default: return EnemyAttackKind.Melee;
                }
            }
        }
    }
}
