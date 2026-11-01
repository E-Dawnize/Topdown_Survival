# Topdown Survival 架构审查与改良报告

> **历史说明**：本文记录的是 v0.2（波次 + 商店 + 金币）时期的审查结论。v0.3 已重做玩法，文中的 Shop/Wave/Coin 相关条目与当前代码不再对应；当前状态见 `STATUS.md` 与 `GDD.md`。
> 文档目录：[docs/README.md](README.md)

审查对象：当前工作区 `feat/demo` 分支的运行时代码、场景与 Build Settings、配置表、测试与设计文档。

审查方法：静态阅读代码和文档，交叉核对调用链、场景引用、配置字段和迭代计划。审查阶段没有启动 Unity，没有重新运行测试，也没有在构建包或目标机器上验证；修复后的编译与测试结果见 11.6。下文的“确认”指代码路径可以直接推出，“未触发”指当前正常流程或现有测试没有覆盖到。

> 修复状态：本报告记录的 P0/P1 项已经在当前工作区修复，代码与测试的验证结果见文末“本轮 P0/P1 修复记录”。正文保留审查时的证据与行号，作为回归依据；行号可能因后续编辑而移动。

## 0. 结论

现有架构不需要推倒重来。固定步长模拟、纯函数伤害、空间网格、延迟事件分发、Excel 到 JSON 的配置管线都值得保留。审查时已有 192 个 EditMode 用例覆盖核心数值和流程；修复后为 196 个，验证结果见 11.6。

真正的问题集中在五个边界：

1. 启动与生命周期：默认构建入口没有驱动模拟，配置失败后无法恢复。
2. 输入与固定步：零步帧和命中定格会丢失单次按键，时钟会吞掉已经计时的步数。
3. 实体身份：玩家和敌人共用同一个小整数 ID，敌人槽位复用会污染投射物命中记录。
4. 伤害写回：玩家、敌人、旅伴各走一套路径，触发型道具和真实伤害没有统一入口。
5. 内容扩展接口：库存、道具效果、敌人行为、目标阵营、BOSS 波次都还压在现有大类里。

前三个已经有会在当前构建或高刷新率、掉帧时直接出错的地方。后两个会在 I3 填 6 角色、18 道具、7 敌种、2 BOSS 时集中爆发。

## 1. 审查范围与验证状态

| 项 | 当前状态 |
|---|---|
| 运行时代码 | 70 个文件，约 9857 行 |
| 程序集 | Runtime / Editor / Tests.EditMode 三层 |
| EditMode 测试 | 文档记载 25 文件、192 用例通过；本次未重跑 |
| 编译 | 文档记载 `dotnet build` 四个工程 0 错 0 警告；本次未重跑 |
| Unity Play | 完整 demo 流程未在 Unity 中跑过，文档已承认 |
| 渲染与帧率 | 只有模拟侧数据，没有 Unity 帧时间与 draw call 实测 |
| 配置管线 | I0 阶段手工验证过；没有 EditMode 测试 |
| 输入、相机、装配根、UI | 没有自动化测试，主要靠代码审查 |
| 平台 | 当前目标 Windows；移动端不是当前目标 |

文档对“Unity 验证”的口径需要统一：I0 阶段跑过配置加载和场景生成，但完整 demo 从主菜单到结算没有在 Unity 里跑过。这两件事可以同时成立，写报告时不要混成一句“Unity 完全没跑过”或“Unity 已经跑通”。

## 2. 优先级总表

| 编号 | 问题 | 性质 | 最晚处理 |
|---|---|---|---|
| P0-1 | 默认构建入口 Boot 没有 RunLoop，开局不推进 | 构建入口必现 | 现在 |
| P0-2 | 零步帧和命中定格期间丢失冲刺等单次按键 | 高刷新率、定格、掉帧时可现 | 现在 |
| P0-3 | 命中定格吞掉本帧已经计时的固定步 | 一帧多步且中途触发定格时可现 | 现在 |
| P0-4 | 配置失败后无法恢复，SetSeed 只改显示值 | 失败路径必现 | 现在 |
| P1-1 | UnitId 玩家与首个敌人冲突，槽位复用会污染投射物命中记录 | 条件触发，身份数据错误 | I3 前 |
| P1-2 | StartRun 只重置流程，不重置实体、生命、随机流和时钟 | 同对象重开必现 | 现在或删除该入口 |
| P1-3 | HUD 调试键默认开启，直接改生命和金币 | 发布构建可现 | 现在 |
| P1-4 | 事件总线挡不住同类型事件自触发，会无限循环 | 条件触发卡死 | I3 前 |
| P1-5 | InputService 禁用再启用后 UiMode 与实际 Map 分叉 | 叠加面板、暂停时可现 | I3 前 |
| P1-6 | 主菜单绕过 ISceneFlow，场景切换不是唯一入口 | 行为分叉，当前不致命 | I3 前 |
| P2-1 | AppRoot 变成服务定位器，View 持有具体 Controller | 当前单场景可接受，多场景和测试会痛 | I3 前 |
| P2-2 | 玩家受伤没有统一事件，真实伤害仍被护甲减免 | 触发型道具和元素伤害前必须改 | I3 前 |
| P2-3 | 库存、叠加、属性修正和商店耦合在 ShopController | 卖出、祭坛、18 件道具前必须拆 | I3 前 |
| P2-4 | 敌人行为、目标阵营、BOSS 波次阶段缺失 | I3-4、I3-5 直接依赖 | I3 前 |
| P2-5 | 配置表接受大量运行时不消费的字段 | 内容越多，静默无效越严重 | I3 前 |
| P2-6 | 单条随机流与硬编码经济参数 | 回放、调参、商店改动会互相影响 | I3 前 |
| P2-7 | 武器行为工厂封闭，投射物只认 EnemySystem | 新武器、新目标类型会返工 | I3 前 |
| P2-8 | Test_Arena 墙体、玩家边界、相机边界三处不一致 | 测试场压测结果不可信 | I3 前 |
| P3-1 | 没有 PlayMode、场景和构建冒烟测试 | 当前最大未知 | 立即排期 |
| P3-2 | 存档、音频、选人、暂停、手柄主菜单、中文字体未做 | I4 计划内 | I4 前 |

