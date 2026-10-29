namespace Game.Model
{
    /// <summary>
    /// 一局的运行数据。I2 之前只有金币和波次占位，够 HUD 显示。
    /// </summary>
    public sealed class RunState
    {
        public int Coins { get; private set; }
        public int WaveIndex { get; set; } = 1;
        public int WavesTotal { get; set; } = 5;

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
