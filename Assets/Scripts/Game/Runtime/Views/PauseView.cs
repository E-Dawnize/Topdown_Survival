using Game.Core;
using Game.Core.Events;
using Game.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 游戏内暂停菜单。Esc / 手柄 Start 打开；暂停期间 RunController.Phase = Paused，
    /// RunSimulation 不再推进任何战斗系统，再按 Esc/Enter 或选择“继续游戏”恢复。
    ///
    /// 面板只负责流程编排：暂停状态与计时归 RunController，场景切换归 ISceneFlow。
    /// 输入走 MenuInput，因此即使 InputService 的 UI Map 没切过来，键盘/手柄也能操作。
    /// </summary>
    public sealed class PauseView : UiView
    {
        const float PanelWidth = 680f;
        const float PanelHeight = 430f;

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.98f);
        static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f, 1f);
        static readonly Color OptionColor = new Color(0.84f, 0.87f, 0.92f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.95f, 0.6f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        static readonly string[] Options = { "继续游戏", "返回主菜单", "退出游戏" };

        InputService _input;
        RunController _run;
        ISceneFlow _sceneFlow;

        GameObject _root;
        readonly Text[] _optionTexts = new Text[Options.Length];

        bool _visible;
        bool _leaving;
        bool _navLatched;
        int _selected;
        int _openFrame = -1;

        public bool IsVisible => _visible;

        public void Bind(InputService input, RunController run, ISceneFlow sceneFlow)
        {
            _input = input;
            _run = run;
            _sceneFlow = sceneFlow;
            BuildUi();
        }

        protected override void SubscribeAll()
        {
            On<RunPaused>(OnRunPaused);
            On<RunResumed>(OnRunResumed);
        }

        void OnRunPaused(RunPaused _)
        {
            if (_root == null) return;

            _visible = true;
            _leaving = false;
            _selected = 0;
            _navLatched = false;
            _openFrame = Time.frameCount;
            _root.SetActive(true);
            _input?.SetUiMode(true);
            RefreshSelection();
        }

        void OnRunResumed(RunResumed _)
        {
            Hide();
        }

        void Update()
        {
            if (!_visible || _leaving) return;

            // 打开暂停的同一帧不要立刻把“打开键”当成“确认/取消”，否则手柄 Start 会一闪而过。
            if (Time.frameCount == _openFrame) return;

            HandleNavigate();

            if (MenuInput.Submit(_input))
            {
                Confirm();
                return;
            }

            if (MenuInput.Cancel(_input)) ResumeGame();
        }

        void HandleNavigate()
        {
            float axis = MenuInput.NavigateY(_input);
            if (Mathf.Abs(axis) < 0.5f)
            {
                _navLatched = false;
                return;
            }

            if (_navLatched) return;
            _navLatched = true;

            _selected = (_selected + (int)Mathf.Sign(axis) + Options.Length) % Options.Length;
            RefreshSelection();
        }

        void Confirm()
        {
            switch (_selected)
            {
                case 0:
                    ResumeGame();
                    break;
                case 1:
                    ReturnToMainMenu();
                    break;
                default:
                    QuitGame();
                    break;
            }
        }

        /// <summary>继续游戏。公开给场景流程测试用；正常输入走 Update。</summary>
        public void ResumeGame()
        {
            if (_leaving) return;

            Hide();
            _run?.Resume();
        }

        /// <summary>返回主菜单。公开给场景流程测试用；正常输入走 Update。</summary>
        public void ReturnToMainMenu()
        {
            if (_leaving) return;
            _leaving = true;

            if (_sceneFlow == null)
            {
                Debug.LogError("[PauseView] 没有注入场景切换，无法返回主菜单");
                return;
            }

            _sceneFlow.LoadMainMenu();
        }

        void Hide()
        {
            _visible = false;
            if (_root != null) _root.SetActive(false);
            _input?.SetUiMode(false);
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void RefreshSelection()
        {
            for (int i = 0; i < _optionTexts.Length; i++)
            {
                if (_optionTexts[i] == null) continue;
                bool selected = i == _selected;
                _optionTexts[i].text = (selected ? "▶  " : "　　") + Options[i];
                _optionTexts[i].color = selected ? SelectedColor : OptionColor;
            }
        }

        void BuildUi()
        {
            UiFactory.AttachCanvas(gameObject, UiLayer.Pause);

            RectTransform rootRect = UiFactory.CreateRect(transform, "Root");
            UiFactory.Stretch(rootRect);
            _root = rootRect.gameObject;

            UiFactory.CreateStretchedImage(rootRect, "Backdrop", BackdropColor);

            Image panel = UiFactory.CreatePanel(rootRect, "Panel", new Vector2(PanelWidth, PanelHeight),
                PanelColor);
            RectTransform panelRect = panel.rectTransform;

            Text title = UiFactory.CreateText(panelRect, "Title", "已暂停", 52, TextAnchor.MiddleCenter,
                TitleColor);
            UiFactory.PlaceTopLeft(title.rectTransform, new Vector2(0f, -42f), new Vector2(PanelWidth, 70f));

            Text subtitle = UiFactory.CreateText(panelRect, "Subtitle", "刷怪、计时与战斗已停止",
                20, TextAnchor.MiddleCenter, HintColor);
            UiFactory.PlaceTopLeft(subtitle.rectTransform, new Vector2(0f, -108f), new Vector2(PanelWidth, 30f));

            for (int i = 0; i < Options.Length; i++)
            {
                Text option = UiFactory.CreateText(panelRect, "Option" + i, Options[i], 30,
                    TextAnchor.MiddleCenter, OptionColor);
                UiFactory.PlaceTopLeft(option.rectTransform, new Vector2(0f, -160f - i * 54f),
                    new Vector2(PanelWidth, 42f));
                _optionTexts[i] = option;
            }

            Text hints = UiFactory.CreateText(panelRect, "Hints",
                "↑ ↓ 选择　　Enter 确认　　Esc 继续游戏", 19, TextAnchor.MiddleCenter, HintColor);
            UiFactory.PlaceTopLeft(hints.rectTransform, new Vector2(0f, -350f), new Vector2(PanelWidth, 28f));

            _root.SetActive(false);
        }
    }
}
