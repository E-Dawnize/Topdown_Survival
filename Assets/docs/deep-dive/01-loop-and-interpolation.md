# 主循环与渲染插值：怎么落地

## 要解决的问题

自动战斗游戏里，模拟必须可复现（同种子同输入得到同结果），渲染必须跟得上显示器。这两件事的节拍不一样：模拟要稳定步长，渲染帧率随机器浮动。直接让 `Update` 里的 `deltaTime` 驱动移动，帧率一变数值就漂；让固定步长直接写 Transform，帧率不是 60 的整数分之一时画面就是一顿一顿。

## 落地步骤

第一步，把"这一帧走几步"从 MonoBehaviour 里拿出来，做成 `RunClock`。

```csharp
public sealed class RunClock
{
    public const float FixedStep = 1f / 60f;
    const int MaxStepsPerFrame = 3;
    const float MaxAccumulator = 0.25f;

    float _accumulator;
    float _hitStop;

    public float Alpha => IsHitStopped ? 1f : Mathf.Clamp01(_accumulator / FixedStep);
    public int Advance(float realDelta) { /* 累积、截断、返回 0..3 */ }
    public void RequestHitStop(float seconds) { /* 取更长的那个 */ }
}
```

抽出类的理由是可测：命令行 harness 能直接喂 1/144、1/60、0.5 这些时间，断言"定格期间不推进、恢复后不补跑"。挂在 MonoBehaviour 上就只能进 Play 看。

第二步，把每步的系统顺序独立成 `RunSimulation`。主循环和集成测试共用同一个实例化路径，测试验的才是真实接入顺序，而不是测试里另抄一份。

```
PlayerController.Tick
EnemySystem.Tick          // 先动
SpatialGrid.Rebuild       // 再重建，武器读的是这一步的位置
PlayerWeapons.Tick
CompanionWeapons.Tick
Cleanup                   // 打死的人在这摘掉，掉落在这生成
PickupSystem.Tick
WaveController.Tick
Events.Flush              // 整轮结束才分发
```

第三步，渲染层只接受插值后的快照。

```csharp
root.PlayerView?.Sync(new ViewSnapshot(pos, dir));
```

`ViewSnapshot` 是 readonly struct，位置和朝向单位向量。玩家、敌人、旅伴、投射物全部走同一条规则：读 `PrevPosition` 和 `Position`，按 `Alpha` 做 `Lerp`。视图层拿不到 `PlayerModel`。

第四步，把 `Alpha` 在 `LateUpdate` 里算出来再分发。`RunLoop` 声明 `[DefaultExecutionOrder(-100)]`，保证它早于 `CinemachineBrain`（默认 0）执行，相机读的是本帧更新后的锚点。

## 关键决定与代价

`MaxStepsPerFrame = 3` 加截断：掉帧或者切回前台时不允许补跑一长串，宁可让游戏慢下来。代价是极端卡顿时模拟时间会比真实时间慢。

`MaxAccumulator = 0.25f`：超过就丢，避免"卡了 2 秒回来连跑 120 步"。

定格期间 `Alpha` 取 1，不是按余量算。按余量算会退回上一步的位置，看起来像被打回去一点。

## 踩过的坑

拼接期最明显的症状是左右移动时相机抖。根因是模拟位置直接写 Transform：一帧走 0 步、1 步、2 步的分布让位置呈阶梯，相机阻尼再去追这个阶梯，就成了忽快忽慢。插值是唯一正确的修法，调阻尼只是掩盖。

CinemachineBrain 的 `m_UpdateMethod` 默认是 `SmartUpdate`，它会自己判断在物理帧还是渲染帧更新相机。跟随目标在 `LateUpdate` 里动，判断和实际更新时刻错开就会隔帧跳，显式改成 `LateUpdate` 才稳定。

## 怎么验证

`RunClock` 用命令行喂时间断言步数与定格。渲染插值在测试里验不了手感，只能验"视图拿到的位置在 prev 和 curr 之间"，以及"定格期间 Alpha 为 1"。最终还是要进 Play 看，记在 `STATUS.md` 的未验证清单里。

## 迁移到别的项目要注意

`Alpha` 一定要用模拟剩余余量算，不要用 `Time.deltaTime` 自己再攒一份。两套累加器必然对不上。视图层如果直接读模型，插值会被绕过——这一条要靠代码结构保证，靠自觉迟早破。
