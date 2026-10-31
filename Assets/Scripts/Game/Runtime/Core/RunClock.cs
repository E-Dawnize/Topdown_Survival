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

        public bool IsHitStopped => _hitStop > 0f;

        /// <summary>
        /// 视图插值系数。定格期间给 1：画面停在最后一步的模拟结果上，
        /// 按累加器余量算会让画面退回上一步，看起来像被打回去了一点。
        /// </summary>
        public float Alpha => IsHitStopped ? 1f : Mathf.Clamp01(_accumulator / FixedStep);

        /// <summary>请求定格。已经在定格里时取更长的那个，短请求不会把长定格截短。</summary>
        public void RequestHitStop(float seconds)
        {
            if (seconds > _hitStop) _hitStop = seconds;
        }

        /// <summary>
        /// 推进一个渲染帧，返回本帧要跑的固定步数。
        /// 定格期间返回 0 且不累积：暂停掉的那段真实时间不补跑，
        /// 恢复后的第一帧最多走正常的两步（余量 + 一帧时间），不会看到"卡一下再瞬移"。
        /// </summary>
        public int Advance(float realDelta)
        {
            if (_hitStop > 0f)
            {
                _hitStop -= realDelta;
                return 0;
            }

            _accumulator += realDelta;
            if (_accumulator > MaxAccumulator)
            {
                _accumulator = MaxAccumulator;
            }

            int steps = 0;
            while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
            {
                _accumulator -= FixedStep;
                steps++;
            }
            return steps;
        }
    }
}
