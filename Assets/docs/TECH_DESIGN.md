# 技术方案 v0.1

配套文档：[游戏设计文档](GDD.md)

Unity 2022.3.34f1c1（中文版）/ URP 14.0.11

---

## 0 当前工程的实际状态

写方案前先确认过的事实，后面所有决策基于它们：

| 项 | 现状 | 影响 |
|---|---|---|
| 渲染管线 | URP 14.0.11，asset 里 `m_RendererType: 1`，即 **Universal Renderer（3D）** | 需要切到 2D Renderer 才能用 Light2D |
| 场景 | `Assets/Scenes/SampleScene.unity`，URP 模板示例场景 | M1 重建 |
| 脚本 | 只有空的 `GameFlow.cs`，落在 Assembly-CSharp | 需要建 asmdef |
| 输入 | `activeInputHandler: 0`，旧 Input Manager | 新手柄支持差，要换 |
| 颜色空间 | Linear | 已符合预期，不改 |
| 包 | 无 Input System、无 Addressables、无 Cinemachine | 按需加 |
| Burst / Mathematics | 已随 URP 装上（1.8.15 / 1.2.6） | 暂不使用 |
| .NET | .NET Standard 2.1 | 可用 Span、System.Memory |

一句话结论：这不是一个可以"直接开始写玩法"的工程，M1 的前半天是工程配置，不是功能开发。

## 1 目标与非目标

目标：

1. 1080p 60fps，同屏 200 敌人 + 400 子弹 + 300 粒子的稳定表现。
2. 改数值不改代码、不重编译。策划在 Inspector 里能直接调。
3. 核心逻辑（伤害、属性、商店抽卡、波次）可在 EditMode 测试里单独跑，不依赖场景。
4. 一局的完整结果可复现：同一种子，同一串输入，同样结果。

初版明确不做：网络与联机、录像回放、DOTS/ECS、自定义后处理、Addressables 分包、移动端适配（M4 再评估）、本地化多语言（数据结构留了口子，但不实现）。

## 2 关键技术选型

| 决策点 | 选择 | 否决的 | 理由 |
|---|---|---|---|
| 渲染管线 | URP **2D Renderer**（Renderer2DData） | 保持 Universal Renderer | 2D 渲染路径专为 Sprite 设计，支持 Light2D 与 2D 阴影；3D 路径下 Sprite 能画，但没有光照体系，后期加灯要返工 |
| 光照 | 只用少量 Light2D（玩家灯、爆炸光、BOSS 黑暗战），敌人与子弹一律 unlit | 全场景受光 | 2D 光照开销随"光源数 × 受光精灵数"增长，200 个受光精灵上不了 60fps |
| 模拟架构 | 单个 MonoBehaviour 驱动 + 纯 C# 系统类，实体是数据不是 MonoBehaviour | DOTS/ECS、每实体一个 Update | 200 实体远未到 ECS 的收益线；ECS 的调试与迭代成本对一个 15 分钟体量的游戏不成比例 |
| 碰撞 | 自写圆形距离判定 + 均匀网格空间哈希 | 全量 PhysX 2D | 400 子弹 × 200 敌人若走 Collider2D，物理步进与回调开销不可控，且 GC 抖动难压 |
| 输入 | 新 Input System（1.7.x） | 旧 Input Manager | 手柄/键鼠热插拔、Action 与 UI 分离、设备切换不用写 if |
| UI | uGUI + TextMeshPro | UI Toolkit | 2022.3 的运行时 UI Toolkit 在动画、拖拽、复杂自适应布局上弱于 uGUI，商店卡片需要这些 |
| 配置数据 | ScriptableObject + 字符串 Id | JSON / CSV 直接读 | SO 有 Inspector 编辑、引用校验、编译期资源引用；CSV 转换工具 M3 再补 |
| 存档 | JsonUtility + `persistentDataPath` + schema 版本号 | PlayerPrefs / SQLite | 结构简单，需要版本迁移，不需要事务 |
| 随机 | 自写 xorshift128，拆两条流（局内 / 局外） | `UnityEngine.Random` | 全局静态状态无法拆流、无法独立复现 |
| 对象池 | 自写薄封装（约 80 行），内部 `Stack<T>` | `UnityEngine.Pool.ObjectPool<T>` | 需要"容量满时拒收而非无限扩容"这种策略，内置类的默认行为要绕过 |
| 音频 | AudioSource 池 + 同帧同音源限流 | FMOD / Wwise | 音源数量与需求都小，中间件只会增加构建复杂度 |
| 动画 | Sprite 序列帧 + 代码驱动（Tween 自写 40 行） | Animator / DOTween | Animator 在 200 个实体上开销明显；Tween 的需求只有位移、缩放、透明度 |
| 字体 | TMP SDF，中文用常用 3500 字子集 | 全字库 SDF | 全字库 SDF 图集会膨胀到几十 MB，且动态生成有卡顿 |
| 版本控制 | git + `.gitattributes`（UnityYAMLMerge 驱动） | git-lfs（暂不启用） | 当前没有大二进制；等有了音频和大图再开 LFS，避免协作者多一步安装 |

