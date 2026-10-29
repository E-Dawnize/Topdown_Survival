# 性能记录

所有数字在本机测得（Windows，Unity 2022.3.34f1c1，编辑器内 + 构建后），每条都写清楚测量方式。没测过的写「未测」，不用估算值填充。

## I0 工程基座

没有运行时数据：这一迭代结束时还没在 Unity 里跑过。

可以记录的非运行时数字：

| 项 | 值 | 方式 |
|---|---|---|
| 导表耗时 | 未计时 | `python tools/export_config.py` 秒级完成，没打点 |
| 生成 JSON 体积 | 约 4.6 KB（8 个文件） | 文件系统 |
| xlsx 体积 | 约 4.3 KB | 文件系统 |
| 运行时程序集编译 | 未测 | 需在 Unity 内看 Console 时间 |

## I1 战斗核心

待测。需要填的项：

| 项 | 目标 | 实测 | 方式 |
|---|---|---|---|
| 帧时间 | ≤ 16.6ms | 未测 | DebugOverlay 滑动平均 |
| 模拟 tick 总耗时 | ≤ 3ms | 未测 | 各系统 Stopwatch |
| SpatialGrid.Rebuild（200 单位） | ≤ 0.5ms | 未测 | Bench |
| QueryCircle × 1000 | ≤ 0.5ms | 未测 | Bench |
| Draw call | ≤ 40 | 未测 | Frame Debugger |
| 模拟层 GC | 0 B/帧 | 未测 | Profiler GC Alloc |
| 同屏敌人 | 200 稳定 | 未测 | 压力场景 |
| 峰值内存 | ≤ 500MB | 未测 | Profiler |
