# 迭代进度

## I0 工程基座

状态：代码与配置已完成，且在 Unity 2022.3.34f1c1 里导入并编译通过。场景生成与 Play 还没跑过。

| 编号 | 任务 | 状态 | 证据 |
|---|---|---|---|
| I0-1 | 装包与项目设置 | 完成 | 三个包已下载到 Library/PackageCache；`activeInputHandler` 0→2；三档 URP 阴影距离统一 40、cascade 2、软阴影与 HDR 全开，差异只剩 MSAA 与阴影贴图分辨率 |
| I0-2 | 场景骨架 | 完成 | Unity 内跑过 `Tools/Game/重建全部场景`，生成 Run(160m) / Test_Arena(40m) / Boot / MainMenu，Build Settings 自动写好；Run.unity 里 12 个 guid 全部能解析，无 missing script |
| I0-3 | asmdef 三层 | 完成 | Unity 产出 `Game.Runtime.dll` / `Game.Editor.dll` / `Game.Tests.EditMode.dll` 三个独立程序集 |
| I0-4 | 事件总线 | 完成 | `Core/Events/*`；15 项断言在命令行真跑过，全通过 |
| I0-5 | 配置管线 | 完成 | `tools/make_config_template.py` 生成 8 sheet 的 xlsx；`tools/export_config.py` 导表；C# 侧加载并校验，真跑过 |
| I0-6 | 输入 | 完成 | `Services/InputService.cs`，运行时用 `InputActionSetupExtensions` 构建 Action，不依赖 .inputactions 资产 |
| I0-7 | DebugOverlay | 完成 | F1 开关，显示 FPS / 帧时间 / GC / 事件订阅数 / 配置版本 / 种子 / 输入状态 |

### 已验证

Unity 内编译：`Library/ScriptAssemblies/` 里三个程序集都是 Unity 自己编出来的，`Game.Runtime.dll` 链接时 227 个引用，编译日志里没有项目相关的 `error CS` 与 `warning CS`。

Unity 内运行：菜单生成四个场景并写 Build Settings，随后进 Play，Console 打出 `[Config] 加载成功 版本 741b4ba082a6｜角色 3｜武器 2｜旅伴 3｜道具 4｜敌人 3｜波次 3`，之后没有任何异常。

命令行导表与运行时：

- EventBus：15 项断言通过，覆盖延后分发、同类型 FIFO、重复订阅去重、退订、分发中退订、handler 内 Publish、Clear、类型隔离、50 订阅者。
- ConfigDatabase：真读 `Assets/StreamingAssets/Config` 下的 8 个 JSON，角色 3 / 武器 2 / 旅伴 3 / 道具 4 / 敌人 3 / 波次 3 / buff 0，枚举、`list:id`、float 精度、Balance 字典都对。
- 校验：改坏引用、改坏怪种、Cooldown 置 0、波次表清空，四种错误都被拦下并给出定位信息；空表（Buff）允许通过。
- 导表：`manifest.version` 由全部表内容的 sha1 前 12 位生成，改一个数版本就变。

写代码过程中修掉的真实 bug：导表脚本把 `list:id:Enemy` 的目标表名切成了 `id:Enemy`，导致所有波次的怪种引用误报；`InputActionSetupExtensions.AddAction` 的参数名是 `expectedControlLayout` 而不是 `expectedControlType`。

Unity 首次导入时出现过一次 `error CS2001: Source file 'Assets/Scripts/GameFlow.cs' could not be found`——删了脚本但 csproj 还挂着旧引用，Unity 重建 csproj 后自行消失，不是代码问题。

### 未验证

1. 画面本身：相机角度、光照、材质颜色是否符合预期。结构对不代表好看。
2. 输入有没有反应（目前还没有读输入的代码，Play 时只构建了 ActionMap）。
3. 导表脚本处理的 xlsx 是脚本自己生成的。Excel 重新保存过的文件还没试过，理论上 `sharedStrings` 与 `inlineStr` 两种都支持。

### 下一步

