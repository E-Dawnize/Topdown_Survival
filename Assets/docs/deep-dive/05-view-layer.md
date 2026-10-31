# 视图层：怎么落地

## 要解决的问题

200 个敌人、400 发投射物、几十件掉落物，每帧都要更新位置。Unity 里最容易踩的三个坑在这里同时出现：每个单位一个材质实例会打断合批，每帧写 Transform 会拖慢主线程，直接读模拟位置会抖。

## 落地步骤

第一步，所有位置一律走插值。模型里存 `PrevPosition` 和 `Position`，模拟每步移动前把当前值记进 `PrevPosition`。视图同步时按 `Alpha` 插值：

```csharp
transform.position = Vector3.Lerp(prev, curr, alpha);
```

视图不持有模型引用来直接读 `Position`。玩家的 `PlayerView` 已经不持有 `PlayerModel`，只接受 `ViewSnapshot`。

第二步，材质共享 + `MaterialPropertyBlock` 换色。

```csharp
static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
_mpb.SetColor(BaseColorId, color);
_renderer.SetPropertyBlock(_mpb);
```

材质从已有资产里取（玩家预制体的那份），不用 `Shader.Find`，因为剥离设置可能把它裁掉，运行时就是粉色。每个单位一份属性块，不克隆材质。整局的材质总数是常数，与单位数无关。

第三步，四类对象各一套池，但投射物池只一套。

敌人池按空间哈希槽位复用实例，旅伴池按队伍槽位（最多 4 个），掉落物池按掉落槽位，投射物池由所有持有者共用一个实例池，按持有者换颜色和大小。池的实例 `SetActive(false)` 回收而不是销毁。

第四步，槽位复用时重新绑定要吸附位置。

```csharp
public void Bind(EnemyModel enemy)
{
    transform.position = new Vector3(enemy.Position.x, transform.position.y, enemy.Position.y);
    ApplyColor(TintOf(enemy.Def.Id));
}
```

不吸附的话，新敌人会从上一具尸体的位置插值过来，画面上是一道横穿场地的残影。

第五步，受击表现只改身体渲染器，不改子节点。接触阴影盘是独立渲染器，受击变亮时它不该跟着亮。

第六步，HUD 不要每帧全量写。把要显示的字段做成位标记加差值：

```csharp
HudField changed = HudValues.Diff(current, _last);
if (changed.HasFlag(HudField.Coins)) _coinText.text = ...;
```

每帧给 uGUI 的 `Text` 赋同一个字符串也会制造脏标记，整套 UI 每帧重建。商店与结算期间只放行金币，其余战斗项不写，但基准值要保留（`Merge`），解冻那一帧补齐。

## 关键决定与代价

玩家描边用放大 1.12 倍、`_Cull = Front` 的背面渲染壳，共享材质加属性块。这比后处理描边便宜，代价是只有玩家有。

接触阴影盘用一份共享网格加共享材质，挂在敌人实例子节点上。它在稳态不写 Transform，跟着父节点走。

字体用系统动态字体，从 `Font.CreateDynamicFontFromOSFont` 之类取，HUD 和商店共用一份图集。预制体里是内置 LegacyRuntime，没有中文字形。

## 踩过的坑

敌人、旅伴、玩家的形状要能一眼分开：敌人是方块，旅伴是矮胶囊按阵营上色，玩家是有描边的高胶囊。这不是美术选择，是玩法可读性——羁绊按阵营计数，队伍里几个阵营要一眼能读出来。

UI 的 `Canvas` 排序层级要显式给：HUD 10、商店 20、结算 30。默认都是 0，谁先加载谁在上。

## 怎么验证

视图层基本测不了。能测的部分抽成了纯类：`CompanionViewTests` 12 条验槽位分配，`ProjectileViewSources` 是纯逻辑（哪些持有者要画、各自颜色），`HudValuesTests` 7 条验差值刷新。

真正要人看的是：材质是否合批、描边宽度、阴影盘大小、字体有没有方块、面板排版、排序层级。这些全部记在未验证清单里。

## 迁移到别的项目要注意

`MaterialPropertyBlock` 只对每个渲染器独立的属性有效，改 shader 关键字或换材质必须换渲染器。池化对象复用时一定要重新 `Bind` 并吸附位置，否则残影是最常见的 bug。插值系数只能有一个来源，多处各算一份必然对不上。