## 3. 当前缺陷

### 3.1 P0-1 默认构建入口不会推进游戏

`ProjectSettings/EditorBuildSettings.asset:9` 把 `Assets/Scenes/Boot.unity` 放在第一个。Boot 场景里只有 `AppRoot`、`DebugOverlay`、`InputService`，没有 `RunLoop` 和 `CameraRig`。`AppRoot.Awake` 在 `AppRoot.cs:106-122` 会加载配置、创建玩家和战斗、调用 `RunController.StartRun()`，但运行时唯一驱动 `Simulation.Tick` 的是 `RunLoop.cs:79-88`。结果是状态机已经进入第一波，模拟却一步都不走，也不会自动进入主菜单。

这不是编辑器里打开 Run 场景的问题。直接 Play Run 可以绕过；构建后的默认入口必现。修复方式二选一：把 Boot 改成只负责加载配置、确定种子、加载 MainMenu 的启动场景，不在 Boot 里创建战斗；或者删掉 Boot，把 MainMenu 设为第一场景。不要保留两个装配入口。

验收标准是干净构建启动后进入 MainMenu，按开始能进 Run，Run 里 HUD、敌人生成、波次计时都在动。

### 3.2 P0-2 单次按键会在零步帧和定格期间丢失

`RunLoop.cs:63-65` 每个渲染帧都用 `root.Input.Sample()` 覆盖 `_snapshot`，然后 `RunLoop.cs:79-88` 才决定这一帧跑几步。`InputService.cs:78-85` 的按键来自 `WasPressedThisFrame()`，下一帧再采样就变回 false。120Hz 或 144Hz 渲染下会出现没有固定步的帧；命中定格期间 `RunClock.cs:44-48` 直接返回零步。这两种情况下，冲刺、技能、暂停按键没有被任何模拟步消费，就被下一帧覆盖掉了。

现有 `PlayerMovementTests` 只验证了“一帧跑三步时冲刺只触发一次”，没有覆盖“一帧零步”。修复方式是在 `InputService` 或 `RunLoop` 里锁存按键，等到至少一个模拟步消费后再清除；连续移动轴不需要锁存。

验收用例应该直接构造零步帧和定格帧，确认冲刺仍然只触发一次。

### 3.3 P0-3 命中定格会吞掉已经计时的固定步

`RunClock.Advance` 在 `RunClock.cs:57-61` 先把本帧应跑的所有固定步从累加器里扣掉，返回步数。`RunLoop.cs:84` 如果第一步触发定格就 `break`，剩下已经扣掉的时间不会返还。比如一帧应跑三步，第一步暴击触发定格，后两步就永久消失。波次计时、移动和攻击会比真实时间少推进一到两步，丢失量还取决于渲染帧如何分组。

定格期间不补跑真实时间是设计选择，问题在于扣掉的是定格前已经积累、尚未执行的时间。修复方式是让时钟只提交实际执行的步数：可以提供 `TryConsumeStep()`，或者 `Advance` 只返回“可执行步数”，由主循环每执行一步再提交一步。

验收用例要覆盖“一帧三步、第一步触发定格”，确认剩余累加器仍在，恢复后不会少时间。

### 3.4 P0-4 配置失败和种子设置都不可恢复

`AppRoot.cs:104-109` 在配置加载失败时直接 `return`。公开的 `TryLoadConfig()` 在 `AppRoot.cs:146-166` 只更新 `Config` 和 `ConfigOk`，不会继续创建玩家、战斗、UI 和模拟。也就是说，重试成功后当前场景仍然不可玩。`ConfigError` 已经记录，但没有任何 UI 读取它。

`SetSeed()` 在 `AppRoot.cs:141-144` 只改属性，而随机流和 `RunController` 在 `CreateCombat()` 的 `AppRoot.cs:249-255` 已经用旧种子构造。Awake 之后调用 `SetSeed`，只会造成 HUD 显示的种子和实际模拟种子不一致。

修复应该把“加载配置、确定种子、装配战局”拆成明确阶段。Boot 或加载层失败时显示错误面板并阻止进入游戏；种子必须在创建随机流之前确定，`SetSeed` 要么删掉，要么改成只能在装配前调用的配置对象。

### 3.5 P1-1 UnitId 冲突与槽位复用

`AppRoot.cs:43` 把玩家固定为 `UnitId(1)`，`EnemySystem.cs:64` 又把第 0 号敌人设为 `slot + 1`，首个敌人也是 `#1`。`CompanionSystem.cs:44` 把旅伴放在 1000 之后，暂时避开，但敌人数量增长后仍可能重叠。`UnitDamaged` 和 `UnitDied` 只带 `UnitId`，没有单位种类，`RunEvents.cs:28-56`。现在击杀数只按 `UnitDied` 条数累加，`RunController.cs:256-258`，所以没有证据显示当前击杀数算错；但这个 ID 不能作为全局实体身份使用。玩家的受伤事件目前根本没发，所以还没有出现“玩家受伤闪到 0 号敌人”的现象。