### 2.1 关于渲染管线切换

工程现在用的是 3D 模板的 `UniversalRendererData`。切换动作：

1. `Assets/Settings/` 下新建 Renderer2DData，命名 `URP-2D-Renderer`。
2. 新建 URP Asset 指向它（或改现有 asset 的 `m_RendererType` 为 2 并换 renderer 列表）。
3. `ProjectSettings > Graphics > Scriptable Render Pipeline Settings` 指向新的 URP Asset。
4. Quality 里三个档位都指向它，避免切画质时 2D 光照消失。

旧的 3D asset 保留不删，方便回退对比。这个操作要在编辑器里做，手改 YAML 的 `m_RendererType` 和 renderer 引用容易把 asset 弄坏——M1 开头的第一步，不省。

### 2.2 相机与单位约定

- 1 Unity 单位 = 1 米，玩家移速 5 米/秒。
- 正交相机 `orthographicSize = 6`，16:9 下可见高度 12 米、宽 21.3 米。
- 敌人从距玩家 13–14 米的位置刷出（屏幕外），与 GDD 的刷怪规则一致。
- PPU = 100，**不做 Pixel Perfect**。像素风如果要上，单独引入 Pixel Perfect Camera + 整数缩放，且要重做全部尺寸，代价不小，需要先定美术方向。

### 2.3 时间与步长

模拟用固定步长 1/60 秒累加，每帧最多跑 3 步（防死亡螺旋），渲染不做插值。

为什么不插值：这是自动战斗游戏，1 帧的位置精度不影响手感，插值反而要给每个实体存上一帧位置，多一份状态和复制开销。为什么不直接用 FixedUpdate：需要控制"一帧最多几步"和在一步内保证执行顺序稳定，自己写累加器更直接。

## 3 模块清单

行数是量级估计，用来判断拆分粒度，不是承诺。

### 3.1 入口与流程

| 模块 | 职责 | 依赖 | 阶段 | 量级 |
|---|---|---|---|---|
| `Boot` | 初始化服务、加载配置、进主菜单 | 全部服务 | M1 | 60 |
| `AppState` | 主菜单 / 选人 / 局内 / 结算 的状态机 | Boot | M1 | 100 |
| `RunState` | 一局的全部数据：金币、波次、计时、旅伴列表、道具列表、统计、种子 | StatBlock, Rng | M1 | 200 |
| `RunLoop` | 唯一的每帧入口，按固定顺序驱动所有系统 | 所有系统 | M1 | 120 |
| `SceneFlow` | 场景加载与过渡 | AppState | M2 | 60 |

### 3.2 模拟

| 模块 | 职责 | 依赖 | 阶段 | 量级 |
|---|---|---|---|---|
| `PlayerSystem` | 玩家输入采样、移动、冲刺、主动技能 | InputService, StatBlock | M1 | 250 |
| `CompanionSystem` | 旅伴跟随、开火、升级 | Unit, WeaponInstance | M2 | 200 |
| `EnemySystem` | 敌人注册表、AI tick、刷怪接收 | SpatialGrid | M1 | 300 |
| `ProjectileSystem` | 子弹生命周期、移动、命中判定 | SpatialGrid, DamageResolver | M1 | 200 |
| `PickupSystem` | 金币/心/宝箱吸附与拾取 | 玩家位置 | M2 | 100 |
| `SpatialGrid` | 均匀网格空间哈希，`QueryCircle` | 无 | M1 | 150 |
| `DamageResolver` | 闪避、护甲、暴击、扣血、触发回调 | StatBlock | M1 | 180 |
| `WaveDirector` | 波次状态机、刷怪配额、BOSS 调度 | EnemySystem | M2 | 250 |
| `TriggerSystem` | 道具触发型效果（受击、击杀、闪避、定时） | 事件 | M3 | 200 |

