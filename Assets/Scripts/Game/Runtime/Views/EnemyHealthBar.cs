using Game.Model;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 敌人头顶的世界空间血条。两条 LineRenderer：深色底 + 按比例缩短的彩色填充。
    ///
    /// 用线而不是世界 Canvas：一个 LineRenderer 只有 2 个点，不需要 RectTransform/字体，
    /// 同屏 120 只怪也不会因为血条各自建 Canvas 而炸批处理。线宽用 View 对齐，永远正对相机。
    ///
    /// 只在“需要读血量”的时候显示：掉过血的怪、精英、BOSS、Tier 4 以上的重装怪。
    /// 满血的杂兵不画，避免满屏都是血条；BOSS 另有顶部血条，这里只是让它在头顶也有一个参照。
    /// </summary>
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        static readonly Color BackColor = new Color(0.03f, 0.03f, 0.04f, 0.85f);
        static readonly Color LowColor = new Color(0.92f, 0.22f, 0.20f, 1f);
        static readonly Color HighColor = new Color(0.35f, 0.90f, 0.42f, 1f);

        LineRenderer _back;
        LineRenderer _fill;
        EnemyModel _enemy;

        public static EnemyHealthBar Attach(Transform parent)
        {
            var go = new GameObject("HealthBar");
            go.transform.SetParent(parent, false);
            return go.AddComponent<EnemyHealthBar>();
        }

        void Awake()
        {
            _back = CreateLine(transform, "Back", 0.075f);
            _fill = CreateLine(transform, "Fill", 0.045f);
            Hide();
        }

        /// <summary>绑定到当前敌人。父节点的缩放会用来反算，让血条在世界空间里保持固定粗细。</summary>
        public void Bind(EnemyModel enemy, Vector3 parentScale)
        {
            _enemy = enemy;
            SetParentScale(parentScale);
            Hide();
        }

        public void SetParentScale(Vector3 scale)
        {
            float x = Mathf.Abs(scale.x) > 1e-4f ? scale.x : 1f;
            float y = Mathf.Abs(scale.y) > 1e-4f ? scale.y : 1f;
            float z = Mathf.Abs(scale.z) > 1e-4f ? scale.z : 1f;
            transform.localScale = new Vector3(1f / x, 1f / y, 1f / z);
        }

        /// <summary>
        /// 每帧写一次。center 是敌人头顶的世界坐标；宽/颜色只在这里算，模拟层不读血条。
        /// </summary>
        public void Refresh(Vector3 center)
        {
            if (_enemy == null || _back == null || _fill == null || _enemy.MaxHP <= 0f)
            {
                Hide();
                return;
            }

            float ratio = Mathf.Clamp01(_enemy.HP / _enemy.MaxHP);
            bool visible = ratio < 0.999f || _enemy.IsElite || _enemy.IsBoss || _enemy.Def.Tier >= 4;
            if (!visible || ratio <= 0f)
            {
                Hide();
                return;
            }

            Camera camera = Camera.main;
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();

            float width = Mathf.Clamp(_enemy.Radius * 2.4f, 0.70f, 2.20f);
            Vector3 left = center - right * (width * 0.5f);
            Vector3 rightPoint = center + right * (width * 0.5f);

            _back.enabled = true;
            _fill.enabled = true;

            _back.positionCount = 2;
            _back.SetPosition(0, left);
            _back.SetPosition(1, rightPoint);
            _back.startColor = BackColor;
            _back.endColor = BackColor;

            _fill.positionCount = 2;
            _fill.SetPosition(0, left);
            _fill.SetPosition(1, Vector3.Lerp(left, rightPoint, ratio));
            Color fill = Color.Lerp(LowColor, HighColor, ratio);
            _fill.startColor = fill;
            _fill.endColor = fill;
        }

        public void Hide()
        {
            if (_back != null) _back.enabled = false;
            if (_fill != null) _fill.enabled = false;
        }

        static LineRenderer CreateLine(Transform parent, string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = CombatEffects.LineMaterial;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }
    }
}