更具体的是槽位复用。`RunSimulation` 的顺序是武器结算 `RunSimulation.cs:165`、清理死亡 `171`、刷怪 `177`。清理阶段 `RunSimulation.cs:204-211` 先发 `UnitDied`，再 `Remove`，`EnemySystem.Remove` 调 `SpatialGrid.Release`，槽位进入空闲栈；下一次 `Allocate` 在 `SpatialGrid.cs:68-74` 直接复用这个槽位。`ProjectileSystem` 的命中记录按敌人槽位存，`ProjectileSystem.cs:83` 和 `211/230`。同一发穿透投射物如果还活着，下一 tick 会把复用同一槽位的新敌人当成“本发已经打过”的旧敌人跳过。

修复方向是稳定实体句柄：`UnitId` 至少包含槽位、代数和种类，或者由 `UnitRegistry` 分配全局 ID；空间网格继续用槽位，事件和命中记录用实体句柄。`UnitDamaged`、`UnitDied` 要带 `UnitKind`，`RunController` 只统计敌人死亡，视图按句柄和种类解析。

验收用例要显式构造“杀死敌人、同槽位刷新敌人、同一发穿透投射物继续飞”的场景。

### 3.6 P1-2 StartRun 是不完整的重开

`RunController.StartRun` 在 `RunController.cs:133-153` 只重置流程、金币、商店、旅伴和波次，不重置玩家生命、敌人、投射物、掉落物、随机流和时钟。`RunSimulation._playerDiedPublished` 在 `RunSimulation.cs:58` 和 `182-185` 也没有重置入口。

死亡后调用 `StartRun()`，下一 Tick 会因为玩家 HP 仍为零再次结束；即使调用方手动补血，旧实体和随机流也还在，后续 `PlayerDied` 不会再发。正式重开走场景重载，所以正常玩家路径避开了这个问题。`RunRestartTests` 每轮手动补满生命，也没有覆盖完整的战局对象重开。

既然公开了这个入口，就要明确契约：要么实现完整的 `ResetRun`，把玩家、敌人、投射物、掉落、时钟、随机流、死亡标记全部重置；要么把它改成只能在 Idle 状态使用的开局方法，GameOver 后直接抛异常，正式重开只允许重新装配。不要保留一个“看起来能用、实际只重置一半”的 API。

### 3.7 P1-3 HUD 调试键会改模拟状态

`HudView.cs:30` 的 `_enableDebugKeys` 默认是 true，`HudView.cs:161-172` 直接读键盘，F2 扣玩家 10 点生命，F3 加 10 金币并发布 `CoinChanged`。这些输入不进入 `InputSnapshot`，绕过了模拟层；发布版里玩家可以改战局，也会破坏“同种子、同输入序列得到同结果”的前提。

修复不需要复杂设计：用 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包住，或者把调试命令移到独立的 `DebugCommands` 控制器，由装配根只在开发构建注入。验收方式是发布构建里按 F2/F3 没有任何效果，代码里也没有 View 直接写 `PlayerModel.HP` 或 `RunState.AddCoins`。

### 3.8 P1-4 事件总线的循环保护挡不住同类型事件

`EventBus.Flush` 在 `EventBus.cs:55-83` 每次进入一个频道时把 guard 加一，超过 10000 抛异常。但 `EventChannel.DispatchPending` 在 `EventChannel.cs:66-84` 用 `while (_pending.Count > 0)` 持续消费队列，包括处理器刚刚入队的同类型事件。只要某个处理器在处理事件时再发布同类型事件，队列就永远非空，`DispatchPending` 不会返回，外层的 guard 也不会再增长。

当前没有处理器自触发同类型事件，所以还没有卡死。但 I3 的触发型道具很容易写出这种链路，比如受击事件里再发受击事件、命中触发再发命中。修复方式是给每个频道设定单次 `Flush` 的分发预算，或者在 `DispatchPending` 开始时快照队列长度，只处理本轮已有的事件，把新事件留到下一轮。无论选哪种，都要加一条“处理器发布同类型事件”的测试，确认是抛异常而不是挂死。

另外，`DispatchPending` 没有隔离处理器异常。某个 UI 或表现订阅者抛异常后，同频道剩余订阅者和后续频道本次都不会分发，未处理事件留到下一次 `Flush`，而模拟状态已经推进。开发期直接抛出可以接受，但发布期需要明确策略：记录并继续，还是记录并丢弃。不能默认静默。

### 3.9 P1-5 InputService 的 UiMode 在禁用再启用后分叉

`InputService.cs:52-55` 的 `OnEnable` 无条件只启用 Player Map；`OnDisable` 在 `57-61` 同时禁用两套 Map；`UiMode` 在 `32` 是独立字段，没有在禁用时重置。`SetUiMode` 在 `93-95` 发现 `UiMode` 相同就直接返回。

如果商店或结算面板打开时根对象被禁用再启用，`UiMode` 仍然是 true，但 UI Map 已经被关掉、Player Map 被打开，面板看起来还在，按键却没有反应。当前场景重载会重建 InputService，正常流程不容易触发；暂停菜单、叠加面板、编辑器里反复开关对象时会遇到。修复是在 `OnEnable` 按当前 `UiMode` 恢复对应的 Map，或者在 `OnDisable` 把 `UiMode` 归零并让上层重新设置。

### 3.10 P1-6 SceneFlow 不是唯一入口

