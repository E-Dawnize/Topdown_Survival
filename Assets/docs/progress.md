# 迭代进度
> 文档目录：[docs/README.md](README.md)

> 下面 I0 / I1 / I2 的记录保留当时进度。I2 之后当前工作区又做了一轮 P0/P1 修复与 v0.3 玩法重做；v0.3 见第一节，当前测试数为 237（227 EditMode + 10 PlayMode：相机装配、武器/范围动作、血条与飘字）。

## v0.4 掉落品阶、全屏吸取与战斗飘字

状态：代码与自动化验证完成；主工程 Unity Play 的观感仍未手测。

| 任务 | 状态 | 证据 |
|---|---|---|
| 回血掉落率 | 完成 | Balance `HeartDropChance` 12%→9%；xlsx 与导出 JSON 同步；掉落概率测试按 9% 重跑 |
| 武器攻击范围 | 完成 | `WeaponRangeIndicator` 基类 + 扇形/目标锁定圈子类；装备即创建、攻击窗口才显示；远程锁定圈跟着目标从外圈收拢；`WeaponModel.ShotAge` 补播晚挂视觉的第一次出手 |
| 经验球品阶 | 完成 | `PickupTier` 由敌人 Tier/精英/BOSS 推导；`PickupViewPool` 五种颜色/大小；经验值仍取 Enemy.XpValue（精英 ×4） |
| 全屏吸取 | 完成 | E/RB 输入、12 秒冷却（Balance `ExperienceCollectCooldown`）、`PickupSystem.CollectAllExperience` 只吸经验；右下 HUD 冷却条 + 扩散环 + 提示 |
| 怪物血条 | 完成 | `EnemyHealthBar` 两条世界空间 LineRenderer；掉血后显示，精英/BOSS/Tier 4+ 常显 |
| 伤害飘字 | 完成 | `DamageNumbers` 世界 TextMesh 池；普通/暴击/玩家受击三种颜色与字号，暴击带“暴击”前缀 |
| 测试 | 完成 | EditMode 222→227；PlayMode 5→10（血条显隐、飘字普通/暴击文本、近战/远程攻击范围、晚挂视觉补播） |

验证：独立工程副本 Unity 2022.3.34f1c1 批量模式 EditMode 227/227、PlayMode 10/10；`dotnet build` Runtime 与测试工程 0 错 0 警告。

## v0.3.4 攻击可见与 HUD 装备栏

状态：代码与自动化验证完成；主工程 Unity Play 的整条流程与观感仍未手测。

| 任务 | 状态 | 证据 |
|---|---|---|
| 武器攻击范围显示 | 完成 | `WeaponRangeIndicator` 抽象基类 + `FanRangeIndicator`/`TargetReticleIndicator` 子类；装备即创建、攻击窗口才显示；远程锁定圈跟着目标怪从外圈收拢；Melee/RangedBehaviour 每步刷新 `LastRange/AimDirection`；`WeaponModel.ShotAge` 补播晚挂视觉的第一次出手 |
| 敌人攻击可见 | 完成 | 近战/冲锋起手红环 + 出手红扇；投手蓄力后发射可见弹丸（`EnemyProjectileSystem` + 暗红箭头视图）；伤害只在起手结束/弹丸命中时结算 |
| 范围伤害可见 | 完成 | 碰撞、爆炸、链电、环绕刀、反伤结算后发 `CombatAreaEffect`，`CombatEffectView` 画不同颜色范围环 |
| HUD 装备栏 | 完成 | `BuildController.LoadoutRevision` 驱动左上装备栏重拼：武器/旅伴及等级、组合、数值卡数、指定 buff/道具名字；受击弹出伤害数字 |
| 测试 | 完成 | EditMode 219→222（起手可躲、冲锋、远程弹丸、碰撞范围事件、HUD 版本号）；PlayMode 5/5 |

验证：独立工程副本 Unity 2022.3.34f1c1 批量模式 EditMode 222/222、PlayMode 5/5；`dotnet build` Runtime 与测试工程 0 错 0 警告。