### 3.3 内容

| 模块 | 职责 | 依赖 | 阶段 | 量级 |
|---|---|---|---|---|
| `Unit` | 单位基类：坐标、朝向、HP、StatBlock、状态位 | StatBlock | M1 | 150 |
| `EnemyBehaviour` | 7 种敌人行为（追击、冲锋、抛射、分裂、护盾、精英、BOSS） | Unit | M1–M3 | 600 |
| `CompanionBehaviour` | 10 种旅伴行为 + 武器绑定 | Unit, WeaponInstance | M2 | 400 |
| `WeaponInstance` | 冷却、索敌、开火、弹道展开 | ProjectileSystem | M1 | 180 |
| `ItemEffect` | 18 件道具的效果实现（属性型 / 触发型 / 经济型 / 特殊型） | StatBlock, TriggerSystem | M3 | 500 |
| `SynergyResolver` | 阵营计数与羁绊生效/失效 | RunState | M2 | 120 |
| `ShopSystem` | 抽卡、去重、保底、价格、刷新、买卖 | ItemDatabase, RunState | M2 | 300 |
| `AltarSystem` | 祭坛献祭与结算 | RunState | M3 | 120 |
| `BossController` | 两场 BOSS 的阶段与弹幕 | Unit | M3 | 400 |

### 3.4 表现

| 模块 | 职责 | 依赖 | 阶段 | 量级 |
|---|---|---|---|---|
| `UnitView` | 把模拟数据同步到 Transform/Sprite，8 向朝向 | 无 | M1 | 150 |
| `HitFeedback` | 闪白、击退、hitstop、震屏 | CameraRig | M2 | 150 |
| `DamageNumberPool` | 伤害跳字，同屏上限 40 | 池 | M2 | 120 |
| `CameraRig` | 跟随、震屏、缩放 | 无 | M1 | 120 |
| `AudioBus` | 音源池、限流、随机变调 | 无 | M3 | 150 |
| `HUD` | 血条、金币、波次、CD | RunState | M2 | 250 |
| `ShopUI` | 卡片、价格、对比、刷新 | ShopSystem | M2 | 350 |
| `ResultUI` | 结算、DPS 曲线 | RunState | M3 | 250 |

### 3.5 数据与服务

| 模块 | 职责 | 依赖 | 阶段 | 量级 |
|---|---|---|---|---|
| `ScriptableObjects` | CharacterDef / CompanionDef / ItemDef / EnemyDef / WaveDef / BossDef | 无 | M1 | 400 |
| `ItemDatabase` | 全道具索引、按品质分组、权重表 | SO | M2 | 100 |
| `SaveSystem` | 读写、schema 迁移、原子写 | 无 | M3 | 150 |
| `MetaProgress` | 解锁条件判定、累计统计 | SaveSystem | M3 | 150 |
| `InputService` | Input System 封装，对外暴露 `MoveAxis` / `DashPressed` / `SkillPressed` | Input System | M1 | 120 |
| `DebugOverlay` | FPS、实体数、各系统耗时、GC、种子 | 全部 | M1 | 200 |

合计约 7000–8000 行量级。这个数字会飘，别拿它排期。

## 4 分层与依赖规则

```
┌─────────────────────────────────────────────┐
│ 表现层  UnitView / HUD / ShopUI / Audio     │  可以读模拟层
│         HitFeedback / CameraRig / VFX       │  不得反向调用
└───────────────────▲─────────────────────────┘
                    │ 只通过事件
┌───────────────────┴─────────────────────────┐
│ 模拟层  RunLoop / *System / DamageResolver   │  纯 C#，不碰 Transform
│         SpatialGrid / RunState / StatBlock   │  不 Instantiate，不查 UI
└───────────────────▲─────────────────────────┘
                    │ 只读
┌───────────────────┴─────────────────────────┐
│ 数据层  ScriptableObject 定义 + 索引         │  运行时不写入
└─────────────────────────────────────────────┘
```

