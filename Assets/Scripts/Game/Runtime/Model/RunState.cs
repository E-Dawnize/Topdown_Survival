namespace Game.Model
{
    /// <summary>
    /// 一局的运行数据。HUD 读它的金币与波次。
    /// 波次两项由 RunController 在构造时从配置表写入，这个模型自己不给默认波数：
    /// 写死一个数字，配置改成几波就两边对不上（HUD 显示、结束判定会一起错）。
    /// </summary>
    public sealed class RunState
    {
        public int Coins { get; private set; }
        public int WaveIndex { get; set; } = 1;

        /// <summary>配置里的总波数。未绑定配置前是 0。</summary>
        public int WavesTotal { get; set; }

        public void AddCoins(int amount)
        {
            if (amount == 0) return;
            Coins += amount;
        }

        public bool TrySpend(int amount)
        {
            if (amount > Coins) return false;
            Coins -= amount;
            return true;
        }
    }
}
