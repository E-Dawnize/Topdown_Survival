using Game.Model;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一个敌人的渲染壳。由 EnemyViewPool 在运行时 AddComponent 到敌人预制体实例上，
    /// 预制体本身不改：模型是美术资源，加组件这种事不该写回资源里。
    ///
    /// 位置一律走 PrevPosition → Position 的插值，不读 EnemyModel.Position 的最终值直接写 Transform：
    /// 模拟是 60Hz 阶梯，渲染帧率不是 60 的整数倍时直接写会看到一顿一顿。
    /// 朝向看速度方向（敌人没有单独的朝向字段，测速方向就是它要去的地方）。
    ///
    /// 脚下还有一块接触阴影盘（见 ContactShadowDisc）：它是实例的子节点，跟着父节点走，
    /// 所以本类每帧只写身体这一个 Transform；受击变亮也只写身体那个渲染器的属性块。
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>受击变亮的持续时间。只做"被打了一下"的可读性，不做数值表达。</summary>
        const float FlashDuration = 0.09f;

        /// <summary>变亮时向白色混多少。1 就是纯白，会看不出种类色，0.8 还能看出是哪个怪。</summary>
        const float FlashWhiteness = 0.8f;

        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        bool _hasBaseColor;

        // 脚下阴影盘。挂在实例下当子节点，位置随父节点跟走，Sync 里不需要碰它
        Transform _shadow;

        Color _tint = Color.white;
        Color _appliedColor;
        bool _applied;
        float _flash;

        /// <summary>当前绑定的敌人。池里复用时靠它判断是不是同一个敌人，换了就重新上色并吸附位置。</summary>
        public EnemyModel Enemy { get; private set; }

        void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();

            if (_renderer != null && _renderer.sharedMaterial != null &&
                _renderer.sharedMaterial.HasProperty(BaseColorId))
            {
                _hasBaseColor = true;
            }
        }

        /// <summary>绑定一个敌人。槽位被复用时会绑到新敌人身上，这时直接吸附到出生点，
        /// 不从上一具尸体的位置插值过来（否则会看到残影横穿场地）。</summary>
        public void Bind(EnemyModel enemy)
        {
            Enemy = enemy;
            _tint = TintOf(enemy.Def.Id);
            _flash = 0f;
            _applied = false;

            transform.position = new Vector3(enemy.Position.x, transform.position.y, enemy.Position.y);
            ApplyColor(_tint);

            // 阴影盘只建一次：池里复用的是同一份预制体的实例，身体半径不会变，没必要每次绑定都重建
            if (_shadow == null && _renderer != null)
            {
                _shadow = ContactShadowDisc.Attach(transform, _renderer.sharedMaterial, enemy.Radius);
            }
        }

        /// <summary>受击变亮。调用方（EnemyViewPool）从 UnitDamaged 事件里拿到目标槽位再转到这里。</summary>
        public void Flash()
        {
            _flash = FlashDuration;
        }

        /// <summary>
        /// 写一个渲染帧的位置与朝向。alpha 是融合系数：0 = 本步起点，1 = 本步终点。
        /// </summary>
        public void Sync(float alpha)
        {
            if (Enemy == null) return;

            Vector2 position = Vector2.Lerp(Enemy.PrevPosition, Enemy.Position, alpha);
            Vector3 p = transform.position;
            p.x = position.x;
            p.z = position.y;
            transform.position = p;

            Vector2 velocity = Enemy.Velocity;
            if (velocity.sqrMagnitude > 1e-6f)
            {
                Vector2 forward = velocity.normalized;
                transform.rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.y), Vector3.up);
            }

            if (_flash > 0f)
            {
                _flash -= Time.deltaTime;
                float t = Mathf.Clamp01(_flash / FlashDuration);
                ApplyColor(Color.Lerp(_tint, Color.white, t * FlashWhiteness));
            }
            else if (_appliedColor != _tint || !_applied)
            {
                ApplyColor(_tint);
            }
        }

        void ApplyColor(Color color)
        {
            if (_renderer == null || !_hasBaseColor) return;
            if (_applied && _appliedColor == color) return;   // 颜色没变就别每帧重设一次属性块

            _mpb.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_mpb);
            _appliedColor = color;
            _applied = true;
        }

        /// <summary>
        /// 按 Id 取一个稳定的色。不写"Id → 颜色"的表：敌人表是策划数据，
        /// 加一个怪种不应该变成一次改代码的事；同一个 Id 在任何机器上都得到同一个色。
        /// </summary>
        static Color TintOf(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return Color.white;

            // FNV-1a：string.GetHashCode 在不同运行时会变（.NET Core 起带随机化），不能用
            uint hash = 2166136261u;
            for (int i = 0; i < enemyId.Length; i++)
            {
                hash ^= enemyId[i];
                hash *= 16777619u;
            }

            float hue = (hash % 360u) / 360f;

            // 饱和度与明度固定：地面是土黄偏亮，太淡的色会看不出来，太亮又会把受击变亮吃掉
            return Color.HSVToRGB(hue, 0.55f, 0.85f);
        }
    }
}
