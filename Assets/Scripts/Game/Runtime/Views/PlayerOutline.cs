using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Views
{
    /// <summary>
    /// 玩家描边壳：把玩家身体放大一圈、只渲染背面（正面剔掉），露在身体外面的那一圈就是描边。
    /// 目的是"200 个敌人围上来时一眼能找到玩家"，不是一个美术向的描边效果。
    ///
    /// 为什么不用后处理描边：那要一份额外的相机 Renderer Feature 与 RT，改的是场景与 URP 资产；
    /// 这里只在运行时给玩家实例挂一个子节点，场景文件和预制体都不用动。
    ///
    /// 材质只建一份（整个游戏一份，不是每个玩家一份）：它是身体材质资产的副本，所以拿着同一个 shader，
    /// 不需要 Shader.Find——后者在构建时可能因为 shader 没被资产引用而被剔除，运行时拿到 null 就是粉色。
    /// 颜色走 MaterialPropertyBlock；剔除模式必须写在材质上（渲染状态不能从属性块设）。
    /// </summary>
    public static class PlayerOutline
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>壳相对身体放大多少。1.12 在 50° 俯视、相机 size 7 下是能看见的一圈，再大就盖住身体细节了。</summary>
        const float ShellScale = 1.12f;

        /// <summary>橙色而不是玩家身上已有的浅蓝：场地是土黄偏亮、敌人是各种杂色，暖橙在哪个方向上都不撞色。</summary>
        static readonly Color ShellColor = new Color(1f, 0.5f, 0.08f, 1f);

        static Material _sharedMaterial;

        /// <summary>
        /// 给玩家实例挂描边壳，返回挂出来的子节点（没挂成返回 null 并警告一次原因）。
        /// 只在玩家身上调一次：玩家只有一个，不需要池化。
        /// </summary>
        /// <param name="host">玩家实例的根节点，朝向写在它身上，壳跟着一起转。</param>
        /// <param name="body">身体渲染器。要求它是 MeshFilter + MeshRenderer（当前玩家预制体就是胶囊体）。</param>
        public static Transform Attach(Transform host, Renderer body)
        {
            if (host == null || body == null) return null;

            Material bodyMaterial = body.sharedMaterial;
            if (bodyMaterial == null)
            {
                Debug.LogWarning("[PlayerOutline] 玩家身体没有共享材质，描边壳跳过：玩家会淹没在敌群里");
                return null;
            }

            var bodyFilter = body.GetComponent<MeshFilter>();
            if (bodyFilter == null || bodyFilter.sharedMesh == null)
            {
                // 骨骼网格（SkinnedMeshRenderer）以后换真美术时再支持：那需要把骨骼层级一起复制，成本不在这一版
                Debug.LogWarning("[PlayerOutline] 玩家身体不是 MeshFilter 网格，描边壳跳过：玩家会淹没在敌群里");
                return null;
            }

            var instance = new GameObject("PlayerOutline");
            Transform shell = instance.transform;
            shell.SetParent(host, false);
            shell.localPosition = Vector3.zero;
            shell.localRotation = Quaternion.identity;
            shell.localScale = Vector3.one * ShellScale;

            // 复用身体那份网格资产，不复制：壳和身体几何一致，靠缩放差出那一圈
            instance.AddComponent<MeshFilter>().sharedMesh = bodyFilter.sharedMesh;

            var renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial(bodyMaterial);
            // 壳只负责露出轮廓：不投影、不接收阴影，否则身体周围会多出一圈被压暗的边，反而看不清
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, ShellColor);
            renderer.SetPropertyBlock(block);

            return shell;
        }

        static Material SharedMaterial(Material bodyMaterial)
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            // 复制而不是 new Material(shader)：副本自带身体用的那个 shader，不依赖运行时 Shader.Find
            var material = new Material(bodyMaterial)
            {
                name = "PlayerOutline",
                hideFlags = HideFlags.DontSave   // 运行时产物：不进场景、不随退出播放残留
            };

            // 只渲染背面：放大后的壳把身体包在里面，正面被身体挡住，能看见的就只剩外面那一圈
            if (material.HasProperty(CullId))
            {
                material.SetFloat(CullId, (float)CullMode.Front);
            }

            // 自发光让描边在背光/阴影里也亮着：玩家在暗处还是要一眼找到。
            // 没有自发光属性（换了 shader）就退回普通着色，不报错——描边还是能看见，只是不那么亮
            if (material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, ShellColor * 0.5f);
            }

            _sharedMaterial = material;
            return _sharedMaterial;
        }
    }
}
