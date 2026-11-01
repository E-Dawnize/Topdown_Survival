using Game.Systems;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 敌人远程弹丸的显示池。与玩家的 ProjectileViewPool 分开：
    /// 这边只画 EnemyProjectileSystem 的弹丸，持有者恒为“敌人”，没有旅伴/玩家多来源的换色逻辑。
    ///
    /// 复用同一支箭 prefab，只换成暗红色并压小一档——投手扔出来的东西一眼要和玩家的箭分开。
    /// 位置按 PrevPosition → Position 插值，与玩家投射物同一个 alpha，60Hz 模拟在任意帧率下都不跳格。
    /// </summary>
    public sealed class EnemyProjectileViewPool
    {
        static readonly ProjectileView.Look EnemyLook =
            new ProjectileView.Look(new Color(1f, 0.30f, 0.26f), 0.8f);

        readonly EnemyProjectileSystem _projectiles;
        readonly Transform _parent;
        readonly GameObject _arrowPrefab;

        readonly ProjectileView[] _views;
        readonly bool[] _visible;

        bool _reportedMissingPrefab;

        public EnemyProjectileViewPool(EnemyProjectileSystem projectiles, Transform parent, GameObject arrowPrefab)
        {
            _projectiles = projectiles;
            _parent = parent;
            _arrowPrefab = arrowPrefab;

            int capacity = projectiles != null ? projectiles.Capacity : 0;
            _views = new ProjectileView[capacity];
            _visible = new bool[capacity];

            if (_arrowPrefab == null)
            {
                Debug.LogError("[EnemyProjectileViewPool] UnitPrefabMap 没有指定 ProjectileArrow，" +
                               "投手的弹丸不会显示。检查 Assets/Prefabs/UnitPrefabMap.asset");
            }
        }

        public void Sync(float alpha)
        {
            if (_projectiles == null) return;

            for (int i = 0; i < _visible.Length; i++) _visible[i] = false;

            int count = _projectiles.Count;
            for (int i = 0; i < count; i++)
            {
                if (!_projectiles.TryGetActive(i, out EnemyProjectileSystem.Snapshot shot)) continue;

                ProjectileView view = Rent(shot.Slot);
                if (view == null) continue;

                // 敌人弹丸没有弓把交接，直接按模拟位置画直线。
                view.Sync(shot.PrevPosition, shot.Position, alpha, shot.Direction,
                    shot.Position, shot.Position, false, default);
                _visible[shot.Slot] = true;
            }

            for (int slot = 0; slot < _views.Length; slot++)
            {
                ProjectileView view = _views[slot];
                if (view == null || _visible[slot]) continue;
                if (view.gameObject.activeSelf) view.gameObject.SetActive(false);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i] == null) continue;
                Object.Destroy(_views[i].gameObject);
                _views[i] = null;
            }
        }

        ProjectileView Rent(int slot)
        {
            if (slot < 0 || slot >= _views.Length) return null;

            ProjectileView view = _views[slot];
            if (view == null)
            {
                view = Create();
                if (view == null) return null;
                _views[slot] = view;
            }

            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            view.ApplyLook(EnemyLook);
            return view;
        }

        ProjectileView Create()
        {
            if (_arrowPrefab == null)
            {
                if (!_reportedMissingPrefab)
                {
                    _reportedMissingPrefab = true;
                    Debug.LogError("[EnemyProjectileViewPool] 没有 ProjectileArrow prefab，敌人弹丸不可见");
                }
                return null;
            }

            GameObject instance = Object.Instantiate(_arrowPrefab, _parent, false);
            instance.name = "EnemyProjectile";

            ProjectileView view = instance.GetComponent<ProjectileView>();
            if (view == null) view = instance.AddComponent<ProjectileView>();
            view.Initialize();
            instance.SetActive(false);
            return view;
        }
    }
}
