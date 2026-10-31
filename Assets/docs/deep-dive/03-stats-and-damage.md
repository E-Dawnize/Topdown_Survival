# 属性与伤害：怎么落地

## 要解决的问题

一局游戏里会有十几个来源同时改属性：角色基础值、道具、旅伴羁绊、临时 buff、等级。这些来源随时会加进来、撤掉，而且必须能干净地撤掉。如果每个来源直接改最终值，撤掉时就会留下残渣，"卖掉一件道具属性没退"这种 bug 会一直存在。

## 落地步骤

第一步，定三个乘区。

```
final = (base + Σflat) × (1 + Σpercent) × Πmultiply
```

`Flat` 先加，`Percent` 相加后乘一次，`Multiply` 每个各乘一次。绝大多数加成用前两个，少数"独立增伤"用第三个。`Multiply` 的 Value 就是倍数，1.2 表示 +20%，这个语义必须写在注释里，因为两种理解都有人用。

第二步，每条修正带上来源。

```csharp
public readonly struct StatModifier
{
    public readonly StatType Stat;
    public readonly float Value;
    public readonly ModOp Op;
    public readonly object Source;   // 道具实例、buff 实例、羁绊源
}
```

`Source` 用引用相等比较。撤掉一个来源就是 `RemoveBySource(source)`，扫描所有属性把该来源的条目全删掉。羁绊重算"先整条退掉再重建"就是靠这个。

第三步，存储用数组不用字典。`StatType` 是连续枚举，值直接当下标：

```csharp
readonly float[] _baseValue = new float[StatCount];
readonly float[] _cached;
readonly bool[] _upToDate;
readonly List<StatModifier>[] _modifiers;   // 惰性分配
```

绝大多数单位的大多数属性一条修正都没有，预先给 16 个属性各开一个 `List` 是纯浪费，所以第一次 Add 时才建。

第四步，读多写少，用脏标记。`Add` 和 `Remove` 只把对应属性的 `_upToDate` 置 false，下次读才整条重算。稳态读取零分配。

第五步，伤害解算做成纯函数。

```csharp
public static DamageResult Resolve(in DamageInfo damage, in DamageTarget target, float dodgeRoll)
```

顺序固定：闪避 → 无敌帧 → 护甲减伤 → 扣血 → 判死。护甲 `armor / (armor + 50)`。血不能为负，过量伤害记 `Killed = true`、`RemainingHp = 0`。已经 0 血再挨打直接返回全零，不重复判死。

第六步，骰子从外面传进来。闪避的概率判定需要随机，但解算器内部不碰随机流：

```
dodgeRoll ∈ [0,1) 由调用方用 runRng 掷好传入
```

这样才能保证"固定种子 + 固定输入 = 相同结果"，也让单测可以直接传 0.0 或 1.0 把边界钉死。

## 关键决定与代价

`StatType` 直接复用 `Game.Config` 里那份枚举，不在 Model 层重定义。原因是配置表的 `ItemDef.StatA/StatB` 已经引用它，重定义同名枚举会让 Excel 里的道具映射不到属性。

概率属性（闪避、暴击率）在 `StatBlock` 里只 clamp 到 [0,1]，GDD 写的闪避 60% 上限没写进来。上限是数值设计，不是数据结构该管的事。

## 踩过的坑

负护甲。`armor/(armor+50)` 在 `armor < -50` 时结果是正数，`Clamp01` 会把它变成 1，也就是满减伤——负护甲变成无敌。正确做法是先归零再除：

```csharp
if (armor <= 0f) return 0f;
return armor / (armor + ArmorConstant);
```

测试只用了 -30，那一片区间碰巧是安全的（结果是负数，clamp 到 0），所以这个 bug 一直没被抓到。

`EnemySystem` 把玩家闪避传 0，注释写着"骰子该由调用方掷"，但没人掷。玩家的闪避属性长期无效，直到补上随机注入。

## 怎么验证

`StatBlockTests` 11 条：三乘区组合与手算一致、`RemoveBySource` 回基线、同来源多条一起移除、clamp 生效、无修正等于 base、缓存跟随后续 Add/Remove、改 base 生效。

`DamageResolverTests` 14 条：闪避成功与失败、不可闪避、护甲 0/50/150、负护甲、暴击、无敌帧、闪避优先于无敌帧、过量伤害、恰好打死、已死不重复结算。

## 迁移到别的项目要注意

来源对象必须稳定且唯一，用道具实例而不是道具 Id——同一个道具买两件是两个来源，撤掉一件不能把另一件也带走。属性表如果会动态增长，数组方案要改成"预留足够下标"或加一层映射。
