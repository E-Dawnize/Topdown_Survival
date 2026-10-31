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

部分已测（I1-5 空间哈希、I1-11 的模拟侧）。其余（渲染、帧率、内存）待 Unity 内测：

| 项 | 目标 | 实测 | 方式 |
|---|---|---|---|
| 帧时间 | ≤ 16.6ms | 未测 | DebugOverlay 滑动平均 |
| 模拟 tick 总耗时（200 敌 + 400 投射物） | ≤ 3ms | 0.21 ms | 仓库外 bench harness，见下（dotnet 数字，不是帧时间） |
| SpatialGrid.Rebuild（200 单位） | ≤ 0.5ms | 0.0071ms | 同上 |
| QueryCircle × 1000 | ≤ 0.5ms | 0.57ms（半径 2 米）/ 2.8ms（半径 8 米） | 同上；两种半径都超出目标，见下 |
| Draw call | ≤ 40 | 未测 | Frame Debugger |
| 模拟层 GC | 0 B/帧 | 0 B/步（稳态） | 仓库外 bench harness（dotnet），见 I1-11 |
| 同屏敌人 | 200 稳定 | 200 已配进 Test_Arena，帧率未测 | 压力场景 + DebugOverlay |
| 峰值内存 | ≤ 500MB | 未测 | Profiler / Memory Profiler |

### I1-5 空间哈希 bench

**测法**：仓库外的临时 harness（`%TEMP%/td_i1_harness`，不进 git）引用 `Game.Runtime.csproj`，
直接调用 `SpatialGrid` / `EnemySystem`。计时用 `System.Diagnostics.Stopwatch`，先预热再采样，
取多次采样的 avg / median / p95。

**环境**：本机 dotnet —— .NET 9.0.22 / x64 / 16 逻辑核 / Release 配置（优化开）。
**这不是 Unity 编辑器或 IL2CPP 的数字，也不是帧时间**；Unity 内的实际开销要等 I1-11 压力测试再打点。

200 单位（位置在 ±20 米内随机游走，每次重建前先移动，计时只包住 `Rebuild`）：

| 项 | avg | median | p95 | 次数 |
|---|---|---|---|---|
| SpatialGrid.Rebuild（200 单位） | 7.1 µs | 6.9 µs | 7.5 µs | 2000 次 × 3 次独立运行，三次一致 |

1000 次 `QueryCircle`（结果容器调用方复用、每次查询前 Clear，模拟 EnemySystem 的用法；
场景是 200 个单位挤在半径 8 米内）：

| 半径 | 1000 次 avg | 单次 | 平均命中 | 次数 |
|---|---|---|---|---|
| 2 米（分离力尺度） | 0.57 ms | 0.57 µs | 0.58 个 | 每 1000 次 × 20 轮，共 3 次独立运行 |
| 8 米（索敌尺度） | 2.8 ms | 2.8 µs | 6.5 个 | 每 1000 次 × 20 轮，共 3 次独立运行 |

结论与偏差：

- Rebuild 比设计预估的 0.1ms 还低一个量级——200 个单位只是往 1024 个格子里散一轮，代价基本是遍历 200 个槽位。
- `QueryCircle × 1000` 的目标 `≤ 0.5ms` 只在半径很小的时候成立：半径 2 米时 0.57ms（略超 14%），
  半径 8 米时 2.8ms（超 5 倍多）。原因是 2 米的格子配 8 米半径要扫 9×9 个格子，
  单次查询的成本几乎全花在空格子上，和"命中几个单位"关系不大。
  这一项要么按实际查询半径重新给目标，要么在 I1-11 里确认索敌频率（武器有 CD，不是每帧 1000 次）后再定。
  现在不改结构：分离力用的是 0.6 米半径（比 2 米那档还小），每 tick 只有 200 次查询，
  这笔开销已经包在 I1-11 的 `RunSimulation.Tick` 里，没有单独拆出来。
- 附带实测（同一 harness，作为 I1-6 的参考，不是验收项）：`EnemySystem.Tick` 带 200 个敌人、
  每步做一次邻格查询，avg 0.218–0.220 ms/步（median 0.223–0.224ms，p95 0.235–0.252ms，1200 步 × 3 轮）。
  这 0.22ms 里大部分是分离力：每敌一次 `QueryCircle` + 一次 sqrt，尚未做任何优化。

