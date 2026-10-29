# 技术方案 v0.2

配套文档：[游戏设计文档](GDD.md) · [迭代方案](ITERATION_PLAN.md)

Unity 2022.3.34f1c1（中文版）/ URP 14.0.11

---

## 0 版本变更

v0.2 依据决策重写，与 v0.1 的差异：

| 项 | v0.1 | v0.2（已确认） |
|---|---|---|
| 渲染 | 切 URP 2D Renderer | **保持 3D URP，正交斜视做 2.5D** |
| 美术单元 | Sprite | **3D 简单几何体** |
| 相机 | 手写跟随 + 震屏 | **Cinemachine** |
| 场景 | 无边界 | **有边界的大竞技场** |
| 架构 | 分层（模拟/表现） | **MVC + 事件总线** |
| 配置 | ScriptableObject | **Excel → JSON → POCO** |
| ECS | 否决 | **留待实测，写清触发条件** |
| 武器 | 只提 `WeaponInstance` | **弓箭 / 砍刀两套行为 + 可扩展行为接口 + buff 预留** |

v0.1 里仍然有效的部分：主循环顺序、空间哈希、伤害管线、属性修正、随机分三流、对象池策略、性能预算方法。

## 1 目标与非目标

目标：

1. 1080p 60fps，同屏 200 敌人 + 400 投射物 + 300 粒子。
2. 数值全在 Excel 里，改数值只跑一次导表，不重编译、不打开 Unity。
3. 逻辑可脱离场景在 EditMode 测试（伤害、属性、抽卡、波次、buff 叠算）。
4. 一局可复现：固定种子 + 固定输入序列 = 相同结果。
5. 武器与 buff 是数据驱动的可扩展模块，加一把新武器只写一个行为类、填一行表。

非目标（本版本不做）：联机、回放、移动端、Addressables 分包、自定义后处理、本地化多语言。

## 2 世界与渲染

### 2.1 坐标约定

- 玩法平面是 **XZ 平面**，Y 是高度。地面在 `y = 0`。
- 1 单位 = 1 米。玩家移速 5 米/秒。
- 所有实体根节点贴地（`y = 0`），高度不超过 1 米。玩家胶囊 1.0 米高，普通敌人 0.6 米。
- 所有距离判定只算 XZ，忽略 Y。写一个 `Vector2 XZ(Vector3)` 的扩展方法，所有模拟层代码只用 Vector2。

为什么不是 XY：3D 场景里用 XZ 做地面平面，方向光、阴影、地面材质都能自然工作；XY 平面在 3D 场景里会让"地面"变成一堵墙，光照和阴影全要造假。

### 2.2 相机

两台相机，都归一个 `CinemachineBrain` 管。

| 相机 | 用途 | 参数 |
|---|---|---|
| `vcam_Follow` | 日常跟随 | 正交 size 7，旋转 `(50, 0, 0)`，Transposer 跟随偏移 `(0, 23, -19.3)`，Aim = None，X 轴死区 2 米 / Y 轴死区 0 |
| `vcam_Boss` | BOSS 战拉远 | 正交 size 10，跟随 BOSS 与玩家的中点 |
| `vcam_Menu` | 结算/商店静帧 | 固定在场地中心上方 |

正交而不是透视，理由：判定在平面上，正交的远近不缩放，视觉和判定一致；透视会让屏幕边缘的敌人看起来更近、实际判定更远，玩家会觉得"明明打到了却没打到"。斜视角度 50° 给一点立体感，同时地面占屏比例够大。

视角带来的遮挡问题：50° 斜视下，高于地面的物体向上投影约 `高 × 0.77`。因此规则是"所有实体都不许高过 1 米，任何高大装饰物（树、柱子）只能放在场地边缘且不参与战斗"。

震屏走 Cinemachine Impulse：`CinemachineImpulseSource` 挂在攻击者上，`GenerateImpulse(强度)`。普通命中不震，暴击 0.1、BOSS 受击 0.2、BOSS 落地 0.4。

