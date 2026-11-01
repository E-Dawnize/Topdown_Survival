using Game.Config;
using Game.Systems;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 掉落物的显示池。按 PickupSystem 的池槽位号存实例（槽位在一枚掉落物的生命周期里不变），
    /// 做法与 ProjectileViewPool 一致：一个共享材质 + 每个实例一份 MaterialPropertyBlock。
    ///
    /// 经验与心共用一个实例池：槽位会被下一枚掉落的另一种掉落物复用，
    /// 所以外形（形状 / 颜色 / 离地高度）在每次取出时按种类换，而不是每种建一套池子。
    /// </summary>
    public sealed class PickupViewPool
    {
        /// <summary>
        /// 经验球按品阶换颜色与大小：普通怪是青色小晶体，高阶怪依次变成蓝/紫/橙，
        /// 精英是金色，BOSS 是最大的金红色球。数值仍由 Enemy.XpValue 决定，颜色只负责“这一球值多少”的可读性。
        /// </summary>
        static readonly PickupView.Look[] ExperienceLooks =
        {
            new PickupView.Look(new Vector3(0.26f, 0.10f, 0.26f), 0.32f, new Color(0.35f, 0.95f, 0.85f)),
            new PickupView.Look(new Vector3(0.30f, 0.12f, 0.30f), 0.34f, new Color(0.35f, 0.70f, 1.00f)),
            new PickupView.Look(new Vector3(0.35f, 0.14f, 0.35f), 0.37f, new Color(0.72f, 0.45f, 1.00f)),
            new PickupView.Look(new Vector3(0.42f, 0.17f, 0.42f), 0.40f, new Color(1.00f, 0.70f, 0.22f)),
            new PickupView.Look(new Vector3(0.54f, 0.22f, 0.54f), 0.46f, new Color(1.00f, 0.86f, 0.28f))
        };

        /// <summary>心的外形：圆球，比经验晶体高一点（心更重要，也更显眼）。</summary>
        static readonly PickupView.Look HeartLook = new PickupView.Look(
            new Vector3(0.30f, 0.30f, 0.30f), 0.5f, new Color(0.95f, 0.28f, 0.35f));

        readonly PickupSystem _pickups;
        readonly Transform _parent;
        readonly Material _sharedMaterial;

        readonly PickupView[] _views;
        readonly bool[] _visible;

        public PickupViewPool(UnitPrefabMap prefabMap, PickupSystem pickups, Transform parent)
        {
            _pickups = pickups;
            _parent = parent;
            _sharedMaterial = SharedViewMaterial.FromPlayerPrefab(prefabMap);

            _views = new PickupView[PickupSystem.Capacity];
            _visible = new bool[_views.Length];

            if (_sharedMaterial == null)
            {
                Debug.LogError("[PickupViewPool] 找不到可复用的共享材质，经验与心不会显示。" +
                               "玩家预制体得有一个 Renderer 带材质");
            }
        }

        /// <summary>一个渲染帧同步一次。</summary>
        public void Sync()
        {
            if (_sharedMaterial == null) return;

            for (int i = 0; i < _visible.Length; i++) _visible[i] = false;

            int count = _pickups.Count;
            for (int i = 0; i < count; i++)
            {
                if (!_pickups.TryGetActive(i, out PickupSystem.Snapshot pickup)) continue;

                PickupView view = Rent(pickup.Slot);
                if (view == null) continue;

                PickupView.Look look = pickup.Kind == PickupKind.Heart
                    ? HeartLook
                    : ExperienceLooks[TierIndex(pickup.Tier)];
                view.Apply(look);
                view.Sync(pickup.Position, pickup.Magnetized);
                _visible[pickup.Slot] = true;
            }

            // 这一帧没出现的（被捡走、被容量顶掉）全部隐藏，实例留在池里等下一枚
            for (int slot = 0; slot < _views.Length; slot++)
            {
                PickupView view = _views[slot];
                if (view == null || _visible[slot]) continue;
                if (view.gameObject.activeSelf) view.gameObject.SetActive(false);
            }
        }

        static int TierIndex(PickupTier tier)
        {
            return Mathf.Clamp((int)tier - 1, 0, ExperienceLooks.Length - 1);
        }

        PickupView Rent(int slot)
        {
            PickupView view = _views[slot];
            if (view != null)
            {
                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                return view;
            }

            // 一个球体就够：经验与心的差别靠压扁与颜色，不必为两种掉落物各建一套预制体
            view = PrimitiveViewFactory.Create<PickupView>("Pickup", PrimitiveType.Sphere, _parent, _sharedMaterial);
            _views[slot] = view;
            return view;
        }
    }
}
