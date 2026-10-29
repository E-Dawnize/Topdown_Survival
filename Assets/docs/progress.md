# 迭代进度

## I0 工程基座

状态：代码与配置已完成，**尚未在 Unity 编辑器里跑过一次**。所有能在命令行验证的部分都验证过，见下面的证据列。

| 编号 | 任务 | 状态 | 证据 |
|---|---|---|---|
| I0-1 | 装包与项目设置 | 完成 | `Packages/manifest.json` 加了 Input System 1.7.0 / Cinemachine 2.9.7 / Newtonsoft 3.2.1；`activeInputHandler` 0→2；三档 URP 的阴影距离统一 40、cascade 2、软阴影与 HDR 全开，差异只剩 MSAA 与阴影贴图分辨率 |
| I0-2 | 场景骨架 | 代码完成 | `Assets/Scripts/Game/Editor/SceneBuilder.cs`，菜单 `Tools/Game/重建全部场景` 生成 Run / Test_Arena / Boot / MainMenu |
| I0-3 | asmdef 三层 | 完成 | Game.Runtime / Game.Editor / Game.Tests.EditMode，后者 `autoReferenced: false` + `UNITY_INCLUDE_TESTS` |
| I0-4 | 事件总线 | 完成 | `Core/Events/*`；15 项断言在命令行真跑过，全通过 |
| I0-5 | 配置管线 | 完成 | `tools/make_config_template.py` 生成 8 sheet 的 xlsx；`tools/export_config.py` 导表；C# 侧加载并校验，真跑过 |
| I0-6 | 输入 | 代码完成 | `Services/InputService.cs`，运行时用 `InputActionSetupExtensions` 构建 Action，不依赖 .inputactions 资产 |
| I0-7 | DebugOverlay | 代码完成 | F1 开关，显示 FPS / 帧时间 / GC / 事件订阅数 / 配置版本 / 种子 / 输入状态 |

### 已验证（命令行，不是推测）

编译：用 Unity 自带的 Roslyn（`Editor/Data/DotNetSdkRoslyn/csc.dll`）编译了运行时与编辑器脚本，0 错误。
Input System 与 Cinemachine 两个包在本机还没下载，那部分用桩编译，桩的方法签名逐个对照过包的源码（Input System 1.7.0、Cinemachine 2.9.7 的 tarball）。

运行时行为：

- EventBus：15 项断言通过，覆盖延后分发、同类型 FIFO、重复订阅去重、退订、分发中退订、handler 内 Publish、Clear、类型隔离、50 订阅者。
- ConfigDatabase：真读 `Assets/StreamingAssets/Config` 下的 8 个 JSON，角色 3 / 武器 2 / 旅伴 3 / 道具 4 / 敌人 3 / 波次 3 / buff 0，枚举、`list:id`、float 精度、Balance 字典都对。
- 校验：改坏引用、改坏怪种、Cooldown 置 0、波次表清空，四种错误都被拦下并给出定位信息；空表（Buff）允许通过。
- 导表：`manifest.version` 由全部表内容的 sha1 前 12 位生成，改一个数版本就变。

写代码过程中修掉的真实 bug：导表脚本把 `list:id:Enemy` 的目标表名切成了 `id:Enemy`，导致所有波次的怪种引用误报；`InputActionSetupExtensions.AddAction` 的参数名是 `expectedControlLayout` 而不是 `expectedControlType`。

### 未验证

1. **Unity 里的一切**：包下载、脚本导入、场景生成、按 Play。这些没有 Unity 编辑器就做不了。
2. Input System / Cinemachine 的真实编译（只有桩编译）。
3. `SceneBuilder` 生成的场景是否符合预期（相机角度、光照、材质颜色）。
4. 导表脚本处理的 xlsx 是脚本自己生成的。Excel 重新保存过的文件还没试过，理论上 `sharedStrings` 与 `inlineStr` 两种都支持。

### 需要人工执行一次

1. 用 Unity Hub 打开工程，等三个包下载完、编译结束。
2. 菜单 `Tools/Game/重建全部场景`。
3. 打开 `Assets/Scenes/Run.unity`，按 Play，确认：相机以 50° 俯视，地面 160×160 有网格感，Console 打出 `[Config] 加载成功 版本 …`，F1 有覆盖层。

如果第 2 步报错，先看 Console 里是编译错误还是 Cinemachine API 不匹配。

## I1 战斗核心

未开始。
