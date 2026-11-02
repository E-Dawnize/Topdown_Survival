using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Services
{
    /// <summary>
    /// 发布包启动时固定 UI 逻辑分辨率，避免 Windows DPI 缩放让 Canvas 实际宽高
    /// 与 CanvasScaler 的 1920×1080 参考分辨率不一致。
    ///
    /// 传入的 1920×1080 是 Unity 的 Screen 逻辑尺寸；窗口由系统按 DPI 缩放，
    /// Canvas 与窗口保持同一套逻辑坐标，文字不会被二次拉伸。
    /// 编辑器不处理，避免每次 Play 都改 Game View。
    /// </summary>
    public static class DisplayBootstrap
    {
        const int TargetWidth = 1920;
        const int TargetHeight = 1080;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            if (Application.isEditor) return;

            int screenWidth = Screen.currentResolution.width;
            int screenHeight = Screen.currentResolution.height;
            if (screenWidth <= 0 || screenHeight <= 0) return;

            int requestWidth = Mathf.Min(TargetWidth, screenWidth);
            int requestHeight = Mathf.Min(TargetHeight, screenHeight);

            Screen.SetResolution(requestWidth, requestHeight, FullScreenMode.Windowed);
            ResizeWindowToClient(requestWidth, requestHeight);
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)]
        struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy,
            uint flags);

        const uint SwpNoMove = 0x0002;
        const uint SwpNoZOrder = 0x0004;
        const uint SwpNoActivate = 0x0010;

        /// <summary>
        /// Windows 在 150% 缩放下会把 Screen.SetResolution 的窗口物理尺寸压小，
        /// 但 Unity 的 Screen.width 仍是请求的逻辑值。这里把窗口客户区直接调到同一尺寸，
        /// 避免 Canvas 1920 宽却被系统缩到 1280 物理像素。
        /// </summary>
        static void ResizeWindowToClient(int clientWidth, int clientHeight)
        {
            IntPtr hwnd = FindWindow(null, Application.productName);
            if (hwnd == IntPtr.Zero) hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero) return;

            if (!GetClientRect(hwnd, out Rect client) || !GetWindowRect(hwnd, out Rect window)) return;

            int frameWidth = (window.Right - window.Left) - (client.Right - client.Left);
            int frameHeight = (window.Bottom - window.Top) - (client.Bottom - client.Top);
            int outerWidth = clientWidth + frameWidth;
            int outerHeight = clientHeight + frameHeight;
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, outerWidth, outerHeight,
                SwpNoMove | SwpNoZOrder | SwpNoActivate);
        }
#else
        static void ResizeWindowToClient(int clientWidth, int clientHeight)
        {
        }
#endif
    }
}
