# Topdown Survival

2.5D 俯视角自动战斗生存。Unity 2022.3.34f1c1 + URP 3D（正交斜视）。

**一局 10 分钟：自动战斗 → 时间轴刷怪 → 捡经验三选一构筑 → 10:00 打 BOSS。** 没有波次、没有商店、没有金币。

## 文档

- [项目现状](Assets/docs/STATUS.md) ← 先看这个
- [游戏设计文档](Assets/docs/GDD.md)
- [构筑系统设计](Assets/docs/BUILD_SYSTEM.md)
- [技术方案](Assets/docs/TECH_DESIGN.md)
- [迭代方案](Assets/docs/ITERATION_PLAN.md)
- [迭代进度](Assets/docs/progress.md)
- [性能记录](Assets/docs/perf-log.md)

## 玩法要点

- 主菜单：Enter/Space/手柄 A 开始，Esc/手柄 B 退出；主菜单 → Run → 升级选卡 → 结算 → 回菜单/重开全流程已接通。
- 游戏内按 Esc / 手柄 Start 暂停，可继续游戏、返回主菜单或退出游戏。
- 单条时间轴：刷怪数量、怪种池、精英概率、血量/伤害/速度倍率随时间上升。
- 击杀掉经验，磁吸拾取；经验球按怪物品阶分色/分大小，升级暂停，三选一（10/20 级四选一），可重抽一次。
- E/RB 全屏吸取：立即收走场上所有经验球，12 秒冷却；回血掉落率 9%。
- 构筑四条主干：武器（最多 4 把，每把 5 级）、旅伴（最多 4 个，3 级 + 阵营羁绊 + 四种 AI）、数值卡、buff/机制道具。
- 机制道具：爆炸、链电、环绕刀、吸血、反伤；4 个标签组合（过载/血契/弩阵/旋风），不占槽位。
- 攻击可见：所有武器装备后都具备范围显示能力，攻击时短暂显示（近战扇形、远程目标锁定圈），待机隐藏；敌人近战/冲锋有起手预警环与出手红扇，投手蓄力后发射可见弹丸；碰撞/爆炸/链电/环绕/反伤都有范围环。
- 战斗反馈：怪物头顶血条、伤害飘字与暴击橙色大字；玩家受击有红色飘字和 HUD 伤害数字。
- HUD 左上装备栏显示当前武器与等级、旅伴与等级、组合、数值卡与 buff/道具名字；右下有全屏吸取冷却条；受击弹出本次伤害数字。
- 前期刷怪密度压低：0–45 秒 2.2 秒 1 只、同屏上限 12，之后逐步上升。
- 10:00 BOSS「缝线巨偶」：召唤小怪、半血狂暴；击杀即通关。
- 死亡/通关结算：存活时间、等级、击杀、武器/旅伴；Enter 重开、Esc 回主菜单。

## 美术

- 玩家/旅伴武器：六把武器各有一个 prefab，模型来自 Quaternius / KayKit 的 CC0 FBX；`WeaponVisualCatalog` 负责 Id → prefab/动作/时长，`WeaponVisual` 只做跟随、瞄准、攻击姿态和闪光。武器用 `SmoothDamp` 追玩家身后的槽位，像旅伴一样有拖尾；转向时武器架用 `SmoothDampAngle` 追角色朝向，不会跟着瞬转。近战挥砍、弓弩后坐、灵球脉冲由 `ShotsFired` 驱动。
- 弓箭：冷却最后 0.28 秒把 `NockedArrow` 拉回弓上，`ShotsFired` 时收起并播放释放后坐；投射物是 Quaternius `Arrow.fbx` 的 prefab，不再是方块。管线与许可见 `Assets/docs/WEAPON_ASSETS.md`。
- 地面：ambientCG PavingStones136（CC0 1.0），处理与平铺说明见 `Assets/Art/Textures/Ground/README.md`。
- 角色/敌人/旅伴仍是内置几何体 + 材质颜色。

