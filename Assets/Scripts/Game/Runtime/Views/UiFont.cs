using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 运行时 uGUI 文本的字体来源。预制体里写的与代码建的 Text 默认都是内置 LegacyRuntime，
    /// 它没有中文字形，所以运行时统一换成系统字体。
    ///
    /// 不做 TextMeshPro：项目没导入 TMP Essentials，运行时建 TMP 文本会缺默认字体。
    /// 候选名按平台排：Windows 上第一个能用的是微软雅黑；macOS / Linux 的名字也列进来，
    /// 但只在 Windows 上验证过，换平台要实测。
    ///
    /// 缓存一份实例：动态字体每 new 一份就多一张字形图集，HUD 与商店各来一份等于双倍字体纹理。
    /// </summary>
    public static class UiFont
    {
        static Font _cached;

        public static Font Resolve()
        {
            if (_cached != null) return _cached;

            _cached = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 18);

            if (_cached == null) _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _cached;
        }
    }
}
