using Game.Views;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 唯一的每帧入口。按固定顺序驱动模拟，模拟走 60Hz 固定步长，每帧最多 3 步。
    /// 每一步要跑哪些系统、按什么顺序，全在 RunSimulation 里（主循环与集成测试共用那一段）。
    ///
    /// 渲染时用累加器余量在相邻两个模拟步之间插值：simulation 是阶梯的，
    /// 帧率与 60 不同步时直接写位置会看到一段一顿，带阻尼的相机跟随时尤其明显。
    /// 玩家、敌人、投射物三层的插值用的是同一个 alpha。
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

            RunSimulation simulation = root.Simulation;
            if (simulation == null) return;

            if (!_viewReady)
            {
                _prevPos = _currPos = simulation.Player.Position;
                _prevDir = _currDir = ViewSnapshot.DirectionOf(simulation.Player.Facing);
                _viewReady = true;
            }

            _accumulator += Time.deltaTime;
            if (_accumulator > MaxAccumulator)
            {
                _accumulator = MaxAccumulator;
            }

            // 2..N 模拟：每步的固定顺序在 RunSimulation.Tick 里
            int steps = 0;
            while (_accumulator >= FixedStep && steps < MaxStepsPerFrame)
            {
                _prevPos = _currPos;
                _prevDir = _currDir;
                simulation.Tick(FixedStep, ref _snapshot);
                _currPos = simulation.Player.Position;
                _currDir = ViewSnapshot.DirectionOf(simulation.Player.Facing);
                _accumulator -= FixedStep;
                steps++;
            }
        }

        void LateUpdate()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null || !_viewReady) return;

            // 表现层同步。模拟是阶梯的，渲染按实际帧率在相邻两步之间插值，
            // 视图只吃插值结果（或 alpha + 两个模拟位置），不直接读 Model 写 Transform。
            float alpha = Mathf.Clamp01(_accumulator / FixedStep);

            Vector2 pos = Vector2.Lerp(_prevPos, _currPos, alpha);
            Vector2 dir = Vector2.Lerp(_prevDir, _currDir, alpha);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : _currDir;

            root.PlayerView?.Sync(new ViewSnapshot(pos, dir));

            // 敌人与投射物各自拿自己的 PrevPosition/Position 和同一个 alpha 插值
            root.EnemyViews?.Sync(alpha);
            root.ProjectileViews?.Sync(alpha);

            if (root.Camera != null)
            {
                root.Camera.Tick(pos);
            }
        }
    }
}
