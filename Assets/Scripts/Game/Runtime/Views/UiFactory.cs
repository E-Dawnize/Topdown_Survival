using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 运行时搭 uGUI 的最小工具集。只收真正重复的东西：
    ///   - Overlay Canvas + CanvasScaler 的固定参数（1920×1080、Match 0.5）；
    ///   - RectTransform 的三种常用摆法（整屏拉伸 / 左上角 / 顶部居中 / 居中面板）；
    ///   - Text / Image / 背景+填充横条 的创建与它们统一的默认值（不接 EventSystem、不复制字体）。
    ///
    /// 不是 UI 框架：没有布局组、没有主题、没有控件树。各个 View 仍然自己决定摆什么、摆在哪，
    /// 只是不再各自抄一遍 new GameObject(...) 与锚点赋值。
    /// </summary>
    public static class UiFactory
    {
        /// <summary>参考分辨率。HUD 预制体、升级、结算、主菜单、启动错误全都用它，改一处即全改。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>一条「背景 + 填充」的横条。填充是 Image.Type.Filled（水平），fillAmount 就是进度。</summary>
        public readonly struct Bar
        {
            public readonly Image Back;
            public readonly Image Fill;

            public Bar(Image back, Image fill)
            {
                Back = back;
                Fill = fill;
            }
        }

        static Sprite _whiteSprite;

        /// <summary>
        /// 1×1 白图。<see cref="Image.Type.Filled"/> 需要一张 sprite，否则 uGUI 退回纯色四边形，
        /// fillAmount 写了也不会有进度效果。运行时补的条没有美术资源，所以共用这一张。
        /// </summary>
        public static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            _whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            _whiteSprite.name = "UiWhite";
            return _whiteSprite;
        }

        /// <summary>在已有对象上装 Overlay Canvas + CanvasScaler（复用已有组件，不重复添加）。</summary>
        public static Canvas AttachCanvas(GameObject host, int sortingOrder)
        {
            Canvas canvas = host.GetComponent<Canvas>();
            if (canvas == null) canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            // 运行时新建的 Canvas 根 RectTransform 默认可能是 0 尺寸，
            // 子节点用 anchor=0.5 时会以这个 0 尺寸矩形为基准，出现整块菜单偏到一侧。
            // 把根节点显式铺满屏幕，布局才与预制体/编辑器里的 Canvas 一致。
            RectTransform root = host.GetComponent<RectTransform>();
            if (root != null)
            {
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
            }

            CanvasScaler scaler = host.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>新建一个独立的 Overlay Canvas（带 RectTransform）。启动错误面板这种“不属于任何 View”的场景用它。</summary>
        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return AttachCanvas(go, sortingOrder);
        }

        public static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>整屏拉伸：四边贴住父容器，子节点按偏移摆放。</summary>
        public static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>整屏拉伸的 Image：压暗背板、闪屏都是它。</summary>
        public static Image CreateStretchedImage(Transform parent, string name, Color color)
        {
            Image image = CreateImage(parent, name, color);
            Stretch(image.rectTransform);
            return image;
        }

        /// <summary>只建 Image，位置与尺寸交给调用方。</summary>
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;   // 全程键盘/手柄，不接 EventSystem
            return image;
        }

        /// <summary>
        /// 居中的面板底：显式尺寸 + 纯色 Image（不接点击）。
        /// 升级面板、结算面板都是这一种，尺寸不同而已。
        /// </summary>
        public static Image CreatePanel(Transform parent, string name, Vector2 size, Color color)
        {
            RectTransform rect = CreateRect(parent, name);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 建 Text。字体统一走 UiFont（内置 LegacyRuntime 没有中文字形），
        /// 默认不接点击、不裁字。需要自动换行/截断的调用方显式传 overflow。
        /// </summary>
        public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Color color, HorizontalWrapMode horizontal = HorizontalWrapMode.Overflow,
            VerticalWrapMode vertical = VerticalWrapMode.Overflow)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = UiFont.Resolve();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = horizontal;
            text.verticalOverflow = vertical;
            return text;
        }

        /// <summary>
        /// 一条「背景 + 填充」的横条，返回填充 Image。
        /// anchor 与 pivot 成对给出：左上角用 (0,1)/(0,1)，顶部居中用 (0.5,1)/(0.5,0.5)。
        /// 填充四边内缩 <paramref name="inset"/> 像素，看起来像嵌在背板里。
        /// </summary>
        public static Bar CreateBar(Transform parent, string backName, string fillName, Vector2 anchor, Vector2 position,
            Vector2 size, Color backColor, Color fillColor, Vector2 pivot, float inset = 2f)
        {
            RectTransform backRect = CreateRect(parent, backName);
            backRect.anchorMin = anchor;
            backRect.anchorMax = anchor;
            backRect.pivot = pivot;
            backRect.sizeDelta = size;
            backRect.anchoredPosition = position;

            var back = backRect.gameObject.AddComponent<Image>();
            back.color = backColor;
            back.raycastTarget = false;

            RectTransform fillRect = CreateRect(backRect, fillName);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(inset, inset);
            fillRect.offsetMax = new Vector2(-inset, -inset);

            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.raycastTarget = false;
            MakeFillable(fill);
            return new Bar(back, fill);
        }

        /// <summary>把一张 Image 变成可写 fillAmount 的横向进度条（缺 sprite 时补白图）。</summary>
        public static void MakeFillable(Image image)
        {
            if (image == null) return;
            if (image.sprite == null) image.sprite = WhiteSprite();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
        }

        /// <summary>按父容器左上角摆放：布局常量因此就是“离左上角多少像素”。</summary>
        public static void PlaceTopLeft(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>按父容器顶边中点摆放：标题、提示行用这一种。</summary>
        public static void PlaceTopCenter(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>按父容器中心摆放：主菜单那种整块居中的竖排文字。</summary>
        public static void PlaceCentered(RectTransform rect, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        /// <summary>
        /// 启动失败面板：整屏压暗 + 一行红色说明。挂在装配根下，不依赖 HUD。
        /// 装配根本身不需要知道 uGUI 怎么搭，只要把 title/detail 丢进来。
        /// </summary>
        public static GameObject CreateStartupError(Transform parent, string title, string detail)
        {
            Canvas canvas = CreateCanvas(parent, "StartupError", UiLayer.StartupError);
            Transform root = canvas.transform;

            CreateStretchedImage(root, "Backdrop", new Color(0.05f, 0.06f, 0.08f, 1f));

            Text text = CreateText(root, "Message",
                "<b>" + title + "</b>\n\n" + (string.IsNullOrEmpty(detail) ? "未知错误" : detail),
                26, TextAnchor.MiddleCenter, new Color(0.92f, 0.36f, 0.36f, 1f),
                HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow);
            PlaceCentered(text.rectTransform, Vector2.zero, new Vector2(1200f, 420f));

            return canvas.gameObject;
        }
    }
}
