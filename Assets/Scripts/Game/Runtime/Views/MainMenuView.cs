using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 主菜单。MainMenu 场景里原本只有一个相机和一个空的 MenuRoot，没有入口也进不了游戏，
    /// 这里用代码搭一块菜单，不改场景文件（与 UpgradeView / ResultView 同一套做法：
    /// 独立 Overlay Canvas + 代码摆位 + UiFont 的中文字体）。
    ///
    /// 输入直接读 InputSystem 的键盘，不复用 InputService 的 UI Map：主菜单场景里没有 AppRoot，
    /// 也就没有 InputService 实例。为一个只有"开始"一项的菜单去造一个 InputService（它还会顺带
    /// 建一整套 Player Map 并在 OnEnable 里启用），比读两个键更容易出问题。
    ///
    /// 按 Enter / Space 进 Run 场景。切场景统一走 ISceneFlow，主菜单不直接碰 SceneManager；
    /// 没有跨场景状态要留：这块菜单挂在 MainMenu 场景里，场景卸载时跟着一起没了。
    /// </summary>
    public sealed class MainMenuView : MonoBehaviour
    {
        static readonly Color BackdropColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f, 1f);
        static readonly Color StartColor = new Color(0.45f, 0.88f, 0.78f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        ISceneFlow _sceneFlow;

        /// <summary>已经按下过开始键。LoadScene 要到帧末才真的切，这一帧之后再按不该再来一次。</summary>
        bool _leaving;

        public void Initialize(ISceneFlow sceneFlow)
        {
            _sceneFlow = sceneFlow;
        }

        void Awake()
        {
            BuildUi();
        }

        void Update()
        {
            if (_leaving) return;

            if (MenuInput.Submit(null))
            {
                StartRun();
                return;
            }

            if (MenuInput.Cancel(null)) QuitGame();
        }

        /// <summary>菜单上的“开始”动作。公开是为了让场景切换测试不必伪造键盘事件。</summary>
        public void StartRun()
        {
            if (_sceneFlow == null)
            {
                Debug.LogError("[MainMenu] 没有注入场景切换，无法进入 Run");
                return;
            }

            _leaving = true;
            _sceneFlow.LoadRun();
        }

        /// <summary>独立构建里按 Esc / 手柄 B 退出；编辑器里直接结束 Play，方便手测。</summary>
        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 层级：Canvas → Backdrop（整屏底色，相机那边是天空盒，不加这一层会看到 URP 的天空）
        /// → Title / Start / Hints。
        /// </summary>
        void BuildUi()
        {
            UiFactory.AttachCanvas(gameObject, UiLayer.Menu);

            UiFactory.CreateStretchedImage(transform, "Backdrop", BackdropColor);

            Text title = UiFactory.CreateText(transform, "Title", "Topdown Survival", 72, TextAnchor.MiddleCenter,
                TitleColor);
            UiFactory.PlaceCentered(title.rectTransform, new Vector2(0f, 160f), new Vector2(1400f, 100f));

            Text subtitle = UiFactory.CreateText(transform, "Subtitle", "俯视角自动战斗生存", 30,
                TextAnchor.MiddleCenter, HintColor);
            UiFactory.PlaceCentered(subtitle.rectTransform, new Vector2(0f, 80f), new Vector2(1400f, 48f));

            Text start = UiFactory.CreateText(transform, "Start", "按 Enter 或 Space 开始游戏", 44,
                TextAnchor.MiddleCenter, StartColor);
            UiFactory.PlaceCentered(start.rectTransform, new Vector2(0f, -60f), new Vector2(1400f, 72f));

            Text hints = UiFactory.CreateText(transform, "Hints",
                "移动  WASD / 左摇杆　　冲刺  空格　　技能  E\n" +
                "生存到时间尽头，BOSS 会出现。捡经验升级，三选一构筑\n" +
                "升级面板：← → 选择，Enter 确认，R 重抽\n" +
                "结算面板：Enter 重开这一局，Esc 回主菜单",
                24, TextAnchor.UpperCenter, HintColor, HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate);
            UiFactory.PlaceCentered(hints.rectTransform, new Vector2(0f, -260f), new Vector2(1400f, 160f));
        }
    }
}
