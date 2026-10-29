# Topdown Survival

2.5D 俯视角自动战斗生存。Unity 2022.3.34f1c1 + URP 3D（正交斜视）。

玩法框架参考《绝区零》「怪奇旅伴」：波次战斗、金币、商店、旅伴养成。角色、数值、道具均为原创设定。

## 文档

- [项目现状](Assets/docs/STATUS.md) ← 先看这个
- [游戏设计文档](Assets/docs/GDD.md)
- [技术方案](Assets/docs/TECH_DESIGN.md)
- [迭代方案](Assets/docs/ITERATION_PLAN.md)
- [迭代进度](Assets/docs/progress.md)
- [性能记录](Assets/docs/perf-log.md)

## 环境

- Unity 2022.3.34f1c1
- 包：`com.unity.inputsystem` 1.7.0、`com.unity.cinemachine` 2.9.7、`com.unity.nuget.newtonsoft-json` 3.2.1（已写进 manifest，首次打开自动下载）
- 导表：Python 3.10，只用标准库

## 首次打开

1. Unity Hub 打开本目录，等包下载完、编译结束。
2. 菜单 `Tools/Game/重建全部场景`，生成 Run / Test_Arena / Boot / MainMenu。
3. 打开 `Assets/Scenes/Run.unity`，按 Play。
4. F1 开关调试覆盖层，应能看到配置版本与输入状态。

场景由 `Assets/Scripts/Game/Editor/SceneBuilder.cs` 生成，不手工改场景文件。参数变了重跑菜单即可。

## 配置表

数值全部来自 `Config/GameConfig.xlsx`（8 个 sheet）。改完导出：

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

## 目录

```
Assets/Scripts/Game/Runtime/   Game.Runtime.asmdef
Assets/Scripts/Game/Editor/    Game.Editor.asmdef
Assets/Scripts/Game/Tests/     Game.Tests.EditMode.asmdef
Config/GameConfig.xlsx         配置表源文件
tools/                         导表与工具脚本
```

`Library/`、`Temp/`、`Logs/`、`*.csproj`、`*.sln` 已被 git 忽略，克隆后由 Unity 重新生成。
