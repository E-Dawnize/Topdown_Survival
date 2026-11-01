# 武器视觉资产管线

v0.3.3 起，武器模型不再由 `WeaponVisual` 用内置 Cube/Cylinder/Sphere 现场拼。
运行时只看 `WeaponVisualCatalog`，模型、动作、时长都在 prefab / catalog 资产里。

## 数据流

```text
Config/GameConfig.xlsx → Weapon.json → WeaponDef.Id
Assets/Art/Models/Weapons/**/*.fbx（CC0）
        │
        │  Tools/Weapons/Create Missing Visual Prefabs（只补缺失；Force Rebuild 才会覆盖）
        ▼
Assets/Prefabs/Weapons/Weapon_{Id}.prefab
  root：WeaponVisual + Model（导入网格/材质）+ Flash（闪光挂点）[+ OrbGlow]
        │
        ▼
Assets/Resources/WeaponVisualCatalog.asset
  WeaponId → Prefab / AttackStyle / AttackDuration
        │
        ├── PlayerView → WeaponRackView → WeaponVisual.Attach
        └── CompanionView → WeaponAnchor → WeaponVisual.Attach

Assets/Art/Models/Weapons/Quaternius/Arrow.fbx
  → Assets/Prefabs/Projectiles/Projectile_Arrow.prefab（ProjectileView + 箭模型，长轴 +Z；UnitPrefabMap 引用）
  → ProjectileViewPool → ProjectileView
```

`WeaponVisual` 只做四件事：跟随槽位、按 `AimDirection` 绕 Y 瞄准、按 `ShotsFired`
播放攻击姿态、闪一下 muzzle flash。它不读伤害、不改判定、不碰 `WeaponSystem`。

## Prefab 是唯一真源

生成出来的 prefab 就是运行时唯一真源：`Model` 的旋转/缩放/位置、`NockedArrow`、`Flash`、
`WeaponVisual._bowDrawLeadSeconds/_bowDrawPull` 都序列化在 prefab 里，可以直接在 Inspector 改。

- `Tools/Weapons/Create Missing Visual Prefabs` 只补缺失的 prefab，不碰已有的；
- 只有 `Tools/Weapons/Force Rebuild Visual Prefabs (overwrites)` 才会用 Spec 里的初始值覆盖；
- 弓的 `-90°Y + 30°` 侧倾、搭箭位置、拉弓参数都已经在 `Weapon_Bow.prefab` 里，不需要改代码；
- 投射物 prefab 由 `UnitPrefabMap.ProjectileArrow` 资产引用，不在代码里按名字查；`ProjectileViewPool` 只消费注入的 prefab。

## 三条硬规则

1. **catalog 是唯一映射。** 运行时没有 `switch (def.Id)`，也没有“未知武器回退成剑”的
   分支。加武器只加 catalog 条目；漏了会由 `WeaponVisualCatalogTests` 直接失败。
2. **prefab 自己带 `WeaponVisual`。** `Attach` 找不到组件或 prefab 时打错误并返回 null，
   不做白模兜底。资源断了应该在 Console/Hierarchy 里一眼看见，而不是悄悄显示一把占位剑。
3. **表现层不写模拟状态。** `WeaponVisual` 只改自己的 Transform；`WeaponModel` 的
   等级、冷却、伤害仍由 `WeaponSystem` / `BuildController` 持有。
4. **尺寸以米写在 prefab 里。** `WeaponPrefabBuilder.TargetSize` 是模型最长边的目标米数
   （剑约 1.2m、弓约 1.3m）；运行时 `WeaponVisualStyle.PlayerScale = 1.0`，
   旅伴 `CompanionScale = 0.5` 只是再乘一档。不要再把 prefab 当几何体时代的小零件去乘 1.8。

## 加一把武器

1. 把 FBX 放进 `Assets/Art/Models/Weapons/<来源>/`，同时补 LICENSE 与
   `Assets/Art/Models/Weapons/README.md` 的来源说明。
2. 在 `WeaponPrefabBuilder.Specs` 加一条：`Id`、`Source`、`TargetSize`、`AttackStyle`、
   `Duration`、`Grip`、`FlashLocal`、`FlashScale`。双持武器（当前只有匕首）加 `Pair = true`，
   builder 会放两份模型、左右各偏 0.08m 并外张 12°。
3. Unity 菜单 `Tools/Weapons/Create Missing Visual Prefabs`，只补缺失的 prefab、材质与 catalog。
   已经存在的 prefab 是唯一真源，不会被覆盖；只有 `Force Rebuild Visual Prefabs (overwrites)` 会重置手调。
4. 在 `Config/GameConfig.xlsx` 的 Weapon / Upgrade 表加行并导表。
5. 跑 EditMode：`WeaponVisualCatalogTests` 会检查“配置里的每把武器都有 prefab、
   动作不重复、近战动作盖住判定窗口、远程动作短于冷却”；弓箭还要通过
   `ProjectileArrowTests`（UnitPrefabMap 引用、箭 prefab 长轴 +Z、长度）和 PlayMode 的搭箭/交接用例。

