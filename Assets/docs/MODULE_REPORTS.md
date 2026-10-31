# 模块报告

日期：2026-10-31
分支：`feat/demo`

按代码模块给出职责、关键技术选型、验证情况与已知问题。测试数字来自 `grep -c "\[Test\]"`，192 个用例的总分布见文末。

---

## 1 主循环与时间

文件：`Core/RunLoop.cs`、`Core/RunClock.cs`、`Core/RunSimulation.cs`

`RunLoop` 只是一个 MonoBehaviour 壳：采样输入、把真实帧时间交给 `RunClock`、按返回的步数跑 `RunSimulation`、在 `LateUpdate` 里把插值系数分发给视图。

`RunClock` 锁死 1/60 固定步长，一帧最多三步，累加器超过 0.25 秒截断，并管理命中定格。定格期间返回 0 步且不累积，`Alpha` 取 1，画面停在最后一步而不是退回上一步。

`RunSimulation` 把每步的系统顺序独立成一个类，主循环和集成测试共用。顺序是玩家移动 → 敌人移动 → 网格重建 → 玩家武器 → 旅伴武器 → 清理死亡 → 拾取 → 刷怪 → 事件分发。

选固定步长而不是变步长，是为了同种子同输入给同结果，将来调数值或做回放时不会因为帧率不同而漂移。代价是渲染必须自己插值，画面恒定滞后不到一步。

验证：`RunSimulationTests` 6 条，`HitFeedbackTests` 4 条（定格期间不推进、恢复不补跑）。

已知问题：没有真正的录制/回放验证过确定性，只是结构上支持。

---

## 2 事件、随机与输入

文件：`Core/Events/*`（4 文件）、`Core/DeterministicRandom.cs`、`Core/InputSnapshot.cs`、`Services/InputService.cs`

事件总线把 `Publish` 写进待发队列，整轮结束统一 `Flush`。订阅方不会读到半更新状态，代价是所有事件晚一帧——商店关店后输入切回 Player 就损失一帧移动输入。

去重按委托相等，同一个委托实例订阅两次只执行一次。这条规则踩过一次：测试里 `_ => total++` 捕获同一闭包，50 次订阅拿到同一个委托，被去重成一个，那个测试从来没真正通过。

随机是单条 xorshift128，取高 24 位换 [0,1)，初始化走 splitmix 避免四个状态相近时前几步输出偏。战斗、刷怪、掉落、商店共用这一条流。技术方案原本写三条流，实际只做了一条，代价是加刷怪逻辑会挪动暴击序列。

输入用 `InputActionSetupExtensions` 在运行时构建 Action，不依赖 `.inputactions` 资产。Player 与 UI 两个 Map，进商店和结算切 UI，关掉切回。

验证：`EventBusTests` 10 条。输入与随机没有独立单测，随机通过刷怪分布与抽卡分布间接验证。

已知问题：改键要改代码，I4 做按键自定义时得迁回资产。

---

## 3 配置管线

文件：`Config/ConfigDatabase.cs`、`Config/ConfigSource.cs`、`Config/Defs/*`、`Config/UnitPrefabMap.cs`、`tools/export_config.py`

数值源是 `Config/GameConfig.xlsx`，导出成 `Assets/StreamingAssets/Config/*.json` 提交进 git，运行时只读 JSON。导表会校验 Id 唯一、引用存在、枚举合法、数值区间，任何一项不过就指出表 / 行 / 字段 / 原因。`manifest.version` 由全部表内容的 sha1 前 12 位生成，改一个数版本就变。

选 Excel 是为了策划能直接改数值，选 JSON 落库是为了看数值 diff。代价是多一道导表步骤，而且现在配置内容严重不足（4 道具、3 旅伴、3 波）。

验证：没有 EditMode 测试。导表幂等、坏数据拦截、枚举与引用校验都在 I0 阶段用命令行验过。

已知问题：`EffectType`、`BossId`、`WeaponDef` 的伤害类型列都还没接。

---

## 4 属性与伤害