四条硬规则：

1. 模拟层不出现 `GetComponent`、`Instantiate`、`FindObjectOfType`、`Collider2D`。
2. 模拟层不直接改 `RunState` 之外的单例状态。`RunState` 由 `RunLoop` 注入，不走静态单例——测试时才不用清全局。
3. 表现层不接受模拟层之外的写操作。UI 想改数据一律发命令（`ShopSystem.TryBuy(index)`），不允许 UI 自己扣金币。
4. ScriptableObject 在运行时只读。任何"运行时强化"都是 `StatBlock` 里的 modifier，不写回资产。

唯一例外：`RunEvents` 是静态事件中心，因为事件的生产者在模拟层、消费者在表现层，单例比传引用省事。它的字段全部是 `Action` / `event`，只有定义，没有状态。

### 4.1 跨层通信

```
模拟层  ──RunEvents.UnitDied──▶  表现层（伤害字、掉落、音效）
       ──RunEvents.WaveCleared─▶  表现层（波次横幅） + 流程层（进商店）
表现层  ──ShopSystem.TryBuy──▶  模拟层（命令）
       ──RunLoop.Pause()────▶   模拟层（命令）
```

事件清单（初版）：`UnitDamaged`、`UnitDied`、`CoinChanged`、`CoinPicked`、`WaveStarted`、`WaveCleared`、`ItemAcquired`、`ItemSold`、`CompanionAcquired`、`CompanionUpgraded`、`ShopOpened`、`ShopClosed`、`SkillUsed`、`PlayerDied`、`RunEnded`。

每个事件都带完整参数（谁、多少、什么来源），不允许让订阅方自己去查别处的状态——否则会出现"事件到了但数据已经被下一步改掉"的顺序 bug。

## 5 核心数据结构

### 5.1 属性与修正

```csharp
public enum StatType {
    MaxHP, Regen, Armor, Dodge, MoveSpeed,
    Attack, AttackSpeed, CritChance, CritDamage,
    Projectiles, Range, PickupRadius, Luck, Treasure, ContactDamage
}

public enum ModOp { Flat, Percent }

public readonly struct StatModifier {
    public readonly StatType Stat;
    public readonly float Value;
    public readonly ModOp Op;
    public readonly object Source;   // 道具实例 / 羁绊 / 药品，卖出时按它批量移除
}

public sealed class StatBlock {
    readonly float[] _base;          // 按 StatType 索引
    readonly List<StatModifier>[] _mods;
    readonly float[] _final;
    bool _dirty;

    public float this[StatType t] { get; }   // 脏了才重算
    public void Add(in StatModifier m);
    public void RemoveBySource(object source);
    void Recalculate();  // final = (base + Σflat) × (1 + Σpercent)，Dodge/CritChance 做 clamp
}
```

计算顺序固定为 `(base + flat) × (1 + percent)`。所有百分比相加后再乘，不是连乘。这是全项目唯一允许算属性的地方，谁绕过它直接改 `hp`，后面的平衡就再也对不上了。

### 5.2 伤害

```csharp
public readonly struct DamageInfo {
    public readonly float Amount;
    public readonly DamageType Type;      // Physical / Fire / Curse / True
    public readonly Unit Source;
    public readonly Vector2 Direction;
    public readonly float Knockback;
    public readonly bool IsCrit;
}

public static class DamageResolver {
    // 顺序：闪避 → 无敌帧 → 暴击已在开火时结算 → 护甲 → 扣血 → 触发回调 → 死亡
    public static void Apply(Unit target, in DamageInfo dmg);
}
```

顺序里有三个坑，写进注释：

1. 暴击必须在开火时算一次，不能打到人再算，否则一个子弹穿透多个目标会各自暴击、伤害跳动不可解释。
2. 闪避判定在护甲之前，闪掉就是完全免伤，不产生任何触发（否则"受击触发"类道具会靠闪避白嫖）。
3. 死亡在触发回调之后，但 `UnitDied` 事件必须在下一 tick 才分发，防止在遍历敌人列表时增删。

### 5.3 单位

