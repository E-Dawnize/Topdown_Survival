using Game.Core;
using Game.Core.Events;
using Game.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 结算面板（I2-10）。运行时用代码搭 uGUI，不改预制体也不改场景，风格与 ShopView 一致：
    /// 独立 Overlay Canvas、全屏压暗背板、居中面板、全程键盘/手柄。
    ///
    /// 触发点是 RunEnded：玩家死亡或打完最后一波后由 RunController 发出，此时 Phase 已经是 GameOver，
    /// 模拟不会再推进（RunSimulation 只在 Battle 态跑战斗七步），所以面板显示期间"游戏是停住的"
    /// 不需要额外去改 Time.timeScale——改时间缩放会把 HUD 的淡出、字体图集这些与游戏时间无关的东西一起冻住。
    ///
    /// 输入切到 UI Map（与商店同一套 Submit / Cancel）。两个动作都走场景：
    ///   Enter 重开一局 —— 重载当前场景（Run / Test_Arena 都能原地重开）
    ///   Esc   回主菜单 —— LoadScene("MainMenu")
    /// 重开不做手工实体清理：场景里的东西由场景重载一起带走。
    /// </summary>
    public sealed class ResultView : UiView
    {
        /// <summary>盖过商店（20）与 HUD（10）：结算之后商店不可能再开，排序取大只是为了不给以后留坑。</summary>
        const int CanvasSortingOrder = 30;

        const float PanelWidth = 720f;
        const float PanelHeight = 460f;

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.13f, 0.97f);
        static readonly Color VictoryColor = new Color(1f, 0.85f, 0.35f, 1f);
        static readonly Color DefeatColor = new Color(0.92f, 0.36f, 0.36f, 1f);
        static readonly Color StatColor = new Color(0.86f, 0.89f, 0.94f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        RunController _runController;
        InputService _input;
        ISceneFlow _sceneFlow;

        GameObject _root;
        Text _titleText;
        Text _durationText;
        Text _waveText;
        Text _coinText;
        Text _killText;

        bool _visible;

        /// <summary>已经按下过键：LoadScene 要到帧末才真的切，这一帧之后再按不该再来一次。</summary>
        bool _leaving;

        /// <summary>装配根在激活之前调用：拿依赖、把面板搭出来。</summary>
        public void Bind(RunController runController, InputService input, ISceneFlow sceneFlow)
        {
            _runController = runController;
            _input = input;
            _sceneFlow = sceneFlow;
            BuildUi();
        }

        protected override void SubscribeAll()
        {
            On<RunEnded>(OnRunEnded);
        }

        /// <summary>
        /// 收尾：把 RunEnded 带出来的摘要写进面板。用摘要而不是回头去读 Model——
        /// RunEnded 就是"这一局结束时的快照"，读 Model 等于再算一次，迟早会和结算数字差一拍。
        /// 波次索引是唯一的例外：死在半路时那一波没打完，摘要里的 WavesCleared 数不到它。
        /// </summary>
        void OnRunEnded(RunEnded e)
        {
            if (_root == null) return;

            ResultLines lines = ResultLines.From(e.Summary, e.Victory,
                _runController != null ? _runController.WaveIndex : 0,
                _runController != null ? _runController.WavesTotal : 0);

            _titleText.text = lines.Title;
            _titleText.color = e.Victory ? VictoryColor : DefeatColor;
            _durationText.text = lines.Duration;
            _waveText.text = lines.Wave;
            _coinText.text = lines.Coins;
            _killText.text = lines.Kills;

            _visible = true;
            _leaving = false;
            _root.SetActive(true);
            _input?.SetUiMode(true);
        }

        void Update()
        {
            if (!_visible || _leaving || _input == null) return;

            // 两键同帧按下时只认第一个：Enter 已经要重开这一局了，不该再顺带回一趟主菜单
            if (_input.UiSubmitPressed) Leave(_sceneFlow, restart: true);
            else if (_input.UiCancelPressed) Leave(_sceneFlow, restart: false);
        }

        void Leave(ISceneFlow sceneFlow, bool restart)
        {
            _leaving = true;

            if (sceneFlow == null)
            {
                Debug.LogError("[ResultView] 没有注入场景切换，按键无效");
                return;
            }

            if (restart) sceneFlow.ReloadCurrent();
            else sceneFlow.LoadMainMenu();
        }

        /// <summary>
        /// 代码搭界面。层级：Canvas → Root（整屏）→ Backdrop（压暗、挡住战场）→ Panel（居中）
        /// → 标题 / 四行摘要 / 键位说明。布局常量与 ShopView 用同一套写法（左上角定位），好一起改。
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
            scaler.matchWidthOrHeight = 0.5f;   // 与 HUD、商店一致

            Font font = UiFont.Resolve();

            RectTransform rootRect = CreateRect(transform, "Root");
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            _root = rootRect.gameObject;

            CreateStretchedImage(rootRect, "Backdrop", BackdropColor);

            RectTransform panel = CreateRect(rootRect, "Panel");
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.anchoredPosition = Vector2.zero;

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = false;

            _titleText = CreateText(panel, "Title", string.Empty, 54, TextAnchor.MiddleCenter, font);
            Place(_titleText.rectTransform, new Vector2(0f, -44f), new Vector2(PanelWidth, 72f));

            _durationText = CreateStatLine(panel, "Duration", font, -140f);
            _waveText = CreateStatLine(panel, "Wave", font, -196f);
            _coinText = CreateStatLine(panel, "Coins", font, -252f);
            _killText = CreateStatLine(panel, "Kills", font, -308f);

            Text legend = CreateText(panel, "Legend", "Enter 再来一局      Esc 返回主菜单", 20, TextAnchor.MiddleCenter,
                font);
            legend.color = HintColor;
            Place(legend.rectTransform, new Vector2(0f, -392f), new Vector2(PanelWidth, 30f));

            _root.SetActive(false);
        }

        Text CreateStatLine(RectTransform panel, string name, Font font, float y)
        {
            Text text = CreateText(panel, name, string.Empty, 28, TextAnchor.MiddleCenter, font);
            text.color = StatColor;
            Place(text.rectTransform, new Vector2(0f, y), new Vector2(PanelWidth, 40f));
            return text;
        }

        static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
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
            image.raycastTarget = false;
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
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>按父容器的左上角摆放：布局常量因此就是"离左上角多少像素"，比算锚点好读（同 ShopView）。</summary>
        static void Place(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }
    }
}