`SceneFlow.cs` 的注释说它是切换场景的唯一入口，结算面板也确实通过 `ISceneFlow` 走。但 `MainMenuView.cs:60` 直接调用 `SceneManager.LoadScene(UnitySceneFlow.RunScene)`。以后给 `UnitySceneFlow` 加异步加载、过渡、加载锁或异常处理，只会覆盖结算路径，主菜单进战局仍然走另一套行为。

修复很小：给 MainMenu 注入 `ISceneFlow`，或者至少通过一个静态的场景服务调用。验收标准是在代码里搜索 `SceneManager.LoadScene`，除了 `UnitySceneFlow` 内部没有其他调用点。

## 4. I3 前必须补的扩展边界

I3 要填 6 个角色、10 个旅伴、18 件道具、7 种敌人、2 个 BOSS。现在这些内容如果直接往 `ShopController`、`EnemySystem`、`RunController` 里加，会迅速把三个类变成新的“什么都知道”的类。下面八项是实际改良需求，不是架构洁癖。

### 4.1 统一实体身份与伤害入口

现状是玩家、敌人、旅伴各有一套 ID 规则，伤害写回也分散在三个地方：`EnemySystem.cs:253-258` 直接写玩家 HP，`WeaponDamage.cs:100-106` 直接写敌人 HP，旅伴的 `HP` 字段在 `CompanionModel` 里但没有任何受伤路径。`DamageResolver.cs:104-122` 不管 `DamageType`，`DamageType.True` 也会走护甲减免；`WeaponDamage.Compose` 在 `87` 硬编码 `DamageType.Physical`。

目标形态是一个统一的 `Combatant` 或 `IDamageable`：持有稳定 `UnitHandle`、`StatBlock`、当前生命和存活状态；提供唯一的 `ApplyDamage(in DamageInfo, float dodgeRoll)`，在里面调用 `DamageResolver`、写回生命、发布 `UnitDamaged`、`UnitDodged`、`UnitDied`。玩家、敌人、旅伴都走同一条路。`DamageType.True` 绕过护甲，元素抗性以后再加；武器表补伤害类型列，`Compose` 从表里读，不再写死物理。

这样做的直接收益是 I3 的触发型道具可以只订阅事件：香水监听玩家 `UnitDamaged`，弹簧监听 `UnitDodged`，玻璃球监听 `UnitDied` 且击杀者是玩家。玩家受击也会有统一的定格和表现，而不是靠 HUD 比较上一帧生命值。

验收用例至少覆盖：玩家受伤发一次 `UnitDamaged`；闪避成功发 `UnitDodged` 且不掉血；真实伤害不被护甲减免；旅伴可以被伤害且击杀归属正确；同一实体死亡只发一次 `UnitDied`。

### 4.2 拆出库存与道具效果

`ShopController.cs:50` 的 `OwnedItem`、`133-134` 的 `_owned` 和 `_stacks`、`322-351` 的 `TryBuy`、`547` 的 `ApplyStats`，说明当前“玩家拥有什么”和“商店卖什么”是同一个类。`CanBuy`、`MaxStack`、属性修正的 `Source` 都在商店里。GDD 10.1 要卖出回收 50%，10.2 要祭坛献祭旅伴和生命上限，18 件道具里还有触发型、经济型、特殊型。这些都不该继续塞进 `ShopController`。

目标形态是 `RunInventory`：拥有道具实例、同名叠层、属性修正来源、增加、移除、卖出、查询；`ItemEffectSystem` 或 `TriggerSystem` 负责被动属性和事件触发；`ShopController` 只负责货位、价格、刷新、买入；`AltarController` 单独处理献祭。UI 通过 `IShopCommands` 和只读库存视图交互，不直接持有 `ShopController`。

验收用例：买入后属性生效；卖出后按 `RemoveBySource` 精确回滚，同名另一层不受影响；卖价是原价 50%；触发型道具在商店关闭时仍然生效；祭坛移除旅伴后羁绊回滚且武器转移只发生一次。

### 4.3 敌人行为、目标阵营、BOSS 与波次阶段

`EnemyDef.Behaviour` 在 `Defs.cs:90` 有 8 个枚举值，`Enums.cs:42-52` 包括 Chase、Charge、Ranged、Split、Shield、EliteSummoner、EliteDischarger、Boss。但 `EnemySystem.Step` 在 `EnemySystem.cs:118-176` 对所有敌人只做追击和接触伤害，行为字段没有消费者。`ProjectileSystem` 的命中查询只认 `EnemySystem`，`ProjectileSystem.cs:209-243`，敌人的远程攻击没有实现；`EnemySystem` 的接触伤害只打玩家，旅伴的 HP 永远不掉。

`WaveDef.BossId` 在 `Defs.cs:111` 只被 `ConfigDatabase` 校验引用是否存在，`RunController.ClearWave` 在 `RunController.cs:206-224` 仍然只看时间到点就结束，没有“BOSS 死亡才完成”的条件。`RunPhase` 在 `RunController.cs:12-30` 只有 Idle、Battle、Shop、GameOver，没有 BOSS 奖励、宝箱三选一、祭坛这些阶段。

目标形态是给敌人加行为策略，类似 `IWeaponBehaviour`：`ChaseBehaviour`、`ChargeBehaviour`、`RangedBehaviour`、`SplitBehaviour`、`ShieldBehaviour`、`BossBehaviour`。`EnemySystem` 只负责移动、分离、接触和生命周期，具体行为决定追击、蓄力、发射、召唤、分裂。再加一个目标查询接口和阵营，玩家、旅伴、敌人投射物都通过同一套目标筛选；敌人投射物复用 `ProjectileSystem`，但带目标掩码，不再写死只打敌人。