相机位置的边界 clamp 自己做，约 30 行，不用 `CinemachineConfiner3D`。原因：正交相机的 confiner 在 2.9 里对 bounding volume 的贴合计算是按透视 FOV 推的，正交下边缘会穿出场地，试过 Confiner3D 的项目最后都改成手 clamp。

### 2.3 光照与材质

- 一盏方向光，`rotation (50, -30, 0)`，与相机同向偏移，软阴影，阴影距离 40 米。
- 材质只允许三种：
  - `MAT_Lit`（URP/Lit，Smoothness 0.1）：玩家、敌人、旅伴、地面。受光 + 投影，有体积感。
  - `MAT_Unlit`（URP/Unlit）：投射物、特效、拾取物、UI 世界元素。不受光，保证可读性。
  - `MAT_Ground`（URP/Lit，带网格纹理）：地面，一张平铺的网格贴图让移动有参照。
- 颜色区分靠材质颜色参数，靠 `MaterialPropertyBlock` 而不是给每个对象 new 材质。用 MPB 时 SRP Batcher 不会断，`renderer.SetPropertyBlock` 每帧一次。
- 后处理：Bloom（阈值 1.1）+ Vignette（0.25）+ 轻微 Color Adjustments。低画质档关 Bloom。
- 三档 Quality 目前各自引用一个 URP asset（HighFidelity / Balanced / Performant）。这三份配置要在迭代 0 对齐（阴影、后处理、MSAA 一致），否则切画质时视觉会跳。

### 2.4 渲染顺序与可读性

3D 场景没有 Sorting Layer 那套便利，靠这几条保证读图：

1. 玩家永远在敌人上方可见：玩家材质开 `ZTest Always` 的描边子材质（一个二次渲染的壳，放大约 5%），或者简单点用 Outline 后处理。初版选前者，实现便宜。
2. 敌方投射物用高饱和冷色，玩家投射物用暖色，两者不共用色板。
3. 地面网格让"移动"可见，避免纯色地面看不出速度。
4. 所有实体底部加一个 0.05 米厚的深色圆盘/方块接触阴影，关掉真实阴影也能看出落点。真实阴影保留，两者叠加不冲突。

## 3 场景与边界

竞技场 `160 × 160` 米，玩家的有效战斗区 `140 × 140`，四周留 10 米缓冲。

- 逻辑边界：`PlayerSystem` 和敌人 AI 出口处 clamp `[-70, 70]`。超出直接夹回，不做反弹。
- 视觉边界：场地外一圈 8 米宽的暗色渐隐带（一张环形网格 + 渐隐材质），再外面是纯黑。玩家能看见"到头了"，但看不清外面有什么。
- 相机不越界：相机目标点 clamp 到 `[-52, 52]`，保证正交视野（24.9 × 18.3 米）不会露出场地边缘之外。
- 刷怪永远在相机视野之外、场地之内：以玩家为中心的环形区域，半径 14–18 米，角度随机，超出场地边界的候选点直接重掷（最多 8 次，都失败就允许刷在场内边缘）。

为什么是 160 米：射程 6 米、移速 5 米/秒、一波 180 秒。玩家横穿场地需要 32 秒，够大，但同时不至于让"跑图"变成主要玩法。这个数值在迭代 1 用实际手感校准。

## 4 输入

新 Input System，`InputActionAsset` 一个，两个 Map：

| Map | Action | 类型 | 键位 |
|---|---|---|---|
| Player | Move | Value/Vector2 | WASD / 左摇杆 |
| Player | Dash | Button | Space / A |
| Player | Skill | Button | E / RB |
| Player | Aim | Value/Vector2 | 鼠标 / 右摇杆 |
| Player | Pause | Button | Esc / Start |
| UI | Navigate / Submit / Cancel / Point | — | 标准 UI action |

- `InputService` 是唯一接触 `InputActionAsset` 的类，对外只暴露 `Vector2 MoveAxis`、`bool DashPressed`、`Vector2 AimDirection` 等属性。模拟层永远不引用 `UnityEngine.InputSystem`。
- 采样在 `RunLoop` 第 1 步，一帧只采一次，写进 `InputSnapshot` struct。
- UI 与游戏 action 分 Map，进商店时切 Map，避免战斗中按 E 触发 UI。

