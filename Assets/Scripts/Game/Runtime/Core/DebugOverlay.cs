using System.Text;
using Game.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Core
{
    /// <summary>
    /// 开发期覆盖层。F1 开关。每帧只做加法，不做字符串拼接之外的分配。
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] bool _visible = true;
        [SerializeField] Vector2 _anchor = new Vector2(8f, 8f);

        readonly StringBuilder _sb = new StringBuilder(256);
        GUIStyle _style;

        float _fps;
        float _frameMs;
        float _worstFrameMs;
        float _windowStart;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                _visible = !_visible;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            float instFps = 1f / dt;
            _fps = _fps <= 0f ? instFps : Mathf.Lerp(_fps, instFps, 0.08f);
            _frameMs = Mathf.Lerp(_frameMs <= 0f ? dt * 1000f : _frameMs, dt * 1000f, 0.08f);
            _worstFrameMs = Mathf.Max(_worstFrameMs, dt * 1000f);

            // 每 2 秒把峰值重置一次，避免一个早期的卡顿永远挂在那
            if (Time.unscaledTime - _windowStart > 2f)
            {
                _windowStart = Time.unscaledTime;
                _worstFrameMs = _frameMs;
            }
        }

        void OnGUI()
        {
            if (!_visible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = false,
                    alignment = TextAnchor.UpperLeft,
                };
                _style.normal.textColor = Color.white;
            }

            _sb.Clear();
            _sb.Append("FPS ").Append(_fps.ToString("0.0"))
                .Append("   ").Append(_frameMs.ToString("0.00")).Append(" ms")
                .Append("   peak ").Append(_worstFrameMs.ToString("0.0")).Append(" ms")
                .Append('\n');
            _sb.Append("GC ").Append((System.GC.GetTotalMemory(false) / 1048576f).ToString("0.0")).Append(" MB");

            AppRoot root = AppRoot.Instance;
            if (root != null)
            {
                _sb.Append("   Events ").Append(root.Events?.SubscriptionCount ?? 0).Append(" subs\n");

                if (root.ConfigOk && root.Config != null)
                {
                    _sb.Append("Config ").Append(root.Config.Version);
                }
                else
                {
                    _sb.Append("Config 未加载");
                }

                _sb.Append("   Seed ").Append(root.RunSeed).Append('\n');

                InputService input = root.Input;
                if (input != null)
                {
                    Vector2 move = input.MoveAxis;
                    _sb.Append("Move (").Append(move.x.ToString("0.00")).Append(", ")
                        .Append(move.y.ToString("0.00")).Append(")   Dash ")
                        .Append(input.DashPressed ? "按下" : "—")
                        .Append("   Skill ")
                        .Append(input.SkillPressed ? "按下" : "—");
                }
            }
            else
            {
                _sb.Append("\n场景里没有 AppRoot");
            }

            Rect rect = new Rect(_anchor.x, _anchor.y, 560f, 200f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height), _sb.ToString(), _style);
        }
    }
}
