using Game.Config;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 武器常驻攻击范围的抽象基类。每种范围形状只实现自己的 <see cref="Refresh"/>：
    ///   - 近战：扇形（FanRangeIndicator）
    ///   - 远程：圆形（CircleRangeIndicator）
    /// 以后要加锥形、直线、锁定框，只需要新增子类并在工厂里登记，不用改 WeaponVisual 里的逻辑。
    ///
    /// 常驻显示与出手时的亮弧是两件事：本类画的是"这件武器现在够到哪里"，
    /// 亮弧仍是攻击时的一次性反馈。范围线统一用世界坐标，挂点只负责层级回收。
    /// </summary>
    public abstract class WeaponRangeIndicator : MonoBehaviour
    {
        protected WeaponModel Model;
        protected Transform Owner;
        protected int OwnerKey;
        protected LineRenderer Line;

        /// <summary>测试与调试用：常驻范围线是否已经创建并处于可见状态。</summary>
        public bool IsVisible => Line != null && Line.enabled;

        protected abstract int Segments { get; }
        protected virtual bool ClosedLoop => false;
        protected virtual float Width => 0.040f;
        protected virtual float IdleAlpha => 0.20f;
        protected virtual float AttackAlpha => 0.10f;

        public void Initialize(WeaponModel model, Transform owner, int ownerKey)
        {
            Model = model;
            Owner = owner;
            OwnerKey = ownerKey;

            Line = gameObject.AddComponent<LineRenderer>();
            Line.sharedMaterial = CombatEffects.LineMaterial;
            Line.useWorldSpace = true;
            Line.alignment = LineAlignment.View;
            Line.textureMode = LineTextureMode.Stretch;
            Line.widthMultiplier = Width;
            Line.numCapVertices = 0;
            Line.numCornerVertices = 0;
            Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Line.receiveShadows = false;
            Line.loop = ClosedLoop;
            Line.positionCount = Segments;
            Line.enabled = true;

            Refresh(false);
        }

        /// <summary>每帧调用。attacking 只影响透明度，不影响范围数值。</summary>
        public abstract void Refresh(bool attacking);

        protected Vector3 Origin(float height = 0.045f)
        {
            Vector3 p = Owner != null ? Owner.position : transform.position;
            p.y = height;
            return p;
        }

        protected Vector2 Aim()
        {
            return WeaponRangeGeometry.Aim(Model, Owner);
        }

        protected float Range()
        {
            return WeaponRangeGeometry.Range(Model);
        }

        protected float HalfAngle()
        {
            return WeaponRangeGeometry.HalfAngle(Model);
        }

        protected void ApplyLineColor(bool attacking)
        {
            Color color = OwnerKey == 0
                ? ProjectileViewSources.PlayerTint
                : ProjectileViewSources.CompanionTint;
            color.a = attacking ? AttackAlpha : IdleAlpha;
            Line.startColor = color;
            Line.endColor = color;
        }
    }

    /// <summary>范围线共用的几何与取值：Owner/朝向/有效射程/半角只在这里算一次。</summary>
    internal static class WeaponRangeGeometry
    {
        public static Transform OwnerOf(Transform weaponTransform)
        {
            Transform anchor = weaponTransform != null && weaponTransform.parent != null
                ? weaponTransform.parent
                : weaponTransform;
            if (anchor == null) return null;
            return anchor.parent != null ? anchor.parent : anchor;
        }

        public static Vector2 Aim(WeaponModel model, Transform owner)
        {
            if (model != null && model.AimDirection.sqrMagnitude >= 1e-8f) return model.AimDirection;
            if (owner == null) return Vector2.up;

            Vector3 forward = owner.forward;
            return new Vector2(forward.x, forward.z);
        }

        public static float Range(WeaponModel model)
        {
            if (model == null) return 0f;
            return model.LastRange > 0f ? model.LastRange : model.Range;
        }

        public static float HalfAngle(WeaponModel model)
        {
            if (model == null) return 45f;
            return model.SpreadDeg > 0f ? model.SpreadDeg : 45f;
        }
    }

    /// <summary>
    /// 按武器类型创建常驻范围显示。新增形状时只在这里加一个 case；
    /// WeaponVisual 只负责把当前 attacking 状态转给基类的 Refresh。
    /// </summary>
    public static class WeaponRangeIndicatorFactory
    {
        public static WeaponRangeIndicator Attach(WeaponModel model, Transform weaponTransform, int ownerKey)
        {
            if (model == null || weaponTransform == null) return null;

            Transform anchor = weaponTransform.parent != null ? weaponTransform.parent : weaponTransform;
            Transform owner = WeaponRangeGeometry.OwnerOf(weaponTransform);
            bool melee = model.Def.Kind == WeaponKind.Melee;
            string shape = melee ? "Fan" : "Circle";

            var go = new GameObject("Range_" + shape + "_" + model.Id);
            go.transform.SetParent(anchor, false);

            WeaponRangeIndicator indicator = melee
                ? go.AddComponent<FanRangeIndicator>()
                : (WeaponRangeIndicator)go.AddComponent<CircleRangeIndicator>();
            indicator.Initialize(model, owner, ownerKey);
            return indicator;
        }
    }
}