## 5 架构：MVC + 事件总线

### 5.1 三层职责

| 层 | 内容 | 允许 | 禁止 |
|---|---|---|---|
| **Model** | `RunState`、`UnitModel`、`StatBlock`、`BuffContainer`、`ConfigDatabase`、`Rng` | 纯 C#，只用 `UnityEngine.Mathf/Vector2` | `MonoBehaviour`、`Transform`、`Instantiate`、`GetComponent`、访问 UI |
| **View** | `UnitView`、`HudView`、`ShopView`、`DamageNumberView`、`AudioView`、`VfxSpawner` | 订阅事件、读 Model、改自己的显示状态 | 写 Model、调用系统方法、反向发命令（只能通过 Controller 暴露的命令接口） |
| **Controller** | `InputController`、`PlayerController`、`CombatController`、`WaveController`、`ShopController`、`RunController` | 调 Model 的方法、发事件、发命令 | 持有 View 的具体类型引用、直接改 `Transform` |

装配只在一处：`AppRoot`（场景里的一个空物体）持有一切对象的构造与注入。除此之外没有任何地方 new 系统或查单例。

### 5.2 依赖方向

```
        ┌─────────────┐
        │  AppRoot    │  装配，唯一知道全部的地方
        └──────┬──────┘
               │ 注入
   ┌───────────┼─────────────┐
   ▼           ▼             ▼
Controller ──▶ Model ◀──── View
   │            │             ▲
   └──事件总线──┴──事件总线────┘
```

允许 `Controller → Model`、`View → Model(只读)`、`Model →(事件)→ View`。
禁止 `Model → Controller`、`Model → View`、`View → Controller(直接调用)`。

View 想表达用户意图（点击购买），走 `View → Controller` 的命令接口，这个接口由 AppRoot 注入给 View 的一个接口引用（`IShopCommands`），不是具体的 `ShopController` 类型。

### 5.3 事件总线

```csharp
public interface IEventBus {
    void Subscribe<T>(Action<T> handler) where T : struct;
    void Unsubscribe<T>(Action<T> handler) where T : struct;
    void Publish<T>(in T evt) where T : struct;
    void Clear();
}
```

实现用 `Dictionary<Type, IChannel>`，每个 `Channel<T>` 内部是 `List<Action<T>>`，遍历时用索引 for 循环并支持反注册（标记删除，遍历结束统一清理）。

规则：

1. 事件必须是 `readonly struct`。class 事件会带来堆分配和"发布后又被修改"的问题。
2. 事件定义集中在 `Core/Events/RunEvents.cs`，一个文件看完所有事件。
3. 订阅在 `OnEnable`，退订在 `OnDisable`；纯 C# 对象在 `Dispose` 里退订。漏退订是这类架构唯一的真实事故来源，所以 `EventBus` 在开发模式下统计订阅数，退出局内时断言订阅数归零。
4. 发布只由 Controller 和 Model 内部的系统发，View 不发业务事件。
5. 一帧内的事件统一在 `RunLoop` 第 11 步分发（`_pending.PublishAll()`），不是当场触发。这样遍历中途改集合的问题不会出现。

首版事件清单：

```csharp
readonly struct UnitDamaged  { UnitId Target; float Amount; bool IsCrit; DamageType Type; }
readonly struct UnitDied     { UnitId Unit; UnitKind Kind; Vector2 Position; UnitId Killer; }
readonly struct CoinChanged  { int Current; int Delta; }
readonly struct CoinPicked   { int Value; Vector2 Position; }
readonly struct WaveStarted  { int Index; WaveDefSnapshot Def; }
readonly struct WaveCleared  { int Index; }
readonly struct WeaponFired  { UnitId Owner; WeaponId Weapon; int ProjectileCount; }
readonly struct ItemAcquired { ItemId Id; int NewCount; }
readonly struct ShopOpened   { int WaveIndex; }
readonly struct ShopClosed   { }
readonly struct PlayerDied   { Vector2 Position; }
readonly struct RunEnded     { RunSummary Summary; }
```

### 5.4 用一次冲刺验证架构

按 E 冲刺的完整链路，如果这条链路能跑通，架构就没有明显断裂：

