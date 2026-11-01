using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一发投射物的渲染壳。prefab 由 UnitPrefabMap.ProjectileArrow 引用（Assets/Prefabs/Projectiles/Projectile_Arrow.prefab），
    /// 模型是 Quaternius 的 CC0 箭，长轴朝 +Z，所以 Sync 只用 LookRotation 对准速度方向。
    ///
    /// 位置一律走 PrevPosition → Position 的插值，不读模拟的最终位置直接写 Transform：
    /// 投射物 18 米/秒，60Hz 下一步就是 0.3 米，直接写死会看到箭在格子上跳。
    ///
    /// 外观（颜色 + 大小）由取出它的持有者给（玩家 / 旅伴，见 ProjectileViewSources）：
    /// 同一个实例可能这一帧画玩家的箭、下一帧画旅伴的箭，所以外观写在 Look 里、
    /// 每次取出时 ApplyLook 一次，而不是像以前那样创建时定死。
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>箭杆中心离地高度。prefab 的原点在箭杆中心，抬到 0.5 米才浮在地面上方。</summary>
        const float BodyHeight = 0.5f;

        /// <summary>一支箭的外观：颜色 + 相对 prefab 的尺寸倍数。</summary>
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

        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        bool[] _tintable;
        bool _hasBaseColor;

        Look _look;
        bool _applied;

        /// <summary>
        /// 只在创建时调一次：缓存所有箭杆/箭头/尾羽的 Renderer，摆到离地高度。
        /// prefab 自带材质，这里不再用玩家身体材质覆盖，否则木/钢细节会被一整块颜色吃掉。
        /// </summary>
        public void Initialize()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();
            _tintable = new bool[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = _renderers[i].sharedMaterial;
                _tintable[i] = material != null && material.HasProperty(BaseColorId);
                if (_tintable[i]) _hasBaseColor = true;
            }

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

            transform.localScale = Vector3.one * look.Scale;

            if (!_hasBaseColor) return;

            _mpb.SetColor(BaseColorId, look.Tint);
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_tintable[i]) _renderers[i].SetPropertyBlock(_mpb);
            }
        }

        /// <summary>
        /// alpha：0 = 本步起点，1 = 本步终点。
        ///
        /// hasMuzzle 时不再“从枪口插值到模拟位置”（那会横向拐一下），而是沿
        /// “枪口 → 这一发的瞄准点”的直线走，进度用模拟层已经飞过的距离 / 到目标的距离。
        /// 这样从弓把出来就是直着指向目标；命中仍由模拟层决定。
        /// </summary>
        public void Sync(Vector2 prevPosition, Vector2 position, float alpha, Vector2 direction,
            Vector2 origin, Vector2 aimPoint, bool hasMuzzle, Vector3 muzzle)
        {
            Vector2 simulated = Vector2.Lerp(prevPosition, position, alpha);

            Vector2 facing = direction;
            Vector3 actual;

            if (hasMuzzle)
            {
                Vector2 muzzleFlat = new Vector2(muzzle.x, muzzle.z);
                float targetDistance = (aimPoint - origin).magnitude;
                float travelled = (simulated - origin).magnitude;
                float progress = targetDistance > 1e-5f ? travelled / targetDistance : 0f;

                Vector2 path = muzzleFlat + (aimPoint - muzzleFlat) * progress;
                actual = new Vector3(path.x, muzzle.y, path.y);

                Vector2 muzzleToTarget = aimPoint - muzzleFlat;
                if (muzzleToTarget.sqrMagnitude > 1e-8f) facing = muzzleToTarget.normalized;
            }
            else
            {
                actual = new Vector3(simulated.x, BodyHeight, simulated.y);
            }

            transform.position = actual;

            if (facing.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
            }
        }
    }
}
