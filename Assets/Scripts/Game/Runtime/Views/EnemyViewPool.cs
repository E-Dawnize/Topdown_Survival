using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 敌人槽位 → 预制体实例的对应关系与回收池。按 EnemySystem 的槽位号存，
    /// 槽位是稳定的（SpatialGrid 保证 Remove 不会挪动别的槽位），视图不用每帧重新配对。
    ///
    /// 死了的敌人当帧回收成 inactive 并压回池里：场上不留空 GameObject，
    /// 刷怪峰值过后那批实例也不会扔——下一波同类型的敌人直接复用，不再 Instantiate。
    /// 池按敌人 Id 分开，不同类型不共用一个实例（共用会让 Crawler 顶着 Charger 的网格跑）。
    /// </summary>
    public sealed class EnemyViewPool
    {
        readonly UnitPrefabMap _prefabMap;
        readonly Transform _parent;
        readonly EnemySystem _enemies;

        readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>(64);
        readonly Dictionary<string, Stack<GameObject>> _idle = new Dictionary<string, Stack<GameObject>>(8);

        // 缺预制体只报一次：一帧刷 50 个同类型的怪，逐条报会把 Console 刷满
        readonly HashSet<string> _missingPrefabReported = new HashSet<string>();

        public EnemyViewPool(UnitPrefabMap prefabMap, Transform parent, EnemySystem enemies, IEventBus events)
        {
            _prefabMap = prefabMap;
            _parent = parent;
            _enemies = enemies;

            // 受击变亮走已有的伤害事件，不新造事件。退订交给 AppRoot：它销毁时 Events.Clear()
            events?.Subscribe<UnitDamaged>(OnUnitDamaged);
        }

        /// <summary>
        /// 一个渲染帧同步一次。alpha 由 RunLoop 按累加器余量算，视图只在
        /// PrevPosition 与 Position 之间插值，不读模拟的最终值。
        /// </summary>
        public void Sync(float alpha)
        {
            if (_enemies == null) return;

            int capacity = _enemies.SlotCapacity;
            for (int slot = 0; slot < capacity; slot++)
            {
                EnemyModel enemy = _enemies.Get(slot);
                bool alive = enemy != null && enemy.IsAlive;

                UnitView view = null;
                _views.TryGetValue(slot, out view);

                if (!alive)
                {
                    if (view != null) Recycle(slot, view);
                    continue;
                }

                // 槽位被新敌人复用了：旧实例可能是另一个怪种，换掉而不是改绑
                if (view != null && !ReferenceEquals(view.Enemy, enemy))
                {
                    Recycle(slot, view);
                    view = null;
                }

                if (view == null)
                {
                    view = Rent(slot, enemy);
                    if (view == null) continue;   // 没有预制体，缺哪个已经报过
                }

                view.Sync(alpha);
            }
        }

        UnitView Rent(int slot, EnemyModel enemy)
        {
            string id = enemy.Def.Id;
            GameObject prefab = _prefabMap == null ? null : _prefabMap.Find(id);
            if (prefab == null)
            {
                if (_missingPrefabReported.Add(id))
                {
                    Debug.LogError("[EnemyViewPool] UnitPrefabMap 里没有 '" + id + "' 的预制体，这种敌人看不见。" +
                                   "先跑一次 Tools/Game/生成预制体");
                }
                return null;
            }

            GameObject instance = null;
            Stack<GameObject> idle;
            if (_idle.TryGetValue(id, out idle) && idle.Count > 0)
            {
                instance = idle.Pop();
            }
            else
            {
                instance = Object.Instantiate(prefab, _parent);
                instance.name = id;
            }

            instance.SetActive(true);

            UnitView view = instance.GetComponent<UnitView>();
            if (view == null)
            {
                // 预制体上不挂 UnitView：资源归美术，这类纯表现组件运行时加，改代码不用动预制体
                view = instance.AddComponent<UnitView>();
            }

            view.Bind(enemy);
            _views[slot] = view;
            return view;
        }

        void Recycle(int slot, UnitView view)
        {
            _views.Remove(slot);

            string id = view.Enemy == null ? null : view.Enemy.Def.Id;
            GameObject instance = view.gameObject;
            instance.SetActive(false);

            if (string.IsNullOrEmpty(id)) return;

            Stack<GameObject> idle;
            if (!_idle.TryGetValue(id, out idle))
            {
                idle = new Stack<GameObject>(16);
                _idle.Add(id, idle);
            }
            idle.Push(instance);
        }

        void OnUnitDamaged(UnitDamaged e)
        {
            if (!e.Target.IsValid) return;

            UnitView view;
            if (_views.TryGetValue(EnemySystem.SlotOf(e.Target), out view)) view.Flash();
        }
    }
}