BOSS 和波次需要单独的遭遇控制：波次完成条件可以是时间到、清怪达标或 BOSS 死亡；BOSS 奖励、宝箱三选一、祭坛进入新阶段。不要把这些条件继续写成 `ClearWave` 里的 if。

验收用例：7 种敌人行为各有一条行为断言；敌人投射物能命中玩家和旅伴，不会误伤敌人；BOSS 波不会因为计时到点而提前结束；BOSS 死亡后进入奖励阶段。

### 4.4 配置表与运行时的契约

现在配置表接受了不少运行时不消费的字段：

- `CharacterDef` 的 `BaseAttackInterval`、`BaseDamage`、`BaseRange`、`TraitType` 在 `Defs.cs:18-25`，只有 `BaseHP`、`BaseMoveSpeed`、`StartingWeaponId` 被 `AppRoot.CreatePlayer` 使用；`BaseAttackInterval` 只在 `ConfigDatabase.cs:171` 做正数校验，`TraitType` 没有消费者。
- `ItemDef.EffectType` 和 `EffectParams` 在 `Defs.cs:72-73`，`ShopController.cs:555` 明确写着这一步不做。
- `EnemyDef.CoinDrop` 在 `Defs.cs:96`，`PickupSystem.cs:45` 仍然固定 `CoinValuePerKill = 1`。
- `SkillPressed` 在 `InputSnapshot.cs:14`，只有 `DebugOverlay.cs:106` 显示，玩家主动技能没有实现。
- `StatType.Regen`、`ContactDamage`、`Range`、`Treasure` 在 `Enums.cs` 里有定义，但玩家侧没有消费方；`ContactDamage` 只被敌人用来做接触伤害。
- `BuffDef` 和 `BuffEffectType` 完全没有接入，属于 I5 计划内，可以暂时保留。

这不是说所有字段都要立刻实现，而是不能让策划以为填了就会生效。I3 前应该做一张“字段到消费者”的契约表：每个可配置字段要么有明确的运行时路径，要么从表里删掉，要么在导表时警告“当前版本不生效”。`ConfigDatabase.cs:15-20` 的 `MissingMemberHandling.Ignore` 还会静默吞掉表头拼写错误，建议改成严格模式，或者在加载后记录未知字段。

`Balance.json` 只有 8 个键，商店的 `CompanionBasePrice`、`RefreshPrice`、`PityThreshold`，掉落的心概率、每杀金币价值，敌人的分离权重、旅伴跟随速度、刷怪半径都还是代码常量。不需要把实现细节全部搬进表，但设计调参项应该进 Balance，并在文档里区分“设计参数”和“实现常量”。

验收方式：给 `ConfigDatabase` 加 EditMode 测试，覆盖未知 `EffectType`、未知 `TraitType`、重复 Balance Id、缺失引用、负的 `ConcurrentCap`；给导表脚本加固定 xlsx fixture，验证坏数据能被拦住。

### 4.5 事件总线与随机流

事件总线除了 P1-4 的同类型循环，还需要明确“处理器异常”和“事件重入”的语义。I3 的触发道具会把事件链变长，不能等到线上出现卡死再补。

随机流现在是 `AppRoot.cs:255-276` 创建一条 `combatRandom`，传给敌人、武器、刷怪、掉落和商店。技术文档已经承认这是取舍：买一件道具、刷新一次商店都会改变后续暴击序列。确定性还在，但因果很难查，回放和数值对比都会变得脆弱。I3 内容变多后，建议至少拆成战斗、刷怪、掉落、商店四条流，各自从主种子派生；如果暂时不拆，就要在 DebugOverlay 和结算里明确显示“单流”，不要让回放功能建立在错误假设上。

### 4.6 装配、视图和反向依赖

`AppRoot.Instance` 在 `AppRoot.cs:45` 定义，`RunLoop.cs:59/65/96`、`PlayerView.cs:44`、`CameraRig.cs:29`、`DebugOverlay.cs:75` 都直接读取。`AppRoot` 还公开了几乎所有系统，`AppRoot.cs:48-78`。类注释说“其余代码通过构造注入，不查静态单例”，实际不是。

View 也持有具体类型：`HudView.Bind(PlayerModel, RunState, RunController, EnemySystem)` 在 `HudView.cs:80`，`ShopView.Bind(ShopController, ...)` 在 `ShopView.cs:93`，`ResultView.Bind(RunController, InputService, ISceneFlow)` 在 `ResultView.cs:54`。技术方案要求 View 通过 `IShopCommands` 这类接口和只读模型交互，当前只做到了 `ISceneFlow` 一处。

反向依赖也已经出现：`RunSimulation.cs:7` 引用 `Game.Views`，`165` 用 `ViewSnapshot.DirectionOf`；`DeterministicRandom.cs:11` 实现武器命名空间的 `IRandom01`；`WeaponDamage.cs:87` 读 `Core.HitFeedback.BigHitThreshold`。这些都在同一个 `Game.Runtime` 程序集里，现在不会编译失败，但会阻止把模拟层抽出来复用或单独测试。

I3 前不需要引入依赖注入容器，也不需要拆成很多 asmdef。实用做法是：保留 `AppRoot` 作为场景装配根，只让它负责构造和绑定；给 `RunLoop`、`HudView`、`ShopView`、`ResultView` 注入接口或只读数据源；把 `IRandom01` 移到 Core，把方向换算移到 Core 的数学工具，把命中表现阈值从 `WeaponDamage` 里拿掉。验收标准可以是两条 grep：View 目录里没有 `PlayerModel.HP =`、`RunState.AddCoins`、`ShopController` 具体类型；Core 目录里没有 `Game.Views`。

