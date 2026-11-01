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
    /// 朝向优先看速度方向；收到起手/出手事件时改成锁定的攻击方向，并叠一个纯表现的下蹲/前顶姿态。
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

        // 头顶血条。挂在实例下，但线本身用世界坐标；父节点缩放在 Attach 后用反算抵消。
        EnemyHealthBar _healthBar;

        // 预制体自己的缩放（Crawler / Charger 是 0.7）。精英与 BOSS 在本值上再乘 VisualScale，
        // 不能直接写 1，否则第一次绑定就把预制体的尺寸改了。
        Vector3 _baseScale = Vector3.one;

        // 绑定后的最终缩放（含 VisualScale）。攻击姿态只在这个值上做挤压，
        // 不会把精英/BOSS 的体型改回普通怪。
        Vector3 _boundScale = Vector3.one;

        // ---- 攻击姿态（纯表现，不参与判定） ----
        // 起手时下蹲蓄力，出手瞬间向前顶一下；与 EnemyViewPool 收到的预警/出手事件对齐。
        float _windupRemaining;
        float _windupDuration;
        float _strikeRemaining;
        float _strikeDuration = 0.14f;
        float _strikeReach = 1f;
        Vector2 _attackDirection;

        Color _tint = Color.white;
        Color _appliedColor;
        bool _applied;
        float _flash;

        /// <summary>当前绑定的敌人。池里复用时靠它判断是不是同一个敌人，换了就重新上色并吸附位置。</summary>
        public EnemyModel Enemy { get; private set; }

        void Awake()
        {
            _baseScale = transform.localScale;
            _renderer = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _healthBar = EnemyHealthBar.Attach(transform);

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
            _tint = TintOf(enemy);
            _flash = 0f;
            _applied = false;
            _windupRemaining = 0f;
            _strikeRemaining = 0f;
            _attackDirection = Vector2.zero;

            _boundScale = _baseScale * enemy.VisualScale;
            transform.localScale = _boundScale;
            _healthBar?.Bind(enemy, _boundScale);
            transform.position = new Vector3(enemy.Position.x, transform.position.y, enemy.Position.y);
            ApplyColor(_tint);

            // 阴影盘只建一次：池里复用的是同一份预制体实例，身体半径变了只重算尺寸，不重建对象
            if (_shadow == null && _renderer != null)
            {
                _shadow = ContactShadowDisc.Attach(transform, _renderer.sharedMaterial, enemy.Def.BodyRadius);
            }
            else
            {
                ContactShadowDisc.Fit(_shadow, enemy.Def.BodyRadius);
            }
        }

        /// <summary>受击变亮。调用方（EnemyViewPool）从 UnitDamaged 事件里拿到目标槽位再转到这里。</summary>
        public void Flash()
        {
            _flash = FlashDuration;
        }

        /// <summary>敌人开始起手：下蹲蓄力。表现层的预警环由 CombatEffects 画，这里只改身体姿态。</summary>
        public void PlayAttackWindup(float duration, float angle)
        {
            _windupDuration = Mathf.Max(0.05f, duration);
            _windupRemaining = _windupDuration;
            _attackDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>敌人真正出手：向前顶一下。近战的红色扇形与远程的枪口闪光由 CombatEffects 画。</summary>
        public void PlayAttackStrike(float angle, float reach)
        {
            _strikeRemaining = _strikeDuration;
            _attackDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // 体型越大，前顶越明显；BOSS 的一下要让人看出“范围很大”。
            _strikeReach = Mathf.Max(0.1f, reach);
            _windupRemaining = 0f;
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
            if ((_windupRemaining > 0f || _strikeRemaining > 0f) &&
                _attackDirection.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(
                    new Vector3(_attackDirection.x, 0f, _attackDirection.y), Vector3.up);
            }
            else if (velocity.sqrMagnitude > 1e-6f)
            {
                Vector2 forward = velocity.normalized;
                transform.rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.y), Vector3.up);
            }

            ApplyAttackPose();

            if (_healthBar != null)
            {
                float height = transform.position.y + _boundScale.y * 0.55f + 0.18f;
                _healthBar.Refresh(new Vector3(position.x, height, position.y));
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

        void ApplyAttackPose()
        {
            float dt = Time.deltaTime;
            float windup = 0f;
            float strike = 0f;

            if (_windupRemaining > 0f)
            {
                _windupRemaining = Mathf.Max(0f, _windupRemaining - dt);
                float elapsed = 1f - _windupRemaining / _windupDuration;
                windup = Mathf.Sin(Mathf.Clamp01(elapsed) * Mathf.PI * 0.5f);
            }

            if (_strikeRemaining > 0f)
            {
                _strikeRemaining = Mathf.Max(0f, _strikeRemaining - dt);
                strike = _strikeRemaining / _strikeDuration;
            }

            if (windup <= 0f && strike <= 0f)
            {
                transform.localScale = _boundScale;
                return;
            }

            // 起手：横向鼓起、纵向压低；出手：快速回弹并向前顶。都不是半径变化，判定不受影响。
            float flatten = 0.12f * windup;
            float punch = 0.10f * strike;
            transform.localScale = new Vector3(
                _boundScale.x * (1f + flatten + punch),
                _boundScale.y * (1f - flatten + punch * 0.3f),
                _boundScale.z * (1f + flatten + punch));

            float push = windup * 0.06f + strike * _strikeReach * 0.12f;
            if (push > 1e-4f)
            {
                Vector3 p = transform.position;
                p.x += _attackDirection.x * push;
                p.z += _attackDirection.y * push;
                transform.position = p;
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
        /// 精英与 BOSS 在同一个色上提亮/加红，玩家一眼能看出这次刷的是高品质怪。
        /// </summary>
        static Color TintOf(EnemyModel enemy)
        {
            string enemyId = enemy.Def.Id;
            if (string.IsNullOrEmpty(enemyId)) return Color.white;

            // FNV-1a：string.GetHashCode 在不同运行时会变（.NET Core 起带随机化），不能用
            uint hash = 2166136261u;
            for (int i = 0; i < enemyId.Length; i++)
            {
                hash ^= enemyId[i];
                hash *= 16777619u;
            }

            float hue = (hash % 360u) / 360f;
            float saturation = enemy.IsBoss ? 0.85f : enemy.IsElite ? 0.72f : 0.55f;
            float value = enemy.IsBoss ? 0.95f : enemy.IsElite ? 0.98f : 0.85f;

            // 地面是土黄偏亮，太淡的色会看不出来，太亮又会把受击变亮吃掉
            return Color.HSVToRGB(hue, saturation, value);
        }
    }
}