## 环境

- Unity 2022.3.34f1c1
- 包：`com.unity.inputsystem` 1.7.0、`com.unity.cinemachine` 2.9.7、`com.unity.nuget.newtonsoft-json` 3.2.1（已写进 manifest，首次打开自动下载）
- 导表：Python 3.10，只用标准库

## 首次打开

1. Unity Hub 打开本目录，等包下载完、编译结束。
2. 打开 `Assets/Scenes/MainMenu.unity`，按 Play；也可以直接打开 `Assets/Scenes/Run.unity` 调试单局。
3. F1 开关调试覆盖层（仅编辑器/Development Build；发布构建不显示），应能看到配置版本与输入状态。

构建入口是 MainMenu，Boot 是旧场景，已在 Build Settings 中禁用。场景由手工维护，仓库里没有“重建全部场景”菜单。

## 配置表

数值全部来自 `Config/GameConfig.xlsx`（10 个 sheet：Character / Weapon / Companion / Enemy / Buff / Director / Level / Upgrade / Combo / Balance）。改完导出：

```
python tools/export_config.py
```

或在 Unity 里用菜单 `Tools/Game/导出配置表`。导表会校验 Id 唯一、引用存在、枚举合法、数值区间，任何一项不过都会指出「表 / 行 / 字段 / 原因」。

输出在 `Assets/StreamingAssets/Config/*.json`，这些 json 提交进 git，方便看数值 diff。

## 工具

| 脚本 | 用途 |
|---|---|
| `tools/make_config_template.py` | 重新生成 xlsx 模板（`--force` 覆盖） |
| `tools/export_config.py` | xlsx → json，带校验 |
| `tools/gen_meta.py` | 给新加的 Assets 文件补稳定 guid 的 .meta |
| `tools/verify.ps1` | 导表校验 + 四个工程编译；设置 `UNITY_EXE` 后可选跑 EditMode/PlayMode |
| `Tools/Weapons/Create Missing Visual Prefabs` | 只补缺失的武器/箭 prefab，不覆盖手调 prefab |
| `Tools/Weapons/Force Rebuild Visual Prefabs (overwrites)` | 由 FBX 强制重建全部 prefab + catalog，会覆盖 prefab 手调 |

## 目录

```
Assets/Scripts/Game/
  Runtime/
    Core/          AppRoot、RunComposition、RunLoop、RunClock、RunController、RunSimulation、事件总线
    Controllers/   PlayerController、BuildController（BuildState/DraftRoller/DraftDescriber）
    Systems/       DirectorSystem、BossFight、EnemySystem、SpatialGrid、PickupSystem、BuffSystem、
                   CompanionSystem（CompanionMovement/CompanionArmory/CompanionBonds）、武器与伤害
    Model/         RunState、Play/Enemy/CompanionModel、StatBlock、ExperienceCurve、UnitId
    Views/         HUD、升级、结算、主菜单、UiFactory、视图池、武器表现、相机
    Config/        配置库与 Def
    Services/      输入
  Editor/          导表入口、武器 prefab 生成器
  Tests/EditMode/  237 个用例；PlayMode 15 个（相机装配、武器 prefab 动作、弓箭搭箭/交接、箭直线路径、主菜单/暂停/结算流程）
Assets/Prefabs/Weapons/   Weapon_{Id}.prefab（WeaponVisual + 模型 + 闪光）
Assets/Resources/         WeaponVisualCatalog.asset
Assets/Prefabs/Projectiles/ Projectile_Arrow.prefab（由 UnitPrefabMap 引用）
Assets/Art/Models/Weapons/ CC0 FBX 与许可（README 记录来源）
Assets/Art/Materials/Weapons/ 闪光/灵球光晕/搭箭材质
Config/            GameConfig.xlsx
tools/             导表、模板、meta
```
