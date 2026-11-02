using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 固定步长时钟：把真实帧时间换成"这一帧该走几步"，外加命中定格。
    /// 从 RunLoop 里拆出来是为了能在命令行直接单测"定格期间不推进、恢复后不补跑"——
    /// MonoBehaviour 的 Update 读 Time.deltaTime，测试里没法那样喂时间。
    /// </summary>
    public sealed class RunClock
    {
        /// <summary>模拟的固定步长。渲染帧率与它不同步，所以位置一律靠 Alpha 插值。</summary>
        public const float FixedStep = 1f / 60f;

        /// <summary>一帧最多推进的步数：掉帧或切回前台时不允许一次补跑一长串。</summary>
        const int MaxStepsPerFrame = 3;

        const float MaxAccumulator = 0.25f;

        float _accumulator;
        float _hitStop;

        /// <summary>
        /// 定格结束的那一帧也要保持 alpha=1。
        /// BeginFrame 在这一帧只扣掉 hitstop、不会执行新的固定步；如果立刻按累加器算 alpha，
        /// 视图会从“最后一步的当前位置”退回到上一步，表现为每次定格结束都抖一下。
        /// </summary>
        bool _holdLastAlpha;

        public bool IsHitStopped => _hitStop > 0f;

        /// <summary>
        /// 视图插值系数。定格期间给 1：画面停在最后一步的模拟结果上，
        /// 按累加器余量算会让画面退回上一步，看起来像被打回去了一点。
        /// </summary>
        public float Alpha => IsHitStopped || _holdLastAlpha ? 1f : Mathf.Clamp01(_accumulator / FixedStep);

        /// <summary>请求定格。已经在定格里时取更长的那个，短请求不会把长定格截短。</summary>
        public void RequestHitStop(float seconds)
        {
            if (seconds > _hitStop) _hitStop = seconds;
        }

        /// <summary>
        /// 开始一个渲染帧：更新定格与累加器，返回本帧最多可以执行几步。
        /// 这里只报告额度，不扣累加器；真正执行一步时由 TryConsumeStep 扣除。
        /// 这样主循环中途因定格退出时，未执行的步数仍然留在累加器里，不会凭空丢掉。
        /// </summary>
        public int BeginFrame(float realDelta)
        {
            _holdLastAlpha = false;

            if (_hitStop > 0f)
            {
                _hitStop -= realDelta;
                if (_hitStop <= 0f)
                {
                    _hitStop = 0f;
                    _holdLastAlpha = true;
                }

                return 0;
            }

            _accumulator += realDelta;
            if (_accumulator > MaxAccumulator)
            {
                _accumulator = MaxAccumulator;
            }

            int steps = 0;
            float remaining = _accumulator;
            while (remaining >= FixedStep && steps < MaxStepsPerFrame)
            {
                remaining -= FixedStep;
                steps++;
            }
            return steps;
        }

        /// <summary>
        /// 提交一个固定步。返回 false 表示当前不该再跑：可能累加器不够，
        /// 也可能上一步命中刚刚请求了定格。只有返回 true 时累加器才会被扣除。
        /// </summary>
        public bool TryConsumeStep()
        {
            if (_hitStop > 0f || _accumulator < FixedStep) return false;

            _accumulator -= FixedStep;
            return true;
        }
    }
}