### 4.7 场地参数的单一来源

`Balance.json:23-24` 的 `ArenaHalfExtent` 是 67，`Test_Arena.unity` 的墙在 ±20.5（`305`、`565`、`775`、`858`），`CameraPreset_TestArena.asset:21` 的相机边界是 20，而 `AppRoot.CreatePlayer` 在 `194-212` 不区分场景地注入 67。玩家移动不经过物理碰撞，`PlayerController.ClampToArena` 允许走到 ±67，所以 Test_Arena 里可以直接穿过墙，相机停在 20 的范围内。

这不是正式 Run 场景的问题，但 Test_Arena 是压测和手感验证场。三处各写一个边界，压测结果就不可信。修复方式是给每个场景一份明确的场地配置，或者让场景从 `Balance` 读取同一个值并通过场景生成器生成墙和相机预设；至少在启动时校验“墙体、相机、玩家边界”三者一致。

### 4.8 武器行为工厂与目标类型

`WeaponSystem.CreateBehaviour` 在 `WeaponSystem.cs:75-99` 是一个封闭的字符串分支，只认识 `Ranged` 和 `Melee`。技术方案说“加一把新武器只写一个行为类、填一行表”，实际新增 `BehaviourType` 仍然要改这个 switch，否则直接抛“未知 BehaviourType”。`IWeaponBehaviour` 也只有 `Tick` 一个入口，没有命中、击杀、换武器等钩子，I5 的 buff 和 I3 的特殊武器会继续往行为类里塞分支。

投射物的目标类型同样是写死的。`ProjectileSystem.cs:209-243` 只查询 `EnemySystem`，只对 `EnemyModel` 结算，所以“攻击旅伴”“可破坏物”“BOSS 召唤物”“敌方弹幕打玩家”都不能只加一个行为类完成。近战 `MeleeBehaviour` 也直接依赖 `EnemySystem`。

目标形态是把行为创建改成注册表或枚举映射，把行为上下文扩成“位置、朝向、目标查询、投射物生成、伤害结算”几个接口；投射物带阵营和目标掩码，命中结算通过 `IDamageable`，不再直接拿 `EnemyModel`。当前只有两把武器，可以不改；但 I3 加特殊武器、I5 接 buff 之前必须改，否则“数据驱动扩展”只是文档里的说法。

验收用例：新增一个测试行为类，只改配置和注册表就能开火；同一套投射物系统能分别命中敌人、玩家和旅伴；近战行为不再直接依赖 `EnemySystem` 的具体类型。

## 5. I4 前需要补的发布能力

这些不属于当前架构缺陷，但 I4 的验收会直接用到：存档与局外解锁、音频总线和限流、选人界面、暂停菜单、手柄能走完主菜单到结算、内置中文字体或 TMP 字体资产、构建后冒烟测试、帧率和 draw call 实测。

当前 `MainMenuView.cs:43-50` 只读键盘，手柄无法开始；`UiFont.cs:19-26` 依赖操作系统字体，找不到时退回没有中文字形的 `LegacyRuntime.ttf`；`InputService` 运行时构建 Action，改键要改代码，I4 做按键自定义时需要迁回资产。`ConfigSource.cs:20-32` 用同步 `File` 读取，只适合 Windows；项目非目标移动端，可以暂缓，但文档不要写成“换个实现就能上 Android”，因为接口是同步的，装配流程也要改。

`DebugOverlay` 也直接读 `AppRoot.Instance` 和键盘，发布构建里是否保留要明确。如果保留，至少用开发构建条件限制。

## 6. 不建议现在做的事

- 不要为了“架构纯粹”拆成很多 asmdef。当前 Runtime、Editor、Tests 三层够用，拆细会增加改字段时的引用成本。等模拟层真的要脱离 Unity 复用时，再单独抽一个不引用 UnityEngine 的程序集。
- 不要上 ECS 或 Jobs。技术方案的条件是同屏稳定 600 以上，或模拟 tick 超过 6ms 且已排除算法问题；当前 200 敌的模拟数据还没在 Unity 里验证，先做测量。
- 不要引入依赖注入容器、UniRx、Zenject 或通用事件框架。当前构造注入加装配根足够，问题在边界没有守住，不在容器不够强。
- 不要上 Addressables、云存档、多语言、联机、回放。都不是当前迭代的目标。
- 不要为了“可扩展”给每个系统都加接口。武器行为、敌人行为、伤害入口、库存、目标查询这五个点值得抽象；波次计时、掉落磁吸、空间网格保持现在的简单实现更好。
- 不要先做移动端配置异步加载。Windows 是当前目标，`FileConfigSource` 够用；等平台目标确定再改接口。

## 7. 实施顺序

| 阶段 | 内容 | 完成标志 |
|---|---|---|
| 第一阶段：当前可玩性与验证链 | 修 Boot/入口、输入锁存、时钟提交、配置失败与种子、调试键条件编译、SceneFlow 统一入口；补 PlayMode 冒烟测试 | 构建启动能进菜单，Run 能跑一局，120Hz 和定格用例通过 |
| 第二阶段：I3 前扩展边界 | 稳定实体句柄与统一伤害事件、库存与道具效果拆分、敌人行为与目标阵营、波次与 BOSS 阶段、事件总线预算、随机流拆分、配置契约测试 | I3 的内容可以只改配置和新增行为类，不再改 ShopController/EnemySystem 主流程 |
| 第三阶段：I3 内容 | 6 角色、10 旅伴、18 道具、7 敌种、2 BOSS 建立在新边界上 | 每件内容都有生效路径和测试，不出现“买了没反应” |
| 第四阶段：I4 发布 | 存档、音频、选人、暂停、手柄、字体、构建冒烟、帧率与 draw call | 外部测试者能独立完成一局，目标机器 1080p 60fps |

