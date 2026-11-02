using Game.Core;
using Game.Core.Events;
using Game.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 结算面板。运行时用代码搭 uGUI，不改预制体也不改场景，风格与 UpgradeView 一致：
    /// 独立 Overlay Canvas、全屏压暗背板、居中面板、全程键盘/手柄。
    ///
    /// 触发点是 RunEnded：玩家死亡或击杀 BOSS 后由 RunController 发出，此时 Phase 已经是 GameOver，
    /// 模拟不会再推进（RunSimulation 只在 Battle 态跑战斗七步），所以面板显示期间"游戏是停住的"
    /// 不需要额外去改 Time.timeScale——改时间缩放会把 HUD 的淡出、字体图集这些与游戏时间无关的东西一起冻住。
    ///
    /// 输入切到 UI Map（与升级面板同一套 Submit / Cancel）。两个动作都走场景：
    ///   Enter 重开一局 —— 重载当前场景（Run / Test_Arena 都能原地重开）
    ///   Esc   回主菜单 —— LoadScene("MainMenu")
    /// 重开不做手工实体清理：场景里的东西由场景重载一起带走。
    /// </summary>
    public sealed class ResultView : UiView
    {
        const float PanelWidth = 720f;
        const float PanelHeight = 460f;

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.13f, 0.97f);
        static readonly Color VictoryColor = new Color(1f, 0.85f, 0.35f, 1f);
        static readonly Color DefeatColor = new Color(0.92f, 0.36f, 0.36f, 1f);
        static readonly Color StatColor = new Color(0.86f, 0.89f, 0.94f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        InputService _input;
        ISceneFlow _sceneFlow;

        GameObject _root;
        Text _titleText;
        Text _durationText;
        Text _levelText;
        Text _killText;
        Text _buildText;

        bool _visible;
        int _openFrame = -1;

        /// <summary>场景流程测试用：结算面板是否已显示。</summary>
        public bool IsVisible => _visible;

        /// <summary>已经按下过键：LoadScene 要到帧末才真的切，这一帧之后再按不该再来一次。</summary>
        bool _leaving;

        /// <summary>装配根在激活之前调用：拿依赖、把面板搭出来。</summary>
        public void Bind(InputService input, ISceneFlow sceneFlow)
        {
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
        /// </summary>
        void OnRunEnded(RunEnded e)
        {
            if (_root == null) return;

            ResultLines lines = ResultLines.From(e.Summary, e.Victory);

            _titleText.text = lines.Title;
            _titleText.color = e.Victory ? VictoryColor : DefeatColor;
            _durationText.text = lines.Duration;
            _levelText.text = lines.Level;
            _killText.text = lines.Kills;
            _buildText.text = lines.Build;

            _visible = true;
            _leaving = false;
            _openFrame = Time.frameCount;
            _root.SetActive(true);
            _input?.SetUiMode(true);
        }

        void Update()
        {
            if (!_visible || _leaving) return;

            // 死亡/通关可能和最后一次按键在同一帧发生；这一帧不要把同一个按键当成重开/回菜单。
            if (Time.frameCount == _openFrame) return;

            // 两键同帧按下时只认第一个：Enter 已经要重开这一局了，不该再顺带回一趟主菜单。
            // MenuInput 同时覆盖 InputService 的 UI Map 与设备直读，测试/热重载下不会失灵。
            if (MenuInput.Submit(_input)) Leave(_sceneFlow, restart: true);
            else if (MenuInput.Cancel(_input)) Leave(_sceneFlow, restart: false);
        }

        /// <summary>重开这一局。公开给场景流程测试用；正常输入仍走 Update 的 MenuInput。</summary>
        public void RestartRun() => Leave(_sceneFlow, restart: true);

        /// <summary>回主菜单。公开给场景流程测试用；正常输入仍走 Update 的 MenuInput。</summary>
        public void ReturnToMainMenu() => Leave(_sceneFlow, restart: false);

        void Leave(ISceneFlow sceneFlow, bool restart)
        {
            if (_leaving) return;
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
        /// → 标题 / 四行摘要 / 键位说明。布局常量与 UpgradeView 用同一套写法（左上角定位），好一起改。
        /// </summary>
        void BuildUi()
        {
            UiFactory.AttachCanvas(gameObject, UiLayer.Result);

            RectTransform rootRect = UiFactory.CreateRect(transform, "Root");
            UiFactory.Stretch(rootRect);
            _root = rootRect.gameObject;

            UiFactory.CreateStretchedImage(rootRect, "Backdrop", BackdropColor);

            Image panel = UiFactory.CreatePanel(rootRect, "Panel", new Vector2(PanelWidth, PanelHeight), PanelColor);
            RectTransform panelRect = panel.rectTransform;

            _titleText = UiFactory.CreateText(panelRect, "Title", string.Empty, 54, TextAnchor.MiddleCenter, Color.white);
            UiFactory.PlaceTopLeft(_titleText.rectTransform, new Vector2(0f, -44f), new Vector2(PanelWidth, 72f));

            _durationText = CreateStatLine(panelRect, "Duration", -140f);
            _levelText = CreateStatLine(panelRect, "Level", -196f);
            _killText = CreateStatLine(panelRect, "Kills", -252f);
            _buildText = CreateStatLine(panelRect, "Build", -308f);

            Text legend = UiFactory.CreateText(panelRect, "Legend", "Enter 再来一局      Esc 返回主菜单", 20,
                TextAnchor.MiddleCenter, HintColor);
            UiFactory.PlaceTopLeft(legend.rectTransform, new Vector2(0f, -392f), new Vector2(PanelWidth, 30f));

            _root.SetActive(false);
        }

        static Text CreateStatLine(RectTransform panel, string name, float y)
        {
            Text text = UiFactory.CreateText(panel, name, string.Empty, 28, TextAnchor.MiddleCenter, StatColor);
            UiFactory.PlaceTopLeft(text.rectTransform, new Vector2(0f, y), new Vector2(PanelWidth, 40f));
            return text;
        }
    }
}