```
InputService 采样 → InputSnapshot{DashPressed}
  → InputController 读快照 → PlayerController.TryDash()
    → PlayerModel.StartDash()（改状态位、写速度、扣 CD）
      → EventBus.Publish(PlayerDashed{...})
        → DashVfxView 订阅 → 播残影
        → AudioView 订阅 → 播音效
        → HudView 订阅 → CD 环开始转
```

注意 `PlayerController` 不知道任何 View 的存在，`PlayerModel` 也不。加一个新表现（比如屏幕闪白）只改 View 层订阅。

## 6 配置管线

### 6.1 为什么不用 ScriptableObject

Excel 是唯一真源。策划改一个数、跑一次导表、重启游戏就能看到，不需要打开 Unity、不需要拖引用、不需要处理「SO 之间的引用断了」。代价是失去了 Inspector 的引用校验，用导表校验器补回来。

### 6.2 表结构规范

一份 `Config/GameConfig.xlsx`，多个 sheet，每个 sheet 对应一个运行时类型。

| 行 | 内容 |
|---|---|
| 1 | 字段名（PascalCase，与 C# 属性名一致） |
| 2 | 类型（见 6.3） |
| 3 | 中文说明（导表忽略） |
| 4+ | 数据 |

约定（违反就导表报错）：

- 第一列必须是 `Id`，字符串，全表唯一。
- 不允许合并单元格、不允许公式、不允许空行夹在数据中间。
- 布尔写 `true/false`，不写 `1/0`。
- 枚举写名字（`Physical`），不写数字。
- 引用其他表写目标 `Id`（`Weapon.Arrow` 或直接 `Arrow`，两者都接受）。
- 多值用 `|` 分隔（`Fire|Curse`），列表语义。

首版 sheet：

| sheet | 对应类型 | 何时用 |
|---|---|---|
| `Character` | `CharacterDef` | 探梦者 |
| `Weapon` | `WeaponDef` | 武器 |
| `Companion` | `CompanionDef` | 旅伴 |
| `Item` | `ItemDef` | 道具 |
| `Enemy` | `EnemyDef` | 敌人 |
| `Wave` | `WaveDef` | 波次 |
| `Buff` | `BuffDef` | buff（迭代 5 前留空表） |
| `Balance` | `BalanceDef` | 全局常量（伤害系数、价格曲线、成长率） |

### 6.3 类型系统

| 写法 | C# 类型 | 说明 |
|---|---|---|
| `int` `float` `bool` `string` | 同名 | 直接映射 |
| `enum:DamageType` | 对应枚举 | 导表时校验名字合法 |
| `id:Weapon` | `string` | 引用校验，导表时确认目标存在 |
| `list:id:Item` | `string[]` | 列表引用，逐个校验 |
| `list:float` | `float[]` | |
| `vec2` | `Vector2` | 写 `1.5,2` |
| `color` | `Color` | 写 `#RRGGBB` |

### 6.4 导表脚本

`tools/export_config.py`，只用 Python 标准库（`zipfile` + `xml.etree.ElementTree` 解析 xlsx）。

为什么不用 openpyxl：本机没装，而且 Excel 的 xlsx 就是 zip 包 XML，读文本单元格 200 行代码够用，换机器不用配环境。代价是不支持公式与合并单元格——这两条本来就在 6.2 的禁止列表里。

流程：

```
Config/GameConfig.xlsx
  → python tools/export_config.py
    → 校验（字段完整性、引用、枚举、数值区间、唯一性）
    → Assets/StreamingAssets/Config/*.json（每 sheet 一个）
    → Assets/StreamingAssets/Config/manifest.json（表名 + 版本 hash + 导表时间）
```

导表失败时返回非 0 退出码，并在控制台列出「表 / 行 / 字段 / 原因」。CI 或 Editor 菜单里都能调。

### 6.5 运行时加载

```csharp
public sealed class ConfigDatabase {
    public IReadOnlyDictionary<string, CharacterDef> Characters;
    public IReadOnlyDictionary<string, WeaponDef> WeaponDefs;
    // ...

    public static ConfigDatabase LoadFrom(StreamingAssetsReader reader);
    public void Validate();   // 引用完整性 + 区间，开发期直接抛异常
}
```

