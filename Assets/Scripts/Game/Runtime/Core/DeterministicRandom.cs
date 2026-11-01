using Game.Systems.Weapons;

namespace Game.Core
{
    /// <summary>
    /// 固定种子的确定性随机（xorshift128，技术方案 7.5）。同一个种子得到同一条序列，
    /// 与机器、帧率、调用时机都无关——"固定种子 + 固定输入 = 相同结果"要能复现，就不能用
    /// UnityEngine.Random（全局状态，别的系统随手抽一下就把整条序列改了）。
    /// 这里同时实现了武器系统要的 IRandom01，刷怪与暴击可以共用同一套流。
    /// </summary>
    public sealed class DeterministicRandom : IRandom01
    {
        /// <summary>24 位尾数的换算倒数：与 xorshift 高 24 位配对，得到 [0,1) 均匀分布。</summary>
        const float ToUnit = 1f / 16777216f;

        uint _x;
        uint _y;
        uint _z;
        uint _w;

        public DeterministicRandom(ulong seed)
        {
            // 把 64 位种子摊进四个 32 位状态。直接 <<32 或 >>32 会让半数字节相同，
            // 四个状态一接近，xorshift 前几步的输出就会明显偏（刷怪位置挤在一片），所以走 splitmix。
            ulong state = seed;
            _x = NextSeedWord(ref state);
            _y = NextSeedWord(ref state);
            _z = NextSeedWord(ref state);
            _w = NextSeedWord(ref state);

            // 全零状态在 xorshift 里是吸收态，永远出 0
            if ((_x | _y | _z | _w) == 0u) _x = 0x9E3779B9u;
        }

        /// <summary>返回 [0,1)。取高 24 位而不是取低位：低位在 xorshift 里周期与相关性都更差。</summary>
        public float Next01()
        {
            uint t = _x ^ (_x << 11);
            _x = _y;
            _y = _z;
            _z = _w;
            _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
            return (_w >> 8) * ToUnit;
        }

        /// <summary>返回 [0, maxExclusive)。maxExclusive &lt;= 0 时返回 0，调用方自己保证不为空表。</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;

            int value = (int)(Next01() * maxExclusive);
            return value < maxExclusive ? value : maxExclusive - 1;   // 浮点边界兜一下
        }

        /// <summary>返回 [min, max) 区间的浮点。用于刷怪距离这类"范围里随便取一个"。</summary>
        public float Range(float min, float max) => min + (max - min) * Next01();

        static uint NextSeedWord(ref ulong state)
        {
            // splitmix64：每取一个 64 位值就把 state 再搅一遍，连续取出来的四个字彼此没有明显关系
            state += 0x9E3779B97F4A7C15ul;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            z ^= z >> 31;
            return (uint)(z ^ (z >> 32));
        }
    }
}
