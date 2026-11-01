using System;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 池化视图的统一入口：共享材质只在创建时注入一次，之后靠 MaterialPropertyBlock 上色。
    /// 实现它的视图可以直接交给 <see cref="PrimitiveViewFactory.Create{T}"/>，不用每个池各写一遍初始化顺序。
    /// </summary>
    public interface IPrimitiveView
    {
        void Initialize(Material sharedMaterial);
    }

    /// <summary>
    /// 代码建几何体的池化视图工厂。三个池（旅伴 / 掉落物 / 投射物）原来各抄了一遍
    /// CreatePrimitive → 删 Collider → SetParent → AddComponent → Initialize，
    /// 顺序稍有出入就会留下一个参与物理计算的空碰撞体，或者先 Initialize 再设缩放（武器锚点按错误尺寸算）。
    /// 收在这里：顺序只有一份，加新池时照抄一行调用即可。
    ///
    /// 不改变池化策略：实例仍然由各自的池持有、复用、隐藏，这里只负责“建第一个”的那一下。
    /// </summary>
    public static class PrimitiveViewFactory
    {
        /// <summary>
        /// 建一个纯表现用的图元实例并挂上视图组件。
        /// </summary>
        /// <param name="name">实例名，方便在 Hierarchy 里认。</param>
        /// <param name="primitive">内置图元类型（胶囊 / 球 / 方块）。</param>
        /// <param name="parent">池的父节点。</param>
        /// <param name="sharedMaterial">共享材质；null 时视图自己决定要不要显示。</param>
        /// <param name="place">
        /// 摆放回调。在 Initialize 之前跑：旅伴视图的武器锚点要按“最终缩放”反算，
        /// 先摆好位置与缩放再 Initialize 才不会有第一帧的错误尺寸。
        /// </param>
        public static T Create<T>(string name, PrimitiveType primitive, Transform parent, Material sharedMaterial,
            Action<Transform> place = null) where T : Component
        {
            GameObject instance = GameObject.CreatePrimitive(primitive);
            instance.name = name;
            instance.transform.SetParent(parent, false);

            // 纯表现物件：自带 Collider 只会让物理系统白算（几十上百个实例时是实打实的开销）
            DestroyCollider(instance);

            place?.Invoke(instance.transform);

            T view = instance.AddComponent<T>();
            if (view is IPrimitiveView poolView) poolView.Initialize(sharedMaterial);
            return view;
        }

        /// <summary>删掉图元自带的碰撞体（编辑器里跑测试时走 DestroyImmediate，否则下一帧前它还在场景里）。</summary>
        public static void DestroyCollider(GameObject instance)
        {
            if (instance == null) return;

            Collider collider = instance.GetComponent<Collider>();
            if (collider == null) return;

            collider.enabled = false;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
            else UnityEngine.Object.DestroyImmediate(collider);
        }
    }
}