## v0.3 玩法重做（单局时间轴 + 经验构筑 + BOSS）

状态：完成，命令行验证通过；Unity Play 未手测。

| 任务 | 状态 | 证据 |
|---|---|---|
| 单局时间轴替代波次 | 完成 | `DirectorSystem` 8 段，0–600 秒；同屏上限 30→140，怪种池 Crawler→Ravager，精英概率 0→14% |
| 经验掉落替代金币 | 完成 | `PickupSystem` 只掉经验/心；`RunState.AddExperience` 处理跨级与排队；真实配置 10 分钟升到 30 级 |
| 升级三选一 | 完成 | `BuildController` + `UpgradeView`；Level 表 30 级、3/4 张卡、稀有度上浮、重抽 1 次 |
| 武器构筑 | 完成 | 6 把武器、4 槽、5 级；伤害 +30%/级、间隔 ×0.92/级、Lv3 弹道、Lv4 穿透 |
| 旅伴构筑 | 完成 | 6 个旅伴、4 队、3 级；四阵营 2/4 件羁绊沿用 |
| 数值构筑 | 完成 | 14 张数值卡；射程/护甲/回复/碰撞伤害已接进战斗管线并有测试 |
| buff 构筑 | 完成 | 12 张 buff；击杀叠攻速/回血、受击叠甲、低血开关、周期回复、Reset 回滚 |
| BOSS | 完成 | 10:00 缝线巨偶；27900 血、12 秒召唤 4 小怪、半血狂暴；击杀通关 |
| HUD / 结算 | 完成 | 等级/经验条、威胁+BOSS 倒计时、BOSS 血条、构筑摘要；结算等级/击杀/武器/旅伴 |
| 配置表重做 | 完成 | 9 sheet：Character/Weapon/Companion/Enemy/Buff/Director/Level/Upgrade/Balance；删除 Wave/Item |
| 文档 | 完成 | GDD v0.3、BUILD_SYSTEM、TECH_DESIGN v0.3、STATUS、README |

验证：

- `dotnet build Game.Runtime.csproj -t:Rebuild` 与测试工程：0 错 0 警告。
- Unity Test Framework（EditMode）：206 / 206 通过；PlayMode 相机 + 武器动作：2 / 2 通过。
- 真实配置全流程：36007 步 / 约 0.5–1.1 秒（托管侧）；等级 30、击杀 1592、最高同屏 101、武器 4、旅伴 3、组合 1、buff 4；10:00 切 BOSS，清空后胜利。
- 未验证：Unity Play、UI 排版、渲染帧率、手感与平衡。

### v0.3.1：手感与构筑补强

| 任务 | 状态 | 证据 |
|---|---|---|
| 前期刷怪密度 | 完成 | 0–45 秒 2.2 秒 1 只、上限 12；0:45–1:45 上限 20；后期上限 120 |
| 机制道具 | 完成 | 燃烧弹/导电/环绕刀/吸血獠牙/荆棘甲，分别接 OnKillExplode/OnHitChain/OrbitDamage/Lifesteal/OnHurtThorns |
| 组合效果 | 完成 | 4 个标签组合：过载/血契/弩阵/旋风；BuildController 选卡后重算，HUD 弹出提示 |
| 构筑池扩充 | 完成 | 38→43 张卡；buff/机制 21 行（17 张卡 + 4 个组合 buff） |
| 旅伴 AI | 完成 | Follow/Aggro/Kiter/Guard；AggroRadius/PreferredRange/LeashDistance 配置 |
| HUD 血条 | 完成 | 预制体 HpFill/DashFill 是 Filled 但 Sprite 为空；运行时补 1×1 白图，fillAmount 才有效 |
| 相机跟随 | 完成 | Run/TestArena：矩形死区 0.14、SmoothDamp 0.32 秒；角色在框内镜头不动，出框后横/纵两轴一起阻尼追上，停下停在框边 |
| 武器表现 | 完成 | 6 把武器全部有几何体模型；SmoothDamp 追玩家身后槽位形成拖尾；剑/匕/锤/弓/弩/灵球六种动作由 `ShotsFired` 驱动；玩家 4 槽、旅伴单槽反向缩放锚点 |
| 测试 | 完成 | 183→208 用例（206 EditMode + 2 PlayMode）；真实配置全流程击杀 1592、最高同屏 101、组合 1、buff 4 |

