using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 主菜单。MainMenu 场景里原本只有一个相机和一个空的 MenuRoot，没有入口也进不了游戏，
    /// 这里用代码搭一块菜单，不改场景文件（与 ShopView / ResultView 同一套做法：
    /// 独立 Overlay Canvas + 代码摆位 + UiFont 的中文字体）。
    ///
    /// 输入直接读 InputSystem 的键盘，不复用 InputService 的 UI Map：主菜单场景里没有 AppRoot，
    /// 也就没有 InputService 实例。为一个只有"开始"一项的菜单去造一个 InputService（它还会顺带
    /// 建一整套 Player Map 并在 OnEnable 里启用），比读两个键更容易出问题。
    ///
    /// 按 Enter / Space 进 Run 场景。切场景与 Play 时重进主菜单都靠 SceneManager.LoadScene，
    /// 没有跨场景状态要留：这块菜单挂在 MainMenu 场景里，场景卸载时跟着一起没了。
    /// </summary>
    public sealed class MainMenuView : MonoBehaviour
    {
        /// <summary>盖在所有游戏内面板之上（HUD 10 / 商店 20 / 结算 30）。主菜单自成场景，取大只是留个顺序。</summary>
        const int CanvasSortingOrder = 40;

        static readonly Color BackdropColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f, 1f);
        static readonly Color StartColor = new Color(0.45f, 0.88f, 0.78f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        /// <summary>已经按下过开始键。LoadScene 要到帧末才真的切，这一帧之后再按不该再来一次。</summary>
        bool _leaving;

        void Awake()
        {
            BuildUi();
        }

        void Update()
        {
            if (_leaving) return;

            Keyboard keyboard = Keyboard.current;

            // 没有键盘（只有手柄、或设备被拔掉）时菜单照常显示，只是按不动。
            // 手柄要在 InputService 里配 UI Map 才有，而主菜单没有 InputService（见类注释）。
            if (keyboard == null) return;
            if (!keyboard.enterKey.wasPressedThisFrame && !keyboard.spaceKey.wasPressedThisFrame &&
                !keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                return;
            }

            StartRun();
        }

        void StartRun()
        {
            _leaving = true;
            SceneManager.LoadScene(UnitySceneFlow.RunScene);
        }

        /// <summary>
        /// 层级：Canvas → Backdrop（整屏底色，相机那边是天空盒，不加这一层会看到 URP 的天空）
        /// → Title / Start / Hints。
        /// </summary>
        void BuildUi()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Font font = UiFont.Resolve();

            CreateStretchedImage(transform, "Backdrop", BackdropColor);

            Text title = CreateText(transform, "Title", "Topdown Survival", 72, TextAnchor.MiddleCenter, font);
            title.color = TitleColor;
            PlaceCentered(title.rectTransform, new Vector2(0f, 160f), new Vector2(1400f, 100f));

            Text subtitle = CreateText(transform, "Subtitle", "俯视角自动战斗生存", 30, TextAnchor.MiddleCenter, font);
            subtitle.color = HintColor;
            PlaceCentered(subtitle.rectTransform, new Vector2(0f, 80f), new Vector2(1400f, 48f));

            Text start = CreateText(transform, "Start", "按 Enter 或 Space 开始游戏", 44, TextAnchor.MiddleCenter, font);
            start.color = StartColor;
            PlaceCentered(start.rectTransform, new Vector2(0f, -60f), new Vector2(1400f, 72f));

            Text hints = CreateText(transform, "Hints",
                "移动  WASD / 左摇杆　　冲刺  空格　　技能  E　　暂停 / 结束  Esc\n" +
                "波间商店：方向键选格，Enter 买入，R 刷新，Esc 继续\n结算面板：Enter 重开这一局，Esc 回主菜单",
                24, TextAnchor.UpperCenter, font);
            hints.color = HintColor;
            PlaceCentered(hints.rectTransform, new Vector2(0f, -260f), new Vector2(1400f, 160f));
        }

        static Image CreateStretchedImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;   // 全程键盘，不接 EventSystem
            return image;
        }

        static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>按屏幕中心摆放：主菜单是一块居中的竖排文字，比按左上角算好读。</summary>
        static void PlaceCentered(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }
    }
}
