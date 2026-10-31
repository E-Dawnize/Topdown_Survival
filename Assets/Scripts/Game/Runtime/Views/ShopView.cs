using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Services;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 波间商店界面（I2-5）。运行时用代码搭 uGUI，不改预制体也不改场景：
    /// HUD 的布局是长期资产，值得在编辑器里拖；商店是一次性的 6 格面板，
    /// 布局写在代码里改起来更快，也不用再往 UnitPrefabMap 里加一条映射。
    ///
    /// 独立 Canvas（Overlay，sortingOrder 20 &gt; HUD 的 10）：商店要盖住血条与金币条，
    /// 挂在 HUD 下面就得跟着它的层级与缩放一起调。缩放参数与 HUD 保持一致（1920×1080、宽高各半）。
    ///
    /// 全程键盘/手柄，不碰鼠标：不接 EventSystem、不加 GraphicRaycaster，
    /// 只轮询 InputService 的 UI Map（导航轴 + Submit/Cancel/Refresh）。没有事件系统就少一套
    /// "焦点在谁身上"的状态，也就不会出现"鼠标移到别处，键盘还在旧控件上"。
    ///
    /// 选格逻辑在 ShopNavigator（纯 C#，有单测）；这里只负责把选中状态画出来。
    /// </summary>
    public sealed class ShopView : UiView
    {
        /// <summary>本界面盖在所有 HUD 之上：HUD 的 Canvas 是 10。</summary>
        const int CanvasSortingOrder = 20;

        // ---- 布局常量（1920×1080 参考分辨率下的像素）----
        const float PanelWidth = 1240f;
        const float PanelHeight = 760f;
        const float CardWidth = 360f;
        const float CardHeight = 240f;
        const float CardGapX = 30f;
        const float CardGapY = 30f;
        const float GridWidth = ShopNavigator.Columns * CardWidth + (ShopNavigator.Columns - 1) * CardGapX;
        const float GridHeight = ShopNavigator.Rows * CardHeight + (ShopNavigator.Rows - 1) * CardGapY;

        /// <summary>导航轴的死区。键盘的 2DVector 复合是瞬间到 ±1，摇杆要过这个阈值才算推了方向。</summary>
        const float NavigateDeadZone = 0.5f;

        static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.72f);
        static readonly Color PanelColor = new Color(0.09f, 0.10f, 0.13f, 0.97f);
        static readonly Color CardColor = new Color(0.15f, 0.16f, 0.20f, 1f);
        static readonly Color EmptyCardColor = new Color(0.09f, 0.10f, 0.12f, 1f);

        /// <summary>选中框的颜色。刻意不随品质变：品质已经写在名字与价格的颜色上，两种色叠在一起反而看不清选了谁。</summary>
        static readonly Color SelectionColor = new Color(1f, 0.95f, 0.6f, 1f);

        /// <summary>买不了（金币不够 / 叠满）时的文字色。整张卡压成灰的，扫一眼就知道按了也没用。</summary>
        static readonly Color DimColor = new Color(0.42f, 0.44f, 0.48f, 1f);
        static readonly Color DescriptionColor = new Color(0.75f, 0.78f, 0.84f, 1f);
        static readonly Color HintColor = new Color(0.62f, 0.66f, 0.72f, 1f);

        /// <summary>一格卡片的控件。布局建一次，之后只改文本与颜色。</summary>
        sealed class Card
        {
            public Image Highlight;
            public Image Background;
            public Text Name;
            public Text Price;
            public Text Description;
        }

        readonly Card[] _cards = new Card[ShopNavigator.SlotCount];

        ShopNavigator _navigator;
        ShopController _shop;
        RunState _run;
        RunController _runController;
        InputService _input;

        /// <summary>整个界面的开关。压暗的背板也挂在它下面，关掉时连背景一起藏。</summary>
        GameObject _root;
        Text _titleText;
        Text _coinText;
        Text _footerText;

        /// <summary>上次画卡片时的金币数。只在变化时重画：每帧拼 6 张卡的文本等于每帧扔一串字符串进 GC。</summary>
        int _shownCoins = int.MinValue;

        /// <summary>导航轴是否还停在非零方向上，见 <see cref="HandleNavigate"/>。</summary>
        bool _navLatched;

        bool _visible;

        /// <summary>
        /// 装配根在激活之前调用：拿依赖、建界面、把选择器接上"能不能买"的判据。
        /// 判据读的是商店当前格子与 RunState 的金币，所以买完、刷新完都要让选择器 Refresh 一次。
        /// </summary>
        public void Bind(ShopController shop, RunState run, RunController runController, InputService input)
        {
            _shop = shop;
            _run = run;
            _runController = runController;
            _input = input;

            _navigator = new ShopNavigator(IsPurchasable);
            BuildUi();
        }

        protected override void SubscribeAll()
        {
            On<ShopOpened>(OnShopOpened);
            On<ShopClosed>(OnShopClosed);
        }

        /// <summary>
        /// 能不能买直接问 ShopController：空格、金币不够、叠满这三条判据只在它那里写一次。
        /// 界面拿它决定"置灰 / 方向键跳过"，所以不会出现"看着能买、按下去没反应"。
        /// </summary>
        bool IsPurchasable(int index)
        {
            return _shop != null && _shop.CanBuy(index);
        }

        /// <summary>
        /// 开商店：ShopController 在发这个事件之前已经把 6 格抽好，所以这里能直接读。
        /// 打开面板与切输入 Map 放在同一处：面板关了但输入还停在 UI Map 上，玩家会发现自己动不了。
        /// </summary>
        void OnShopOpened(ShopOpened e)
        {
            if (_titleText != null) _titleText.text = "商店 —— 第 " + e.WaveIndex + " 波结束";

            // 自动选中第一个可买的格子：一进来按 Enter 就该能买东西
            _navigator.Refresh();

            _visible = true;
            _navLatched = false;
            if (_root != null) _root.SetActive(true);
            _input?.SetUiMode(true);
            RefreshAllCards();
        }

        void OnShopClosed(ShopClosed _)
        {
            _visible = false;
            if (_root != null) _root.SetActive(false);
            _input?.SetUiMode(false);
        }

        void Update()
        {
            if (!_visible || _shop == null) return;

            // 金币也可能被面板以外的路径改（F3 调试键、以后的祭坛）：变了就重画，
            // 不然卡片会一直按旧的金币数显示"买得起"
            if (_run != null && _run.Coins != _shownCoins) RefreshAllCards();

            // 没有输入服务时面板照常显示，只是按不动：没有 InputService 的调试场景不该崩在空引用上
            if (_input != null) HandleNavigate();

            if (_input == null) return;
            if (_input.UiSubmitPressed) BuySelected();
            if (_input.UiRefreshPressed) RefreshSlots();
            if (_input.UiCancelPressed) _runController?.CloseShop();
        }

        /// <summary>
        /// 方向键 / WASD / 摇杆 → 走一格。轴是模拟量（键盘瞬间到 ±1、摇杆慢慢推），
        /// 所以用"回到死区以内才算松手"的锁存：不锁存的话一帧里轴一直非零，按住右键会一路飞到边。
        /// 锁存之后按一下走一格，长按不连发——6 格的小面板上连发只会选过头。
        /// </summary>
        void HandleNavigate()
        {
            Vector2 axis = _input.UiNavigate;

            if (Mathf.Abs(axis.x) < NavigateDeadZone && Mathf.Abs(axis.y) < NavigateDeadZone)
            {
                _navLatched = false;
                return;
            }

            if (_navLatched) return;
            _navLatched = true;

            // 一次只走一格：两个轴同时有值时取绝对值大的那个，斜着推摇杆不会走成对角线
            if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y))
            {
                MoveSelected(0, axis.x > 0f ? 1 : -1);
            }
            else
            {
                // 屏幕上的"上"是行号变小：行号沿面板从上往下长，摇杆/方向键的 +y 是往上
                MoveSelected(axis.y > 0f ? -1 : 1, 0);
            }
        }

        void MoveSelected(int rowDelta, int columnDelta)
        {
            if (!_navigator.Move(rowDelta, columnDelta)) return;
            RefreshSelection();
        }

        /// <summary>
        /// 买入当前选中格。买完立刻重算可买性并重画：这一格空出来、金币变少（别的卡可能因此置灰），
        /// 选择器会顺延到下一格可买的。
        /// </summary>
        void BuySelected()
        {
            int index = _navigator.Selected;
            if (index < 0) return;
            if (!_shop.TryBuy(index)) return;

            _navigator.Refresh();
            RefreshAllCards();
        }

        /// <summary>
        /// 刷新手牌。价格由 ShopController 说话（每间商店首次免费、之后 10），这里只显示。
        /// 失败只可能是金币不够；给一句话，不然按 R 没反应会像键坏了。
        /// </summary>
        void RefreshSlots()
        {
            if (_shop.TryRefresh())
            {
                _navigator.Refresh();
                RefreshAllCards();
                return;
            }

            ShowFooter("金币不够，刷新要 " + _shop.RefreshPriceNow + " 金币");
        }

        /// <summary>
        /// 把 6 格的当前状态画出来：买走的格子空出来、买不了的整张卡置灰、名字与价格用品质颜色、
        /// 当前格加一圈边框。只在"东西可能变了"的时候调（开商店、买卖、刷新、金币变化）。
        /// </summary>
        void RefreshAllCards()
        {
            _shownCoins = _run != null ? _run.Coins : 0;

            if (_coinText != null) _coinText.text = "金币  " + _shownCoins;

            for (int i = 0; i < _cards.Length; i++)
            {
                Card card = _cards[i];
                ShopSlot slot = _shop != null ? _shop.Slot(i) : default;

                if (slot.IsEmpty)
                {
                    // 买走的格子就留白，不写"已售出"：一块空着的卡比一句话更直接
                    card.Background.color = EmptyCardColor;
                    card.Name.text = string.Empty;
                    card.Price.text = string.Empty;
                    card.Description.text = string.Empty;
                    SetHighlight(card, false);
                    continue;
                }

                // 这一格卖的是道具还是旅伴，只看格子里装的是哪种 Def
                ItemDef def = slot.Item;
                CompanionDef companion = slot.Companion;

                // 置灰的判据与导航用的是同一个方法：不会出现"看着买得起、方向键却选不中"的矛盾
                bool buyable = IsPurchasable(i);
                Color accent = def != null ? RarityColor(def.Rarity) : CompanionColor;

                card.Background.color = CardColor;
                card.Name.text = def != null ? def.Name : companion.Name;
                card.Name.color = buyable ? accent : DimColor;
                card.Price.text = slot.Price + " 金币";
                card.Price.color = buyable ? accent : DimColor;
                card.Description.text = def != null ? def.Description ?? string.Empty : CompanionDescription(companion);
                card.Description.color = buyable ? DescriptionColor : DimColor;

                SetHighlight(card, i == _navigator.Selected);
            }

            UpdateFooter();
        }

        /// <summary>只改选中框：方向键走一格不该把 6 张卡的文本重拼一遍。</summary>
        void RefreshSelection()
        {
            for (int i = 0; i < _cards.Length; i++) SetHighlight(_cards[i], i == _navigator.Selected);
        }

        static void SetHighlight(Card card, bool selected)
        {
            if (card?.Highlight == null) return;
            card.Highlight.color = selected ? SelectionColor : Color.clear;
        }

        /// <summary>
        /// 底部一行：刷新价格与当前能不能买。刷新价随"首次免费/已用过"变，所以不写死。
        /// 一格都买不了的时候换成一句提示：全买完还停在"按 Enter 买入"会让人以为按坏了。
        /// </summary>
        void UpdateFooter()
        {
            if (_footerText == null) return;

            if (!_navigator.HasSelection)
            {
                _footerText.text = "没有能买的了 —— 按 Esc 继续下一波";
                _footerText.color = HintColor;
                return;
            }

            int price = _shop != null ? _shop.RefreshPriceNow : 0;
            _footerText.text = price <= 0 ? "刷新本次免费" : "刷新要 " + price + " 金币";
            _footerText.color = HintColor;
        }

        /// <summary>临时提示（金币不够刷新之类）。下一次重画卡片时会被 UpdateFooter 的正常文案顶掉。</summary>
        void ShowFooter(string message)
        {
            if (_footerText == null) return;
            _footerText.text = message;
            _footerText.color = SelectionColor;
        }

        /// <summary>GDD 8.1 的品质颜色：白灰、蓝、紫、金。</summary>
        static Color RarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Rare: return new Color(0.36f, 0.60f, 1f);
                case ItemRarity.Epic: return new Color(0.72f, 0.43f, 1f);
                case ItemRarity.Legendary: return new Color(1f, 0.79f, 0.26f);
                default: return new Color(0.80f, 0.82f, 0.85f);
            }
        }

        /// <summary>旅伴没有品质列（Companion.json 只有阵营），用一条固定的青色作为强调色。</summary>
        static readonly Color CompanionColor = new Color(0.45f, 0.88f, 0.78f);

        /// <summary>
        /// 旅伴卡的说明。CompanionDef 没有 Description 列，这里用表里真有的信息拼：
        /// 阵营（羁绊靠它统计）与重复购买的规则。文字只在重画卡片时拼，不在每帧。
        /// </summary>
        static string CompanionDescription(CompanionDef def)
        {
            return "旅伴 · " + FactionName(def.Faction) + "阵营　重复购买升 1 级（上限 " + CompanionModel.MaxLevel + " 级）";
        }

        /// <summary>GDD 7.1 的阵营显示名。</summary>
        static string FactionName(CompanionFaction faction)
        {
            switch (faction)
            {
                case CompanionFaction.Machine: return "机械";
                case CompanionFaction.Ghost: return "幽灵";
                case CompanionFaction.Beast: return "野兽";
                case CompanionFaction.Doll: return "玩偶";
                default: return "无";
            }
        }

        /// <summary>
        /// 代码搭界面。层级：Canvas → Backdrop（全屏压暗、挡住战场）→ Panel（居中）
        /// → 标题 / 金币 / 6 格网格 / 键位说明 / 状态行。
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
            scaler.matchWidthOrHeight = 0.5f;   // 与 HUD 一致

            Font font = UiFont.Resolve();

            // 背板与面板都挂在 Root 下面：关商店是关 Root，背板不会留在屏幕上把画面压暗
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

            _titleText = CreateText(panel, "Title", "商店", 34, TextAnchor.MiddleLeft, font);
            Place(_titleText.rectTransform, new Vector2(40f, -26f), new Vector2(760f, 46f));

            _coinText = CreateText(panel, "Coins", "金币  0", 30, TextAnchor.MiddleRight, font);
            PlaceRight(_coinText.rectTransform, new Vector2(-40f, -26f), new Vector2(360f, 46f));

            RectTransform grid = CreateRect(panel, "Grid");
            Place(grid, new Vector2(50f, -110f), new Vector2(GridWidth, GridHeight));
            for (int i = 0; i < _cards.Length; i++) _cards[i] = BuildCard(grid, i, font);

            Text legend = CreateText(panel, "Legend",
                "方向键 / WASD 选择      Enter / Space 买入      R 刷新      Esc 继续下一波",
                20, TextAnchor.MiddleLeft, font);
            legend.color = HintColor;
            Place(legend.rectTransform, new Vector2(50f, -642f), new Vector2(GridWidth, 32f));

            _footerText = CreateText(panel, "Footer", string.Empty, 22, TextAnchor.MiddleLeft, font);
            Place(_footerText.rectTransform, new Vector2(50f, -678f), new Vector2(GridWidth, 36f));

            _root.SetActive(false);
        }

        Card BuildCard(RectTransform grid, int index, Font font)
        {
            RectTransform rect = CreateRect(grid, "Card" + index);
            Place(rect,
                new Vector2(ShopNavigator.ColumnOf(index) * (CardWidth + CardGapX),
                    -ShopNavigator.RowOf(index) * (CardHeight + CardGapY)),
                new Vector2(CardWidth, CardHeight));

            var card = new Card();

            // 选中框画在卡片背后、比卡片大一圈：盖在卡片上方会把名字压住，从背后漏一圈边最清楚
            card.Highlight = CreateStretchedImage(rect, "Highlight", Color.clear, 6f);
            card.Background = CreateStretchedImage(rect, "Background", EmptyCardColor);

            card.Name = CreateText(rect, "Name", string.Empty, 26, TextAnchor.UpperLeft, font);
            Place(card.Name.rectTransform, new Vector2(20f, -18f), new Vector2(CardWidth - 40f, 34f));

            card.Price = CreateText(rect, "Price", string.Empty, 22, TextAnchor.UpperLeft, font);
            Place(card.Price.rectTransform, new Vector2(20f, -58f), new Vector2(CardWidth - 40f, 30f));

            card.Description = CreateText(rect, "Description", string.Empty, 18, TextAnchor.UpperLeft, font);
            Place(card.Description.rectTransform, new Vector2(20f, -100f), new Vector2(CardWidth - 40f, 120f));

            return card;
        }

        static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// 铺满父容器。padding 为负方向的扩展量：选中框要比卡片大一圈，就靠它从背后漏出来。
        /// </summary>
        static Image CreateStretchedImage(Transform parent, string name, Color color, float padding = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-padding, -padding);
            rect.offsetMax = new Vector2(padding, padding);

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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>按父容器的左上角摆放。布局常量因此就是"离左上角多少像素"，比算锚点好读。</summary>
        static void Place(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        static void PlaceRight(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }
    }
}