---

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


### 生成器已删除

场景与预制体生成成功后，`SceneBuilder.cs` 与 `PrefabBuilder.cs` 已从仓库删除，只剩 `ConfigMenu.cs`（导表入口，日常要用）。

删前的核对：4 个单位预制体、HudCanvas、UnitPrefabMap 里的 guid 引用共 16 个，全部能在项目或包内解析，无缺失。Run 场景里 HUD 是 prefab 实例（`!u!1001`），Player 与 vcam 只在实际运行时出现。

要从零重建，从提交 `90301c2` 取回两个脚本，跑完再删。


### UI 注入与订阅清理

- 新增 `UiView` 基类：负责订阅登记与取消。子类在 `SubscribeAll()` 里用 `On<T>()` 注册，隐藏时基类统一退订，不用自己记。
- `HudView` 改成继承 `UiView`，去掉 `OnEnable` 里查 `AppRoot.Instance` 的写法，改由装配根 `Initialize(bus)` + `Bind(player, run)`。
- HUD 从"摆在场景里的 prefab 实例"改成"`AppRoot` 运行时实例化"，跟玩家一致。场景里现在只剩 8 个根对象：AppRoot、方向光、地面、四块墙、主相机。
- `UnitPrefabMap` 加一条 `HudCanvas`，HUD 的预制体也从这张表拿，不再单独一个字段。
- HUD 预制体根节点是 inactive，`AppRoot` 实例化后先 `Initialize` 再激活，保证首次 `OnEnable` 时依赖已就绪。基类里对漏注入的情况直接报错，不静默放行。

一个已知的遗留：`HudView` 在 `Awake` 里替换字体（预制体用的是内置 LegacyRuntime，没有中文字形）。这一步依赖 `GetComponentsInChildren`，如果以后 HUD 结构变得很深，会慢一点，但只在实例化时跑一次。

## I1 战斗核心（完成）

`feat/demo` 分支上 11 个任务全部落地。192 个 EditMode 用例在仓库外 harness 全通过；`dotnet build` 四个工程 0 错 0 警告。

| 编号 | 任务 | 状态 | 证据 |
|---|---|---|---|
| I1-3 | StatBlock 三乘区 | 完成 | `(base+Σflat)×(1+Σpercent)×Πmultiply`，概率属性 clamp，`RemoveBySource` 批量移除，稳态读零分配 |
| I1-4 | DamageResolver | 完成 | 闪避 → 无敌帧 → 护甲 `armor/(armor+50)` → 扣血 → 判死，纯函数不掷随机 |
| I1-5 | 空间哈希 | 完成 | 2 米格 32×32，槽位号固定，200 单位重建 7.1µs |
| I1-6 | 敌人追击 AI | 完成 | 追击 + 邻格分离 + 位置松解，50 敌 600 步不叠成一坨 |
| I1-7 | 弓箭 | 完成 | 射程边界、冷却、穿透、弹道数分布 |
| I1-8 | 砍刀 | 完成 | 扇形判定、伤害窗口、单次挥砍去重，攻速翻倍 DPS 精确两倍 |
| I1-9 | UnitView | 完成 | 共享材质 + 属性块，接触阴影盘与玩家描边壳 |
| I1-10 | 命中反馈 | 完成 | 受击变亮、击退、hitstop、震屏、屏幕红闪 |
| I1-11 | 压力测试 | 部分 | 模拟侧 200 敌 0.211ms/步、0 B/步；Unity 帧率与 draw call 未测 |

这一轮修掉的真实 bug：