文件：`Model/StatBlock.cs`、`Systems/DamageResolver.cs`

`StatBlock` 用 `float[StatCount]` 加惰性分配的 `List<StatModifier>[]`，枚举值直接当下标，读取不哈希不装箱。修正读多写少，Add 和 Remove 只标脏，下次读才整条重算。公式是 `(base + Σflat) × (1 + Σpercent) × Πmultiply`，`Multiply` 的 Value 是倍数不是增量，概率属性封顶 1。`RemoveBySource` 按引用相等批量移除。

`DamageResolver` 是纯静态函数，骰子由调用方掷好传入。顺序固定：闪避 → 无敌帧 → 护甲 → 扣血 → 判死。护甲 `armor/(armor+50)`，先归零再除。

选纯函数是为了可测与确定性。代价是每个调用方都要自己管随机，`EnemySystem` 就因为"骰子该由调用方掷"而一直传 0，玩家的闪避属性长期无效，后来才补上。

验证：`StatBlockTests` 11 条、`DamageResolverTests` 14 条。稳态读零分配在外部 harness 量过。

已知问题：GDD 的闪避 60% 上限没写进 `StatBlock`，留给数值侧控制。

---

## 5 空间索引

文件：`Systems/SpatialGrid.cs`

均匀网格，格子 2 米，32×32 覆盖 64 米，以中心滚动时整表重建。槽位号由 `Allocate` 分配后固定不变，移除走格内列表与稠密列表两次末尾交换，别的单位编号不会前移。每格 `List<int>` 预分配 8。查询返回上一次重建时的位置快照。

不用四叉树也不用物理引擎：200 个单位、场地固定，散一轮就是遍历 200 个槽位，实测重建 7.1 微秒，比维护树结构省事也省错。

`QueryCircle` 一千次在半径 2 米时 0.57 毫秒、半径 8 米时 2.8 毫秒，超出技术方案定的 0.5 毫秒目标，原因是 2 米格子配 8 米半径要扫 9×9 格，成本几乎全在空格子上。现在没改结构，理由写在 `perf-log.md`。

验证：`SpatialGridTests` 9 条，`SpawnRingTests` 4 条（刷怪环分布）。

已知问题：查询结果依赖上一次重建，调用方必须按"先移动后重建"的顺序用。

---

## 6 敌人

文件：`Model/EnemyModel.cs`、`Systems/EnemySystem.cs`、`Systems/IEnemySpawnTarget.cs`

追击玩家，到攻击距离停下，接触伤害按 `AttackInterval` 结算并走 `DamageResolver`。分离力只查自己周围一小片（半径等于两倍身体半径），一次遍历同时得到推速和位置修正。位置修正这一步是必要的：几十个敌人围住玩家时推速有上限，纯速度分离总残余互相插进去，实测最小间距只有身体直径的六成。

敌人按槽位号从小到大推进，纯为确定性。打死后不由系统自己删，外部清理阶段调 `Remove`，免得"谁该看见尸体"变成隐式时序问题。

验证：`EnemySystemTests` 7 条，其中 50 敌 600 步统计最小间距与聚集。接触伤害、击退衰减、闪避骰子都有用例。

已知问题：敌人伤害类型固定 Physical，索敌优先级（精英 > 远程 > 近战）没实现，`EnemyDef` 里没有可用于排序的字段。

---

## 7 武器与投射物

文件：`Systems/Weapons/*`（8 文件）

`WeaponSystem` 持有若干 `WeaponModel`，按 `BehaviourType` 建行为类，`Kind` 只做一致性校验，冲突直接抛。开火条件、冷却、射程、弹道数、扇形分布、穿透都在这一层。伤害公式按 GDD 5.2，护甲留给 `DamageResolver`。

`ProjectileSystem` 是定长槽位数组，不是 GameObject 池。命中记录用"槽位号 + 递增戳记"，比每发维护命中集合省。池满了返回失败而不扩容，压力要能被看见。

近战不走投射物，扇形判定加伤害窗口（0.08 到 0.22 秒），同一次挥砍对同一敌人只结算一次。判定绑窗口不绑冷却，否则攻速加成会让伤害凭空变高。

