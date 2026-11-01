namespace Game.Views
{
    /// <summary>
    /// 游戏内所有 Overlay Canvas 的排序层。数值集中在这里，新增面板时只改这一处，
    /// 不用在各自的 BuildUi 里再写一个魔法数字（历史上 HUD=10/升级=25/结算=30/菜单=40 是散着写的）。
    /// </summary>
    public static class UiLayer
    {
        /// <summary>局内 HUD。HudCanvas 预制体里的 Canvas sortingOrder 与这里保持一致。</summary>
        public const int Hud = 10;

        /// <summary>升级选卡。盖住 HUD。</summary>
        public const int Upgrade = 25;

        /// <summary>结算面板。升级之后不可能再开，取更大只是给以后留顺序。</summary>
        public const int Result = 30;

        /// <summary>主菜单。自成场景，取最大只是留顺序。</summary>
        public const int Menu = 40;

        /// <summary>启动失败提示。装配根专用：它要盖住一切，包括画错的 HUD。</summary>
        public const int StartupError = 1000;
    }
}