1. `UnitPrefabMap` 里 HudCanvas 的 fileID 指向预制体内部的 Hint 子节点，实例化出来只有一张 Image，`GetComponent<HudView>()` 永远为 null。
2. `EventBusTests.ManySubscribersAllReceive` 从来没真正通过：循环里的 lambda 捕获同一个闭包，50 次订阅拿到同一个委托实例，被去重成一个。
3. `DamageResolver` 负护甲在 armor < -50 时 `armor/(armor+50)` 变正数，clamp 后成了满减伤。
4. 砍刀命中不发 `UnitDamaged`，所以近战打中没有定格、震屏和受击变亮。
5. `RunState` 重开时金币与波次不归零；`ShopController` 重置时只清列表、没从 `StatBlock` 回滚道具加值。

已知偏差：

- 距离衰减公式恒为 1.0，锁定目标必在射程内，公式有单测但不生效。
- `Pierce` 定义为命中目标总数，`SpreadDeg` 远程是相邻弹道夹角、近战是扇形半角。
- 暴击按一次扳机掷一次，同一枪多发改同暴击。

## I2 循环闭环（完成）

| 编号 | 任务 | 状态 | 证据 |
|---|---|---|---|
| I2-1 | RunState + RunController | 完成 | Idle/Battle/Shop/GameOver，事件顺序整条断言 |
| I2-2 | WaveController | 完成 | 三波各 179.983 秒（误差 0.017），同屏上限与刷怪间隔都有断言 |
| I2-3 | 金币掉落磁吸 | 完成 | 1 币 + 25% 心，吸附半径读 `PickupRadius`，心回血 clamp |
| I2-4 | ShopController | 完成 | 60000 抽分布与权重差 0.12pp 内，保底 8 次，价格与刷新 |
| I2-5 | 商店 UI | 完成 | 键盘全流程，选格逻辑抽成 `ShopNavigator` 纯类 |
| I2-6 | 旅伴 | 完成 | 环位 + 速度拖尾 + SmoothDamp 阻尼跟随、4 米瞬移、每旅伴一套武器、重复购买升级 |
| I2-7 | 羁绊 | 完成 | 2/4 件触发，重算先整条退掉再重建，掉档逐项回基线 |
| I2-8 | 道具骨架 | 部分 | 属性型道具生效；`EffectType` 未接，触发型道具只扣钱 |
| I2-9 | HUD | 完成 | 快照差值只写变化的项，补波次倒计时、场上敌人、击杀数 |
| I2-10 | 死亡与结算 | 完成 | 结算面板、场景重载重开、跨局状态重置 |

配置表内容不足导致的两处退化：道具只有 4 件撑不满 6 格去重（每间商店都会摆出紫金），旅伴只有 3 个每阵营一个（4 件羁绊到不了）。两处都在等 I3 补内容。

## P0/P1 修复（当前工作区）

这一轮不改玩法内容，只修底层边界：

- 启动：MainMenu 成为构建入口，Boot 禁用；AppRoot 缺 RunLoop 时显示错误面板；配置失败可见；种子在装配前确定。
- 输入与时间：InputBuffer 锁存按钮边沿；RunClock 改为 BeginFrame + TryConsumeStep，定格不再吞掉已计时步数。
- 实体：UnitIdAllocator + UnitKind；玩家、敌人、旅伴 ID 唯一；投射物/近战/视图改用稳定 ID。
- 生命周期：RunController.StartRun 只允许 Idle 调用一次，GameOver 后二次调用抛异常。
- 事件：EventChannel 每轮处理快照，EventBus 总预算防止同类型自触发；订阅者异常隔离。
- 输入模式：InputService.OnEnable 按当前 UiMode 恢复 Map。
- 调试：F2/F3 移到 DebugOverlay，限制在编辑器/开发构建。
- 相机：`CameraFocus` 矩形死区 + SmoothDamp；相机朝向固定，Transposer 只做定位；CameraPreset 扩展为 Run / TestArena / Boss / Menu 四份。