暴击按一次扳机掷一次，同一枪的多发弹道同暴击。这与技术方案原文的"每发单独结算"不同，已改文档。

验证：`RangedWeaponTests` 12 条、`MeleeWeaponTests` 6 条。射程边界、冷却、穿透、扇形、单次去重、攻速翻倍 DPS 精确两倍都钉死了。

已知问题：距离衰减恒为 1.0 是死代码；`Pierce` 定义为命中总数而非额外穿透数；`SpreadDeg` 一个字段两种含义。

---

## 8 玩家

文件：`Model/PlayerModel.cs`、`Controllers/PlayerController.cs`、`Views/PlayerView.cs`、`Views/PlayerOutline.cs`

位置、速度、朝向、冲刺计时、HP 在 Model 里。移速与最大生命从 `StatBlock` 读，冲刺速度为移速的三倍、持续 0.15 秒、冷却 2 秒，冲刺期间 `IsInvulnerable` 为真。八向归一化，边界 clamp 到配置的场地半边长。

玩家不直接写 Transform：`PlayerView.Sync` 只接受 `ViewSnapshot`，位置与朝向都是插值后的值。描边壳是一个放大 1.12 倍、背面渲染的网格副本，用共享材质加属性块。

验证：`PlayerMovementTests` 10 条、`PlayerModelTests` 4 条。

已知问题：手感参数（移速 5、冲刺 15）只在纸面和断言里成立。

---

## 9 局流程

文件：`Core/RunController.cs`、`Systems/WaveController.cs`、`Model/RunState.cs`

`RunController` 管 Idle/Battle/Shop/GameOver 四个状态，波次计时、金币结算、击杀计数。事件顺序是 RunStarted → WaveStarted → WaveCleared → CoinChanged → ShopOpened → ShopClosed → 下一波，最后一波结束或玩家死亡走 RunEnded。Shop 和 GameOver 状态下战斗模拟不推进。

`WaveController` 按 `WaveDef` 刷怪，带同屏上限。刷怪点用黄金角加面积均匀半径铺开：随机撒 200 个点会有一批出生就重叠，压测量到的是出生堆叠而不是稳态。

验证：`RunControllerTests` 7 条、`WaveControllerTests` 5 条、`WaveFlowTests` 2 条（三波全流程 32400 步）、`RunStateTests` 4 条、`RunRestartTests` 3 条。三波各 179.983 秒，误差 0.017 秒；同屏上限从未突破；刷怪循环零分配。

已知问题：配置只有 3 波，GDD 写的是 5 波。

---

## 10 掉落与商店

文件：`Systems/PickupSystem.cs`、`Controllers/ShopController.cs`、`Controllers/ShopNavigator.cs`

掉落物 512 定长池，满了顶掉最旧的一枚——拒绝生成会让"每只怪掉一币"的收支悄悄断掉，调数值时变成查不清的差。吸附半径读玩家 `PickupRadius`，0.5 米结算，金币发事件、心回血 clamp。

商店 6 格加权抽卡，品质权重按 GDD 8.1 的两端点线性插值，连续 8 次无紫以上强制紫，价格按波次上浮，首次刷新免费之后 10 金币，买入走 `StatBlock.Add` 并按 `MaxStack` 叠加。选格逻辑抽成 `ShopNavigator` 纯类。

验证：`PickupSystemTests` 8 条、`ShopControllerTests` 17 条、`ShopNavigatorTests` 15 条。60000 抽分布与权重表差 0.12 个百分点内；保底 2000 手触发 646 次。

已知问题：道具只有 4 件，6 格去重凑不满，每间商店都会摆出紫金；触发型道具的 `EffectType` 未接。

---

## 11 旅伴与羁绊

文件：`Model/CompanionModel.cs`、`Systems/CompanionSystem.cs`、`Systems/CompanionBonds.cs`、`Views/CompanionView*.cs`

