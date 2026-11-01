using System.Collections.Generic;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 玩家身上的武器架。武器视觉由 catalog 里的 prefab 实例化，不再现场拼几何体。
    /// 每个渲染帧用 SmoothDamp 追“本地槽位 + 玩家速度拖尾”，像旅伴一样落后一点再追上来。
    /// 位置跟玩家根节点，世界 yaw 用 SmoothDampAngle 追玩家朝向：玩家根节点瞬时转时，
    /// 武器排平滑甩到新一侧，不会跟着瞬移；瞄准仍由 WeaponVisual 按真实 AimDirection 换算。
    /// </summary>
    public sealed class WeaponRackView : MonoBehaviour
    {
        readonly List<WeaponVisual> _visuals = new List<WeaponVisual>(4);
        readonly List<WeaponModel> _models = new List<WeaponModel>(4);
        readonly List<Vector3> _anchors = new List<Vector3>(4);
        readonly List<Vector3> _anchorVelocities = new List<Vector3>(4);

        WeaponVisualCatalog _catalog;
        float _yaw;
        float _yawVelocity;

        public static WeaponRackView Attach(Transform parent, WeaponVisualCatalog catalog)
        {
            if (parent == null) return null;

            if (catalog == null)
            {
                Debug.LogError("[WeaponRackView] 找不到 WeaponVisualCatalog，" +
                               "检查 Assets/Resources/WeaponVisualCatalog.asset 是否生成");
                return null;
            }

            var go = new GameObject("WeaponRack");
            go.transform.SetParent(parent, false);

            WeaponRackView rack = go.AddComponent<WeaponRackView>();
            rack._catalog = catalog;
            rack._yaw = parent.eulerAngles.y;
            return rack;
        }

        public void Sync(WeaponSystem weapons, Vector2 playerVelocity)
        {
            if (weapons == null)
            {
                if (_visuals.Count > 0) Clear();
                return;
            }

            SmoothFacing();
            if (!SameWeapons(weapons)) Rebuild(weapons);
            Follow(playerVelocity);
        }

        /// <summary>
        /// 武器架不跟着玩家根节点瞬转：玩家根节点的 rotation 每帧直接写，
        /// 这里追一个带阻尼的 yaw，转向时武器排会绕着角色平滑地甩到新一侧。
        /// 位置仍然跟玩家根节点（rack 的 localPosition 是 0），只解耦旋转。
        /// </summary>
        void SmoothFacing()
        {
            Transform frame = transform.parent;
            if (frame == null) return;

            float targetYaw = frame.eulerAngles.y;
            _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity,
                WeaponVisualStyle.FollowTurnSmoothTime, Mathf.Infinity, Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        void Rebuild(WeaponSystem weapons)
        {
            Clear();
            int count = weapons.Count;
            for (int i = 0; i < count; i++)
            {
                WeaponModel model = weapons.Weapons[i];
                Vector3 slot = WeaponVisualStyle.PlayerSlot(i, count);
                WeaponVisualCatalog.Entry entry = _catalog.Find(model.Id);
                WeaponVisual visual = WeaponVisual.Attach(transform, model, entry, slot,
                    WeaponVisualStyle.PlayerScale, i, 0);
                if (visual == null) continue;

                visual.SetAnchor(slot);
                _visuals.Add(visual);
                _models.Add(model);
                _anchors.Add(slot);
                _anchorVelocities.Add(Vector3.zero);
            }
        }

        void Follow(Vector2 playerVelocity)
        {
            int count = _visuals.Count;
            if (count == 0) return;

            // 槽位是 rack 本地坐标；速度拖尾要换到 rack 本地空间，而不是玩家根节点的瞬时旋转。
            Quaternion rotation = transform.rotation;
            float dt = Time.deltaTime;

            for (int i = 0; i < count; i++)
            {
                WeaponVisual visual = _visuals[i];
                if (visual == null) continue;

                Vector3 target = WeaponVisualStyle.FollowLocalTarget(
                    WeaponVisualStyle.PlayerSlot(i, count), playerVelocity, rotation);

                Vector3 anchor = _anchors[i];
                Vector3 anchorVelocity = _anchorVelocities[i];
                anchor = Vector3.SmoothDamp(anchor, target, ref anchorVelocity,
                    WeaponVisualStyle.FollowSmoothTime, Mathf.Infinity, dt);
                _anchors[i] = anchor;
                _anchorVelocities[i] = anchorVelocity;
                visual.SetAnchor(anchor);
            }
        }

        bool SameWeapons(WeaponSystem weapons)
        {
            if (_visuals.Count != weapons.Count) return false;

            for (int i = 0; i < _visuals.Count; i++)
            {
                if (!ReferenceEquals(_models[i], weapons.Weapons[i])) return false;
            }

            return true;
        }

        void Clear()
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null) Destroy(_visuals[i].gameObject);
            }

            _visuals.Clear();
            _models.Clear();
            _anchors.Clear();
            _anchorVelocities.Clear();
        }
    }
}
