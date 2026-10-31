using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Views
{
    /// <summary>
    /// 敌人脚下的接触阴影盘：贴地的一个暗色四边形，让敌人在没有实时阴影的配置下也能看出"站在地上"而不像浮着。
    ///
    /// 三份渲染资源全是静态共享的：一份网格、一份材质（直接复用敌人身体那份材质资产，不 new Material）、
    /// 一个属性块（颜色固定）。所以 200 个敌人 = 200 个渲染器 + 1 份网格 + 1 份材质 + 1 个属性块，
    /// 材质实例数不随敌人数涨——每个敌人克隆一份 Material 会让 200 个身体各占一个批次，也会多出 200 份材质内存。
    ///
    /// 挂成敌人实例的子节点而不是每个渲染帧写世界坐标：父节点只绕 Y 转（UnitView 的朝向是 LookRotation 平面朝向），
    /// 子节点在本地绕 X 转 90° 之后在世界上一直是水平的，位置随父节点自动跟走。
    /// 稳态下一个渲染帧在阴影上花 0 次 Transform 写入、0 字节分配。
    ///
    /// 不跟受击变亮：变亮写在身体那个渲染器的属性块上，阴影是另一个渲染器、另一个属性块，
    /// 两者共用材质资产但互不影响。
    /// </summary>
    public static class ContactShadowDisc
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>离地高度。正好贴地会和地面 z-fighting（50° 俯视相机下最先在地面亮处看出来），0.02 米看不出悬空。</summary>
        const float GroundOffset = 0.02f;

        /// <summary>阴影盘直径相对身体直径的倍数。和身体一样大就整块被身体盖住，只露出边缘一点点才像阴影。</summary>
        const float DiameterScale = 1.5f;

        /// <summary>
        /// 阴影颜色。用的是不透明材质，所以出来的是"深色盘"而不是软阴影：
        /// 柔边要一张带 alpha 的贴图，那是美术资源，本迭代不做（见 docs/perf-log.md 的待测项）。
        /// </summary>
        static readonly Color ShadowColor = new Color(0.12f, 0.11f, 0.10f, 1f);

        static Mesh _sharedMesh;
        static MaterialPropertyBlock _sharedBlock;

        /// <summary>
        /// 给一个敌人实例挂上阴影盘，返回挂出来的子节点（没挂成返回 null）。
        /// 调用方只需要在绑定敌人时调一次，之后什么都不用做。
        /// </summary>
        /// <param name="host">敌人实例的根节点。位置与朝向都写在它身上。</param>
        /// <param name="sharedMaterial">敌人身体用的那份共享材质。必须是资产里的材质，不能是 renderer.material 克隆出来的。</param>
        /// <param name="bodyRadius">身体半径，用来定阴影盘大小。</param>
        public static Transform Attach(Transform host, Material sharedMaterial, float bodyRadius)
        {
            if (host == null || sharedMaterial == null) return null;

            // 没有 _BaseColor 就没法上色：宁可没有阴影，也不要给每个敌人头顶一个白色的盘
            if (!sharedMaterial.HasProperty(BaseColorId)) return null;

            var instance = new GameObject("ContactShadow");
            Transform shadow = instance.transform;
            shadow.SetParent(host, false);

            instance.AddComponent<MeshFilter>().sharedMesh = SharedMesh();

            var renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            // 纯贴地装饰：自己投影等于每个怪多一份阴影贴图开销，也不该被场景里别的阴影再压暗一层
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.SetPropertyBlock(SharedBlock());

            // 父节点的世界缩放不一定等于 1（Crawler 预制体是 0.7），本地缩放要折回去，
            // 否则不同怪种的阴影大小跟着预制体缩放走，看起来像"小怪的影子更小"这种没有理由的差异
            Vector3 hostScale = host.lossyScale;
            float scaleX = Mathf.Max(hostScale.x, 1e-4f);
            float scaleY = Mathf.Max(hostScale.y, 1e-4f);
            float diameter = bodyRadius * 2f * DiameterScale;
            shadow.localScale = new Vector3(diameter / scaleX, diameter / scaleY, 1f);

            // 本地绕 X 转 90°：网格在本地 XY 平面，转完躺到世界 XZ 平面（多边形的绕序见 SharedMesh）
            shadow.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // 贴地：父节点原点在模型自己抬起来的高度上（Crawler 是 y = 0.35），要按父节点缩放折回本地
            shadow.localPosition = new Vector3(0f, (GroundOffset - host.position.y) / scaleY, 0f);

            return shadow;
        }

        /// <summary>
        /// 一份共享网格：4 顶点、正反各两个三角形。
        /// 做双面是为了不必依赖 Unity 的绕序约定（同样一份网格从上方、从下方都看得见），
        /// 4 个顶点的代价不值得为它单独调一次朝向。
        /// </summary>
        static Mesh SharedMesh()
        {
            if (_sharedMesh != null) return _sharedMesh;

            var mesh = new Mesh { name = "ContactShadowQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f)
            });
            mesh.SetNormals(new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();

            _sharedMesh = mesh;
            return _sharedMesh;
        }

        /// <summary>
        /// 一个属性块给所有阴影共用：里面只有「深色」这一个常量，值完全一样，
        /// 每个敌人各建一个只会白占一份托管对象（200 个敌人的阴影就是 200 个属性块，每个都带自己的属性表）。
        /// SetPropertyBlock 会把值拷进渲染器，共用一个不会被后写的单位改掉。
        /// </summary>
        static MaterialPropertyBlock SharedBlock()
        {
            if (_sharedBlock != null) return _sharedBlock;

            _sharedBlock = new MaterialPropertyBlock();
            _sharedBlock.SetColor(BaseColorId, ShadowColor);
            return _sharedBlock;
        }
    }
}