旅伴走固定角度环绕点跟随，无寻路，超过 4 米瞬移回位，另加一道 O(n²) 两两松解处理追赶途中的短暂重叠。没有单独建空间网格，也不参与敌人的分离力。每个旅伴一套 `WeaponSystem`，`UnitId` 从 1000 段位分配，击杀记在旅伴头上。

重复购买升级，每级伤害 +40%、攻击间隔 -8%，满级 3。羁绊按阵营计数，2 件一级、4 件二级，二级替换不叠加；重算时先把旧的整条 `RemoveBySource` 再重建，掉档时逐项回基线。

验证：`CompanionSystemTests` 8 条、`CompanionBondTests` 3 条、`CompanionViewTests` 12 条。4 个旅伴跟随时最小间距大于身体直径、不掉队；掉到 1 件后逐项与招募前快照比对。

已知问题：配置只有 3 个旅伴且每阵营一个，4 件羁绊实际到不了；旅伴不会受伤；不夹场地边界。

---

## 12 视图与 UI

文件：`Views/*`（24 文件）

视图层统一遵守三条：位置只吃插值快照、材质共享加 `MaterialPropertyBlock`、对象池化。敌人是方块加接触阴影盘，玩家是带描边的胶囊，旅伴是矮胶囊按阵营上色，投射物按持有者换颜色与大小。池有敌人、旅伴、投射物、掉落物四套，投射物池所有持有者共用一个实例池。

UI 全部运行时用 uGUI 代码搭：HUD、商店、结算、主菜单。不碰预制体和场景，因为那两者是手工维护的唯一真源，脚本生成的东西进去以后就分不清哪部分是代码建的。代价是布局只能靠数值调，看不见。

`HudView` 从每帧全量写改成快照差值只写变化的项（`HudValues` 纯类），商店与结算期间只放行金币。字体从系统动态加载，商店与 HUD 共用一份图集。

验证：`HudValuesTests` 7 条、`CompanionViewTests` 12 条（视图槽位分配等纯逻辑）、`RunEndedTests` 4 条。面板排版、字形、排序层级、按键响应零覆盖。

已知问题：中文字形依赖 Windows 系统字体；面板布局只按 16:9 估过。

---

## 13 相机与装配

文件：`Views/CameraRig.cs`、`Views/CameraPreset.cs`、`Core/AppRoot.cs`、`Core/SceneFlow.cs`、`Views/MainMenuBootstrap.cs`

`CameraRig` 在运行时建 `CameraTarget` 与 vcam，取景参数（正交视野、俯角、距离、裁剪、阻尼、边界半宽）来自 `CameraPreset` 资产，两份预设分别服务 Run 与 Test_Arena。跟随平滑交给 `CinemachineTransposer` 的阻尼，边界夹紧自己做：Confiner3D 要一个斜着包住 50° 轨迹的 3D 体积，规则反而看不见。

`AppRoot` 是唯一装配根，加载配置、实例化预制体、持有所有系统与视图池。主菜单用 `RuntimeInitializeOnLoadMethod` 加 `sceneLoaded` 钩子运行时搭，不改场景。

验证：无单测。相机参数与边界只有代码审查。

已知问题：`Damping` 的语义是"追上 99% 所需秒数"，不是时间常数，填错会看着像锁死；这份预设资产是手写 YAML，Unity 里读到什么值没确认。

---

## 测试分布

| 模块 | 用例 |
|---|---|
| 商店与导航 | 32 |
| 属性与伤害 | 25 |
| 武器 | 18 |
| 旅伴与羁绊 | 23 |
| 局流程与重开 | 23 |
| 玩家 | 14 |
| 空间索引与刷怪环 | 13 |
| HUD | 7 |
| 敌人 | 7 |
| 事件总线 | 10 |
| 集成（RunSimulation / WaveFlow） | 8 |
| 掉落与拾取 | 8 |
| 定格与命中反馈 | 4 |
| 合计 | 192 |

没有测试覆盖的模块：配置管线、输入服务、相机、装配根、全部 UI 渲染与布局。这些只能靠 Unity 里跑一遍。
