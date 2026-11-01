# 项目现状

更新于 2026-11 · v0.3.4 攻击可见与 HUD 装备栏

## 一句话

一局 10 分钟：自动战斗、时间轴刷怪、经验升级三选一构筑、时间到打 BOSS。流程与数值通过命令行 + Unity Test Runner；完整 Play 流程仍未手测。

## 现在能做什么

- 主菜单进入 Run，立即开始单局。
- 玩家移动、冲刺、自动开火；武器独立索敌。
- 攻击可见：
  - 近战：挥砍弧线与实际扇形判定同半径/同半角，弧线从伤害窗口开始亮起；没有目标不空挥。
  - 碰撞伤害、爆炸、链电、环绕刀、反伤都有各自颜色的范围环。
  - 敌人近战/冲锋先起手，脚下红环收拢，结束才按锁定方向结算；投手蓄力后发射暗红色弹丸，弹丸飞到玩家身上才扣血。
  - 玩家受击时血条右侧弹出本次伤害数字，屏幕红闪保留。
- `DirectorSystem` 按时间轴刷怪：数量、怪种池、精英概率、同屏上限、血量/伤害/速度倍率随时间上升。
- 前期密度已压低：0–45 秒 2.2 秒 1 只、同屏上限 12；0:45–1:45 上限 20，避免开局清不动。
- 击杀掉经验，磁吸拾取后升级；升级暂停并弹三选一（10/20 级四选一），可免费重抽一次。
- 构筑四条主干：
  - 武器：6 把，最多 4 把，每把 5 级；Lv3 +1 弹道、Lv4 +1 穿透；每把都有 CC0 模型 prefab、拖尾跟随和攻击动作。
  - 弓箭：冷却末尾在弓上搭箭并后拉，放箭时收起；飞行物就是弓上搭的那支 `Projectile_Arrow` 实例：开火时从弓上摘下交给投射物池，随后在弓上复制新搭箭；视觉上从弓把沿直线飞向瞄准点，不再横向平移。
  - 旅伴：6 个，最多 4 个，每个 3 级；四阵营 2/4 件羁绊。
  - 数值：14 张，按层数叠 StatBlock。
  - 机制道具：5 张行为型道具（爆炸/链电/环绕刀/吸血/反伤）。
  - buff：12 张常驻/触发型，加 5 张机制道具共 17 张。
  - 组合：4 个标签组合（过载/血契/弩阵/旋风），不占槽位，HUD 会提示。
- 旅伴 AI：Follow / Aggro / Kiter / Guard 四种，受 LeashDistance 限制，不再只环绕玩家。
- 10:00 BOSS「缝线巨偶」出场，每 12 秒召唤小怪，半血狂暴；击杀即通关。
- 死亡/通关结算：存活时间、等级、击杀、武器/旅伴数量；Enter 重开、Esc 回主菜单。
- HUD：生命、等级/经验条、左上装备栏（武器/旅伴及等级、组合、数值卡数量、buff/机制道具名字）、威胁 + BOSS 倒计时、击杀/场上、BOSS 血条、冲刺 CD、受击伤害数字。

## 还没做

- Unity Play 下从主菜单到结算的整条流程、UI 排版、输入切换、字体、渲染帧率、实际手感仍需手测；Test Runner 只覆盖装配与规则。
- 角色特性 `TraitType` 未接；三个角色只有基础数值与起始武器差异。
- 敌人 `Shield / Split` 行为差异未实现；`Charge / Ranged` 已接可见攻击。
- 没有音频、没有局外解锁、没有无尽模式、没有存档。
- 没有角色美术；场景里只有地面换成了 CC0 石材贴图（ambientCG PavingStones136，处理后 2m 石板）。角色、敌人、旅伴仍是内置几何体 + 颜色；玩家与旅伴的 6 种武器已换成 CC0 模型 prefab（Quaternius / KayKit），带攻击动作与拖尾。

## 规模

| 分类 | 内容 | 规模 |
|---|---|---|
| 运行时代码 | 95 文件 | 约 13.2k 行 |
| 测试 | 35 文件 / 227 用例（222 EditMode + 5 PlayMode） | 约 6.3k 行 |
| 配置 | 10 张表 | 3 角色 / 6 武器 / 6 旅伴 / 8 敌人 / 21 buff / 8 导演段 / 30 级 / 43 构筑卡 / 4 组合 |
| 构筑卡 | 43 张 | 武器 6 / 旅伴 6 / 数值 14 / buff 与机制 17 |

## 架构与资产收口（v0.3.2 → v0.3.3）

