using Game.Core.Events;
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
    /// 玩家、敌人、旅伴、投射物四层的插值用的是同一个 alpha。
    ///
    /// 命中定格（hitstop）也在这里：时钟停在定格上时本帧一步都不跑，但输入照常采样、
    /// LateUpdate 照常同步视图，所以"卡住"的是模拟而不是整帧。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RunLoop : MonoBehaviour
    {
        readonly RunClock _clock = new RunClock();

        IEventBus _bus;
        InputSnapshot _snapshot;
        Vector2 _prevPos;
        Vector2 _currPos;
        Vector2 _prevDir;
        Vector2 _currDir;
        bool _viewReady;

        void Awake()
        {
            // AppRoot 的执行顺序在前（-1000），它的 Awake 里 Events 已经建好，这里订阅不会落空。
            // 没有 AppRoot 的场景（空场景里单独放一个 RunLoop 调试）就没有事件，跳过即可。
            _bus = AppRoot.Instance?.Events;
            _bus?.Subscribe<UnitDamaged>(OnUnitDamaged);
        }

        void OnDestroy()
        {
            _bus?.Unsubscribe<UnitDamaged>(OnUnitDamaged);
            _bus = null;
        }

        /// <summary>
        /// 命中反馈的触发点集中在主循环：定格与震屏用同一条"值得强调"的判定，
        /// 各系统自己决定要不要卡一下的话，两处阈值迟早对不上。
        /// </summary>
        void OnUnitDamaged(UnitDamaged e)
        {
            if (!HitFeedback.IsEmphasized(e.Amount, e.IsCrit)) return;

            _clock.RequestHitStop(HitFeedback.HitStopSeconds);
            AppRoot.Instance?.Camera?.Shake(HitFeedback.ShakeForce);
        }

        void Update()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null) return;

            // 1. 采样输入。定格时也照常采：恢复后不会因为漏了一帧输入而丢一次冲刺
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

            // 2..N 模拟：每步的固定顺序在 RunSimulation.Tick 里
            int steps = _clock.Advance(Time.deltaTime);
            for (int i = 0; i < steps; i++)
            {
                // 上一步命中触发了定格：本帧剩下的步也不再跑，
                // 否则一帧跑三步时，“卡住”要等到下一帧才开始
                if (_clock.IsHitStopped) break;

                _prevPos = _currPos;
                _prevDir = _currDir;
                simulation.Tick(RunClock.FixedStep, ref _snapshot);
                _currPos = simulation.Player.Position;
                _currDir = ViewSnapshot.DirectionOf(simulation.Player.Facing);
            }
        }

        void LateUpdate()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null || !_viewReady) return;

            // 表现层同步。模拟是阶梯的，渲染按实际帧率在相邻两步之间插值，
            // 视图只吃插值结果（或 alpha + 两个模拟位置），不直接读 Model 写 Transform。
            float alpha = _clock.Alpha;

            Vector2 pos = Vector2.Lerp(_prevPos, _currPos, alpha);
            Vector2 dir = Vector2.Lerp(_prevDir, _currDir, alpha);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : _currDir;

            root.PlayerView?.Sync(new ViewSnapshot(pos, dir));

            // 敌人与投射物各自拿自己的 PrevPosition/Position 和同一个 alpha 插值。
            // 掉落物没有上一步位置可分（槽位会换绑），直接按快照写位置。
            // 旅伴与敌人同一套插值：它也每步记 PrevPosition，渲染层只吃插值后的位置。
            root.EnemyViews?.Sync(alpha);
            root.CompanionViews?.Sync(alpha);
            root.ProjectileViews?.Sync(alpha);
            root.PickupViews?.Sync();

            if (root.Camera != null)
            {
                root.Camera.Tick(pos);
            }
        }
    }
}
