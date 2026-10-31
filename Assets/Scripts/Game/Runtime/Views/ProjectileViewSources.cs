using System.Collections.Generic;
using Game.Systems;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 场上"要画哪些投射物"以及每个来源的外观。玩家一把武器一个 ProjectileSystem，
    /// 每个旅伴也各有一个（CompanionSystem.Recruit 里为每个旅伴建一套 WeaponSystem），
    /// 所以"所有在飞的箭"是 1 + 旅伴数 个投射物池拼起来的。
    ///
    /// 合并成一个视图池，只按持有者区分颜色与大小（见 <see cref="Source"/>）：
    /// 每个旅伴各建一套池，池数会跟着队伍人数涨，而箭的外观本来就只有"谁的"这一个维度。
    ///
    /// 这个类是纯逻辑（不碰 GameObject），因为"哪几个来源、各自是谁、外观是什么"必须能验：
    /// 旅伴入队 / 升级（不改来源）/ 离队 / 同一槽位重新招募（换了一套 ProjectileSystem）
    /// 都要让视图层知道该建表还是该收表。真正的创建与回收在 ProjectileViewPool 里。
    ///
    /// 稳态不分配：Refresh 只在队伍真的变了的那一帧重建列表，列表本身复用。
    /// </summary>
    public sealed class ProjectileViewSources
    {
        /// <summary>玩家箭的颜色。暴击/命中的差异表现归 I1-10，这里只要求"看得见"。</summary>
        public static readonly Color PlayerTint = new Color(1f, 0.88f, 0.4f);

        /// <summary>
        /// 旅伴箭的颜色。与商店里旅伴卡片的强调色同一个青绿：玩家是暖黄的箭，旅伴是冷色的箭，
        /// 混战时不用数数量就知道这一发是谁打出去的。
        /// </summary>
        public static readonly Color CompanionTint = new Color(0.45f, 0.88f, 0.78f);

        /// <summary>旅伴箭相对玩家箭的尺寸倍数。颜色之外再压一档大小，色弱时也能分。</summary>
        public const float CompanionScale = 0.8f;

        /// <summary>
        /// 一个投射物来源。OwnerKey 是"谁的"的唯一编号：0 是玩家，槽位号 + 1 是旅伴
        /// （槽位号来自 CompanionModel.Slot，与 CompanionSystem.UnitIdOf 的编码差 1，
        /// 这里不用 UnitId 是为了不把视图的字典键和伤害归属的 id 搅在一起）。
        /// </summary>
        public readonly struct Source
        {
            public readonly int OwnerKey;
            public readonly ProjectileSystem Projectiles;
            public readonly Color Tint;

            /// <summary>相对玩家箭的尺寸倍数，玩家自己是 1。</summary>
            public readonly float Scale;

            public Source(int ownerKey, ProjectileSystem projectiles, Color tint, float scale)
            {
                OwnerKey = ownerKey;
                Projectiles = projectiles;
                Tint = tint;
                Scale = scale;
            }
        }

        readonly ProjectileSystem _player;
        readonly CompanionSystem _companions;

        // 按旅伴槽位号记"当前这一格用的是哪个投射物池"。旅伴离队时置空、同一槽位重新招募时换成新实例，
        // 两种情况都让 Refresh 报"变了"，视图池据此收掉旧持有者的实例。
        readonly ProjectileSystem[] _slotProjectiles = new ProjectileSystem[CompanionSystem.MaxCompanions];

        readonly List<Source> _sources = new List<Source>(1 + CompanionSystem.MaxCompanions);

        /// <summary>companions 可空：只装玩家战斗的场景（与 AppRoot 里其它视图池的容错一致）。</summary>
        public ProjectileViewSources(ProjectileSystem playerProjectiles, CompanionSystem companions)
        {
            _player = playerProjectiles;
            _companions = companions;

            // 构造时先扫一遍再建表：装配根建视图池时可能已经有旅伴了（调试场景里直接塞了人），
            // 不扫的话第一帧会把它们当成"没有来源"，箭要等下一次招募才出现
            Scan();
            Rebuild();
        }

        /// <summary>当前要画的来源：玩家在最前，之后按旅伴槽位号从小到大。</summary>
        public IReadOnlyList<Source> Sources => _sources;

        /// <summary>
        /// 来源表被重建过几次。视图池只靠 <see cref="Refresh"/> 的返回值对齐持有者表，
        /// 这个计数是给测试与调试用的：稳态下它不应该涨（涨了就是每帧都在收表建表）。
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// 每个渲染帧调一次：旅伴队伍变了就重建来源表并返回 true。
        /// 只比对投射物池的引用：等级不换武器，所以升级不会让这张表变；离队与重新招募都会。
        /// </summary>
        public bool Refresh()
        {
            if (!Scan()) return false;

            Rebuild();
            return true;
        }

        /// <summary>比对每个旅伴槽位上的投射物池，有变化就记下来并返回 true。</summary>
        bool Scan()
        {
            bool changed = false;

            for (int slot = 0; slot < _slotProjectiles.Length; slot++)
            {
                WeaponSystem weapons = _companions == null ? null : _companions.WeaponsOf(slot);
                ProjectileSystem projectiles = weapons == null ? null : weapons.Projectiles;
                if (ReferenceEquals(projectiles, _slotProjectiles[slot])) continue;

                _slotProjectiles[slot] = projectiles;
                changed = true;
            }

            return changed;
        }

        void Rebuild()
        {
            _sources.Clear();

            // 玩家缺席（装配失败）时不给这一项：宁可不画，也不给一个空引用让整个渲染帧掉出去
            if (_player != null) _sources.Add(new Source(0, _player, PlayerTint, 1f));

            for (int slot = 0; slot < _slotProjectiles.Length; slot++)
            {
                ProjectileSystem projectiles = _slotProjectiles[slot];
                if (projectiles == null) continue;

                _sources.Add(new Source(slot + 1, projectiles, CompanionTint, CompanionScale));
            }

            Version++;
        }
    }
}
