using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 战斗中的一次性世界表现：近战扇形、敌人起手预警环。
    ///
    /// 单独收一个池子而不是每个武器/每个敌人各挂一个 LineRenderer：
    /// 场上最多 120 只怪，绝大多数在待机；只有真的挥砍/起手的那几只才借一个渲染器。
    /// 位置全部用世界坐标，调用方给“脚底坐标 + 朝向 + 半径/角度”即可，
    /// 不要求特效挂在谁的 Transform 下面，因此玩家、旅伴、敌人可以共用同一套。
    ///
    /// 只读表现数据，不参与伤害判定；池满时复用最老的一条，不会因为同屏攻击太多而无限建对象。
    /// </summary>
    public static class CombatEffects
    {
        const int MaxEffects = 64;

        static readonly List<Effect> _effects = new List<Effect>(MaxEffects);
        static Transform _root;
        static Material _material;
        static int _next;

        /// <summary>
        /// 画一个扇形挥砍。delay 让弧线在伤害窗口开始时才出现，duration 是可见时长。
        /// </summary>
        public static void Slash(Vector3 origin, Vector2 aim, float range, float halfAngleDeg, Color color,
            float delay, float duration)
        {
            if (range <= 0.01f || duration <= 0f) return;

            Effect effect = Rent();
            if (effect == null) return;

            effect.PlaySlash(origin, aim, range, halfAngleDeg, color, delay, duration);
        }

        /// <summary>
        /// 画一个地面预警环。startRadius → endRadius 收拢，配合 EnemyAttackStarted 的持续时间。
        /// </summary>
        public static void Ring(Vector3 origin, float startRadius, float endRadius, Color color, float duration)
        {
            if (duration <= 0f) return;

            Effect effect = Rent();
            if (effect == null) return;

            effect.PlayRing(origin, startRadius, Mathf.Max(0.05f, endRadius), color, duration);
        }

        /// <summary>场景卸载时由 AppRoot 调：销毁池根节点并清掉静态引用。</summary>
        public static void Clear()
        {
            _effects.Clear();
            _next = 0;

            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
                _root = null;
            }
        }

        static Effect Rent()
        {
            EnsureRoot();

            for (int i = 0; i < _effects.Count; i++)
            {
                int index = (_next + i) % _effects.Count;
                Effect candidate = _effects[index];
                if (candidate == null) continue;
                if (candidate.IsPlaying) continue;

                _next = (index + 1) % _effects.Count;
                candidate.gameObject.SetActive(true);
                return candidate;
            }

            if (_effects.Count >= MaxEffects)
            {
                // 全部在播：抢最老的一条。攻击频率远低于这个上限，这里只是兜底，
                // 不为了极端同屏去扩池，否则一次大混战会让常驻渲染器越来越多。
                Effect oldest = _effects[_next];
                _next = (_next + 1) % _effects.Count;
                if (oldest == null) return Create();
                oldest.gameObject.SetActive(true);
                return oldest;
            }

            Effect created = Create();
            if (created != null) _effects.Add(created);
            return created;
        }

        static void EnsureRoot()
        {
            if (_root != null) return;

            var go = new GameObject("[CombatEffects]");
            _root = go.transform;
            _effects.Clear();
            _next = 0;
        }

        static Effect Create()
        {
            EnsureRoot();

            var go = new GameObject("Effect");
            go.transform.SetParent(_root, false);

            var line = go.AddComponent<LineRenderer>();
            Material material = SharedMaterial();
            if (material != null) line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.generateLightingData = false;

            var effect = go.AddComponent<Effect>();
            effect.Bind(line);
            go.SetActive(false);
            return effect;
        }

        static Material SharedMaterial()
        {
            if (_material != null) return _material;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Debug.LogError("[CombatEffects] 找不到可用的无光照 shader，攻击弧线不会显示");
                return null;
            }

            _material = new Material(shader);
            _material.hideFlags = HideFlags.DontSave;
            _material.name = "MAT_CombatEffect";
            return _material;
        }

        sealed class Effect : MonoBehaviour
        {
            enum Mode
            {
                None,
                Slash,
                Ring
            }

            const int ArcSegments = 20;
            const int RingSegments = 40;

            LineRenderer _line;
            Mode _mode;
            Vector3 _origin;
            Vector2 _aim = Vector2.up;
            float _range;
            float _halfAngleDeg;
            float _startRadius;
            float _endRadius;
            Color _color;
            float _delay;
            float _duration;
            float _elapsed;

            public bool IsPlaying => _mode != Mode.None;

            public void Bind(LineRenderer line)
            {
                _line = line;
                _line.enabled = false;
            }

            public void PlaySlash(Vector3 origin, Vector2 aim, float range, float halfAngleDeg, Color color,
                float delay, float duration)
            {
                _mode = Mode.Slash;
                _origin = new Vector3(origin.x, 0.06f, origin.z);
                _aim = aim.sqrMagnitude > 1e-8f ? aim.normalized : Vector2.up;
                _range = range;
                _halfAngleDeg = Mathf.Clamp(halfAngleDeg, 1f, 180f);
                _color = color;
                _delay = Mathf.Max(0f, delay);
                _duration = Mathf.Max(0.01f, duration);
                _elapsed = 0f;

                ConfigureLine(closed: false);
                DrawSlash(0f);
            }

            public void PlayRing(Vector3 origin, float startRadius, float endRadius, Color color, float duration)
            {
                _mode = Mode.Ring;
                _origin = new Vector3(origin.x, 0.05f, origin.z);
                _startRadius = startRadius;
                _endRadius = endRadius;
                _color = color;
                _delay = 0f;
                _duration = Mathf.Max(0.01f, duration);
                _elapsed = 0f;

                ConfigureLine(closed: true);
                DrawRing(0f);
            }

            void ConfigureLine(bool closed)
            {
                if (_line == null) return;

                _line.positionCount = _mode == Mode.Slash ? ArcSegments + 2 : RingSegments;
                _line.loop = closed;
                _line.enabled = true;
            }

            void Update()
            {
                if (_mode == Mode.None || _line == null) return;

                _elapsed += Time.deltaTime;

                if (_elapsed < _delay)
                {
                    _line.enabled = false;
                    return;
                }

                float t = Mathf.Clamp01((_elapsed - _delay) / _duration);
                if (t >= 1f)
                {
                    Stop();
                    return;
                }

                _line.enabled = true;
                if (_mode == Mode.Slash) DrawSlash(t);
                else DrawRing(t);
            }

            void DrawSlash(float t)
            {
                // 完整扇形从伤害窗口开始就亮起：判定在这一刻对整个扇形结算一次，
                // 弧线必须同时覆盖整个判定范围，不能让右半边的敌人“先掉血、后看到弧线”。
                float half = _halfAngleDeg * Mathf.Deg2Rad;

                _line.positionCount = ArcSegments + 2;
                _line.SetPosition(0, _origin);
                for (int i = 0; i <= ArcSegments; i++)
                {
                    float angle = Mathf.Lerp(-half, half, i / (float)ArcSegments);
                    _line.SetPosition(i + 1, Point(angle));
                }
                _line.SetPosition(ArcSegments + 1, _origin);

                float alpha = _color.a * Mathf.Pow(1f - t, 0.65f);
                Color color = new Color(_color.r, _color.g, _color.b, alpha);
                _line.startColor = color;
                _line.endColor = color;
                _line.widthMultiplier = Mathf.Lerp(0.13f, 0.035f, t);
            }

            void DrawRing(float t)
            {
                float radius = Mathf.Lerp(_startRadius, _endRadius, EaseOut(t));
                for (int i = 0; i < RingSegments; i++)
                {
                    float angle = i / (float)RingSegments * Mathf.PI * 2f;
                    _line.SetPosition(i, _origin + new Vector3(Mathf.Cos(angle) * radius, 0f,
                        Mathf.Sin(angle) * radius));
                }

                // 前 20% 淡入，保持到 75%，最后快速消失：预警环要“能看见”而不是一直亮。
                float alpha = _color.a;
                if (t < 0.2f) alpha *= t / 0.2f;
                else if (t > 0.75f) alpha *= 1f - (t - 0.75f) / 0.25f;

                Color color = new Color(_color.r, _color.g, _color.b, Mathf.Clamp01(alpha));
                _line.startColor = color;
                _line.endColor = color;
                _line.widthMultiplier = Mathf.Lerp(0.08f, 0.035f, t);
            }

            Vector3 Point(float angle)
            {
                // aim 的 x/y 映射到世界 x/z；角度是相对 aim 的偏移。
                float aimAngle = Mathf.Atan2(_aim.y, _aim.x);
                float world = aimAngle + angle;
                return _origin + new Vector3(Mathf.Cos(world), 0f, Mathf.Sin(world)) * _range;
            }

            static float EaseOut(float t)
            {
                return 1f - (1f - t) * (1f - t);
            }

            void Stop()
            {
                _mode = Mode.None;
                if (_line != null) _line.enabled = false;
                gameObject.SetActive(false);
            }
        }
    }
}