- 对象图只有一份：`Core/RunComposition` 同时供 `AppRoot` 与测试夹具 `TestRun` 使用，构造参数不再两处漂移。
- 三个大类已拆：`CompanionSystem` → 花名册/`CompanionMovement`/`CompanionArmory`；`BuildController` → `BuildState`/`DraftRoller`/`DraftDescriber`；`DirectorSystem` 的 BOSS 战拆为 `BossFight`。
- 展示层收敛：`UiFactory`/`HudWidgets`/`PrimitiveViewFactory`；武器模型走 `WeaponVisualCatalog` + prefab，`WeaponVisual` 只做跟随/瞄准/姿态/闪光，不再在运行时拼几何体。
- 武器资产管线：`Assets/Art/Models/Weapons/`（CC0 FBX）→ `WeaponPrefabBuilder` → `Assets/Prefabs/Weapons/` + `Assets/Resources/WeaponVisualCatalog.asset`；加武器只改 builder 的 Spec 和配置表。详见 `WEAPON_ASSETS.md`。
- 投射物与弓：`Assets/Prefabs/Projectiles/Projectile_Arrow.prefab` 由 `UnitPrefabMap.ProjectileArrow` 引用（不再按 Resources 名字查）；弓 prefab 带 `NockedArrow`，`WeaponVisual` 在冷却末尾拉箭、放箭时收起，拉弓参数也在 Bow prefab 上；弓的 Model 旋转同样在 prefab 里，builder 只补缺失 prefab。
- 武器架转向：`WeaponRackView` 用 `SmoothDampAngle` 追玩家朝向，位置仍跟玩家，转向时武器排不会瞬移。
- 依赖方向：`UnitId` 下沉到 `Model`，`ViewSnapshot` 删除（改为 `PlayerModel.Forward` + 显式坐标），`RunLoop`/`PlayerView`/`CameraRig` 不再回查 `AppRoot.Instance`。
- 删除死代码与死事件：`UpgradeChosen`、`BuildChanged`、`DamageFlags.TriggerHit/ShowNumber`、`SpatialGrid.QueryNearest`、`HudValues.Merge/Equals`、`InputSnapshot.Aim/Skill/Pause`。

## 验证矩阵

已验证：

| 项 | 怎么验的 | 结果 |
|---|---|---|
| 编译 | 独立验证工程编译 Runtime + Editor + 全部测试源码 | 0 错 0 警告 |
| EditMode 全部用例 | Unity Test Framework（EditMode，工程副本） | 222 / 222 通过（2026-11-01，含敌人起手/冲锋/远程弹丸与碰撞范围表现） |
| PlayMode 装配 | Unity PlayMode 测试（工程副本） | 5 / 5 通过（相机死区、武器 prefab 动作、弓箭搭箭、搭箭交接、箭直线路径） |
| 真实配置 10 分钟 | 36007 步模拟，自动选卡 | 等级 30、击杀 1592、最高同屏 101、武器 4、旅伴 3、组合 1、buff 4，约 0.5 秒（托管侧） |
| 真实配置 BOSS | 10:00 切 Boss，清空血条 | 胜利收尾 |
| 抽卡规则 | 单元测试 | 不重复、槽位/满级/满层过滤、重抽次数、标签组合触发 |
| buff 触发 | 单元测试 | 击杀叠攻速/回血、受击叠甲、低血开关、周期回复、爆炸/链电/环绕/吸血/反伤、Reset 回滚 |
| 旅伴 AI | 单元测试 | Aggro 靠近、Kiter 后退、超出 Leash 回位 |
| 武器表现 | 单元 + PlayMode + 资产验收图 | catalog 覆盖 6 把武器、prefab 非空且带 WeaponVisual/Flash、6 种动作互异、近战动作盖住判定窗口、远程短于冷却、槽位不插身体、拖尾/转向跟随与冲刺夹紧、ShotsFired 触发挥砍并收势、弓箭搭箭、搭箭交接与箭 prefab；6 个 prefab 在 Run 场景渲染通过 |
| 经验曲线 | 单元测试 | 跨级、排队、满级停止 |
| 属性接线 | 单元测试 | 射程、护甲、回复、碰撞伤害 |
| 配置校验 | 单测加载真实 JSON | 10 张表、四类构筑卡、43 张卡带标签、4 个组合、最后一段带 BOSS |

未验证：

| 项 | 为什么没验 |
|---|---|
| 从主菜单到结算的整条流程 | 需要在 Unity Play 里手动走一遍；Test Runner 只覆盖装配与规则 |
| 升级面板 / BOSS 条 / HUD 排版 | 只有代码，未手测 |
| 帧率、draw call、GC | `perf-log.md` 是托管侧数字；武器从几何体换成模型后需要进 Play 重新看渲染开销 |
| 中文字形 | 依赖 Windows 系统字体 |
| 手感与平衡 | 移速/冲刺/刷怪曲线/BOSS 血量都只在纸面与测试里 |

## 已知问题

1. 敌人 `Shield / Split` 行为差异未实现：守卫目前按近战追击处理。
2. 角色特性未接：`TraitType` 是预留字段。
3. 旅伴 AI 只做直线目标点，不做寻路；大群阻挡时可能绕不理想。
4. 真实配置全流程测试里玩家血量/拾取范围拉满，只验证链路，不代表实际生存难度。
5. BOSS 固定 27900 血，没有按玩家 DPS 动态调整；熟练玩家可能秒杀。
6. 30 级满级后经验不再有用；终局最后 1 分钟没有额外目标。
7. 商店/波次代码已删除，旧文档（ARCHITECTURE_REVIEW / MODULE_REPORTS / DEMO_REPORT）保留为历史记录。
8. v0.3.4 已在独立工程副本跑 EditMode 222/222、PlayMode 5/5；主工程 Unity Play 的整条流程、近战弧线与敌人预警的实际观感、武器 draw call 仍未手测。

## 下一步

1. 在 Unity Play 里走一遍：主菜单 → 升级面板 → BOSS → 结算，顺带看 HUD 排版与武器/旅伴在实际相机下的比例。
2. 手调刷怪曲线与 BOSS 血量，跑 3 局不同构筑。
3. 接角色特性与 `Shield / Split` 敌人行为差异。
4. 补构筑卡牌面（图标/数值变化对比）与音效。
5. 评估是否要把 BOSS 血量做成按 DPS 回归。

## 仓库

`feat/demo` 分支。配置源 `Config/GameConfig.xlsx`，导出 `Assets/StreamingAssets/Config/*.json`，导表 `python tools/export_config.py`。
