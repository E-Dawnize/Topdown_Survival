# UI 与输入：怎么落地

## 要解决的问题

商店和结算要做成键盘可操作、不依赖鼠标，而且项目里已经没有场景生成器了，预制体和场景都归手工维护。在这种约束下加界面，要么改资源、要么代码生成。

## 落地步骤

第一步，选代码生成，不碰预制体与场景。原因很实际：场景和预制体是手工维护的唯一真源，脚本生成的东西进去以后，下次想重建就分不清哪部分是代码建的、哪部分是人摆的。

```csharp
var go = new GameObject("ShopCanvas", typeof(RectTransform));
var canvas = go.AddComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
canvas.sortingOrder = 20;
go.AddComponent<CanvasScaler>();
```

第二步，把订阅收进基类。`UiView` 只做一件事：子类在 `SubscribeAll()` 里用 `On<T>()` 登记，隐藏时基类统一退订，子类不用自己记。

约定是预制体根节点保持 inactive，装配根先 `Initialize(bus)` 再 `SetActive(true)`。漏注入时基类直接报错，不静默放行。踩过这个坑：预制体根节点是激活的，实例化那一刻 `OnEnable` 就跑了，此时 `Bus` 还是 null，订阅被静默跳过。

第三步，把能测的部分抽成纯类。商店的选格逻辑是唯一值得测的：移动、边界、跳过不可买的格子、全买完的空状态。

```csharp
public sealed class ShopNavigator
{
    public int Selected { get; }
    public bool Move(int dx, int dy);
    public bool Select(int index);
}
```

视图只负责把 `Selected` 画出来，不参与判定。

第四步，输入用两个 ActionMap 切换。Player 管移动与冲刺，UI 管 Navigate（WASD 加方向键）、Submit（Enter 加 Space）、Cancel、Refresh。进商店和结算切 UI，关掉切回 Player。切换点和面板开关写在同一处，避免"面板关了但输入还留在 UI"。

第五步，主菜单用运行时钩子，不改场景。

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void Install()
{
    SceneManager.sceneLoaded -= OnSceneLoaded;
    SceneManager.sceneLoaded += OnSceneLoaded;
    OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
}
```

用 `sceneLoaded` 而不是只建一次：从结算按 Esc 回主菜单会再 `LoadScene` 一次，那时 `RuntimeInitializeOnLoadMethod` 已经不会跑了。启动时手动补调一次，是因为 `AfterSceneLoad` 阶段第一个场景的事件已经发过。

先退订再订阅，并检查已存在的实例：编辑器关掉域重载时静态字段会活过两次 Play，不去重会搭出两块菜单。

## 关键决定与代价

代码生成的代价是布局只能靠数值调，看不见。面板的位置、字号、换行全部按 16:9 估过一遍，没有在别的分辨率上验证。

选格逻辑抽成纯类多了一层间接，但换来 15 条可跑的测试。

## 踩过的坑

`Esc` 关店后 `ShopClosed` 要等下一次事件分发才走，输入切回 Player 晚一帧，损失一帧移动输入。这是事件延后分发的固有代价，没绕。

中文字形：内置 `LegacyRuntime` 没有中文字形，预制体里现成的文字全是方块。运行时用系统字体替换，Windows 上可用，换平台要换方案。

## 怎么验证

`ShopNavigatorTests` 15 条：首格自动选中、左右跳过不可买、上下同列、四个边界不绕行、整列不可买原地不动、全买完空状态、刷新后保持可选、买完顺延、买走最后一格绕回第一格。

`HudValuesTests` 7 条验差值刷新。

排版、字形、排序层级、键位实际响应、摇杆死区、手柄按键一个都没测过，只能进 Unity 看。

## 迁移到别的项目要注意

`RuntimeInitializeOnLoadMethod` 在每个进程只跑一次，任何"场景加载后要做的事"都必须挂 `sceneLoaded`，否则第二次加载场景就失效。UI 的排序层级要显式给，默认全是 0。
