using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一发投射物的渲染壳。由 ProjectileViewPool 池化，位置一律走 PrevPosition → Position 的插值，
    /// 不读模拟的最终位置直接写 Transform：投射物 18 米/秒，60Hz 下一步就是 0.3 米，
    /// 直接写死会看到箭在格子上跳。
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer _renderer;

        /// <summary>只在创建时调一次：共享材质 + 一次性上色，之后每帧只写位置与朝向。</summary>
        public void Initialize(Material sharedMaterial, Color color)
        {
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer == null || sharedMaterial == null) return;

            // 共享材质而不是 renderer.material：后者会克隆一份材质实例，
            // 400 发箭就是 400 份材质，合批直接没了
            _renderer.sharedMaterial = sharedMaterial;

            if (!sharedMaterial.HasProperty(BaseColorId)) return;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(mpb);
        }

        /// <summary>alpha：0 = 本步起点，1 = 本步终点。</summary>
        public void Sync(Vector2 prevPosition, Vector2 position, float alpha, Vector2 direction)
        {
            Vector2 interpolated = Vector2.Lerp(prevPosition, position, alpha);

            Vector3 p = transform.position;
            p.x = interpolated.x;
            p.z = interpolated.y;
            transform.position = p;

            if (direction.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up);
            }
        }
    }
}