```csharp
public abstract class Unit {
    public int Id;                    // 全局递增，表现层用它做映射 key
    public Vector2 Position;
    public Vector2 Velocity;
    public float Facing;
    public StatBlock Stats;
    public float HP;
    public bool IsDead;
}

public sealed class EnemyUnit : Unit {
    public EnemyDef Def;
    public float AttackCooldown;
    public byte BehaviourState;       // 追击/蓄力/冲锋/抛射/分裂
}
```

`Unit` 是纯 C# 类，没有 MonoBehaviour。每个单位对应场景里一个 `UnitView`，两者用 `Id` 在字典里对应，`UnitView` 每帧从 `Unit` 拉位置。

### 5.4 武器

```csharp
public sealed class WeaponInstance {
    public WeaponDef Def;
    public float Cooldown;
    public Unit Owner;
    public float RangeMul, DamageMul, IntervalMul;   // 来自主人的属性

    public void Tick(float dt, SpatialGrid grid, ProjectileSystem projectiles);
}
```

索敌一律走 `SpatialGrid.QueryNearest(owner.Position, range, filter)`，不遍历全量数组。

### 5.5 对象池

```csharp
public sealed class Pool<T> where T : Component {
    public Pool(T prefab, int prewarm, int hardCap, Transform parent);
    public T Rent();                 // 到 hardCap 返回 null，调用方必须处理
    public void Return(T item);
}
```

`Rent` 返回 `null` 而不是扩容，是刻意的：子弹池被打爆时我要知道，而不是让它悄悄吃掉内存然后掉帧。

### 5.6 随机

```csharp
public struct Rng {
    ulong _s;
    public Rng(ulong seed);
    public uint NextUInt();
    public float Value01();
    public int Range(int minInclusive, int maxExclusive);
}
```

三条流：`runRng`（掉落、商店）、`metaRng`（局外）、`fxRng`（纯表现，音调、粒子角度）。表现层的随机绝对不能和局内随机共用一条流，否则加一个粒子效果就能改变整局结果，回放和复现会全部失效。

## 6 关键机制

### 6.1 主循环顺序

`RunLoop.Update()` 一个入口，内部按固定顺序：

```
1  InputService.Sample()
2  RunState.AdvanceTime(dt)          // 波次计时、商店计时
3  PlayerSystem.Tick(dt)
4  CompanionSystem.Tick(dt)
5  EnemySystem.Tick(dt)              // AI + 移动
6  SpatialGrid.Rebuild()             // 移动之后、判定之前
7  ProjectileSystem.Tick(dt)
8  PickupSystem.Tick(dt)
9  TriggerSystem.Tick(dt)            // 定时型道具
10 WaveDirector.Tick(dt)
11 RunEvents.Flush()                 // 本 tick 累积的事件统一分发
12 UnitView.Sync()                   // 表现层同步
```

顺序不能随意调：空间哈希必须在移动之后重建（否则子弹打到上一帧的位置），事件必须在全部模拟结束后统一分发（否则订阅方在遍历中途改了集合）。

### 6.2 空间哈希

- 格子边长 2 米，网格 32×32 覆盖 64×64 米，跟随玩家做区域滚动，滚动时整表重建（200 个单位的重建开销估计在 0.1ms 量级，M1 第 8 步实测确认）。
- 每格用 `List<int>`，预分配 8，超过就扩容，不回收（避免每 tick 分配）。
- 敌人注册时分配固定索引，`Remove` 用末尾交换法，O(1)。

### 6.3 开火与弹道

```
冷却归零 → QueryNearest(射程内, 优先级 精英 > 远程 > 近战)
        → 命中则把朝向转到目标
        → 计算弹道数 n，扇形展开角 (n-1) × 5°
        → 为每发子弹做一次暴击判定，写入 DamageInfo
        → 从池里 Rent 子弹，满了就放弃这一发（不排队）
```

### 6.4 旅伴跟随

不做寻路。目标点 = 玩家位置 + 按索引均分的角度 × 半径 1.6 米。旅伴向目标点移动，速度上限 1.2 × 玩家当前速度。距离超过 4 米直接瞬移回目标点。这套在 200 敌人的场景里是唯一能压住开销的做法，代价是旅伴会穿过敌人（不需要处理，视觉上可接受）。

### 6.5 波次状态机

```
Idle ──Start──▶ Spawning ──配额刷完──▶ Clearing ──场上清空──▶ Reward ──▶ Shop
                                                              │
                                                   BOSS 波则进 BossFight
```

