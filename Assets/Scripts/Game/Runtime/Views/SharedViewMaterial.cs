using Game.Config;
using Game.Core;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 池化视图（投射物、掉落物）的共享材质来源。
    ///
    /// 复用一个已经在项目里的材质，而不是 new Material(Shader.Find(...))：后者在打包时
    /// 可能因为 shader 没被任何资产引用而被剔除，运行时变成粉色。
    /// 颜色差异走每个实例一份的 MaterialPropertyBlock，所以同一个材质能被所有掉落物共用。
    /// </summary>
    public static class SharedViewMaterial
    {
        public static Material FromPlayerPrefab(UnitPrefabMap prefabMap)
        {
            if (prefabMap == null) return null;

            GameObject unitPrefab = prefabMap.Find(AppRoot.PlayerPrefabId);
            if (unitPrefab == null) return null;

            Renderer renderer = unitPrefab.GetComponentInChildren<Renderer>();
            return renderer == null ? null : renderer.sharedMaterial;
        }
    }
}
