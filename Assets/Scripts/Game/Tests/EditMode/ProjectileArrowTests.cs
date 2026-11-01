using Game.Config;
using Game.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 投射物 prefab 由 UnitPrefabMap 资产显式引用，不再在代码里按 Resources 名字查。
    /// 这里验证引用存在、组件契约、长轴朝 +Z（ProjectileView 用 LookRotation 对准速度），
    /// 以及 ApplyLook 的缩放确实作用在整支箭上。
    /// </summary>
    public class ProjectileArrowTests
    {
        [Test]
        public void ArrowPrefabExistsAndPointsAlongForwardAxis()
        {
            var map = AssetDatabase.LoadAssetAtPath<UnitPrefabMap>("Assets/Prefabs/UnitPrefabMap.asset");
            Assert.IsNotNull(map, "缺少 Assets/Prefabs/UnitPrefabMap.asset");
            GameObject prefab = map.ProjectileArrow;
            Assert.IsNotNull(prefab, "UnitPrefabMap 没有指定 ProjectileArrow");
            Assert.IsNotNull(prefab.GetComponent<ProjectileView>(), "Projectile_Arrow.prefab 上没有 ProjectileView");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                Assert.Greater(renderers.Length, 0, "箭 prefab 没有 Renderer");

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                Assert.Greater(bounds.size.z, bounds.size.x, "箭的长轴必须朝 +Z，否则飞行方向会歪");
                Assert.Greater(bounds.size.z, bounds.size.y, "箭的长轴必须朝 +Z，否则飞行方向会歪");
                Assert.That(bounds.size.z, Is.EqualTo(0.55f).Within(0.08f),
                    "箭的长度应接近 builder 的 ProjectileArrowLength");

                var view = instance.GetComponent<ProjectileView>();
                view.Initialize();
                view.ApplyLook(new ProjectileView.Look(Color.cyan, 0.8f));

                Assert.AreEqual(0.8f, instance.transform.localScale.x, 1e-4f);
                Assert.AreEqual(0.8f, instance.transform.localScale.y, 1e-4f);
                Assert.AreEqual(0.8f, instance.transform.localScale.z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
