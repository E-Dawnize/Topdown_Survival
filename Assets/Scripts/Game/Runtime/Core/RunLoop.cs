using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 唯一的每帧入口。按固定顺序驱动模拟，模拟走 60Hz 固定步长，
    /// 每帧最多 3 步，渲染不做插值。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RunLoop : MonoBehaviour
    {
        const float FixedStep = 1f / 60f;
        const int MaxStepsPerFrame = 3;
        const float MaxAccumulator = 0.25f;

        float _accumulator;
        InputSnapshot _snapshot;

        void Update()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null) return;

            // 1. 采样输入
            if (root.Input != null)
            {
                _snapshot = root.Input.Sample();
            }

            // 2..10 模拟
            if (root.Player != null && root.PlayerController != null)
            {
                _accumulator += Time.deltaTime;
                if (_accumulator > MaxAccumulator)
                {
                    _accumulator = MaxAccumulator;
                }

                int steps = 0;
                while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
                {
                    root.PlayerController.Tick(FixedStep, ref _snapshot);
                    _accumulator -= FixedStep;
                    steps++;
                }
            }

            // 11. 事件统一分发
            root.Events?.Flush();
        }

        void LateUpdate()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null) return;

            // 12. 表现层同步
            root.PlayerView?.Sync();

            if (root.Camera != null)
            {
                root.Camera.Tick(root.Player != null ? root.Player.Position : Vector2.zero);
            }
        }
    }
}