如果只能做三件事：第一，修 Boot 并在 Unity 里跑通完整一局；第二，修输入锁存和时钟提交；第三，做稳定 `UnitId` 和统一伤害事件。第四件是拆库存与道具效果，放在 18 件道具之前。

## 8. 验收清单

### 8.1 立即完成

1. 干净构建启动进入 MainMenu，不经过 Boot 里的半初始化 AppRoot。
2. 120Hz 模拟下，零步帧按下的冲刺在下一个模拟步只触发一次。
3. 一帧三步、第一步触发定格时，剩余累加器保留，波次计时误差不超过一个固定步。
4. 配置加载失败有可见错误面板，不会停在半装配场景；HUD 显示的种子和实际随机流一致。
5. 发布构建按 F2/F3 无效，View 目录没有直接写模拟状态的代码。
6. PlayMode 冒烟：主菜单、Run、波次、商店、结算、重开、回主菜单全部走通。

### 8.2 I3 前完成

7. `UnitId` 跨玩家、敌人、旅伴唯一，带种类和代数；槽位复用后旧投射物不会跳过新敌人。
8. 玩家、敌人、旅伴受伤都发统一事件；真实伤害绕过护甲；闪避成功有独立事件。
9. 买入、卖出、祭坛移除都能精确回滚属性；触发型道具在商店外也生效。
10. 7 种敌人行为各有测试；敌人投射物能命中玩家和旅伴；BOSS 波按 BOSS 死亡结束。
11. 同类型事件自触发会抛异常或按预算停止，不会挂死。
12. 买道具和刷新商店不会改变战斗随机序列，或至少文档和调试层明确说明仍然是单流。
13. 配置表每个字段都有消费者，未知 `EffectType`、`TraitType`、重复 Balance Id 会被导表或加载校验拦住。
14. Test_Arena 的墙体、相机、玩家边界来自同一份配置。

### 8.3 I4 前完成

15. 存档写入与读取、删档重来。
16. 音频总线限流，连续命中不破音。
17. 手柄能走完主菜单、选人、战斗、商店、结算。
18. 中文字体随包，不依赖系统字体。
19. Windows 构建在两台目标机器上跑 1080p 60fps，draw call 和帧时间有记录。
20. CI 跑导表校验、dotnet 编译、EditMode 测试和 Unity batchmode 冒烟。

## 9. 文件级索引

| 问题 | 关键位置 |
|---|---|
| Boot 默认入口无 RunLoop | `ProjectSettings/EditorBuildSettings.asset:9`；`Assets/Scenes/Boot.unity:245,260,274`；`AppRoot.cs:106-122`；`RunLoop.cs:79-88` |
| 输入在零步帧丢失 | `RunLoop.cs:63-65,79-88`；`InputService.cs:78-85` |
| 定格吞步 | `RunClock.cs:42-61`；`RunLoop.cs:84` |
| 配置失败不可恢复 | `AppRoot.cs:104-109,146-166` |
| SetSeed 与装配脱节 | `AppRoot.cs:141-144,249-255` |
| UnitId 冲突 | `AppRoot.cs:43`；`EnemySystem.cs:64`；`CompanionSystem.cs:44` |
| 槽位复用污染投射物 | `SpatialGrid.cs:68-74,96-111`；`EnemySystem.cs:83-112`；`ProjectileSystem.cs:83,211,230`；`RunSimulation.cs:165,171,177,204-211` |
| StartRun 不完整 | `RunController.cs:133-153`；`RunSimulation.cs:58,182-185` |
| HUD 调试写模拟 | `HudView.cs:30,161-172` |
| 事件总线同类型循环 | `EventBus.cs:55-83`；`EventChannel.cs:66-84` |
| InputService UiMode 分叉 | `InputService.cs:32,52-61,93-95` |
| MainMenu 绕过 SceneFlow | `MainMenuView.cs:60`；`SceneFlow.cs:38-53` |
| AppRoot 服务定位 | `AppRoot.cs:45,48-78`；`RunLoop.cs:59,65,96`；`PlayerView.cs:44`；`CameraRig.cs:29`；`DebugOverlay.cs:75` |
| View 持有具体 Controller | `HudView.cs:80`；`ShopView.cs:93`；`ResultView.cs:54` |
| 反向依赖 | `RunSimulation.cs:7,165`；`DeterministicRandom.cs:11`；`WeaponDamage.cs:87` |
| 玩家受伤无事件 | `EnemySystem.cs:253-258`；`RunEvents.cs:28-56` |
| 真实伤害仍被护甲减 | `DamageResolver.cs:104-122`；`WeaponDamage.cs:84` |
| 库存与商店耦合 | `ShopController.cs:50,133-134,322-351,547`；`Defs.cs:72` |
| 敌人行为未接 | `Defs.cs:90`；`Enums.cs:42-52`；`EnemySystem.cs:118-176` |
| 敌人只打玩家 | `EnemySystem.cs:253-258`；`CompanionModel.cs:57`；`ProjectileSystem.cs:209-243` |
| BOSS/波次阶段缺失 | `Defs.cs:111`；`ConfigDatabase.cs:231-233`；`RunController.cs:12-30,206-224` |
| 配置字段无消费者 | `Defs.cs:18-25,72-73,96,111`；`ConfigDatabase.cs:171,195`；`PickupSystem.cs:45`；`InputSnapshot.cs:14` |
| 随机流单流 | `AppRoot.cs:255-276` |
| 经济参数硬编码 | `ShopController.cs:90,96,99`；`PickupSystem.cs:45,48` |
| 武器工厂封闭 | `WeaponSystem.cs:75-99` |
| 场地三处不一致 | `Balance.json:23-24`；`Test_Arena.unity:305,565,775,858`；`CameraPreset_TestArena.asset:21`；`AppRoot.cs:194-212` |
| 无 PlayMode 测试 | `Assets/Scripts/Game/Tests` 只有 `Game.Tests.EditMode.asmdef` |
| 文档过期 | `README.md:25,29` 仍写 SceneBuilder；`Assets/docs/progress.md:94` 说已删除 |