- JSON 反序列化用 `com.unity.nuget.newtonsoft-json`（3.2.1）。不用 `JsonUtility`：它不支持字典、不支持字符串写枚举、不支持可空类型。
- 类型是纯 POCO，字段名与 Excel 字段名一致，加 `[JsonProperty]` 保证大小写不敏感。
- 加载在 Boot 场景，加载完 `Validate()`，失败直接弹错误面板并阻止进游戏（开发期），发版时降级为警告 + 用默认值。

### 6.6 资源引用

数值走 Excel，美术资源不走。约定：

- 实体预制体按 Id 命名，放在 `Assets/Prefabs/Units/{Kind}/{Id}.prefab`。
- 运行时用一张 `ResourceMap` SO 把 Id → Prefab 建索引，Boot 时加载。这张表小、变动少，用 SO 比塞进 Excel 合理。
- 图标按 Id 命名放 `Assets/Art/Icons/{Id}.png`，走 `Addressables`？不做，初版用图集 + 直接引用。

这样划分的好处：策划改数值不用碰 Unity，美术加资源需要程序加一条映射，频率不同，不冲突。

## 7 模拟核心

### 7.1 主循环

```
RunLoop.Update()
  1  InputService.Sample() → InputSnapshot
  2  RunController.Tick(dt)           // 局状态、计时、暂停判断
  3  PlayerController.Tick(dt)
  4  CombatController.Tick(dt)        // 武器、投射物、伤害结算
  5  CompanionSystem.Tick(dt)
  6  EnemySystem.Tick(dt)
  7  SpatialGrid.Rebuild()
  8  PickupSystem.Tick(dt)
  9  WaveController.Tick(dt)
  10 Model.Cleanup()                  // 处理死亡、移除、事件入队
  11 EventBus.PublishPending()
  12 UnitView.SyncAll()
```

模拟固定步长 1/60，每帧最多 3 步，渲染不插值。顺序里三处不能动：空间哈希在移动之后（否则子弹打上一帧的位置）、清理在遍历之后（否则边遍历边删）、事件在整轮之后（否则订阅方读到半更新的状态）。

### 7.2 空间哈希

格子边长 2 米，网格 32×32 覆盖 64 米，跟随玩家滚动，滚动时整表重建。每格 `List<int>` 预分配 8。敌人索引分配固定槽位，移除用末尾交换，O(1)。

200 单位的重建开销预计 0.1ms 量级，迭代 1 实测。

### 7.3 属性与修正

```csharp
public enum StatType { MaxHP, Regen, Armor, Dodge, MoveSpeed, Attack, AttackSpeed,
                       CritChance, CritDamage, ProjectileCount, Range, PickupRadius, Luck, Treasure }

public enum ModOp { Flat, Percent, Multiply }   // 加了 Multiply 给"独立乘区"用

public readonly struct StatModifier {
    public readonly StatType Stat;
    public readonly float Value;
    public readonly ModOp Op;
    public readonly object Source;      // 按来源批量移除
}

public sealed class StatBlock {
    public float this[StatType t] { get; }
    public void Add(in StatModifier m);
    public void RemoveBySource(object source);
    // final = (base + Σflat) × (1 + Σpercent) × Πmultiply，Dodge/CritChance 做 clamp
}
```

三个乘区：`Flat` 先加、`Percent` 相加后乘一次、`Multiply` 每个各乘一次。大多数道具用前两个，少数"独立增伤"用第三个。所有属性必须走这里，禁止直接改 `HP` 之类的最终值。

### 7.4 伤害管线

```csharp
public readonly struct DamageInfo {
    public readonly float Amount;
    public readonly DamageType Type;
    public readonly UnitId Source;
    public readonly Vector2 Direction;
    public readonly float Knockback;
    public readonly bool IsCrit;
    public readonly DamageFlags Flags;   // 是否可闪避、是否触发受击、是否显示数字
}
```

结算顺序固定：`闪避 → 无敌帧 → 护甲减伤 → 扣血 → 受击触发 → 死亡`，死亡事件入队延后处理。

