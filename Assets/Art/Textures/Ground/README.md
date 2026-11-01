# 地面贴图

`T_Ground_Paving136.png` 是 `MAT_Ground` 唯一的颜色贴图，贴在 160 × 160 米的 `Ground` 立方体顶面。

## 来源

- **素材**：ambientCG — PavingStones136（CC0 1.0，可商用、可修改、无需署名）
- **页面**：https://ambientcg.com/view?id=PavingStones136
- **文件**：`PavingStones136_1K-JPG.zip` → `PavingStones136_1K-JPG_Color.jpg`
- **许可**：https://ambientcg.com/license

## 处理

原图是偏暖的米色石板，直接放进偏蓝灰的场景会太亮、太抢角色。处理只做三件事：

1. 取 RGB 平均值转灰度；
2. 压缩对比度到标准差约 8，压到项目的暗部亮度；
3. 按蓝灰色调重新着色（R × 0.88，G × 0.96，B × 1.12）。

没有裁剪、没有加画线，板缝、裂纹、倒角都来自原图。PNG 为 1024 × 1024、8 bit RGB。

## 平铺

`MAT_Ground` 里 `_BaseMap` 的 tiling 是 **20 × 20**：

- 地面 160 米 ÷ 20 = **8 米一张图**；
- 一张图是 4 × 4 块石板，所以每块石板 **2 米**，正好和空间网格的 2 米边长一致；
- 角色 1 米宽，移动时每 0.4 秒（5 m/s）跨过一条板缝，能明显看出速度。

`Test_Arena` 的地面只有 40 米，所以它用同一张贴图的 `MAT_Ground_Arena`（tiling **5 × 5**），每张图仍然是 8 米、每块石板 2 米。

## 导入设置

- Texture Type: Default
- sRGB: on
- Wrap: Repeat
- Filter: Bilinear，Aniso 8
- Mipmaps: on
- Max Size: 2048，压缩 Normal Quality

## 以后要更真实

同目录没有放 Normal / Roughness / AO，是为了先保持一个贴图、一个材质、零额外 keyword。需要时可以把 ambientCG 包里的 NormalGL / Roughness 一起导入，在 `MAT_Ground` 上开 `_NORMALMAP`、`_METALLICGLOSSMAP`；平铺仍用 20 × 20，Normal 的 tiling 保持一致。
