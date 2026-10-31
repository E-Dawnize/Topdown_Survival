using Game.Core.Events;
using Game.Model;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 基础 HUD：血条、金币、波次、冲刺冷却。
    /// 布局在 HudCanvas 预制体里摆，这里只负责刷新数据。
    /// </summary>
    public sealed class HudView : UiView
    {
        [SerializeField] Image _hpFill;
        [SerializeField] Text _hpText;
        [SerializeField] Text _coinText;
        [SerializeField] Text _waveText;
        [SerializeField] Image _dashFill;
        [SerializeField] Text _dashText;
        [SerializeField] GameObject _hintBackdrop;
        [SerializeField] Text _hintText;
        [SerializeField] bool _enableDebugKeys = true;

        /// <summary>受击红闪的峰值不透明度。再高就把画面遮住，连敌人在哪都看不清了。</summary>
        const float DamageFlashPeak = 0.22f;

        /// <summary>红闪从峰值淡到 0 的秒数。与受击变亮同量级，读得出“刚挨了一下”。</summary>
        const float DamageFlashFade = 0.35f;

        /// <summary>死亡提示的字号。要一眼看到，但不要做成结算面板（那是 I2-10）。</summary>
        const int DeathTextSize = 64;

        /// <summary>全屏受击红闪。运行时代码生成，不写进 HudCanvas 预制体（预制体是布局资产）。</summary>
        Image _damageFlash;
        Text _deathText;

        float _hintAlpha;
        float _flashAlpha;
        float _lastHp;

        PlayerModel _player;
        RunState _run;

        void Awake()
        {
            BuildFeedbackOverlays();

            // 预制体里的字体是内置的 LegacyRuntime，没有中文字形；运行时换成系统字体。
            // 必须在建完两个覆盖层之后跑，新建的死亡提示才能一起拿到字体。
            Font font = UiFont.Resolve();
            if (font == null) return;
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.font = font;
            }
        }

        /// <summary>由装配根在激活之前调用。</summary>
        public void Bind(PlayerModel player, RunState run)
        {
            _player = player;
            _run = run;
            _lastHp = player != null ? player.HP : 0f;
            Refresh();   // 先刷一次，避免激活后第一帧显示占位值
        }

        protected override void SubscribeAll()
        {
            On<CoinChanged>(OnCoinChanged);
            On<PlayerDied>(OnPlayerDied);
        }

        void LateUpdate()
        {
            HandleDebugKeys();
            Refresh();
            FadeHint();
            FadeDamageFlash();
        }

        /// <summary>
        /// 受击红闪与死亡提示都是纯反馈，没有布局要调，所以运行时生成而不是往预制体里加：
        /// 加进预制体以后调颜色、改字号都要开编辑器，反馈这类东西改得比布局勤。
        /// </summary>
        void BuildFeedbackOverlays()
        {
            _damageFlash = CreateStretchedImage("DamageFlash", new Color(0.85f, 0.08f, 0.08f, 0f));
            _damageFlash.enabled = false;

            _deathText = CreateCenteredText("DeathText", "你死了", DeathTextSize);
            _deathText.gameObject.SetActive(false);
        }

        Image CreateStretchedImage(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;   // 全屏遮罩不能把 HUD 的点击/拖拽拦在自己身上
            return image;
        }

        Text CreateCenteredText(string name, string content, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, 180f);
            rect.anchoredPosition = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = size;
            text.color = new Color(0.95f, 0.22f, 0.22f, 1f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
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
                Bus?.Publish(new CoinChanged(_run.Coins, 10));
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

        /// <summary>
        /// 死亡提示。只做一行字，不做结算面板；重开流程归 I2-10，
        /// 现在给提示是为了不让“画面停住、什么也不说”看起来像卡死。
        /// </summary>
        void OnPlayerDied(PlayerDied _)
        {
            if (_deathText != null) _deathText.gameObject.SetActive(true);
        }

        /// <summary>
        /// 掉血就红闪一下。直接比上一帧的血，不新开一个“玩家受伤”事件：
        /// 玩家掉血的路径目前只有接触伤害，HP 下降本身就足以判定，
        /// 少一个事件就少一处“忘了发/忘了收”的可能。
        /// </summary>
        void CheckDamageFlash()
        {
            if (_player == null) return;

            if (_player.HP < _lastHp)
            {
                _flashAlpha = DamageFlashPeak;
            }
            _lastHp = _player.HP;
        }

        void FadeDamageFlash()
        {
            if (_damageFlash == null || _flashAlpha <= 0f) return;

            _flashAlpha = Mathf.Max(0f, _flashAlpha - DamageFlashPeak / DamageFlashFade * Time.unscaledDeltaTime);

            Color c = _damageFlash.color;
            c.a = _flashAlpha;
            _damageFlash.color = c;
            _damageFlash.enabled = _flashAlpha > 0f;
        }

        void Refresh()
        {
            CheckDamageFlash();

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
            Color c = _hintText.color;
            c.a = Mathf.Clamp01(_hintAlpha);
            _hintText.color = c;
            if (_hintAlpha <= 0f)
            {
                _hintBackdrop.SetActive(false);
            }
        }
    }
}