暴击在开火瞬间结算一次并写进 `DamageInfo`，不能到命中时再算——一发穿透箭打三个目标如果各自暴击，伤害数字会莫名其妙。

### 7.5 随机

`xorshift128` 三条流：`runRng`（战斗与商店）、`metaRng`（局外）、`fxRng`（音调、粒子角度）。表现层永远不碰 `runRng`，否则加个粒子效果就能改变整局结果。

### 7.6 对象池

```csharp
public sealed class Pool<T> where T : Component {
    public T Rent();     // 到 hardCap 返回 null，调用方必须处理
    public void Return(T item);
}
```

`Rent` 返回 null 而不是扩容是刻意的：池被打爆要在 DebugOverlay 里看见，而不是悄悄吃掉内存再掉帧。

## 8 武器系统

### 8.1 三层

```csharp
// 1. 定义（来自 Excel，只读）
public sealed class WeaponDef {
    public string Id;
    public WeaponKind Kind;         // Ranged / Melee
    public float BaseDamage;
    public float Cooldown;
    public float Range;
    public int Projectiles;
    public float SpreadDeg;
    public string BehaviourType;    // 行为类名，见 8.4
    public float[] Params;          // 行为自定义参数（弧度、穿透数等）
}

// 2. 运行时（每个持有者一份）
public sealed class WeaponRuntime {
    public WeaponDef Def;
    public IWeaponBehaviour Behaviour;
    public BuffContainer Buffs;
    public float Cooldown;
    public void Tick(float dt, in WeaponContext ctx);
}

// 3. 行为（策略，加新武器只加一个类）
public interface IWeaponBehaviour {
    void OnEquip(WeaponRuntime w, UnitModel owner);
    void Tick(WeaponRuntime w, float dt, in WeaponContext ctx);
    void OnUnequip(WeaponRuntime w);
}
```

`WeaponContext` 携带 owner 位置、朝向、目标查询接口、投射物生成接口、伤害结算接口。行为类只通过这些接口做事，拿不到 MonoBehaviour。

### 8.2 弓箭（RangedBehaviour）

```
冷却归零 → QueryNearest(射程, 优先级) → 朝向目标
  → 计算 n 发弹道，扇形展开 (n-1) × SpreadDeg
  → 每发单独结算暴击，写入 DamageInfo
  → 生成投射物（速度、穿透数、生命周期从 Params 读）
  → 发 WeaponFired 事件
```

投射物自己管移动与碰撞，命中后由 `DamageResolver` 结算，命中数达到穿透上限就回收。弓箭的初版参数：伤害 8、冷却 0.85 秒、射程 8 米、1 发、速度 18 米/秒、穿透 1。

### 8.3 砍刀（MeleeBehaviour）

近战不走投射物，用扇形判定：

```
冷却归零 → 记录挥砍起始角与结束角（按攻击方向 ±45°）
  → 在挥砍的伤害窗口内（0.08s ~ 0.22s）对扇形内敌人判定一次
  → 每个敌人一次挥砍只结算一次（记录 hit 集合，用位集或 List<int>，挥砍结束清空）
  → 结算伤害 + 击退
```

砍刀初版参数：伤害 15、冷却 0.6 秒、半径 2.2 米、扇角 90°、击退 2 米。

近战的坑：`Cooldown 0.6` 而伤害窗口 0.14 秒，如果按冷却触发判定，攻速加成会让伤害凭空变高，所以判定必须绑定动画窗口的时间戳，不能绑定冷却。初版没有动画，就用"挥砍开始后 0.08s 判定一次"的固定延迟。

### 8.4 扩展点（为 buff 系统预留）

加武器不改框架，只需要：

1. 新建 `IWeaponBehaviour` 实现类（比如 `ReturningBoomerangBehaviour`）。
2. `BehaviourType` 列填类名，`Params` 填参数。
3. 反射实例化（开发期）或预先注册的工厂字典（发布版，避免 IL2CPP 裁剪掉类）。

为 buff 留的三个口子：

