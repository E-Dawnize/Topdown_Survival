namespace Game.Systems.Weapons
{
    /// <summary>
    /// 武器里唯一的随机来源，只有"取一个 [0,1) 浮点"这一件事。
    /// 武器内部不许自己 new Random、不许读 Time：单测要把"暴不暴击"钉死就得能传一个固定值桩，
    /// 一局要能复现就得整条随机流由外部一条线管（技术方案 7.5）。
    /// </summary>
    public interface IRandom01
    {
        /// <summary>返回 [0,1) 的浮点。与闪避的约定一致：恰好相等算命中，边界不留给浮点巧合。</summary>
        float Next01();
    }
}
