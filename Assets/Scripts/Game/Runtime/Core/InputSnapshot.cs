using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 一帧的输入快照。RunLoop 每帧生成一份，模拟步进时按引用传递，
    /// 消费过的按钮位会被清掉，保证一帧跑多步时按下只生效一次。
    /// </summary>
    public struct InputSnapshot
    {
        public Vector2 Move;
        public bool DashPressed;

        /// <summary>全屏吸取经验（E/RB）。一次按下只让第一个模拟步看到，和冲刺同一条边沿语义。</summary>
        public bool CollectPressed;

        public bool AnyPressed => DashPressed || CollectPressed;
    }

    /// <summary>
    /// 模拟层输入缓冲。连续轴每帧覆盖，按钮边沿锁存到至少一个模拟步真的跑过为止。
    /// 这样 120Hz/144Hz 下没有固定步的帧、命中定格期间按下的冲刺不会被下一帧清掉；
    /// 一帧跑多步时，第一步消费掉按钮，后续步骤看到的是 false。
    /// 它是纯 C# 类，InputService 和 EditMode 测试共用同一套语义。
    /// </summary>
    public sealed class InputBuffer
    {
        InputSnapshot _current;

        public InputSnapshot Current => _current;

        /// <summary>把本帧边沿并入缓冲。按钮用 OR，连续轴直接覆盖。</summary>
        public InputSnapshot Capture(in InputSnapshot edges)
        {
            _current.Move = edges.Move;
            _current.DashPressed |= edges.DashPressed;
            _current.CollectPressed |= edges.CollectPressed;
            return _current;
        }

        /// <summary>一个模拟步已经观察过当前按钮，清掉边沿，避免下一步重复触发。</summary>
        public void ConsumeSimulationStep()
        {
            _current.DashPressed = false;
            _current.CollectPressed = false;
        }

        public void Clear()
        {
            _current = default;
        }
    }
}
