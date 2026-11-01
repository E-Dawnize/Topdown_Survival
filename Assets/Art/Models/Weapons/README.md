# 武器模型来源

这些模型用于 `WeaponVisual` prefab；运行时由 `WeaponVisualCatalog` 实例化，不再用代码拼几何体。

## Quaternius — Medieval Weapons

- 来源页：https://quaternius.com/packs/medievalweapons.html
- 作者：Quaternius
- 许可：CC0 1.0（`Quaternius/LICENSE.txt`）
- 使用：
  - `Bow_Wooden.fbx` → 弓
  - `Dagger.fbx` → 双匕
  - `Hammer_Small.fbx` → 战锤
  - `Sword.fbx` → 剑
- 修改：导入 Unity 后由 `WeaponPrefabBuilder` 统一缩放、把模型握点对齐到 prefab 原点，并加闪光挂点；没有修改原始网格。

## KayKit — Adventurers Character Pack

- 来源：https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0
- 作者：Kay Lousberg / KayKit
- 许可：CC0 1.0（`KayKit/LICENSE.txt`）
- 使用：
  - `crossbow_1handed.fbx` → 弩
  - `wand.fbx` → 灵球（法杖水晶作为灵球本体）
  - `mage_texture.png` / `rogue_texture.png` → 对应 FBX 的贴图
- 修改：仅导入与 prefab 包装，没有修改原始网格与贴图。

## 再生成

Unity 菜单：`Tools/Weapons/Rebuild Visual Prefabs`。它会重新生成
`Assets/Prefabs/Weapons/*.prefab`、闪光/灵球材质和 `Assets/Resources/WeaponVisualCatalog.asset`。
