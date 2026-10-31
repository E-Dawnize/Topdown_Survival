using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一枚掉落物（金币 / 心）的渲染壳。由 PickupViewPool 池化，位置按 PickupSystem 的 60Hz 快照同步。
    ///
    /// 磁吸时放大一点并把颜色往白里提：玩家要能看出"它正在飞过来"，
    /// 否则磁吸在画面上只是"东西啪一下不见了"。
    /// </summary>
    public sealed class PickupView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>磁吸时的放大倍数。再大就会遮住玩家的脚，看着像敌人贴脸。</summary>
        const float MagnetScale = 1.35f;

        /// <summary>
        /// 一种掉落物的外形。金币是压扁的球（一眼看出是枚硬币），心是圆球：
        /// 形状与颜色两份区分，挤在一起的一堆掉落物才分得开。
        /// </summary>
        public readonly struct Look
        {
            public readonly Vector3 Scale;
            public readonly float Height;
            public readonly Color Color;

            public Look(Vector3 scale, float height, Color color)
            {
                Scale = scale;
                Height = height;
                Color = color;
            }

            public bool SameAs(in Look other)
            {
                return Scale == other.Scale && Height == other.Height && Color == other.Color;
            }
        }

        Renderer _renderer;
        MaterialPropertyBlock _block;

        Look _look;
        bool _hasLook;
        bool _magnetized;

        /// <summary>只在创建时调一次：共享材质 + 自己的一份属性块（每份 MPB 不会克隆材质）。</summary>
        public void Initialize(Material sharedMaterial)
        {
            _renderer = GetComponentInChildren<Renderer>();
            _block = new MaterialPropertyBlock();

            if (_renderer == null || sharedMaterial == null) return;

            // sharedMaterial 而不是 renderer.material：后者会克隆一份材质实例，
            // 场上几百枚掉落物就是几百份材质，合批直接没了
            _renderer.sharedMaterial = sharedMaterial;
        }

        /// <summary>
        /// 换一种外形。池槽位会被下一枚掉落物复用，金币与心共用同一批实例，所以每个槽位都得能换形状。
        /// 只在真的变了的时候写 Transform 与属性块。
        /// </summary>
        public void Apply(in Look look)
        {
            if (_hasLook && _look.SameAs(look)) return;

            _look = look;
            _hasLook = true;

            _magnetized = false;
            transform.localScale = look.Scale;
            WriteColor();
        }

        /// <summary>
        /// 一个渲染帧同步一次。位置直接写快照值，不做平滑：
        /// PickupSystem.Snapshot 的槽位号是稠密下标，回收时会末尾换位，同一个槽位下一帧可能已经
        /// 换成了另一枚掉落物；按上一步位置做插值/平滑会让金币在两枚掉落物之间横穿战场。
        /// 模拟是 60Hz 固定步长，逐帧直写看到的是一步 0.3 米的位移，已经够连续。
        /// </summary>
        public void Sync(Vector2 position, bool magnetized)
        {
            transform.position = new Vector3(position.x, _look.Height, position.y);

            if (magnetized == _magnetized) return;

            _magnetized = magnetized;
            transform.localScale = _look.Scale * (magnetized ? MagnetScale : 1f);
            WriteColor();
        }

        void WriteColor()
        {
            if (_renderer == null || _block == null) return;
            if (_renderer.sharedMaterial == null || !_renderer.sharedMaterial.HasProperty(BaseColorId)) return;

            Color color = _magnetized ? Color.Lerp(_look.Color, Color.white, 0.45f) : _look.Color;
            _block.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
