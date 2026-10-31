using System.Collections.Generic;
using Game.Config;
using Game.Systems;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 投射物的显示池。玩家与所有旅伴共用这一个池：按"持有者 + 持有者自己的池槽位号"存实例，
    /// 一个共享材质的小方块，飞走以后隐藏、下一次任何人的箭都可以复用。
    ///
    /// 为什么合并成一个池而不是每个旅伴各建一套：旅伴最多 4 个，各自一套池就是 5 套实例池、
    /// 5 份槽位表，而箭的外观本来只有"谁的"这一个维度——按持有者换颜色与大小就够了
    /// （玩家的箭是暖黄的，旅伴的箭是冷的青绿、压小一档，见 ProjectileViewSources）。
    ///
    /// 不按序号存：活跃列表会做末尾交换，序号和"第几发"并不稳定；池槽位号才稳定。
    /// 持有者表只在队伍变化的那一帧重建（ProjectileViewSources.Refresh），稳态下每帧只读表。
    /// </summary>
    public sealed class ProjectileViewPool
    {
        /// <summary>一个持有者（玩家或某个旅伴）的视图表。大小等于它自己那个 ProjectileSystem 的容量。</summary>
        sealed class OwnerViews
        {
            public ProjectileSystem Projectiles;
            public ProjectileView.Look Look;
            public ProjectileView[] Views;
            public bool[] Visible;
        }

        readonly ProjectileViewSources _sources;
        readonly Transform _parent;
        readonly Material _sharedMaterial;

        readonly Dictionary<int, OwnerViews> _owners = new Dictionary<int, OwnerViews>(1 + CompanionSystem.MaxCompanions);

        // 所有持有者共用的一堆空实例。持有者表被重建（旅伴离队 / 同一槽位重新招募）时，
        // 收下来的实例压回这里，下一张表直接拿去用，不再 Instantiate
        readonly Stack<ProjectileView> _idle = new Stack<ProjectileView>(64);

        // 收表时用的临时表：Dictionary 边遍历边删会抛异常
        readonly List<int> _staleOwnerKeys = new List<int>(1 + CompanionSystem.MaxCompanions);

        /// <summary>companions 可空：只装玩家战斗的场景照旧只有玩家那一路箭。</summary>
        public ProjectileViewPool(UnitPrefabMap prefabMap, ProjectileSystem playerProjectiles,
            CompanionSystem companions, Transform parent)
        {
            _parent = parent;
            _sharedMaterial = SharedViewMaterial.FromPlayerPrefab(prefabMap);
            _sources = new ProjectileViewSources(playerProjectiles, companions);

            if (_sharedMaterial == null)
            {
                Debug.LogError("[ProjectileViewPool] 找不到可复用的共享材质，箭不会显示。" +
                               "玩家预制体得有一个 Renderer 带材质");
                return;
            }

            RebuildOwners();
        }

        /// <summary>一个渲染帧同步一次。alpha 由 RunLoop 按累加器余量算。</summary>
        public void Sync(float alpha)
        {
            if (_sharedMaterial == null) return;

            // 队伍变了（招募 / 离队 / 同槽位换人）：先把持有者表对齐，之后每帧都不再分配
            if (_sources.Refresh()) RebuildOwners();

            foreach (KeyValuePair<int, OwnerViews> pair in _owners)
            {
                OwnerViews owner = pair.Value;

                bool[] visible = owner.Visible;
                for (int i = 0; i < visible.Length; i++) visible[i] = false;

                int count = owner.Projectiles.Count;
                for (int i = 0; i < count; i++)
                {
                    if (!owner.Projectiles.TryGetActive(i, out ProjectileSystem.Snapshot shot)) continue;

                    ProjectileView view = Rent(owner, shot.Slot);
                    if (view == null) continue;

                    view.Sync(shot.PrevPosition, shot.Position, alpha, shot.Direction);
                    visible[shot.Slot] = true;
                }

                // 这一帧没出现的（打中目标、飞满射程被回收）全部隐藏，实例留在本持有者的表里等下一发
                for (int slot = 0; slot < owner.Views.Length; slot++)
                {
                    ProjectileView view = owner.Views[slot];
                    if (view == null || visible[slot]) continue;
                    if (view.gameObject.activeSelf) view.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 让持有者表与来源表对齐：不再存在的持有者收表（实例回共用池），
        /// 新出现的建表，同一槽位换了一套投射物池（重新招募）的也按"收表 + 建表"处理。
        /// </summary>
        void RebuildOwners()
        {
            IReadOnlyList<ProjectileViewSources.Source> sources = _sources.Sources;

            _staleOwnerKeys.Clear();
            foreach (KeyValuePair<int, OwnerViews> pair in _owners)
            {
                if (!HasSource(sources, pair.Key)) _staleOwnerKeys.Add(pair.Key);
            }

            for (int i = 0; i < _staleOwnerKeys.Count; i++) Release(_staleOwnerKeys[i]);

            for (int i = 0; i < sources.Count; i++)
            {
                ProjectileViewSources.Source source = sources[i];

                _owners.TryGetValue(source.OwnerKey, out OwnerViews owner);

                if (owner != null && !ReferenceEquals(owner.Projectiles, source.Projectiles))
                {
                    // 同一槽位重新招募：旧的投射物池已经随旅伴一起丢掉了，它表里的视图不能再画新箭
                    Release(source.OwnerKey);
                    owner = null;
                }

                if (owner == null) _owners.Add(source.OwnerKey, CreateOwner(source));
            }
        }

        static bool HasSource(IReadOnlyList<ProjectileViewSources.Source> sources, int ownerKey)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].OwnerKey == ownerKey) return true;
            }
            return false;
        }

        static OwnerViews CreateOwner(in ProjectileViewSources.Source source)
        {
            int capacity = source.Projectiles.Capacity;

            return new OwnerViews
            {
                Projectiles = source.Projectiles,
                Look = new ProjectileView.Look(source.Tint, source.Scale),
                Views = new ProjectileView[capacity],
                Visible = new bool[capacity]
            };
        }

        /// <summary>
        /// 收掉一个持有者的整张表：实例全部隐藏并压回共用池。
        /// 不销毁：下一个招募的旅伴接着用，省一次 Instantiate 与一次材质重建。
        /// </summary>
        void Release(int ownerKey)
        {
            if (!_owners.TryGetValue(ownerKey, out OwnerViews owner)) return;
            _owners.Remove(ownerKey);

            for (int i = 0; i < owner.Views.Length; i++)
            {
                ProjectileView view = owner.Views[i];
                if (view == null) continue;

                if (view.gameObject.activeSelf) view.gameObject.SetActive(false);
                _idle.Push(view);
            }
        }

        ProjectileView Rent(OwnerViews owner, int slot)
        {
            ProjectileView view = owner.Views[slot];
            if (view == null)
            {
                view = _idle.Count > 0 ? _idle.Pop() : CreateView();
                if (view == null) return null;
                owner.Views[slot] = view;
            }

            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            view.ApplyLook(owner.Look);
            return view;
        }

        ProjectileView CreateView()
        {
            if (_sharedMaterial == null) return null;

            GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = "Projectile";
            instance.transform.SetParent(_parent, false);

            // 纯表现物件：自带 Collider 只会让物理系统白算
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            ProjectileView view = instance.AddComponent<ProjectileView>();
            view.Initialize(_sharedMaterial);
            return view;
        }
    }
}
