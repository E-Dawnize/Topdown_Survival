# Topdown Survival

2D 俯视角自动战斗生存。Unity 2022.3.34f1 + URP 2D。

玩法框架参考《绝区零》「怪奇旅伴」：波次战斗、金币、商店、旅伴养成。角色、数值、道具均为原创设定。

## 文档

- [游戏设计文档](Assets/docs/GDD.md)
- [技术方案](Assets/docs/TECH_DESIGN.md)
- [迭代方案](Assets/docs/ITERATION_PLAN.md)

## 环境

- Unity 2022.3.34f1c1
- 渲染：3D URP，正交斜视 2.5D（Assets/Settings 已配置）
- 待装：`com.unity.inputsystem` 1.7.0、`com.unity.cinemachine` 2.9.7、`com.unity.nuget.newtonsoft-json` 3.2.1
- 导表：Python 3.10（仅标准库）+ `tools/export_config.py`

## 本地运行

用 Unity Hub 打开本目录，启动 `Assets/Scenes/SampleScene.unity`。

`Library/`、`Temp/`、`Logs/`、`*.csproj`、`*.sln` 已被 git 忽略，克隆后由 Unity 重新生成。