## I1-9 / I1-11 压力场景与模拟侧实测

### 压力场景：200 敌 + 400 投射物

场景参数集中在一个地方，改一个常量就能换量级：

| 项 | 值 | 位置 |
|---|---|---|
| 同屏敌人数 | 200 | `AppRoot.TestArenaEnemyCount` |
| 投射物池容量 | 512（验收要求 400+，留一倍余量） | `AppRoot.ProjectilePoolCapacity` |
| 刷怪环 | 玩家周围 8~15 米，黄金角 + 面积均匀半径铺开 | `AppRoot.TestSpawnRingOffset` |
| 刷怪环最小间距（200 个点） | 1.50 米 | `SpawnRingTests` 实测；最大的敌人身体直径 0.8 米 |

刷怪分布为什么改了：原先「每个敌人各随机一个角度与半径」撒 200 个点时，中位数有 12 对出生点相距 0.3 米以内（最近一对 0.01 米），
开局就是一团，分离力要跑几百步才推得开——压测量到的等于「出生堆叠」而不是稳态。
现在铺均匀后出生不重叠；3 秒后它们仍然会全部挤到玩家身边互相推挤，**那是预期的压力稳态**，不是缺陷。

### 模拟侧实测（I1-11 的 tick 预算与 GC）

**测法**：仓库外 harness（`%TEMP%/td_i1_harness`，不进 git）引用 `Game.Runtime.csproj`，
`dotnet run -c Release -- --bench sim` 直接调 `RunSimulation.Tick`——玩家移动 → 敌人追击/分离 → 网格重建 →
武器开火 → 投射物命中 → 清理死亡 → 事件分发，即 RunLoop 每步跑的同一段代码。
场景：200 个敌人（血量设成 1e6，整段不死，粒数恒定）、每步开始把在场投射物补到 400 发（补发不计时）、
玩家站着不动；预热 4000 步再采样 2000 步，计时用 `System.Diagnostics.Stopwatch`，只包住 `Tick`。

**环境**：AMD Ryzen 7 9700X（8 核 16 线程）/ 31 GB / Windows 10.0.26300 / dotnet 9.0.22 / Release（优化开）。
**这些是 dotnet 纯托管数字，不是 Unity 编辑器或 IL2CPP 的帧时间**：不含渲染、物理、Transform 写入、相机与 UI。

| 项 | avg | median | p95 | max | 样本 |
|---|---|---|---|---|---|
| `RunSimulation.Tick`（200 敌，步末平均在场 370 发） | 0.211–0.219 ms | 0.211–0.212 ms | 0.220–0.240 ms | 0.41–0.51 ms | 6 次独立运行 × 2000 步 |

- 预算 3 ms/步 → 余量约 14 倍。六次独立运行的 avg 极差 3.5%。
- max 那几档是孤立尖峰（已确认不是 GC：整段进程 gen0/1/2 回收次数都是 0），没有单独定位原因。
- 投射物按「每步开始补到 400 发」计，步末平均 370 发（单发飞行时间只有十几个模拟步）。

稳态分配：

| 项 | 值 | 测法 |
|---|---|---|
| 每步分配 | 0 B | `GC.GetAllocatedBytesForCurrentThread()` 差值，预热 4000 步后连续 2000 步 |
| GC 回收 | gen0/1/2 = 0/0/0 | 同上（整段进程累计） |

预热只给 600 步时，测量窗口里还会出现一次性 456 字节——那是某个 List 在预热带尾部的扩容，
不随步数增长（2000 / 6000 / 12000 步都是 456 字节），所以稳态仍是 0 B/步。

### 分配点审查（I1-11 顺手项）

200 敌 + 400 投射物的场景下逐个系统量了一遍（各 2000 步，`BENCH_ALLOC_DEBUG=1`）：
`SpatialGrid.Rebuild` 0 B、`EnemySystem.Tick` 0 B、`WeaponSystem.Tick` + `Events.Flush` 0 B、`PlayerController.Tick` 0 B。