`WaveDirector` 只认数据：每一波读 `WaveDef`（刷怪间隔、单次数量、怪种池、上限、精英节奏）。加一波不需要改代码。

### 6.6 商店抽卡

```
候选池 = 全部道具 - 已满 2 件的道具 - 品质不满足波次下限的
加权抽取（品质权重受幸运修正，白品质概率向紫色转移）
保底：每波必须出现至少 1 件 ≥ 蓝色的道具，没出就替换最后一个货位
价格 = 基准价 × (1 + 0.1 × (波次 - 1)) × 角色折扣 × 道具折扣
```

抽卡是纯函数 `ShopSystem.Roll(rng, runState, slotCount)`，可以在 EditMode 里跑一万次统计分布。

### 6.7 存档

```json
{ "schema": 1, "totalCoins": 0, "totalKills": 0, "unlocks": [], "bestScores": {} }
```

`schema` 不匹配时跑迁移函数链。写入用"写 `.tmp` → 删除旧文件 → 移动"的顺序，避免断电留下半个文件。

## 7 场景与资源组织

| 场景 | 用途 | 内容 |
|---|---|---|
| `Boot` | 启动 | 服务初始化、加载配置与存档，然后跳 MainMenu |
| `MainMenu` | 主菜单 + 选人 | 6 个角色卡、解锁状态、开始 |
| `Run` | 局内 | 只放相机、UI、系统容器；实体全部运行时生成 |
| `Test_Arena` | 测试场 | 固定敌人刷新、可调参数、用于手测和性能压力 |

- 所有 Sprite 走两张图集：`Atlas_Units`（角色/敌人/旅伴）、`Atlas_FX`（特效/弹幕/图标）。图集外的 Sprite 一律不允许进 `Resources`。
- 材质：`Sprite-Unlit-Default` 一个，`Sprite-Lit-Default` 一个，全局共用。逐对象改材质属性会打断合批。
- Prefab 只保留"实体原型"，不做场景内摆件。
- `Resources` 只放 Boot 必须加载的东西，其余走直接引用。

## 8 性能预算

这是设计目标，不是实测值。M1 结束时用 `DebugOverlay` 逐项打表核对，对不上的项要重新设计，不允许"先这样后面优化"。

| 项 | 目标 | 测量方式 |
|---|---|---|
| 帧时间 | ≤ 16.6ms | `DebugOverlay` 滑动平均 |
| 模拟 12 个 tick 的总耗时 | ≤ 3ms | 各系统 Stopwatch 累加 |
| 渲染 | ≤ 8ms | Profiler Rendering 采样 |
| Draw call | ≤ 25 | Frame Debugger |
| 同屏 SpriteRenderer | ≤ 600 | 场景统计 |
| 活跃 AudioSource | ≤ 16 | AudioBus 计数 |
| 模拟层每帧 GC 分配 | 0 B | Profiler GC Alloc 列 |
| 峰值内存 | ≤ 400MB | Profiler Memory |

零分配的具体做法：不用 LINQ、不在 tick 里 new 数组、`List<T>` 的遍历用 `for` 而不是 `foreach`（List 的 foreach 不装箱，但仍会走枚举器接口的方法调用）、事件参数用 struct 或可复用的类实例、字符串拼接只出现在 UI 且每次变化才拼。

## 9 工程约定

### 9.1 程序集

```
Assets/Scripts/
  Game/Runtime/       Game.Runtime.asmdef      全部运行时代码
  Game/Editor/        Game.Editor.asmdef       SO 校验、CSV 导入、场景工具
  Game/Tests/EditMode Game.Tests.EditMode.asmdef
  Game/Tests/PlayMode Game.Tests.PlayMode.asmdef
```

分 asmdef 的收益：改 UI 代码不会重编译全部模拟代码；测试能引用内部类型（配合 `InternalsVisibleTo`）。代价：每次加引用要改 asmdef，初始化时容易漏。M1 一次配好。

### 9.2 命名与结构

- 类型 `PascalCase`，私有字段 `_camelCase`，常量 `SCREAMING_SNAKE`（Unity 官方风格）。
- 一个文件一个 public 类型，文件名与类型名一致。
- `System` 后缀的类是纯 C# 系统；`Controller` 是 MonoBehaviour 控制层；`Def` 是 ScriptableObject。
- 不写 `#region`。

