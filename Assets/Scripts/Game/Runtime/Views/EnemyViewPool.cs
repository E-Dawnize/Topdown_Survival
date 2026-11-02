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
    public sealed class EnemyViewPool : System.IDisposable
    {
        readonly UnitPrefabMap _prefabMap;
        readonly Transform _parent;
        readonly EnemySystem _enemies;
        readonly IEventBus _events;

        readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>(64);
        readonly Dictionary<string, Stack<GameObject>> _idle = new Dictionary<string, Stack<GameObject>>(8);

        // 缺预制体只报一次：一帧刷 50 个同类型的怪，逐条报会把 Console 刷满
        readonly HashSet<string> _missingPrefabReported = new HashSet<string>();

        bool _disposed;

        public EnemyViewPool(UnitPrefabMap prefabMap, Transform parent, EnemySystem enemies, IEventBus events)
        {
            _prefabMap = prefabMap;
            _parent = parent;
            _enemies = enemies;
            _events = events;

            // 受击变亮走已有的伤害事件，不新造事件。
            // 退订必须显式做：EventBus.Clear 只有在装配根正常销毁时才发生，池被单独丢弃时
            // 留在总线上的这个委托会把整个池连同它的视图字典一起拖住。
            _events?.Subscribe<UnitDamaged>(OnUnitDamaged);
            _events?.Subscribe<EnemyAttackStarted>(OnAttackStarted);
            _events?.Subscribe<EnemyAttackStrike>(OnAttackStrike);
        }

        /// <summary>
        /// 退订并释放池拥有的实例。装配根在 OnDestroy 里调（与 Events.Clear 同一时机），
        /// 不要只依赖 EventBus.Clear：那是“总线没了”，不是“池走了”。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _events?.Unsubscribe<UnitDamaged>(OnUnitDamaged);
            _events?.Unsubscribe<EnemyAttackStarted>(OnAttackStarted);
            _events?.Unsubscribe<EnemyAttackStrike>(OnAttackStrike);

            foreach (UnitView view in _views.Values)
            {
                if (view != null) DestroyInstance(view.gameObject);
            }

            foreach (Stack<GameObject> idle in _idle.Values)
            {
                while (idle.Count > 0)
                {
                    GameObject instance = idle.Pop();
                    if (instance != null) DestroyInstance(instance);
                }
            }

            _views.Clear();
            _idle.Clear();
            _missingPrefabReported.Clear();
        }

        static void DestroyInstance(GameObject instance)
        {
            if (Application.isPlaying) Object.Destroy(instance);
            else Object.DestroyImmediate(instance);
        }

        /// <summary>
        /// 一个渲染帧同步一次。alpha 由 RunLoop 按累加器余量算，视图只在
        /// PrevPosition 与 Position 之间插值，不读模拟的最终值。
        /// </summary>
        public void Sync(float alpha)
        {
            if (_disposed || _enemies == null) return;

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
            // 资源键是 PrefabId 而不是敌人 Id：新怪种可以共用已有几何体（靠颜色与缩放区分），
            // 这样加一行 Enemy 表不用同时做一份预制体。
            string prefabId = string.IsNullOrEmpty(enemy.Def.PrefabId) ? enemy.Def.Id : enemy.Def.PrefabId;
            GameObject prefab = _prefabMap == null ? null : _prefabMap.Find(prefabId);
            if (prefab == null)
            {
                if (_missingPrefabReported.Add(prefabId))
                {
                    Debug.LogError("[EnemyViewPool] UnitPrefabMap 里没有 '" + prefabId + "' 的预制体，这种敌人看不见。" +
                                   "检查 Enemy.PrefabId 或 UnitPrefabMap");
                }
                return null;
            }

            GameObject instance = null;
            Stack<GameObject> idle;
            if (_idle.TryGetValue(prefabId, out idle) && idle.Count > 0)
            {
                instance = idle.Pop();
            }
            else
            {
                instance = Object.Instantiate(prefab, _parent);
                instance.name = prefabId;
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

            string prefabId = view.Enemy == null
                ? null
                : (string.IsNullOrEmpty(view.Enemy.Def.PrefabId) ? view.Enemy.Def.Id : view.Enemy.Def.PrefabId);
            GameObject instance = view.gameObject;
            instance.SetActive(false);

            if (string.IsNullOrEmpty(prefabId)) return;

            Stack<GameObject> idle;
            if (!_idle.TryGetValue(prefabId, out idle))
            {
                idle = new Stack<GameObject>(16);
                _idle.Add(prefabId, idle);
            }
            idle.Push(instance);
        }

        void OnUnitDamaged(UnitDamaged e)
        {
            // 统一伤害入口也会为旅伴发 UnitDamaged；敌人视图池只处理敌人目标。
            if (e.Target.Kind != UnitKind.Enemy) return;
            if (!e.Target.IsValid || e.Amount <= 0f) return;

            // 事件带的是稳定 UnitId；槽位只用于本池的实例索引，不能再用 id.Value - 1 反推。
            if (_enemies.TryGetSlot(e.Target, out int slot) && _views.TryGetValue(slot, out UnitView view))
            {
                view.Flash();
            }

            // 飘字用事件里自带的命中位置：清阶段可能已经把尸体移除，槽位查不到时照样能显示。
            EnemyModel enemy = _enemies.Get(slot);
            float height = enemy != null ? 0.70f * enemy.VisualScale + 0.80f : 1.0f;
            DamageNumbers.Show(new Vector3(e.X, height, e.Z), e.Amount, e.IsCrit);
        }

        /// <summary>
        /// 起手预警：身体下蹲 + 脚下的红环从大收拢。环的时长与模拟的起手时长完全一致，
        /// 玩家看到环收完的那一刻就是真正结算的那一步。
        /// </summary>
        void OnAttackStarted(EnemyAttackStarted e)
        {
            if (!_enemies.TryGetSlot(e.Attacker, out int slot)) return;

            EnemyModel enemy = _enemies.Get(slot);
            if (enemy == null) return;

            if (_views.TryGetValue(slot, out UnitView view)) view.PlayAttackWindup(e.Duration, e.Angle);

            float radius = Mathf.Max(0.35f, enemy.Radius + 0.30f);
            CombatEffects.Ring(new Vector3(e.X, 0.05f, e.Z), radius * 1.9f, radius,
                new Color(1f, 0.26f, 0.20f, 0.85f), e.Duration);
        }

        /// <summary>
        /// 真正出手：近战/冲锋画红色扇形，远程在枪口位置闪一下。
        /// 伤害是否命中不在表现层判断，这里只负责让玩家看见“这一下打出来了”。
        /// </summary>
        void OnAttackStrike(EnemyAttackStrike e)
        {
            if (!_enemies.TryGetSlot(e.Attacker, out int slot)) return;

            EnemyModel enemy = _enemies.Get(slot);
            if (enemy == null) return;

            if (_views.TryGetValue(slot, out UnitView view)) view.PlayAttackStrike(e.Angle, e.Reach);

            if (e.Kind == EnemyAttackKind.Ranged)
            {
                CombatEffects.Ring(new Vector3(e.X, 0.05f, e.Z), 0.25f, Mathf.Max(0.8f, enemy.Radius + 0.4f),
                    new Color(1f, 0.35f, 0.22f, 0.75f), 0.16f);
                return;
            }

            float halfAngle = e.HalfAngleDeg > 0f
                ? e.HalfAngleDeg
                : (e.Kind == EnemyAttackKind.Charge ? 18f : 45f);
            float reach = e.Reach > 0.1f ? e.Reach : Mathf.Max(0.6f, enemy.Radius + PlayerModel.BodyRadius);
            Vector2 aim = new Vector2(Mathf.Cos(e.Angle), Mathf.Sin(e.Angle));
            CombatEffects.Slash(new Vector3(e.X, 0.06f, e.Z), aim, reach, halfAngle,
                new Color(1f, 0.24f, 0.18f, 0.9f), 0f, e.Kind == EnemyAttackKind.Charge ? 0.16f : 0.20f);
        }
    }
}
