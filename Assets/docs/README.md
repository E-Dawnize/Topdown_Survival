# 文档总目录

这是 `Assets/docs` 的唯一入口。先看这里，再按角色和文档状态选择。

> 当前事实以 [`STATUS.md`](STATUS.md)、[`GDD.md`](GDD.md) 与 [`BUILD_SYSTEM.md`](BUILD_SYSTEM.md) 为准。`DEMO_REPORT.md`、`MODULE_REPORTS.md`、`ARCHITECTURE_REVIEW.md` 和 `progress.md` 的早期章节是历史快照，保留当时口径。

## 1. 先看什么

### 新加入项目

1. [`../../README.md`](../../README.md)：环境、启动方式、导表与工具。
2. [`STATUS.md`](STATUS.md)：当前能做到什么，哪些还没验证。
3. [`GDD.md`](GDD.md)：目标玩法、内容规模和设计红线。
4. [`BUILD_SYSTEM.md`](BUILD_SYSTEM.md)：武器/旅伴/数值/buff 四类构筑、抽卡算法与节奏。
5. [`TECH_DESIGN.md`](TECH_DESIGN.md)：当前技术方案与架构约束。
6. [`ITERATION_PLAN.md`](ITERATION_PLAN.md)：迭代拆分、验收标准和范围控制。
7. [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md)：历史架构审查与 P0/P1 修复记录。

### 策划与数值

1. [`GDD.md`](GDD.md)
2. [`BUILD_SYSTEM.md`](BUILD_SYSTEM.md)
3. [`ITERATION_PLAN.md`](ITERATION_PLAN.md) 的历史 I3 / I4 / I5 参考
3. [`perf-log.md`](perf-log.md) 中与性能预算相关的约束
4. [`STATUS.md`](STATUS.md) 的“不能做什么”和“已知问题”

### 程序

1. [`TECH_DESIGN.md`](TECH_DESIGN.md)
2. [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md)
3. [`MODULE_REPORTS.md`](MODULE_REPORTS.md) 了解各模块职责和已知偏差
4. [`deep-dive/`](deep-dive/) 中的实现说明

### 验收与审查

1. [`STATUS.md`](STATUS.md) 的验证矩阵
2. [`ITERATION_PLAN.md`](ITERATION_PLAN.md) 的每个迭代门禁
3. [`perf-log.md`](perf-log.md) 的测量条件
4. [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md) 的验收清单和未完成项

## 2. 文档清单

| 文档 | 类型 | 状态 | 负责内容 |
|---|---|---|---|
| [`../../README.md`](../../README.md) | 项目入口 | 当前 | 环境、启动、导表、目录 |
| [`STATUS.md`](STATUS.md) | 当前事实 | 当前 | 可玩范围、验证矩阵、已知问题、下一步 |
| [`ARCHITECTURE_REVIEW.md`](ARCHITECTURE_REVIEW.md) | 审计与修复记录 | 历史（v0.2） | 架构缺陷、P0/P1 修复、扩展边界、验收清单 |
| [`GDD.md`](GDD.md) | 设计规范 | 目标 | 单局时间轴、经验、构筑、敌人、BOSS、UI 目标 |
| [`BUILD_SYSTEM.md`](BUILD_SYSTEM.md) | 设计规范 | 目标 | 四类构筑、抽卡权重、等级节奏、协同与验收 |
| [`TECH_DESIGN.md`](TECH_DESIGN.md) | 技术规范 | 当前意图 | 架构、配置、模拟、渲染、相机、性能预算 |
| [`ITERATION_PLAN.md`](ITERATION_PLAN.md) | 计划 | 路线图 | 迭代目标、任务、验收、砍项顺序 |
| [`progress.md`](progress.md) | 迭代记录 | 历史 + 当前追加 | I0/I1/I2 当时进度，文末为 P0/P1 修复记录 |
| [`MODULE_REPORTS.md`](MODULE_REPORTS.md) | 模块快照 | 历史快照 | feat/demo 交付时的模块职责、测试分布、已知问题 |
| [`DEMO_REPORT.md`](DEMO_REPORT.md) | 交付报告 | 历史快照 | feat/demo 的范围、证据、修复和未验证项 |
| [`perf-log.md`](perf-log.md) | 测量数据 | 数据 | 模拟耗时、分配、空间哈希 bench、待测项 |
| [`deep-dive/`](deep-dive/) | 实现说明 | 参考 | 主循环、空间哈希、属性伤害、武器、视图、UI 输入 |

## 3. 冲突时以谁为准

按以下顺序判断：

1. 当前代码与测试：实现的最终事实。
2. `STATUS.md` 与 `ARCHITECTURE_REVIEW.md` 的当前修复记录：当前已知状态。
3. `GDD.md`、`BUILD_SYSTEM.md` 与 `TECH_DESIGN.md`：目标设计和当前技术意图。
4. `ITERATION_PLAN.md`：计划与验收口径。
5. `progress.md`、`MODULE_REPORTS.md`、`DEMO_REPORT.md`：历史快照，不作为当前事实。

如果规范与实现不一致，应在 `STATUS.md` 或 `ARCHITECTURE_REVIEW.md` 记录偏差，再决定改代码还是改规范。不要让历史报告承担“当前状态”的职责。

## 4. 目录结构

```text
README.md                         项目入口
Assets/docs/
  README.md                       本目录，唯一文档入口
  STATUS.md                       当前状态
  ARCHITECTURE_REVIEW.md          架构审查与 P0/P1 修复记录
  GDD.md                          设计规范
  BUILD_SYSTEM.md                 构筑系统设计
  TECH_DESIGN.md                  技术规范
  ITERATION_PLAN.md               迭代计划
  progress.md                     迭代历史 + 当前修复追加
  MODULE_REPORTS.md               模块快照（历史）
  DEMO_REPORT.md                  交付报告（历史）
  perf-log.md                     性能数据
  deep-dive/
    01-loop-and-interpolation.md
    02-spatial-grid.md
    03-stats-and-damage.md
    04-weapons-and-projectiles.md
    05-view-layer.md
    06-ui-and-input.md
```

## 5. 维护规则

- 每次修完会影响可玩性、架构边界或验证结论的缺陷，更新 `STATUS.md` 的当前状态和验证矩阵。
- 结构性修复追加到 `ARCHITECTURE_REVIEW.md` 的“本轮 P0/P1 修复记录”，不要覆盖审查时的证据。
- 迭代过程追加到 `progress.md`，已完成的旧章节保持原样，避免把历史改成“早就知道”。
- `MODULE_REPORTS.md` 和 `DEMO_REPORT.md` 是交付快照；需要更新时先写新的当前状态，不要直接在旧快照上改数字。
- `perf-log.md` 只记录可复现的测量：机器、场景、样本数、预算和结论。
- `deep-dive/` 是实现说明，不替代 `TECH_DESIGN.md`；设计约束仍以技术规范为准。
- 新增文档先登记到本目录，再在根 `README.md` 放一条入口链接。

## 6. 当前文档缺口

- 完整 MainMenu → Run → 商店 → 结算 → 重开仍未在 Unity PlayMode 验证。
- `Test_Arena` 的相机边界、玩家边界和场景墙体仍是三份数据，尚未统一为按场景的 StageProfile。
- `CameraPreset_Boss` / `CameraPreset_Menu` 已有配置，但相机多模式切换尚未接线。
- `MODULE_REPORTS.md` 的测试分布是当前 196 用例的更新版，但模块文字仍保留 feat/demo 时期的部分判断；以 `STATUS.md` 和 `ARCHITECTURE_REVIEW.md` 为准。