```csharp
// 口子 1：伤害修饰链，在 DamageResolver 之前跑
public interface IDamageModifier {
    int Priority { get; }
    void Modify(ref DamageInfo dmg, in DamageContext ctx);
}

// 口子 2：武器级事件，buff 可以挂在这里
public interface IWeaponHook {
    void OnFire(WeaponRuntime w);
    void OnHit(WeaponRuntime w, UnitId target, ref DamageInfo dmg);
    void OnKill(WeaponRuntime w, UnitId target);
}

// 口子 3：buff 容器，武器和携带者各有
public sealed class BuffContainer {
    public void Add(BuffDef def, UnitId source, int stacks);
    public void Remove(string buffId);
    public void Tick(float dt);
    public bool Has(string tag);
}
```

迭代 5 接 buff 系统时，`BuffDef` 的效果类型只做四种：`StatModifier`（走 7.3）、`PeriodicDamage`、`OnHitTrigger`、`OnKillTrigger`。这四种覆盖 GDD 里 18 件道具的触发型需求，剩下的特殊道具用专门的行为类。

## 9 性能与 ECS 决策条件

### 9.1 预算

设计目标，迭代 1 结束时逐项打表核对，对不上就重新设计，不"先这样后面优化"。

| 项 | 目标 | 测量 |
|---|---|---|
| 帧时间 | ≤ 16.6ms | DebugOverlay 滑动平均 |
| 模拟 tick 总耗时 | ≤ 3ms | 各系统 Stopwatch 累加 |
| 渲染 | ≤ 8ms | Profiler |
| Draw call | ≤ 40（3D 场景比 2D 多，含阴影 pass） | Frame Debugger |
| 同屏敌人 | 200 | 场景统计 |
| 活跃 AudioSource | ≤ 16 | AudioBus |
| 模拟层 GC | 0 B/帧 | Profiler GC Alloc |
| 峰值内存 | ≤ 500MB | Profiler |

### 9.2 什么时候才考虑 ECS

初版不引入 ECS。触发评估的硬条件，两条同时满足才开会讨论：

1. 同屏敌人稳定 ≥ 600，或模拟 tick 耗时 ≥ 6ms 且已排除自身算法问题（Profiler 确认热点在敌人位移与索敌）。
2. 已有方案（批量位移、降低索敌频率、空间哈希分级）都把帧时间压不回 10ms 以内。

即使触发，也按这个顺序走，不直接上 ECS：先把敌人位移和索敌改成 `NativeArray + IJobParallelFor + Burst`（保留现有数据结构和 MonoBehaviour 表现层），实测收益。如果 Jobs 版本仍然不够，再考虑 ECS。理由：ECS 会重写 40% 以上的代码，对一个 15 分钟体量的游戏来说，除非实测数据说话，否则是纯负债。

`DebugOverlay` 里加一个开关，实时显示"敌人 tick 耗时 / 敌人数量"，方便日后判断。

## 10 工程结构

### 10.1 需要加的包

| 包 | 版本 | 用途 |
|---|---|---|
| `com.unity.inputsystem` | 1.7.0 | 输入 |
| `com.unity.cinemachine` | 2.9.7 | 相机 |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 | 配置反序列化 |

Cinemachine 用 2.9.x 而不是 3.x：3.x 的 API 大改（`CinemachineCamera` 替代 `CinemachineVirtualCamera`），2.9 在 2022.3 上是最稳的，社区资料也多。日后升级要重写相机层，代码集中在 `CameraRig` 一个文件里能接受。

### 10.2 asmdef

```
Assets/Scripts/
  Game/Runtime/   Game.Runtime.asmdef   （引用 UniRx? 不，引用 InputSystem / Cinemachine / Newtonsoft）
  Game/Editor/    Game.Editor.asmdef    （引用 Runtime，仅 Editor 平台）
  Game/Tests/     Game.Tests.EditMode.asmdef
```

运行时程序集内部再分目录，不分成多个 asmdef。理由：分太细会让"改一个字段要改 5 个 asmdef 引用"，在这个体量上是纯负担。只在 Editor 与 Tests 处切分。

### 10.3 目录

