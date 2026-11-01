# 模块报告

> **历史快照**：本文对应 v0.2（波次 + 商店 + 金币）。当前 v0.3 已改为单局时间轴 + 经验构筑 + BOSS，见 `STATUS.md` / `GDD.md`。
> 文档目录：[docs/README.md](README.md)

日期：2026-10-31
分支：`feat/demo`

按代码模块给出职责、关键技术选型、验证情况与已知问题。测试数字来自 `grep -c "[Test]"`；本文保留 v0.2/v0.3 历史数字，当前为 219 EditMode + 5 PlayMode（相机、近战动作、弓箭搭箭、搭箭交接、箭直线路径），以 `STATUS.md` 为准。

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

事件总线把 `Publish` 写进待发队列，整轮结束统一 `Flush`。订阅方不会读到半更新状态，代价是所有事件晚一帧——商店关店后输入切回 Player 就损失一帧移动输入。每个频道每轮只处理进入本轮时已有的事件，`EventBus` 用总预算限制同类型自触发；单个订阅者异常会被隔离，不再阻断同事件的其他订阅者。

去重按委托相等，同一个委托实例订阅两次只执行一次。这条规则踩过一次：测试里 `_ => total++` 捕获同一闭包，50 次订阅拿到同一个委托，被去重成一个，那个测试从来没真正通过。

随机是单条 xorshift128，取高 24 位换 [0,1)，初始化走 splitmix 避免四个状态相近时前几步输出偏。战斗、刷怪、掉落、商店共用这一条流。技术方案原本写三条流，实际只做了一条，代价是加刷怪逻辑会挪动暴击序列。

输入用 `InputActionSetupExtensions` 在运行时构建 Action，不依赖 `.inputactions` 资产。Player 与 UI 两个 Map，进商店和结算切 UI，关掉切回。

验证：`EventBusTests` 11 条（含同类型自触发预算）。输入缓冲在 `PlayerMovementTests` 有一条零步帧锁存用例；随机通过刷怪分布与抽卡分布间接验证。

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

表现层是 `Views/WeaponVisualStyle.cs`、`WeaponVisualCatalog.cs`、`WeaponVisual.cs`、`WeaponRackView.cs`：

> v0.3.3 更新：下面这段已从"运行时拼几何体"改为"CC0 FBX → prefab → catalog"。
> 旧实现保留在 git 历史里；当前管线以 `Assets/docs/WEAPON_ASSETS.md` 为准。

- `WeaponVisualStyle` 只放与资源无关的纯数据：玩家 1–4 槽位、旅伴槽位/缩放、跟随拖尾参数。动作类型/时长与模型 Id 的映射在 `WeaponVisualCatalog` 资产里。
- `WeaponVisualCatalog` 是武器 Id → prefab/动作/时长的唯一入口；prefab 根节点自带 `WeaponVisual`，模型来自 Quaternius / KayKit 的 CC0 FBX。缺条目/缺 prefab 直接报错，不生成白模。
- `WeaponVisual` 只操作 prefab 根 Transform：每帧比较 `WeaponModel.ShotsFired`，变化时播放一次剑扫、双匕双扫、战锤下砸、弓拉弦后坐、弩后坐、灵球脉冲；枪口/刀尖闪光只亮 0.16 秒。远程开火把真实 `aim` 写进 `WeaponModel.AimDirection`，模型按它绕 Y 转。
- `WeaponRackView` 只在武器数量、顺序或 WeaponModel 实例引用变化时重建；升级同一把武器不重造模型。`PlayerView.Sync` 每帧把当前 `WeaponSystem.Weapons` 和玩家速度交给它；武器用 `SmoothDamp` 追“槽位 + 速度反方向拖尾”，静止回槽位，冲刺按 1.1m 夹紧。
- 旅伴只有一把武器，挂在 `WeaponAnchor` 上：旅伴胶囊根节点被压扁成 (0.7, 0.3, 0.7)，锚点反向缩放，世界空间仍是 1:1，再乘 0.5 的武器缩放；`CompanionViewPool` 每帧对一次武器引用，以后换刀能显示。

验证：`RangedWeaponTests` 14 条、`MeleeWeaponTests` 6 条；`WeaponVisualCatalogTests` 5 条（真实配置覆盖、prefab 契约、六种动作互异、近战/远程时长约束）、`WeaponVisualStyleTests` 3 条（槽位、拖尾/转向、旅伴缩放）、`ProjectileArrowTests` 1 条（箭 prefab 与 +Z 长轴）；`WeaponVisualPlayTests` 5 条 PlayMode（相机、catalog prefab 近战、弓箭搭箭窗口、搭箭交接、箭直线路径）。射程边界、冷却、穿透、扇形、单次去重、攻速翻倍 DPS 精确两倍都钉死了。

已知问题：距离衰减恒为 1.0 是死代码；`Pierce` 定义为命中总数而非额外穿透数；`SpreadDeg` 一个字段两种含义；表现层靠轮询 `ShotsFired`，若以后改开火逻辑必须保持该计数在真正挥砍/开火时 +1；武器模型无 LOD，最多 4 把玩家武器 + 4 个旅伴武器，零件数需在真机看 draw call。

---

## 8 玩家

文件：`Model/PlayerModel.cs`、`Controllers/PlayerController.cs`、`Views/PlayerView.cs`、`Views/PlayerOutline.cs`

位置、速度、朝向、冲刺计时、HP 在 Model 里。移速与最大生命从 `StatBlock` 读，冲刺速度为移速的三倍、持续 0.15 秒、冷却 2 秒，冲刺期间 `IsInvulnerable` 为真。八向归一化，边界 clamp 到配置的场地半边长。