加一种新动作：在 `WeaponAttackStyle` 加枚举、在 `WeaponVisual.ApplyPose` 加姿态分支、
在 builder 的 Spec 里选它；`AttackStylesAreDistinctAndCoverEveryShape` 只要求六把
当前武器互异，新动作需要同时更新该断言的数量。

## 弓箭与投射物

- 弓 prefab 的 `Model` transform 已经是 `-90°Y + 30°Z` 侧倾：`-90°Y` 让“弦 → 弓把”
  对齐 +Z，箭沿弓平面内的 +Z 飞；`30°` 侧倾让背后视角还能看见弓弧。这个旋转在 prefab 里，
  builder 的 Force Rebuild 会从已有 prefab 的 `Model` 读回，不再用代码覆盖。
- 弓 prefab 的 `NockedArrow` 是 `Projectile_Arrow.prefab` 的一个嵌套实例（同一网格、同一长度、
  同一材质），并设为 `WeaponVisual._muzzle`。开火时 `WeaponVisual` 把这**同一个实例**从弓上
  摘下来交给 `ProjectileNockedHandoff`；`ProjectileViewPool` 为新投射物取走它，所以飞出去的
  就是搭在弓上的那支箭。然后 `WeaponVisual` 立即用 `_nockedArrowPrefab` 复制一支新的放到弓上，
  等下一发再拉弓。
- 开火帧仍把 `MuzzleWorld` 写入 `ProjectileMuzzleRegistry`；`ProjectileView` 不再从枪口
  横向插值到模拟弹道，而是沿“枪口 → 这一发的 AimPoint”直线走，进度 = 已飞行距离 / 到目标距离，
  所以箭从弓中间出来就是直着指向目标。命中仍由模拟层决定。
- `WeaponVisual`
  在冷却最后 `_bowDrawLeadSeconds`（Bow prefab 上默认 0.28s）里把箭向后拉 `_bowDrawPull`（默认 0.20m）；
  `ShotsFired` 一变化就收起，并播放 `BowDraw` 的释放后坐。
- 真正飞出去的箭是 `Projectile_Arrow.prefab`，由 `UnitPrefabMap.ProjectileArrow` 引用、`ProjectileViewPool` 实例化；模型长轴朝 +Z，
  `ProjectileView.Sync` 用 `LookRotation(direction)` 对准速度。颜色仍由 `ProjectileViewSources`
  按玩家/旅伴区分，`ProjectileView.ApplyLook` 只改 `localScale` 与 Renderer 的 `_BaseColor`。
- 弓的 `NockedArrow` 与投射物是同一个 prefab（`Projectile_Arrow.prefab`），因此
  形状、长度、材质一致；搭箭和飞行箭由 `WeaponVisual`/`ProjectileView` 用同一 owner 颜色
  的 `MaterialPropertyBlock` 上色，交接时不会换外观。

## 武器架转向

`WeaponRackView` 的位置跟玩家根节点，世界朝向用 `SmoothDampAngle` 追玩家 yaw
（`WeaponVisualStyle.FollowTurnSmoothTime = 0.16s`）。玩家根节点仍然瞬时转，武器排不会
跟着瞬移；`WeaponVisual.AimRotation` 按 rack 当前 yaw 换算，瞄准方向仍然正确。

## 模型来源

| 文件 | 来源 | 许可 |
|---|---|---|
| Quaternius `Bow_Wooden / Dagger / Hammer_Small / Sword / Arrow` | https://quaternius.com/packs/medievalweapons.html | CC0 1.0 |
| KayKit `crossbow_1handed / wand` + 贴图 | https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0 | CC0 1.0 |

原始网格未修改；`WeaponPrefabBuilder` 只做统一缩放、握点对齐、加闪光挂点。
详细许可文件在各子目录的 `LICENSE.txt` / `License.txt`。

## 为什么用 Resources，而不是 Addressables

当前只有 6 个固定条目、随包发布、没有 DLC/热更需求，`Resources.Load` 一次同步加载
最简单，也不会在启动时引入异步状态机。条目数上到几十、或者要按关卡/皮肤分包时，
把 `WeaponVisualCatalog` 换成 Addressables 的 label 查询即可，调用点只有
`WeaponVisualCatalog.LoadDefault()` 一处。

## 已知边界

- 模型是静态网格，没有骨骼/Animator；攻击动作是程序姿态，复杂动作（换弹、蓄力）
  不适合继续往 `ApplyPose` 里堆。
- 没有 LOD、没有合批优化；当前上限是玩家 4 把 + 旅伴 4 把，draw call 需要进 Play 实测。
- `WeaponVisual` 的闪光/灵球光晕是 prefab 里的 sphere + Unlit 材质，不是粒子系统。