**没有需要修的分配点**：这些路径本来就把每帧要用的容器长期复用（`EnemySystem._neighbors`、
`ProjectileSystem._candidates/_ordered/_orderedDistance`、`RangedBehaviour._candidates`），
`ShooterStats` / `DamageInfo` / 事件全是 struct，事件走复用队列。
`EnemySystem._slots` 与 `ProjectileSystem._hitStamp` 只在槽位扩容时各分配一次（64→256），发生在预热带。

留下的待办（不影响每帧分配，本次没动）：

- `ProjectileView` 每发投射物一个 `MaterialPropertyBlock`（最多 512 个，一次性）。颜色是常量，
  本可以像 `ContactShadowDisc` 那样共用一个属性块。
- `EnemyViewPool.Sync` 每帧做 `SlotCapacity`（256）次字典查询，200 个活敌人里有 56 个是空槽；
  改成按存活敌人遍历（`SpatialGrid` 已有稠密存活表）能省掉这部分查询，收益小，没做。

### I1-9：逐对象材质实例数

| 物件 | 每敌人实例数 | 说明 |
|---|---|---|
| 身体 MeshRenderer | 1 | 材质是预制体里的资产（3 种怪 3 份），种类色与受击变亮都走 `MaterialPropertyBlock` |
| 接触阴影盘 | 1 | 与身体共用同一份材质资产；网格全局 1 份；属性块全局 1 个（颜色是常量） |
| 玩家描边壳 | — | 全局 1 份运行时材质副本（`new Material(身体材质)` + `_Cull = Front`），1 个属性块 |

⇒ **材质实例总数 = 预制体里的 4 份资产 + 描边壳 1 份 = 5，与敌人数无关**；
网格实例数同理（身体网格来自资产，阴影盘 1 份全局共享）。
渲染器与 GameObject 数仍是每敌人 2 个（身体 + 阴影盘）：每个单位要在不同位置出现，
这条成本在 GameObject 渲染路径下躲不掉。阴影盘挂成敌人实例的子节点，
稳态下每帧 0 次额外 Transform 写入（位置随父节点跟走）。

### 只能在 Unity 里测的部分（本迭代没测）

1. **帧率与「5 分钟最低帧率 ≥ 50」**：进 Test_Arena 构建包跑满 5 分钟，
   用 `DebugOverlay`（F1）的 FPS / 帧时间滑动平均记录最低值，配合 Profiler 的 CPU 模块看 PlayerLoop 各段。
2. **Draw call 与 SRP Batcher 是否命中**：Window > Analysis > Frame Debugger。
   **已知冲突**：`MaterialPropertyBlock` 会让渲染器退出 SRP Batcher（Unity 的既定规则），
   所以这批渲染器大概率不命中 SRP Batcher，draw call 会接近渲染器数量（200 敌 ≈ 400+），离 ≤ 40 很远。
   要同时满足「逐对象换色」和「SRP Batcher 命中」，得改成每种怪一份带颜色的材质 + GPU Instancing
   （颜色走实例化属性缓冲），要新材质资产与一套实例化渲染路径，不在 I1 范围。
   本次按任务要求选的是共享材质 + 属性块。
3. **Unity / IL2CPP 下的 tick 时间与 GC Alloc**：Profiler 的 CPU 模块（GC Alloc 列、`RunLoop.Update` 深度）。
   上面那张表只有 dotnet 数字，量级可以参考，绝对值不能直接当帧预算用。
4. **峰值内存 ≤ 500MB**：Profiler / Memory Profiler（200 敌 ×（身体 + 阴影盘）的 GameObject 与渲染器开销）。
5. **接触阴影盘与描边壳的观感**：阴影盘会不会和地面 z-fighting（现给 0.02 米离地）、
   描边壳在 50° 俯视下能不能一眼认出玩家、不透明材质做的阴影盘看起来像不像「接触阴影」
   （柔边要一张带 alpha 的美术贴图，属美术资源）。只能在 Unity Play 模式下看。
6. **200 敌一次性刷出时的首帧卡顿**：`EnemyViewPool` 是首次用到才 `Instantiate`，
   200 个实例 + 200 个阴影盘集中在开局几帧建出来；开局预池化还没做，
   要先在 Profiler 里看到首帧尖峰的量级再决定要不要做。
