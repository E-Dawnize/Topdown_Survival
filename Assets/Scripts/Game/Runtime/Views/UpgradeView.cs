using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 升级选卡界面。运行时用代码搭 uGUI，与旧版商店面板同一套风格：
    /// 独立 Overlay Canvas、全屏压暗、横向卡片、全程键盘/手柄。
    ///
    /// 它只读 BuildController.Options，不重算"这张卡现在几级"；
    /// 用户选择回传 RunController.ChooseUpgrade，重抽回传 RunController.RerollUpgrade。
    /// 面板打开时把输入切到 UI Map，关闭时切回 Player Map——否则升级暂停期间玩家还能按出冲刺。
    /// </summary>
    public sealed class UpgradeView : UiView
    {
        const int MaxCards = BuildController.MaxDraftOptions;

        const float PanelWidth = 1500f;
        const float PanelHeight = 760f;
        const float CardWidth = 330f;
        const float CardHeight = 430f;
        const float CardGap = 24f;
        const float NavigateDeadZone = 0.5f;

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.78f);
        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.98f);
        static readonly Color CardColor = new Color(0.14f, 0.15f, 0.19f, 1f);
        static readonly Color SelectionColor = new Color(1f, 0.95f, 0.6f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        sealed class Card
        {
            public GameObject Root;
            public Image Background;
            public Image Highlight;
            public Text Title;
            public Text Level;
            public Text Detail;
        }

        readonly Card[] _cards = new Card[MaxCards];

        BuildController _build;
        RunController _runController;
        InputService _input;

        GameObject _root;
        Text _title;
        Text _hint;
        RectTransform _cardRow;

        bool _visible;
        bool _navLatched;
        int _selected;

        public void Bind(BuildController build, RunController runController, InputService input)
        {
            _build = build;
            _runController = runController;
            _input = input;
            BuildUi();
        }

        protected override void SubscribeAll()
        {
            On<LevelUpStarted>(OnLevelUpStarted);
            On<UpgradeOptionsChanged>(OnOptionsChanged);
            On<LevelUpClosed>(OnLevelUpClosed);
        }

        void OnLevelUpStarted(LevelUpStarted e)
        {
            if (_title != null) _title.text = "等级 " + e.Level + " · 选择一个构筑";
            _selected = 0;
            _navLatched = false;
            _visible = true;
            if (_root != null) _root.SetActive(true);
            _input?.SetUiMode(true);
            Refresh();
        }

        void OnOptionsChanged(UpgradeOptionsChanged e)
        {
            if (!_visible) return;
            Refresh();
        }

        void OnLevelUpClosed(LevelUpClosed e)
        {
            _visible = false;
            if (_root != null) _root.SetActive(false);
            _input?.SetUiMode(false);
        }

        void Update()
        {
            if (!_visible || _build == null) return;

            if (_input == null) return;

            HandleNavigate();

            if (_input.UiSubmitPressed) _runController?.ChooseUpgrade(_selected);
            if (_input.UiRefreshPressed) _runController?.RerollUpgrade();
        }

        void HandleNavigate()
        {
            // 卡片是横向排列的，只认水平轴。W/S 在 UI Map 里也会进 Navigate，
            // 用双轴判断会让"按 W"变成"向右选"。
            float axis = _input.UiNavigate.x;
            if (Mathf.Abs(axis) < NavigateDeadZone)
            {
                _navLatched = false;
                return;
            }

            if (_navLatched) return;
            _navLatched = true;

            int count = _build.Options.Count;
            if (count <= 1) return;

            _selected = (_selected + (axis < 0f ? -1 : 1) + count) % count;
            RefreshSelection();
        }

        void Refresh()
        {
            if (_build == null) return;

            IReadOnlyList<BuildController.DraftOption> options = _build.Options;
            int count = options.Count;

            for (int i = 0; i < _cards.Length; i++)
            {
                Card card = _cards[i];
                bool active = i < count;
                if (card != null) card.Root.SetActive(active);
                if (!active) continue;

                BuildController.DraftOption option = options[i];
                card.Title.text = option.Title;
                card.Title.color = RarityColor(option.Def.Rarity);
                card.Level.text = LevelText(option);
                card.Detail.text = option.Detail;
                card.Background.color = Color.Lerp(CardColor, RarityColor(option.Def.Rarity), 0.10f);
            }

            if (_selected >= count) _selected = count > 0 ? count - 1 : 0;
            if (count == 0) _selected = 0;

            LayoutCards(count);
            RefreshSelection();

            if (_hint != null)
            {
                _hint.text = "← → 选择　　Enter 确认　　R 重抽（剩余 " +
                             (_build.RerollsLeft > 0 ? _build.RerollsLeft.ToString() : "0") + "）";
            }
        }

        void RefreshSelection()
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                Card card = _cards[i];
                if (card == null || !card.Root.activeSelf) continue;
                card.Highlight.color = i == _selected ? SelectionColor : new Color(1f, 1f, 1f, 0.06f);
            }
        }

        void LayoutCards(int count)
        {
            if (_cardRow == null) return;
            if (count <= 0) return;

            float totalWidth = count * CardWidth + (count - 1) * CardGap;
            float startX = -totalWidth * 0.5f + CardWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Card card = _cards[i];
                if (card == null) continue;

                RectTransform rect = (RectTransform)card.Root.transform;
                rect.anchoredPosition = new Vector2(startX + i * (CardWidth + CardGap), 0f);
            }
        }

        static string LevelText(BuildController.DraftOption option)
        {
            if (option.IsNew)
            {
                return option.Def.Category == UpgradeCategory.Weapon
                    ? "新武器 · Lv1"
                    : option.Def.Category == UpgradeCategory.Companion
                        ? "新旅伴 · Lv1"
                        : "新构筑 · Lv1";
            }

            if (option.Def.Category == UpgradeCategory.Buff || option.Def.Category == UpgradeCategory.Stat)
            {
                return "已选 " + option.CurrentLevel + " 次 → " + option.NextLevel;
            }

            return "Lv" + option.CurrentLevel + " → Lv" + option.NextLevel;
        }

        static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Rare: return new Color(0.38f, 0.65f, 1f);
                case Rarity.Epic: return new Color(0.78f, 0.45f, 1f);
                case Rarity.Legendary: return new Color(1f, 0.82f, 0.28f);
                default: return new Color(0.88f, 0.90f, 0.94f);
            }
        }

        void BuildUi()
        {
            UiFactory.AttachCanvas(gameObject, UiLayer.Upgrade);

            RectTransform rootRect = UiFactory.CreateRect(transform, "Root");
            UiFactory.Stretch(rootRect);
            _root = rootRect.gameObject;

            UiFactory.CreateStretchedImage(rootRect, "Backdrop", BackdropColor);

            Image panel = UiFactory.CreatePanel(rootRect, "Panel", new Vector2(PanelWidth, PanelHeight), PanelColor);
            RectTransform panelRect = panel.rectTransform;

            _title = UiFactory.CreateText(panelRect, "Title", "升级", 44, TextAnchor.MiddleCenter, Color.white);
            UiFactory.PlaceTopCenter(_title.rectTransform, new Vector2(0f, -36f), new Vector2(PanelWidth, 60f));

            _hint = UiFactory.CreateText(panelRect, "Hint", string.Empty, 20, TextAnchor.MiddleCenter, HintColor);
            UiFactory.PlaceTopCenter(_hint.rectTransform, new Vector2(0f, -700f), new Vector2(PanelWidth, 30f));

            _cardRow = UiFactory.CreateRect(panelRect, "CardRow");
            UiFactory.PlaceCentered(_cardRow, new Vector2(0f, -20f), new Vector2(PanelWidth, CardHeight));

            for (int i = 0; i < MaxCards; i++) _cards[i] = CreateCard(_cardRow, i);

            _root.SetActive(false);
        }

        Card CreateCard(RectTransform parent, int index)
        {
            Image background = UiFactory.CreatePanel(parent, "Card" + index,
                new Vector2(CardWidth, CardHeight), CardColor);
            RectTransform rect = background.rectTransform;

            var card = new Card
            {
                Root = rect.gameObject,
                Background = background,
                Highlight = UiFactory.CreateStretchedImage(rect, "Highlight", new Color(1f, 1f, 1f, 0.06f)),
                Title = UiFactory.CreateText(rect, "Title", string.Empty, 32, TextAnchor.MiddleCenter, Color.white),
                Level = UiFactory.CreateText(rect, "Level", string.Empty, 22, TextAnchor.MiddleCenter,
                    new Color(0.82f, 0.85f, 0.9f)),
                Detail = UiFactory.CreateText(rect, "Detail", string.Empty, 21, TextAnchor.UpperCenter,
                    new Color(0.74f, 0.78f, 0.84f), HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate),
            };

            UiFactory.PlaceTopCenter(card.Title.rectTransform, new Vector2(0f, -32f),
                new Vector2(CardWidth - 24f, 48f));
            UiFactory.PlaceTopCenter(card.Level.rectTransform, new Vector2(0f, -92f),
                new Vector2(CardWidth - 24f, 30f));
            UiFactory.PlaceTopCenter(card.Detail.rectTransform, new Vector2(0f, -140f),
                new Vector2(CardWidth - 36f, CardHeight - 160f));

            rect.gameObject.SetActive(false);
            return card;
        }
    }
}