验证：`Game.Runtime`、`Game.Tests.EditMode`、`Game.Tests.PlayMode` 编译 0 错误 0 警告；206 个 EditMode 用例 + 2 条 PlayMode 用例通过。Unity PlayMode 的完整流程仍未验证。

遗留：Test_Arena 的相机边界（20）与玩家边界（Balance 的 67）不一致；Boss/Menu 预设尚未接入多相机模式。

## v0.3.3 武器资产管线（完成）

这一轮把"运行时用 Cube/Cylinder/Sphere 拼武器"换成真实资产管线：

- `Assets/Art/Models/Weapons/`：Quaternius / KayKit 的 CC0 FBX（弓、弩、剑、双匕、战锤、灵球），许可与来源写在子目录 `LICENSE.txt` 与 `README.md`。
- `WeaponPrefabBuilder`：按最长边缩放、按握点对齐到 prefab 原点、补闪光/灵球光晕；菜单 `Tools/Weapons/Create Missing Visual Prefabs` 只补缺失，`Force Rebuild` 才覆盖；生成 6 个 prefab 与 `Assets/Resources/WeaponVisualCatalog.asset`。
- `WeaponVisualCatalog`：Id → prefab / AttackStyle / AttackDuration 的唯一映射；运行时删除 `WeaponVisualDescriptor` 与 `WeaponGeometry`，不再按 Id 拼几何体。
- `WeaponVisual`：只操作 prefab 根 Transform（跟随、瞄准、攻击姿态、闪光）；缺条目/缺 prefab 直接报错，不做白模兜底。
- `WeaponRackView` / `CompanionView`：从 catalog 取条目实例化；玩家四槽与旅伴锚点行为不变。

验证：独立工程副本 EditMode 219 / 219、PlayMode 5 / 5；`WeaponVisualCatalogTests` 5 条检查真实 `Weapon.json` 的每把武器都有 prefab、六种动作互异、近战动作盖住判定窗口、远程短于冷却；`ProjectileArrowTests` 1 条检查箭 prefab 的长轴与缩放；`WeaponVisualPlayTests` 新增弓箭搭箭窗口、“搭箭交接给投射物池”和箭视觉直线路径用例。6 个 prefab 在 Run 场景用局内正交相机渲染验收（侧视、俯视、四槽武器架、弓/箭近景）。

### 弓箭与投射物补完

- 新增 `Arrow.fbx`（Quaternius，CC0）；`WeaponPrefabBuilder` 同时生成 `Assets/Prefabs/Projectiles/Projectile_Arrow.prefab`（`ProjectileView` + 箭模型，长轴 +Z，由 `UnitPrefabMap` 引用）和弓 prefab 里的 `NockedArrow`。弓 prefab 的 `NockedArrow` 直接是 `Projectile_Arrow.prefab` 实例；开火时这个实例被摘下来交给 `ProjectileNockedHandoff`/`ProjectileViewPool`，飞出去的就是搭在弓上的那支箭，随后 `WeaponVisual` 用 `_nockedArrowPrefab` 复制新搭箭。
- `ProjectileView` 不再由池创建方块，改为实例化箭 prefab；保留按玩家/旅伴上色与缩放。
- `WeaponVisual` 在冷却最后 0.28 秒把搭箭后拉 0.20 米，`ShotsFired` 变化时收起并播放释放后坐；弓 prefab 的 `WeaponVisual._nockedArrow` 由 builder 绑定。
- `WeaponRackView` 的世界 yaw 用 `SmoothDampAngle` 追玩家朝向（0.16 秒），位置仍跟玩家根节点；转向时武器排平滑甩到新一侧，不再跟着角色瞬转。
- 修正弓的朝向：Quaternius 弓的弓弧在本地 XY 平面，Bow prefab 的 `Model` 序列化为 `-90°Y + 30°Z`，箭在弓平面内飞出，不再垂直于弓平面。方向配置归 prefab，builder 的 Force Rebuild 从已有 prefab 读回旋转。