## 10. 审查限制

行号取自当前工作区，Unity 重新生成 csproj 或场景后可能移动。本文没有验证运行时行为，只确认了代码路径和文档约定。P0 项应在 Unity PlayMode 或构建包中复现后再修，修复后也需要用同样的场景回归。P2 项属于 I3 之前的边界设计，不是要求立即重写；如果 I3 内容很少、团队规模很小，可以只做其中的实体身份、伤害入口和库存拆分三项，其余留到需要时再改。

## 11. 本轮 P0/P1 修复记录

### 11.1 启动与生命周期

- `ProjectSettings/EditorBuildSettings.asset` 把 MainMenu 放到第一个启用的场景，Boot 标记为不参与构建。
- `AppRoot` 在装配前检查场景里是否存在 `RunLoop`；没有驱动时显示启动错误面板，不再创建半成品战局。
- 配置加载改为 `LoadConfig`，失败时显示错误面板；删除了装配后仍可调用的 `SetSeed`，种子改为序列化字段 `_runSeed`，在创建随机流之前确定。
- `RunController.StartRun` 只允许 Idle 状态调用一次；GameOver 后二次调用直接抛异常，正式重开只走场景重载。
- `ISceneFlow` 增加 `LoadRun`，MainMenu 通过注入的实现进入 Run，不再直接调用 `SceneManager.LoadScene`。

### 11.2 输入与固定步

- 新增 `InputBuffer`：连续轴每帧覆盖，按钮边沿锁存到至少一个模拟步消费后；零步帧和定格帧不会丢冲刺。
- `RunClock` 改为 `BeginFrame` 只报告本帧额度，`TryConsumeStep` 真正执行一步才扣累加器；定格中途退出不会吞掉剩余步数。
- `RunLoop` 按新 API 驱动，并在每个模拟步后清掉输入锁存。
- `InputService.OnEnable` 按当前 `UiMode` 恢复对应 Input Map；禁用时清理输入缓冲。

### 11.3 实体身份

- 新增 `UnitKind` 与 `UnitIdAllocator`。玩家 ID 为 1，敌人和旅伴从本局共享分配器拿单调递增的 ID，槽位复用不再复用实体身份。
- `EnemyModel` 持有稳定 `Id`；`EnemySystem.UnitIdOf`/`TryGetSlot` 按 ID 和槽位映射；`CompanionSystem` 也按稳定 ID 找回槽位。
- 投射物命中戳、近战单次挥砍去重、受击事件和视图受击查找全部改用稳定 ID；`UnitDied` 的击杀统计只认 `UnitKind.Enemy`。

### 11.4 事件与调试

- `EventChannel` 每轮只处理进入本轮时的事件，新增事件留到下一轮；`EventBus` 用总预算限制自触发链，超过上限抛异常而不是挂死。
- 处理器异常单独捕获并记录，不再让一个订阅者阻断同事件其他订阅者和后续事件。
- F2/F3 调试键从 HUD 移到 `DebugOverlay`，并用 `UNITY_EDITOR || DEVELOPMENT_BUILD` 条件编译；HUD 不再直接写模拟状态。

### 11.5 相机跟随

- `CameraRig` 创建的 `vcam_Follow` 现在同时设置 `LookAt` 和 `CinemachineComposer`。
- 之前只有 `Follow` + `CinemachineTransposer`，Cinemachine 只把相机放到目标加偏移的位置，不会自动转向目标；目标 Transform 的朝向是 identity，镜头朝世界 +Z，角色因此偏出画面。
- 相机改为有限阻尼：位置阻尼 0.35 秒、朝向阻尼 0.18 秒，死区 0.02、软区 0.08。参数放进 `CameraPreset`，不再把 Composer 参数写死在 `CameraRig` 里；既不会完全僵硬，也不会角色微动就抖。
- 预设拆成 Run / TestArena / Boss / Menu 四份；Run 与 Test_Arena 已接场景，Boss/Menu 作为后续相机模式的配置基础。

### 11.6 验证结果

- `dotnet build Game.Runtime.csproj -t:Rebuild`：0 错误 0 警告。
- `dotnet build Game.Tests.EditMode.csproj -t:Rebuild`：0 错误 0 警告。
- 用同一批测试源码编译到 NUnitLite 3.14 的临时 harness 运行：196 个用例全部通过。
- 新增针对性用例：输入锁存跨零步帧、定格中途不丢累加器、事件总线同类型自触发预算、玩家/敌人 ID 唯一、槽位复用后投射物不跳过新敌人、GameOver 后拒绝二次 StartRun。

未完成：没有在 Unity PlayMode 或构建包里跑 MainMenu → Run → 商店 → 结算 → 重开的完整冒烟。该验证仍是当前最高优先级。
