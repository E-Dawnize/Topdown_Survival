using Game.Config;
using Game.Core;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 投射物的显示池。按 ProjectileSystem 的池槽位号存实例（槽位在一发的生命周期里不变），
    /// 一个共享材质的小方块，飞走以后隐藏、下一发复用。
    /// 不按序号存：活跃列表会做末尾交换，序号和"第几发"并不稳定；槽位号才稳定。
    /// </summary>
    public sealed class ProjectileViewPool
    {
        /// <summary>小方块的尺寸。0.32 米长对应一步 0.3 米的位移，看着像根箭而不是一个点。</summary>
        static readonly Vector3 BodyScale = new Vector3(0.08f, 0.08f, 0.32f);

        /// <summary>离地高度。方块原点在中心，放 0.5 米高才刚好浮在地面上方。</summary>
        const float BodyHeight = 0.5f;

        /// <summary>箭的颜色。暴击/命中的差异表现归 I1-10，这里只要求"看得见"。</summary>
        static readonly Color BodyColor = new Color(1f, 0.88f, 0.4f);

        readonly ProjectileSystem _projectiles;
        readonly Transform _parent;
        readonly Material _sharedMaterial;

        readonly ProjectileView[] _views;
        readonly bool[] _visible;

        public ProjectileViewPool(UnitPrefabMap prefabMap, ProjectileSystem projectiles, Transform parent)
        {
            _projectiles = projectiles;
            _parent = parent;
            _sharedMaterial = ResolveSharedMaterial(prefabMap);

            _views = new ProjectileView[_projectiles.Capacity];
            _visible = new bool[_views.Length];

            if (_sharedMaterial == null)
            {
                Debug.LogError("[ProjectileViewPool] 找不到可复用的共享材质，箭不会显示。" +
                               "玩家预制体得有一个 Renderer 带材质");
            }
        }

        /// <summary>一个渲染帧同步一次。alpha 由 RunLoop 按累加器余量算。</summary>
        public void Sync(float alpha)
        {
            if (_sharedMaterial == null) return;

            int count = _projectiles.Count;
            for (int i = 0; i < _visible.Length; i++) _visible[i] = false;

            for (int i = 0; i < count; i++)
            {
                if (!_projectiles.TryGetActive(i, out ProjectileSystem.Snapshot shot)) continue;

                ProjectileView view = Rent(shot.Slot);
                if (view == null) continue;

                view.Sync(shot.PrevPosition, shot.Position, alpha, shot.Direction);
                _visible[shot.Slot] = true;
            }

            // 这一帧没出现的（打中目标、飞满射程被回收）全部隐藏，实例留在池里等下一发
            for (int slot = 0; slot < _views.Length; slot++)
            {
                ProjectileView view = _views[slot];
                if (view == null || _visible[slot]) continue;
                if (view.gameObject.activeSelf) view.gameObject.SetActive(false);
            }
        }

        ProjectileView Rent(int slot)
        {
            ProjectileView view = _views[slot];
            if (view != null)
            {
                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                return view;
            }

            GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = "Projectile";
            instance.transform.SetParent(_parent, false);
            instance.transform.localScale = BodyScale;

            Vector3 p = instance.transform.position;
            p.y = BodyHeight;
            instance.transform.position = p;

            // 纯表现物件：自带 Collider 只会让物理系统白算
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            view = instance.AddComponent<ProjectileView>();
            view.Initialize(_sharedMaterial, BodyColor);
            _views[slot] = view;
            return view;
        }

        /// <summary>
        /// 复用一个已在项目里的共享材质，不用 new Material(Shader.Find(...))：
        /// 后者在打包时可能因为 shader 没被任何资产引用而被剔除，运行时变成粉色。
        /// </summary>
        static Material ResolveSharedMaterial(UnitPrefabMap prefabMap)
        {
            if (prefabMap == null) return null;

            GameObject unitPrefab = prefabMap.Find(AppRoot.PlayerPrefabId);
            if (unitPrefab == null) return null;

            Renderer renderer = unitPrefab.GetComponentInChildren<Renderer>();
            return renderer == null ? null : renderer.sharedMaterial;
        }
    }
}