玩家不直接写 Transform：`PlayerView.Sync` 只接受 `ViewSnapshot`，位置与朝向都是插值后的值。描边壳是一个放大 1.12 倍、背面渲染的网格副本，用共享材质加属性块。

`PlayerView.Awake` 同时挂 `WeaponRackView`，武器模型是玩家根节点的子物体，随玩家转向；渲染位置仍来自 `ViewSnapshot`，不读 Model。

验证：`PlayerMovementTests` 11 条（含输入锁存跨零步帧）、`PlayerModelTests` 4 条。

已知问题：手感参数（移速 5、冲刺 15）只在纸面和断言里成立；武器是悬浮在胶囊周围的表现件，没有手臂/握持动画。

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

旅伴走固定角度环绕点，目标点按玩家速度轻微拖尾，用 SmoothDamp 阻尼跟随（0.22 秒）；无寻路，超过 4 米瞬移回位，另加一道 O(n²) 两两松解处理追赶途中的短暂重叠。没有单独建空间网格，也不参与敌人的分离力。每个旅伴一套 `WeaponSystem`，`UnitId` 由本局共享的 `UnitIdAllocator` 分配，击杀记在旅伴头上。

重复购买升级，每级伤害 +40%、攻击间隔 -8%，满级 3。羁绊按阵营计数，2 件一级、4 件二级，二级替换不叠加；重算时先把旧的整条 `RemoveBySource` 再重建，掉档时逐项回基线。

验证：`CompanionSystemTests` 覆盖阻尼起步、速度拖尾、4 个旅伴间距/不掉队、4 米瞬移、AI 接敌/撤退；`CompanionBondTests` 与 `CompanionViewTests` 覆盖羁绊与视图。

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

`CameraRig` 在运行时建 `CameraTarget` 与 vcam，取景参数（正交视野、俯角、距离、裁剪、跟踪偏移、屏幕偏移、跟随阻尼、矩形死区、边界、优先级、震动增益）来自 `CameraPreset` 资产。相机朝向固定在 `(Tilt, 0, 0)`；`CinemachineTransposer` 只按焦点定位，阻尼为 0；死区与 SmoothDamp 在纯类 `CameraFocus` 里按地面 X/Z 两轴算。边界夹紧自己做：Confiner3D 要一个斜着包住 50° 轨迹的 3D 体积，规则反而看不见。

当前四份预设：

| 预设 | 用途 | 关键参数 |
|---|---|---|
| `CameraPreset_Run` | 正式 Run 场景 | size 7，矩形死区 0.14，SmoothDamp 0.32，边界 80 |
| `CameraPreset_TestArena` | 40×40 测试场 | size 7，矩形死区 0.14，SmoothDamp 0.32，边界 20 |
| `CameraPreset_Boss` | BOSS 拉远（供后续相机模式使用） | size 10，俯角 55，死区 0.12，SmoothDamp 0.40，优先级 110 |
| `CameraPreset_Menu` | 商店/结算静帧（供后续相机模式使用） | size 8，阻尼 0，死区 0，关闭边界夹紧，优先级 90，震屏 0 |

Run 与 Test_Arena 的场景引用已经切到对应预设；Boss/Menu 目前只是配置基础，`CameraRig` 还没有多相机模式切换。

`AppRoot` 是唯一装配根，加载配置、实例化预制体、持有所有系统与视图池。主菜单用 `RuntimeInitializeOnLoadMethod` 加 `sceneLoaded` 钩子运行时搭，不改场景。

验证：`CameraFocusTests` 覆盖矩形死区、横/纵单轴、对角、阻尼收敛、零阻尼和回框停住；`CameraRigPlayTests` 用真实 CameraRig + CinemachineBrain 验证 0.14 死区和横/纵两轴落点。边界夹紧与震屏仍只有手验。

已知问题：`Damping` 现在是 `CameraFocus` 的 SmoothDamp `smoothTime`（秒），不是 Cinemachine 的阻尼时间；相机只有装配层 PlayMode 用例，完整流程、边界夹紧和震屏仍未自动化。Test_Arena 的相机边界（20）与玩家边界（Balance 里的 67）不一致，靠墙测试时相机会提前停止。四份预设资产是手写 YAML，Unity 里读到什么值需要在 PlayMode 确认。

---

## 测试分布

| 模块 | 用例 |
|---|---|
| 商店与导航 | 32 |
| 属性与伤害 | 25 |
| 武器 | 18 |
| 旅伴与羁绊 | 23 |
| 局流程与重开 | 23 |
| 玩家 | 15 |
| 空间索引与刷怪环 | 13 |
| HUD | 7 |
| 敌人 | 7 |
| 事件总线 | 11 |
| 集成（RunSimulation / WaveFlow） | 9 |
| 掉落与拾取 | 8 |
| 定格与命中反馈 | 5 |
| 合计（v0.3） | 196 |

新增 `CameraFocusTests` 7 条、旅伴拖尾 1 条、`WeaponVisualStyleTests` 7 条后，当前 EditMode 为 206 条；另有 `CameraRigPlayTests` 与 `WeaponVisualPlayTests` 2 条 PlayMode 用例。表里是 v0.3 阶段的历史分布，合计 196 不含这些后加用例。

没有测试覆盖的模块：配置管线、输入服务、装配根、全部 UI 渲染与布局；相机的边界夹紧与震屏仍只有手验。这些只能靠 Unity 里跑一遍。
