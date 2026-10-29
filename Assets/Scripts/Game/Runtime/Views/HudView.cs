using Game.Core;
using Game.Core.Events;
using Game.Model;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 基础 HUD：血条、金币、波次、冲刺冷却。
    /// 全部用代码搭，不依赖 prefab，也不用 TextMeshPro（项目还没导入 TMP Essentials）。
    /// 只读 Model，不写。
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField] bool _enableDebugKeys = true;

        static Sprite _whiteSprite;

        Font _font;
        Image _hpFill;
        Text _hpText;
        Text _coinText;
        Text _waveText;
        Image _dashFill;
        Text _dashText;
        GameObject _hintBackdrop;
        Text _hintText;
        float _hintAlpha;

        PlayerModel _player;
        RunState _run;
        IEventBus _bus;

        void Awake()
        {
            _font = ResolveFont();
            BuildUi();
        }

        void OnEnable()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null) return;
            _player = root.Player;
            _run = root.Run;
            _bus = root.Events;
            _bus?.Subscribe<CoinChanged>(OnCoinChanged);
        }

        void OnDisable()
        {
            _bus?.Unsubscribe<CoinChanged>(OnCoinChanged);
            _bus = null;
        }

        void LateUpdate()
        {
            HandleDebugKeys();
            Refresh();
            FadeHint();
        }

        void HandleDebugKeys()
        {
            if (!_enableDebugKeys || Keyboard.current == null) return;

            if (Keyboard.current.f2Key.wasPressedThisFrame && _player != null)
            {
                _player.HP = Mathf.Max(0f, _player.HP - 10f);
                ShowHint("调试：扣 10 生命");
            }

            if (Keyboard.current.f3Key.wasPressedThisFrame && _run != null)
            {
                _run.AddCoins(10);
                _bus?.Publish(new CoinChanged(_run.Coins, 10));
                ShowHint("调试：加 10 金币");
            }
        }

        void OnCoinChanged(CoinChanged e)
        {
            if (_coinText != null)
            {
                _coinText.text = "金币  " + e.Current;
            }
        }

        void Refresh()
        {
            if (_hpFill != null)
            {
                float ratio = _player != null ? _player.HpRatio : 0f;
                _hpFill.fillAmount = ratio;
                _hpFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.25f), new Color(0.35f, 0.8f, 0.45f), ratio);
            }

            if (_hpText != null)
            {
                _hpText.text = _player != null
                    ? Mathf.CeilToInt(_player.HP) + " / " + Mathf.CeilToInt(_player.MaxHP)
                    : "-- / --";
            }

            if (_coinText != null && _run != null)
            {
                _coinText.text = "金币  " + _run.Coins;
            }

            if (_waveText != null && _run != null)
            {
                _waveText.text = "第 " + _run.WaveIndex + " / " + _run.WavesTotal + " 波";
            }

            if (_dashFill != null && _dashText != null)
            {
                float ratio = 1f;
                if (_player != null && _player.DashCooldown > 0f)
                {
                    ratio = 1f - Mathf.Clamp01(_player.DashCooldownTimer / _player.DashCooldown);
                }

                _dashFill.fillAmount = ratio;

                if (_player == null)
                {
                    _dashText.text = "冲刺";
                }
                else if (_player.DashCooldownTimer > 0f)
                {
                    _dashText.text = "冲刺  " + _player.DashCooldownTimer.ToString("0.0") + "s";
                    _dashText.color = new Color(0.75f, 0.78f, 0.85f, 0.8f);
                }
                else
                {
                    _dashText.text = "冲刺就绪";
                    _dashText.color = new Color(0.55f, 0.9f, 1f, 1f);
                }
            }
        }

        void ShowHint(string message)
        {
            if (_hintText == null || _hintBackdrop == null) return;
            _hintText.text = message;
            _hintAlpha = 1.2f;
            Color c = _hintText.color;
            c.a = 1f;
            _hintText.color = c;
            _hintBackdrop.SetActive(true);
        }

        void FadeHint()
        {
            if (_hintBackdrop == null || !_hintBackdrop.activeSelf) return;
            _hintAlpha -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(_hintAlpha);
            Color c = _hintText.color;
            c.a = a;
            _hintText.color = c;
            if (_hintAlpha <= 0f)
            {
                _hintBackdrop.SetActive(false);
            }
        }

        // ---- UI 构建 ----

        static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null) return _whiteSprite;

                // Image 的 Filled 模式在 sprite 为空时不生效，必须给一张图
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "HudWhite",
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();

                _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
                _whiteSprite.name = "HudWhite";
                return _whiteSprite;
            }
        }

        static Font ResolveFont()
        {
            Font f = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 18);

            if (f != null) return f;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("HUD Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform root = canvasGo.transform;

            // 血条
            RectTransform hpBack = CreateRect("HpBack", root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -24f), new Vector2(320f, 26f));
            AddImage(hpBack, new Color(0.08f, 0.09f, 0.11f, 0.85f));

            RectTransform hpFillRect = CreateRect("HpFill", hpBack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(3f, 0f), new Vector2(314f, 20f));
            _hpFill = AddImage(hpFillRect, new Color(0.35f, 0.8f, 0.45f));
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillOrigin = 0;

            RectTransform hpTextRect = CreateRect("HpText", hpBack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(290f, 22f));
            _hpText = AddText(hpTextRect, "100 / 100", 15, TextAnchor.MiddleLeft, Color.white);

            // 金币
            RectTransform coinRect = CreateRect("CoinText", root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(26f, -58f), new Vector2(320f, 26f));
            _coinText = AddText(coinRect, "金币  0", 20, TextAnchor.MiddleLeft, new Color(1f, 0.86f, 0.4f));

            // 波次
            RectTransform waveRect = CreateRect("WaveText", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(400f, 32f));
            _waveText = AddText(waveRect, "第 1 / 5 波", 22, TextAnchor.MiddleCenter, new Color(0.9f, 0.92f, 0.96f));

            // 冲刺
            RectTransform dashBack = CreateRect("DashBack", root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 24f), new Vector2(220f, 52f));
            AddImage(dashBack, new Color(0.08f, 0.09f, 0.11f, 0.85f));

            RectTransform dashFillRect = CreateRect("DashFill", dashBack, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(3f, 3f), new Vector2(214f, 8f));
            _dashFill = AddImage(dashFillRect, new Color(0.55f, 0.9f, 1f));
            _dashFill.type = Image.Type.Filled;
            _dashFill.fillMethod = Image.FillMethod.Horizontal;
            _dashFill.fillOrigin = 0;

            RectTransform dashTextRect = CreateRect("DashText", dashBack, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 6f), new Vector2(210f, 26f));
            _dashText = AddText(dashTextRect, "冲刺就绪", 17, TextAnchor.MiddleCenter, new Color(0.55f, 0.9f, 1f));

            // 调试提示
            RectTransform hintRect = CreateRect("Hint", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 92f), new Vector2(440f, 40f));
            AddImage(hintRect, new Color(0f, 0f, 0f, 0.55f));
            _hintBackdrop = hintRect.gameObject;

            RectTransform hintTextRect = CreateRect("HintText", hintRect, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 34f));
            _hintText = AddText(hintTextRect, "", 18, TextAnchor.MiddleCenter, Color.white);
            _hintBackdrop.SetActive(false);

            // 操作提示
            RectTransform keysRect = CreateRect("Keys", root, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 24f), new Vector2(560f, 26f));
            AddText(keysRect, "WASD 移动    空格 冲刺    F2 扣血    F3 加金币    F1 调试信息",
                15, TextAnchor.MiddleLeft, new Color(0.75f, 0.78f, 0.85f, 0.75f));
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text AddText(RectTransform rect, string content, int size, TextAnchor anchor, Color color)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.text = content;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
