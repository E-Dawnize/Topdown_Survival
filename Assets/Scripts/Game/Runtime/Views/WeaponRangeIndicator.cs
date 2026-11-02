using Game.Config;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 武器攻击范围效果的抽象基类。每种形状只实现自己的 <see cref="Draw"/>：
    ///   - 近战：扇形（FanRangeIndicator）
    ///   - 远程：目标锁定圈（TargetReticleIndicator）
    /// 以后要加锥形、直线、锁定框，只需要新增子类并在工厂里登记，不用改 WeaponVisual 里的逻辑。
    ///
    /// 显示规则：对象在武器装上的第一帧就创建好（所以随时可以显示），
    /// 但线只在攻击窗口内由 <see cref="Refresh"/> 打开；待机时隐藏，不是永久画在场上。
    /// 范围线统一用世界坐标，挂点只负责层级回收。
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
        protected virtual float Width => 0.090f;
        protected virtual float AttackAlpha => 0.90f;

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
            Line.enabled = false;

            Refresh(false, 0f);
        }

        /// <summary>
        /// 每帧调用。visible 控制是否显示，intensity 让攻击弧在窗口内淡出。
        /// 待机时 visible=false，线直接关掉；子类只在可见时绘制形状。
        /// </summary>
        public void Refresh(bool visible, float intensity)
        {
            if (Line == null) return;

            if (Line.enabled != visible) Line.enabled = visible;
            if (!visible) return;

            // intensity=0 是拉弓窗口的第一帧；形状子类仍要画外圈，不能因为进度为 0 就消失。
            Draw(Mathf.Clamp01(intensity));
        }

        protected abstract void Draw(float intensity);

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

        protected void ApplyLineColor(float intensity)
        {
            Color color = OwnerKey == 0
                ? ProjectileViewSources.PlayerTint
                : ProjectileViewSources.CompanionTint;
            color.a = AttackAlpha * Mathf.Clamp01(intensity);
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
    /// WeaponVisual 只负责把攻击窗口的 visible/intensity 转给基类 Refresh。
    /// </summary>
    public static class WeaponRangeIndicatorFactory
    {
        public static WeaponRangeIndicator Attach(WeaponModel model, Transform weaponTransform, int ownerKey)
        {
            if (model == null || weaponTransform == null) return null;

            Transform anchor = weaponTransform.parent != null ? weaponTransform.parent : weaponTransform;
            Transform owner = WeaponRangeGeometry.OwnerOf(weaponTransform);
            bool melee = model.Def.Kind == WeaponKind.Melee;
            string shape = melee ? "Fan" : "Reticle";

            var go = new GameObject("Range_" + shape + "_" + model.Id);
            go.transform.SetParent(anchor, false);

            WeaponRangeIndicator indicator = melee
                ? go.AddComponent<FanRangeIndicator>()
                : (WeaponRangeIndicator)go.AddComponent<TargetReticleIndicator>();
            indicator.Initialize(model, owner, ownerKey);
            return indicator;
        }
    }
}
