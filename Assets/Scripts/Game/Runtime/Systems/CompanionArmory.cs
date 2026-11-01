using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 旅伴的武器持有与开火。每个槽位一套 WeaponSystem，Shooter 是旅伴自己的 UnitId，
    /// 所以伤害、受击反馈与击杀归属记在旅伴头上，不会算成玩家的输出。
    ///
    /// 与花名册/羁绊分开：招募时 Equip、离队时 Unequip、换局时 Clear，Tick 只读模型与敌人网格，
    /// 不重算羁绊、不碰玩家属性。旅伴在本阶段没有武器成长——升级走 CompanionModel 的属性修正，
    /// WeaponModel 一直停在 1 级（CompanionSystem 的 weaponRules 兼容重载被显式忽略）。
    ///
    /// 开火排在玩家开火之后（RunSimulation 第 5 步末尾），前提是敌人网格已经 Rebuild 过：
    /// 旅伴的武器与玩家共用同一个 EnemySystem 和同一份位置快照，不另开线程也不用协程。
    /// 稳态不分配：ShooterStats 是结构体，索敌结果容器逐部件复用。
    /// </summary>
    internal sealed class CompanionArmory
    {
        /// <summary>索敌扫描的兜底半径：武器 Range 填了 0 或负数时，至少给朝向一个有意义的方向来源。</summary>
        const float MinAimRadius = 1f;

        readonly CompanionModel[] _slots;
        readonly WeaponSystem[] _systems = new WeaponSystem[CompanionSystem.MaxCompanions];
        readonly EnemySystem _enemies;
        readonly IRandom01 _rng;
        readonly IEventBus _events;

        // 每步索敌结果复用同一个容器：这条路径每 tick 调"旅伴数"次，自己 new 就是稳定 GC。
        // 容器逐调用者持有：EnemySystem 的共享索敌只借用，不缓存调用方的容器。
        readonly List<int> _targets = new List<int>(16);

        public CompanionArmory(CompanionModel[] slots, EnemySystem enemies, IRandom01 rng, IEventBus events)
        {
            _slots = slots;
            _enemies = enemies;
            _rng = rng;
            _events = events;
        }

        /// <summary>当前槽位上的武器系统。槽位空或越界返回 null。</summary>
        public WeaponSystem WeaponsOf(int slot)
        {
            return slot >= 0 && slot < _systems.Length ? _systems[slot] : null;
        }

        /// <summary>给槽位装配一套新的武器系统。只在 Recruit 占到这个槽位时调。</summary>
        public WeaponSystem Equip(int slot, WeaponDef def, UnitId shooter)
        {
            var weapons = new WeaponSystem(_enemies, _rng, shooter, CompanionSystem.ProjectileCapacity, _events);
            weapons.AddWeapon(def);
            _systems[slot] = weapons;
            return weapons;
        }

        /// <summary>卸下一套武器系统（旅伴离队）。武器随系统一起丢掉，不需要单独退。</summary>
        public void Unequip(int slot)
        {
            if (slot >= 0 && slot < _systems.Length) _systems[slot] = null;
        }

        /// <summary>换局清空全部武器系统：新一轮不能带着上一轮旅伴的武器继续打。</summary>
        public void Clear()
        {
            for (int i = 0; i < _systems.Length; i++) _systems[i] = null;
        }

        /// <summary>
        /// 每个旅伴开火一次。加成从旅伴自己的 StatBlock 现读：等级修正与羁绊都写在那里面，这里不再算一遍。
        /// </summary>
        public void Tick(Vector2 playerPos, float dt)
        {
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                CompanionModel companion = _slots[slot];
                if (companion == null) continue;

                WeaponSystem weapons = _systems[slot];
                ShooterStats stats = ShooterStats.From(companion.Stats);

                // 远程只在贴身重合时用朝向兜底，近战的扇形判定必须有朝向：一律朝射程内最近的敌人
                Vector2 facing = AimDirection(companion, playerPos);

                weapons.Tick(companion.Position, facing, stats, dt);
            }
        }

        /// <summary>
        /// 朝射程内最近的活目标。近战的扇形判定直接拿它当方向，远程只在目标与射手重合时兜底用。
        /// 扫不到目标时朝玩家：没有目标就没有可打的方向，这个朝向不会造成误伤。
        /// 索敌走 EnemySystem 的共享 API，与远程武器同一套"跳过死亡/已移除槽位"的复核语义。
        /// </summary>
        Vector2 AimDirection(CompanionModel companion, Vector2 playerPos)
        {
            float range = companion.Weapon.Range > MinAimRadius ? companion.Weapon.Range : MinAimRadius;
            EnemyModel target = _enemies.FindNearestAlive(companion.Position, range, _targets);

            if (target != null)
            {
                Vector2 toTarget = target.Position - companion.Position;
                if (toTarget.sqrMagnitude > 1e-8f) return toTarget.normalized;
            }

            Vector2 toPlayer = playerPos - companion.Position;
            return toPlayer.sqrMagnitude > 1e-8f ? toPlayer.normalized : Vector2.up;
        }
    }
}
