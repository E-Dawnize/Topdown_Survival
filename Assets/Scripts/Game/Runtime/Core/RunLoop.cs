using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Views;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 唯一的每帧入口。按固定顺序驱动模拟，模拟走 60Hz 固定步长，每帧最多 3 步。
    /// 渲染时用累加器余量在相邻两个模拟步之间插值：simulation 是阶梯的，
    /// 帧率与 60 不同步时直接写位置会看到一段一顿，带阻尼的相机跟随时尤其明显。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RunLoop : MonoBehaviour
    {
        const float FixedStep = 1f / 60f;
        const int MaxStepsPerFrame = 3;
        const float MaxAccumulator = 0.25f;

        float _accumulator;
        InputSnapshot _snapshot;
        Vector2 _prevPos;
        Vector2 _currPos;
        Vector2 _prevDir;
        Vector2 _currDir;
        bool _viewReady;

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
                if (!_viewReady)
                {
                    _prevPos = _currPos = root.Player.Position;
                    _prevDir = _currDir = ViewSnapshot.DirectionOf(root.Player.Facing);
                    _viewReady = true;
                }

                _accumulator += Time.deltaTime;
                if (_accumulator > MaxAccumulator)
                {
                    _accumulator = MaxAccumulator;
                }

                int steps = 0;
                while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
                {
                    _prevPos = _currPos;
                    _prevDir = _currDir;
                    root.PlayerController.Tick(FixedStep, ref _snapshot);
                    _currPos = root.Player.Position;
                    _currDir = ViewSnapshot.DirectionOf(root.Player.Facing);
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

            // 12. 表现层同步。模拟是阶梯的，渲染按实际帧率在相邻两步之间插值，
            // 视图只吃插值结果，不读 Model。
            if (!_viewReady) return;

            float alpha = Mathf.Clamp01(_accumulator / FixedStep);
            Vector2 pos = Vector2.Lerp(_prevPos, _currPos, alpha);

            Vector2 dir = Vector2.Lerp(_prevDir, _currDir, alpha);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : _currDir;

            root.PlayerView?.Sync(new ViewSnapshot(pos, dir));

            if (root.Camera != null)
            {
                root.Camera.Tick(pos);
            }
        }
    }
}
