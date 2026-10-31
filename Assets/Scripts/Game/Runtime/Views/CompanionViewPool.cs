using Game.Config;
using Game.Model;
using Game.Systems;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 旅伴槽位 → 胶囊实例的对应关系。按 CompanionSystem 的槽位号存（槽位在一局里是稳定的，
    /// 离队后会被下一个旅伴复用），与 EnemyViewPool 同一个做法。
    ///
    /// 与 EnemyViewPool 的差别只有两处，都是因为旅伴的规模小得多（最多 4 个）：
    ///   - 几何体是代码建的一个胶囊，不查 UnitPrefabMap：Companion.json 里没有"预制体"这一列，
    ///     这一版也不许改表与加预制体；
    ///   - 离队不做"压回池里再等下一个"：实例按槽位留着，只切 inactive。一个槽位最多一个实例，
    ///     4 个槽位就是 4 个物体，销毁再重建省不下什么，反而要额外记住"这个槽位现在没有实例"。
    ///
    /// 旅伴入队 / 离队时实例跟着增减，全部由每个渲染帧的这一趟同步完成：视图层不订阅招募事件，
    /// 也就不会出现"事件与模型状态差一拍"（事件在模拟步里发，渲染帧晚半帧读模型才是对的）。
    /// </summary>
    public sealed class CompanionViewPool
    {
        readonly CompanionSystem _companions;
        readonly Transform _parent;
        readonly Material _sharedMaterial;

        readonly CompanionView[] _views;

        public CompanionViewPool(UnitPrefabMap prefabMap, CompanionSystem companions, Transform parent)
        {
            _companions = companions;
            _parent = parent;
            _sharedMaterial = SharedViewMaterial.FromPlayerPrefab(prefabMap);

            _views = new CompanionView[companions == null ? 0 : companions.SlotCapacity];

            if (_sharedMaterial == null)
            {
                Debug.LogError("[CompanionViewPool] 找不到可复用的共享材质，旅伴不会显示。" +
                               "玩家预制体得有一个 Renderer 带材质");
            }
        }

        /// <summary>一个渲染帧同步一次。alpha 由 RunLoop 按累加器余量算，与敌人、投射物同一个值。</summary>
        public void Sync(float alpha)
        {
            // 没有共享材质就不建实例：胶囊会用内置材质亮成一个白模，还不如什么都不显示
            if (_sharedMaterial == null || _companions == null) return;

            for (int slot = 0; slot < _views.Length; slot++)
            {
                CompanionModel companion = _companions.Get(slot);
                if (companion == null)
                {
                    // 离队（祭坛献祭、以后的卖出）：实例留在本槽位上，只是看不见
                    CompanionView idle = _views[slot];
                    if (idle != null && idle.gameObject.activeSelf) idle.gameObject.SetActive(false);
                    continue;
                }

                CompanionView view = _views[slot];

                // 槽位被下一个旅伴复用了（阵营可能不同）：重新上色并吸附到它的位置，不沿用上一个的
                if (view != null && !ReferenceEquals(view.Companion, companion))
                {
                    view.gameObject.SetActive(true);
                    view.Bind(companion);
                }

                if (view == null) view = Create(slot, companion);
                if (view == null) continue;

                view.Sync(alpha);
            }
        }

        CompanionView Create(int slot, CompanionModel companion)
        {
            GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            instance.name = "Companion_" + slot;
            instance.transform.SetParent(_parent, false);
            instance.transform.localScale = CompanionViewStyle.BodyScale;
            instance.transform.position = new Vector3(companion.Position.x, CompanionViewStyle.BodyCenterHeight,
                companion.Position.y);

            // 纯表现物件：自带 Collider 只会让物理系统白算
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            CompanionView view = instance.AddComponent<CompanionView>();
            view.Initialize(_sharedMaterial);
            view.Bind(companion);

            _views[slot] = view;
            return view;
        }
    }
}