1. ~~用 Unity Hub 打开工程~~ 已完成。
2. ~~菜单 `Tools/Game/重建全部场景`~~ 已完成。
3. ~~打开 Run.unity 按 Play，确认配置加载日志~~ 已完成。
4. 进迭代 1：玩家移动、属性与伤害、空间哈希压测。

## I1 战斗核心

进行中。I1-1、I1-2 已完成。

| 编号 | 任务 | 状态 | 证据 |
|---|---|---|---|
| I1-1 | 玩家移动 | 代码完成，待 Unity 内手测 | PlayerModel / PlayerController / PlayerView 三层，RunLoop 固定 60Hz 步长；10 项断言 |
| I1-2 | 冲刺 | 代码完成，待 Unity 内手测 | 0.15s 持续、3 倍速、2s 冷却；起步这一步立即有速度；同帧多步只触发一次 |
| 提前 | 基础 HUD | 代码完成，待 Unity 内手测 | 血条、金币、波次、冲刺冷却条，全部代码搭建；玩家生命接 CharacterDef.BaseHP；F2/F3 调试键 |
| I1-3 | StatBlock | 未开始 | 当前移速等属性在 PlayerModel 构造时定死 |
| I1-4 | DamageResolver | 未开始 | |
| I1-5 | 空间哈希 | 未开始 | |
| I1-6 | 敌人 AI | 未开始 | |
| I1-7 | 弓箭 | 未开始 | |
| I1-8 | 砍刀 | 未开始 | |
| I1-9 | UnitView | 未开始 | |
| I1-10 | 命中反馈 | 未开始 | |
| I1-11 | 压力测试 | 未开始 | |

已验证（命令行跑断言，11 项全过）：
沿输入以移速前进、对角线归一化、边界夹紧、冲刺触发事件并进入冲刺态、同帧多步只冲一次、冷却期间不能冲、冲刺结束回到走速、无输入时按朝向冲刺、朝向跟随速度、冲刺位移在 2.2–2.9 米、贴边冲刺仍被夹住。

写的过程中修掉一个 bug：StartDash 只设了计时器没设速度，导致冲刺第一步人没动但事件已经发了。

工程结构调整（按"预制体 + 最小场景"重做资源管理）：

- 场景只留方向光、地面、边界墙、主相机、AppRoot、HUD 实例。虚拟相机、CameraTarget、玩家都在运行时创建，改参数不用重建场景。
- `PrefabBuilder` 生成一次性的初始资源：Player、三个敌人占位、HudCanvas、UnitPrefabMap。生完就归手工维护，不再参与日常流程。
- `HudView` 从代码搭 UI 改成预制体 + 序列化引用，布局在编辑器里拖，改字号不用重编译。
- Id 到预制体的映射用 `UnitPrefabMap`（ScriptableObject 显式引用），不用 `Resources.Load` 按名字查。启动时校验：玩家缺失是致命错误，敌人缺失只警告，因为 I1 阶段还有两个敌人没做。

第一次跑 PrefabBuilder 只生成了材质没有预制体。原因是它用 `System.IO.Directory.CreateDirectory` 建 `Assets/Prefabs/Units`，Unity 的 AssetDatabase 不认这个目录，`SaveAsPrefabAsset` 静默失败。改成 `AssetDatabase.CreateFolder` 逐级创建后正常。这个坑对所有往 Assets 下写新目录的编辑器脚本都成立。

HUD 提前做了（原计划在 I2-9）：没有敌人时血条不会掉，所以加了 F2 扣血、F3 加金币两个调试键来验证 UI 是活的，等敌人接上后删掉。UI 用 uGUI + 代码生成，没走 TextMeshPro——项目还没导入 TMP Essentials，运行时建 TMP 文本会缺默认字体。中文字体在 Windows 上用系统字体动态加载，换平台要换方案。

未验证：
1. Unity 内编译（Game.Runtime.dll 停在 19:58，编辑器没重新编译新代码）。
2. 场景需要重跑 `Tools/Game/重建全部场景`，否则场景里没有 CameraTarget 和 PlayerView 组件，玩家不会动、相机也不会夹边界。
3. 手感：移速 5、冲刺 15 的实际感受，只能靠手测。

## I1 之后

未开始。