### 9.3 提交

`类型: 一句话`（feat / fix / refactor / docs / chore）。一次提交只做一件事。Unity 的 `.meta` 必须与资源同一次提交，这是 Unity 项目最容易踩的坑。

### 9.4 编辑器设置

`.gitattributes` 已加入 UnityYAMLMerge 驱动配置，用于 `.unity` / `.prefab` / `.asset` 的合并。使用前需要在本地执行一次（路径按自己的编辑器安装位置改）：

```
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/2022.3.34f1c1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
```

不做这个配置也能用，但场景冲突会变成一行行 YAML 的文本冲突，基本无法靠肉眼解决。

## 10 实施顺序

M1（骨架）拆成 8 步，每步都有可验证的产出。前 7 步没有玩家看得见的进度，这是正常的。

| 步 | 内容 | 验证方式 |
|---|---|---|
| 1 | 切 2D Renderer、装 Input System、改 activeInputHandler、建 asmdef | 编辑器和构建都能跑，日志里无 URP 警告 |
| 2 | 数据层：6 个 Def 类型 + 几条测试数据 | Inspector 里建得出来，字段完整 |
| 3 | `RunLoop` + `RunState` + 假时钟 | 打出 5 波的时间线日志，顺序正确 |
| 4 | 玩家移动 + 相机 + `UnitView` | 手感到位（响应 1 帧内） |
| 5 | `SpatialGrid` + `EnemySystem` + 1 种敌人 | 200 个敌人追人，DebugOverlay 显示 tick 耗时 |
| 6 | `WeaponInstance` + `ProjectileSystem` + `DamageResolver` | 打得死，伤害数字对得上手算 |
| 7 | `Pool<T>` 全量接入 | Profiler 里 GC Alloc 归零 |
| 8 | 压力测试场景 + 预算打表 | 200 敌 / 400 弹下 60fps，逐项核对第 8 节 |

M2 到 M4 的拆分在 GDD 里，技术上只补充一条：**M2 结束前必须有一次数值全表的自动化校验**（比如"任何一件道具的三项属性之和不超过阈值"），否则到 M3 内容一多，平衡错误会从类问题退化成个例问题。

## 11 风险与未决

| 风险 | 影响 | 处理 |
|---|---|---|
| 2D Renderer 切换可能影响现有 SampleScene 与 URP asset | 中 | 旧 asset 保留；切换后立刻截图对比；出问题回退只改一个设置 |
| TMP 中文字库体积 | 中 | 用 3500 常用字子集；缺字回退系统字体；M2 前实测图集大小 |
| 自写碰撞在敌人大量重叠时的行为异常（比如全挤在玩家身上） | 中 | 敌人之间加轻量分离力（每帧一次邻格查询，代价低）；实在不行回退到只用"推开玩家" |
| BOSS 动态血量需要玩家 DPS 统计 | 低 | `RunState` 里维护 30 秒滑动窗口，事件驱动，不额外开销 |
| 触屏/手柄的商店交互 | 中 | 商店 UI 从一开始就按"可键盘焦点导航"设计，不做纯鼠标 hover |
| 200 敌人下的 SpriteRenderer 开销未实测 | 高 | 这是 M1 第 8 步的验收项。如果打不到 60fps，备选方案是把敌人改成 `Graphics.DrawMeshInstanced` 的四边形 + 图集 UV 偏移，代价是失去逐对象材质与 Sort 便利 |
| 中文字体 + TMP 在 200 个伤害数字下的性能 | 中 | 伤害数字用数字专用图集（0-9 + 暴击符号），不用 TMP |

## 12 待你拍板的三件事

1. 目标平台：PC 独显优先，还是也要满足集显/掌机？这决定第 8 节预算是硬指标还是参考。
2. 美术方向：HD 2D（高清贴图，PPU 100，非 Pixel Perfect）还是像素风（PPU 32/64，需要 Pixel Perfect Camera 和全部尺寸重定）。这个决定越早越好，晚了所有尺寸和相机参数都要重做。
3. 是否需要移动端。需要的话，输入层要从 M1 就抽象成设备无关，商店 UI 也要按触屏设计。
