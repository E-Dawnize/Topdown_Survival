using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 固定俯角相机的跟随焦点：屏幕中心有一个矩形死区，角色在框内时焦点不动；
    /// 出框后焦点以阻尼追到"让角色停在框边"的位置。
    ///
    /// 为什么不用 Cinemachine 的 Composer 来做死区：Composer 是给"相机看向目标"的
    /// 自由镜头用的，它会改相机朝向；在 50° 斜视的正交相机上，朝向一变，屏幕纵向
    /// 的世界投影也跟着变，死区就不再是一个稳定的矩形。这里把相机朝向固定在
    /// (Tilt, 0, 0)，只移动焦点，矩形死区才是真正按屏幕横/纵两根轴工作的。
    ///
    /// 坐标约定：输入输出都是地面 XZ 平面的 Vector2，x 对应屏幕横向，y 对应屏幕纵向。
    /// halfWidth / halfDepth 是相机在地面上的可视半宽/半深（不是屏幕像素）：
    ///   halfWidth = OrthographicSize × Aspect
    ///   halfDepth = OrthographicSize / sin(Tilt)
    /// 死区比例是屏幕宽/高的比例，所以世界半宽 = DeadZone × halfWidth。
    /// </summary>
    public sealed class CameraFocus
    {
        readonly float _deadZone;
        readonly float _smoothTime;

        Vector2 _focus;
        Vector2 _velocity;
        bool _initialized;

        public CameraFocus(float deadZone, float smoothTime)
        {
            _deadZone = Mathf.Clamp(deadZone, 0f, 0.5f);
            _smoothTime = Mathf.Max(0f, smoothTime);
        }

        public Vector2 Focus => _focus;

        /// <summary>第一帧直接对上目标，不从原点滑过去。</summary>
        public void Reset(Vector2 focus)
        {
            _focus = focus;
            _velocity = Vector2.zero;
            _initialized = true;
        }

        /// <summary>
        /// 推进一帧。target 是希望出现在画面上的点（角色脚底 + 跟踪偏移 - 屏幕偏移）；
        /// 返回夹紧前的焦点。调用方负责把焦点再夹到场地边界。
        /// </summary>
        public Vector2 Tick(Vector2 target, float dt, float halfWidth, float halfDepth)
        {
            if (!_initialized)
            {
                Reset(target);
                return _focus;
            }

            if (dt <= 0f) return _focus;

            float deadX = _deadZone * Mathf.Max(0f, halfWidth);
            float deadZ = _deadZone * Mathf.Max(0f, halfDepth);

            Vector2 offset = target - _focus;
            Vector2 desired = _focus;
            bool outside = false;

            if (offset.x > deadX)
            {
                desired.x = target.x - deadX;
                outside = true;
            }
            else if (offset.x < -deadX)
            {
                desired.x = target.x + deadX;
                outside = true;
            }

            if (offset.y > deadZ)
            {
                desired.y = target.y - deadZ;
                outside = true;
            }
            else if (offset.y < -deadZ)
            {
                desired.y = target.y + deadZ;
                outside = true;
            }

            if (!outside)
            {
                // 已经回到框里：清掉残余速度，避免下一次出框时带着旧速度冲一下
                _velocity = Vector2.zero;
                return _focus;
            }

            // 只追到"角色刚好在框边"的点，不是直接追角色本人。
            // 这样角色停下时会停在框边，而不是被吸回画面中心。
            _focus = Vector2.SmoothDamp(_focus, desired, ref _velocity, _smoothTime,
                Mathf.Infinity, dt);
            return _focus;
        }
    }
}
