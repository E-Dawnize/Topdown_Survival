using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// HUD 的控件集合：预制体上摆好的那几个 + 运行时补的三块（受伤闪屏、状态行、经验条、BOSS 条）。
    ///
    /// 抽出来的原因不是“分层好看”，而是 HudView 的 Bind 会在根节点 SetActive 之前跑：
    /// 那时预制体上的引用已经可写，但 Awake 还没执行。把“建控件”做成一个幂等的、只依赖
    /// 序列化引用的 Build，Bind 与 Awake 谁先到都能拿到完整的一套控件，不用靠 Awake 里
    /// 重置 _hasShown 来掩盖“第一次 Refresh 发生在控件创建之前”。
    ///
    /// 这里只管创建与摆放；什么时候写值、写哪些值归 HudView（Diff）与 HudValues（取值）。
    /// </summary>
    public sealed class HudWidgets
    {
        // 运行时补的三块条/行的尺寸与配色。与预制体里的 HpBack/DashBack 同一套视觉语言。
        static readonly Color DamageFlashColor = new Color(0.85f, 0.08f, 0.08f, 0f);
        static readonly Color StatusColor = new Color(0.82f, 0.85f, 0.9f, 1f);
        static readonly Color XpBackColor = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color XpFillColor = new Color(0.35f, 0.85f, 0.95f, 1f);
        static readonly Color BossBackColor = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color BossFillColor = new Color(0.88f, 0.25f, 0.28f, 1f);
        static readonly Color BossNameColor = new Color(1f, 0.72f, 0.35f, 1f);
        static readonly Color EquipmentBackColor = new Color(0.05f, 0.06f, 0.08f, 0.55f);
        static readonly Color EquipmentColor = new Color(0.82f, 0.86f, 0.93f, 1f);
        static readonly Color DamageColor = new Color(1f, 0.42f, 0.36f, 0f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        public Image HpFill;
        public Text HpText;
        public Text ProgressText;
        public Text ThreatText;
        public Image DashFill;
        public Text DashText;
        public GameObject HintBackdrop;
        public Text HintText;

        public Image DamageFlash;
        public Text StatusLine;
        public Image XpFill;
        public GameObject BossRoot;
        public Image BossFill;
        public Text BossName;

        /// <summary>当前装备（武器/旅伴/组合/数值与 buff 数量）。文字在 HudView 里按版本号重拼。</summary>
        public Text EquipmentText;

        /// <summary>玩家受击时从血条右侧弹出的伤害数字，让“掉了多少血、什么时候掉的”可见。</summary>
        public Text DamageText;

        /// <summary>
        /// 用预制体上的引用 + 新建的运行时控件拼出完整的一套。
        /// 只在 HudView 里调一次，之后所有刷新都直接写字段。
        /// </summary>
        public static HudWidgets Build(Transform root, Image hpFill, Text hpText, Text progressText, Text threatText,
            Image dashFill, Text dashText, GameObject hintBackdrop, Text hintText)
        {
            var widgets = new HudWidgets
            {
                HpFill = hpFill,
                HpText = hpText,
                ProgressText = progressText,
                ThreatText = threatText,
                DashFill = dashFill,
                DashText = dashText,
                HintBackdrop = hintBackdrop,
                HintText = hintText
            };

            // 预制体里的两条 Fill 是 Image.Type.Filled 但没给 Sprite：不补白图的话
            // fillAmount 写了也不会有进度效果（血条一直满着）。
            UiFactory.MakeFillable(widgets.HpFill);
            UiFactory.MakeFillable(widgets.DashFill);

            widgets.DamageFlash = UiFactory.CreateStretchedImage(root, "DamageFlash", DamageFlashColor);
            widgets.DamageFlash.enabled = false;

            widgets.BuildStatusLine(root);
            widgets.BuildEquipmentPanel(root);
            widgets.BuildDamageText(root);
            widgets.BuildXpBar(root);
            widgets.BuildBossBar(root);

            // 预制体里写的字体是内置 LegacyRuntime（没有中文字形），运行时统一换成系统字体。
            Font font = UiFont.Resolve();
            if (font != null)
            {
                foreach (Text text in root.GetComponentsInChildren<Text>(true)) text.font = font;
            }

            return widgets;
        }

        void BuildStatusLine(Transform root)
        {
            StatusLine = UiFactory.CreateText(root, "StatusLine", string.Empty, 18, TextAnchor.MiddleCenter,
                StatusColor);
            UiFactory.PlaceTopCenter(StatusLine.rectTransform, new Vector2(0f, -62f), new Vector2(900f, 26f));
        }

        /// <summary>等级经验条：左上角血条/等级文字下面，与进度文字同一个视觉位置。</summary>
        void BuildXpBar(Transform root)
        {
            UiFactory.Bar bar = UiFactory.CreateBar(root, "XpBack", "XpFill", TopLeft, new Vector2(26f, -84f),
                new Vector2(320f, 10f), XpBackColor, XpFillColor, TopLeft);
            XpFill = bar.Fill;
        }

        /// <summary>
        /// 左上角的装备栏。放在血条/经验条下面，不与顶部状态行和底部按键区争位置。
        /// 内容由 HudView 按 BuildController.LoadoutRevision 重拼，这里只负责建框与文字。
        /// </summary>
        void BuildEquipmentPanel(Transform root)
        {
            RectTransform panel = UiFactory.CreateRect(root, "EquipmentPanel");
            UiFactory.PlaceTopLeft(panel, new Vector2(26f, -108f), new Vector2(444f, 296f));

            var back = panel.gameObject.AddComponent<Image>();
            back.color = EquipmentBackColor;
            back.raycastTarget = false;

            EquipmentText = UiFactory.CreateText(panel, "EquipmentText", string.Empty, 14, TextAnchor.UpperLeft,
                EquipmentColor, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow);
            EquipmentText.lineSpacing = 1.15f;

            RectTransform text = EquipmentText.rectTransform;
            text.anchorMin = Vector2.zero;
            text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(12f, 10f);
            text.offsetMax = new Vector2(-12f, -10f);
        }

        void BuildDamageText(Transform root)
        {
            DamageText = UiFactory.CreateText(root, "DamageText", string.Empty, 20, TextAnchor.MiddleLeft,
                DamageColor);
            UiFactory.PlaceTopLeft(DamageText.rectTransform, new Vector2(358f, -20f), new Vector2(180f, 28f));
            DamageText.enabled = false;
        }

        /// <summary>BOSS 条：顶部中间，状态行下方。隐藏时不占画面。</summary>
        void BuildBossBar(Transform root)
        {
            RectTransform bossRoot = UiFactory.CreateRect(root, "BossBar");
            UiFactory.PlaceTopCenter(bossRoot, new Vector2(0f, -96f), new Vector2(760f, 42f));

            BossRoot = bossRoot.gameObject;
            BossName = UiFactory.CreateText(bossRoot, "BossName", string.Empty, 20, TextAnchor.MiddleCenter,
                BossNameColor);
            UiFactory.PlaceTopCenter(BossName.rectTransform, Vector2.zero, new Vector2(760f, 24f));

            UiFactory.Bar bar = UiFactory.CreateBar(bossRoot, "BossBack", "BossFill", TopCenter,
                new Vector2(0f, -26f), new Vector2(760f, 12f), BossBackColor, BossFillColor,
                new Vector2(0.5f, 0.5f));
            BossFill = bar.Fill;

            BossRoot.SetActive(false);
        }
    }
}