```
Assets/
  Scenes/           Boot, MainMenu, Run, Test_Arena
  Scripts/Game/
    Core/           AppRoot, RunLoop, EventBus, Pool, Rng, DebugOverlay
    Core/Events/    RunEvents.cs（所有事件定义）
    Model/          RunState, UnitModel, StatBlock, BuffContainer, ProjectileModel
    Config/         ConfigDatabase, *Def.cs
    Controllers/    InputController, PlayerController, CombatController, WaveController, ShopController, RunController
    Systems/        SpatialGrid, EnemySystem, CompanionSystem, PickupSystem, DamageResolver
    Weapons/        IWeaponBehaviour, RangedBehaviour, MeleeBehaviour, WeaponRuntime
    Views/          UnitView, HudView, ShopView, DamageNumberView, VfxSpawner, AudioView
    Services/       InputService, SaveService, StreamingAssetsReader
    Editor/         导表调用、Config 校验窗口、场景工具
  Prefabs/Units/    Player, Enemies, Companions
  Prefabs/Weapons/
  Art/Materials/
  Art/Meshes/
  Config/           GameConfig.xlsx（源文件）
  StreamingAssets/Config/   *.json（生成的，提交进 git 便于复查 diff）
  docs/
tools/
  export_config.py
```

`StreamingAssets/Config` 的 json 提交进 git。理由：策划改了表但忘了跑导表时，能靠 diff 看出来；也方便回滚到某一版的数值。

### 10.4 Layer 与 Tag

| Layer | 用途 |
|---|---|
| 6 `Ground` | 地面 |
| 7 `Player` | 玩家 |
| 8 `Enemy` | 敌人 |
| 9 `Projectile` | 投射物 |
| 10 `Pickup` | 拾取物 |
| 11 `Boundary` | 边界 |

模拟层不用 Layer 做判定（自写距离判定），Layer 只用于相机裁剪、光照层级和调试显示。

## 11 里程碑映射

迭代拆分见 [ITERATION_PLAN.md](ITERATION_PLAN.md)。本方案对应的实现顺序：

| 迭代 | 本方案中落地的章节 |
|---|---|
| I0 工程基座 | 2、3、4、6、10 |
| I1 战斗核心 | 5、7、8.2、9 |
| I2 循环与内容 | 6（完整表）、8.3、商店、波次 |
| I3 内容填充 | 全部数值表、buff 预留验证 |
| I4 打磨 | 2.3、2.4、音频、结算 |
| I5 扩展 | 8.4 接 buff、难度、无尽 |

## 12 风险

| 风险 | 影响 | 处理 |
|---|---|---|
| 正交斜视的遮挡：玩家被高个子敌人挡住 | 中 | 玩家描边壳 + 敌人高度上限 0.8 米；实测再决定要不要做 X 光 |
| Cinemachine 2.9 的正交相机 follow 在快速移动时有轻微抖动 | 中 | Transposer 的 damping 从 0.3 起调；抖动明显就换成手写跟随（20 行），Cinemachine 只留 Impulse |
| Excel 是二进制，git 无法 diff | 中 | 生成的 json 提交进 git，用 json 的 diff 做审核；xlsx 只当源文件 |
| 导表脚本自实现 xlsx 解析，遇到特殊格式会崩 | 中 | 6.2 的禁令 + 导表报错清晰；提供一条紧急路径：Excel 另存 CSV 再导 |
| 3D 几何体美术量比 2D 大 | 高 | 全部用 Unity 内置几何体（Capsule/Cube/Sphere）+ 材质颜色区分，迭代 3 前不做自定义模型 |
| 200 个 3D 物体的实时阴影开销 | 高 | 敌人默认关投射阴影，只开接触阴影盘；方向光阴影距离限制在 40 米内 |
| MVC 在游戏里容易退化成"Controller 什么都知道" | 中 | 5.2 的禁止项写进 code review 清单；每个 Controller 的字段里不许出现 `*View` 类型 |

## 13 待定

1. 目标帧率与最低配置（决定阴影和 Bloom 的取舍）。
2. 美术是否用自发光描边还是后处理 Outline（影响材质数量与 draw call）。
3. 是否要 4 人本地同屏（现在架构能支持，但输入与相机要改）。
