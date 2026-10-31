using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 一个持有者的全部武器。行为由 WeaponDef.BehaviourType 决定（技术方案 8.4 的扩展点，
    /// 表里填 Ranged / Melee，以后填新类名就接新行为），Kind 只做一致性校验：
    /// 表里 Kind 和 BehaviourType 写岔了直接抛，不静默退回 Kind——
    /// 静默退回会让"表里改成 Melee 却还在放箭"这种错误一直藏到美术资源做完才发现。
    ///
    /// 结构上只认注入进来的 EnemySystem / IRandom01 / 位置与朝向参数，拿不到 MonoBehaviour 与单例。
    /// 接进主循环的要求：每步先 EnemySystem.Tick 移动、再 Grid.Rebuild、然后才调本系统的 Tick，
    /// 否则武器读到的是上一 tick 的敌人位置。
    /// </summary>
    public sealed class WeaponSystem
    {
        public const string RangedBehaviourType = "Ranged";
        public const string MeleeBehaviourType = "Melee";

        readonly EnemySystem _enemies;
        readonly IRandom01 _rng;

        /// <summary>命中事件往哪发。近战行为需要它才能和远程一样发 UnitDamaged；可空：武器单测不用事件总线。</summary>
        readonly IEventBus _events;

        readonly List<WeaponModel> _weapons = new List<WeaponModel>(4);

        public WeaponSystem(EnemySystem enemies, IRandom01 rng, UnitId shooter, int projectileCapacity = 256,
            IEventBus events = null)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _events = events;
            Shooter = shooter;
            Projectiles = new ProjectileSystem(rng, projectileCapacity, events);
        }

        /// <summary>这把武器打出来的伤害记在谁头上。</summary>
        public UnitId Shooter { get; }

        public ProjectileSystem Projectiles { get; }

        public IReadOnlyList<WeaponModel> Weapons => _weapons;

        /// <summary>按表加一件武器。BehaviourType 未知或与 Kind 冲突会抛 ArgumentException。</summary>
        public WeaponModel AddWeapon(WeaponDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            IWeaponBehaviour behaviour = CreateBehaviour(def);
            var weapon = new WeaponModel(def, behaviour);
            _weapons.Add(weapon);
            return weapon;
        }

        /// <summary>每步推进一次：投射物先走完这一段，再让各件武器决定开不开火。</summary>
        public void Tick(Vector2 shooterPosition, Vector2 facing, in ShooterStats stats, float dt)
        {
            // 顺序是刻意的：这一 tick 新生成的投射物从下一 tick 才开始移动，
            // 免得"刚出膛就吃掉一整步位移"——同样的输入在不同的步数切分下会命中不同的目标
            Projectiles.Tick(dt, _enemies);

            for (int i = 0; i < _weapons.Count; i++)
            {
                WeaponModel weapon = _weapons[i];
                weapon.Tick(dt);
                weapon.Behaviour.Tick(weapon, shooterPosition, facing, stats, dt);
            }
        }

        IWeaponBehaviour CreateBehaviour(WeaponDef def)
        {
            if (string.IsNullOrEmpty(def.BehaviourType))
            {
                // 表里空着就按 Kind 兜底。Kind 是枚举，导表已经校验过，不会指向不存在的行为
                return def.Kind == WeaponKind.Melee ? NewMelee() : NewRanged();
            }

            switch (def.BehaviourType)
            {
                case RangedBehaviourType:
                    RequireKind(def, WeaponKind.Ranged);
                    return NewRanged();
                case MeleeBehaviourType:
                    RequireKind(def, WeaponKind.Melee);
                    return NewMelee();
                default:
                    throw new ArgumentException(
                        "未知的 BehaviourType：" + def.BehaviourType + "（武器 " + def.Id + "）", nameof(def));
            }
        }

        IWeaponBehaviour NewRanged() => new RangedBehaviour(_enemies, Projectiles, _rng, Shooter);

        IWeaponBehaviour NewMelee() => new MeleeBehaviour(_enemies, _rng, Shooter, _events);

        static void RequireKind(WeaponDef def, WeaponKind expected)
        {
            if (def.Kind == expected) return;

            throw new ArgumentException(
                "武器 " + def.Id + " 的 Kind 是 " + def.Kind + "，BehaviourType 却是 " + def.BehaviourType,
                nameof(def));
        }
    }
}
