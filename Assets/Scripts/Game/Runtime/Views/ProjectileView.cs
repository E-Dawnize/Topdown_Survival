using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一发投射物的渲染壳。由 ProjectileViewPool 池化，位置一律走 PrevPosition → Position 的插值，
    /// 不读模拟的最终位置直接写 Transform：投射物 18 米/秒，60Hz 下一步就是 0.3 米，
    /// 直接写死会看到箭在格子上跳。
    ///
    /// 外观（颜色 + 大小）由取出它的持有者给（玩家 / 旅伴，见 ProjectileViewSources）：
    /// 同一个实例可能这一帧画玩家的箭、下一帧画旅伴的箭，所以外观写在 Look 里、
    /// 每次取出时 ApplyLook 一次，而不是像以前那样创建时定死。
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>玩家箭的尺寸。0.32 米长对应一步 0.3 米的位移，看着像根箭而不是一个点。</summary>
        static readonly Vector3 BaseScale = new Vector3(0.08f, 0.08f, 0.32f);

        /// <summary>离地高度。方块原点在中心，放 0.5 米高才刚好浮在地面上方。</summary>
        const float BodyHeight = 0.5f;

        /// <summary>一支箭的外观：颜色 + 相对 <see cref="BaseScale"/> 的尺寸倍数。</summary>
        public readonly struct Look
        {
            public readonly Color Tint;
            public readonly float Scale;

            public Look(Color tint, float scale)
            {
                Tint = tint;
                Scale = scale;
            }
        }

        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        bool _hasBaseColor;

        Look _look;
        bool _applied;

        /// <summary>
        /// 只在创建时调一次：换共享材质、摆到离地高度。
        /// 共享材质而不是 renderer.material：后者会克隆一份材质实例，
        /// 几百发箭就是几百份材质，合批直接没了。
        /// </summary>
        public void Initialize(Material sharedMaterial)
        {
            _renderer = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();

            if (_renderer == null || sharedMaterial == null) return;

            _renderer.sharedMaterial = sharedMaterial;
            _hasBaseColor = sharedMaterial.HasProperty(BaseColorId);

            Vector3 p = transform.position;
            p.y = BodyHeight;
            transform.position = p;
        }

        /// <summary>
        /// 每次从池里取出时调一次：按持有者上色与改大小。
        /// 外观没变就什么都不写——同一个来源连着几帧用同一个实例时是常态。
        /// </summary>
        public void ApplyLook(Look look)
        {
            if (_applied && _look.Tint == look.Tint && Mathf.Approximately(_look.Scale, look.Scale)) return;

            _look = look;
            _applied = true;

            transform.localScale = BaseScale * look.Scale;

            if (_renderer == null || !_hasBaseColor) return;

            _mpb.SetColor(BaseColorId, look.Tint);
            _renderer.SetPropertyBlock(_mpb);
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
