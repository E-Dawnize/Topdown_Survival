using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 局内 HUD：血条、金币、波次、波次倒计时、存活敌人数、击杀数、冲刺冷却。
    /// 布局在 HudCanvas 预制体里摆，这里只负责刷新数据；波次倒计时/存活/击杀这一行是运行时补的
    /// （见 <see cref="BuildStatusLine"/>），因为预制体是长期资产，加一行字不值得改它。
    ///
    /// 刷新走 <see cref="HudValues"/> 的差值：LateUpdate 每帧取一份快照跟自己上一次写的比，
    /// 只有变了的那几项才写 text / fillAmount。每帧无条件写会让 uGUI 一直重建，
    /// 一屏不动的数字也在白白制造脏标记。
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

        /// <summary>全屏受击红闪。运行时代码生成，不写进 HudCanvas 预制体（预制体是布局资产）。</summary>
        Image _damageFlash;

        /// <summary>波次倒计时 / 存活敌人数 / 击杀数。挂在 WaveText 下面一行。</summary>
        Text _statusText;

        float _hintAlpha;
        float _flashAlpha;
        float _lastHp;

        /// <summary>上一次真正写进 uGUI 的那份数值。<see cref="_hasShown"/> 为假时第一次全写。</summary>
        HudValues _shown;
        bool _hasShown;

        PlayerModel _player;
        RunState _run;
        RunController _runController;
        EnemySystem _enemies;

        void Awake()
        {
            BuildFeedbackOverlays();
            BuildStatusLine();

            // 预制体里的字体是内置的 LegacyRuntime，没有中文字形；运行时换成系统字体。
            // 必须在建完覆盖层之后跑，新建的文本才能一起拿到字体。
            Font font = UiFont.Resolve();
            if (font != null)
            {
                foreach (Text text in GetComponentsInChildren<Text>(true))
                {
                    text.font = font;
                }
            }

            // HudCanvas 预制体根节点是 inactive，所以 Awake 会在 SetActive 之后才跑，
            // 而 Bind 里的第一次 Refresh 已经跑过了——那时候 _statusText 还不存在。
            // 把“还没写过”的标记放回去，下一帧整屏重写一次，倒计时行才不会等到值变化才出现。
            _hasShown = false;
        }

        /// <summary>由装配根在激活之前调用。</summary>
        public void Bind(PlayerModel player, RunState run, RunController runController, EnemySystem enemies)
        {
            _player = player;
            _run = run;
            _runController = runController;
            _enemies = enemies;
            _lastHp = player != null ? player.HP : 0f;

            Refresh();   // 先刷一次，避免激活后第一帧显示占位值
        }

        protected override void SubscribeAll()
        {
            // 金币改了也要刷：F3 调试键与商店买入都不经战斗步，不能只靠 LateUpdate 的快照差值等下一帧
            On<CoinChanged>(OnCoinChanged);
        }

        void LateUpdate()
        {
            HandleDebugKeys();
            CheckDamageFlash();
            FadeHint();
            FadeDamageFlash();
            Refresh();
        }

        /// <summary>
        /// 受击红闪是纯反馈，没有布局要调，所以运行时生成而不是往预制体里加：
        /// 加进预制体以后调颜色、改透明度都要开编辑器，反馈这类东西改得比布局勤。
        /// </summary>
        void BuildFeedbackOverlays()
        {
            _damageFlash = CreateStretchedImage("DamageFlash", new Color(0.85f, 0.08f, 0.08f, 0f));
            _damageFlash.enabled = false;
        }

        /// <summary>
        /// 波次倒计时 / 存活敌人数 / 击杀数。位置贴着预制体里的 WaveText（顶部中间、y = -26、高 32），
        /// 直接排在它下面一行；预制体里没有这一行，所以坐标写在这里而不是序列化字段里。
        /// </summary>
        void BuildStatusLine()
        {
            var go = new GameObject("StatusLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(560f, 26f);
            rect.anchoredPosition = new Vector2(0f, -60f);

            _statusText = go.GetComponent<Text>();
            _statusText.text = string.Empty;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.fontSize = 18;
            _statusText.color = new Color(0.82f, 0.85f, 0.9f, 1f);
            _statusText.raycastTarget = false;
            _statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _statusText.verticalOverflow = VerticalWrapMode.Overflow;
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

        void OnCoinChanged(CoinChanged _)
        {
            // 具体文本不在这里拼：下一帧 Refresh 的快照差值会带上金币这一项，
            // 文本格式只留在 ApplyValues 一处，不会出现两处格式不一致
            Refresh();
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

        /// <summary>
        /// 写一帧 HUD。先比差值，只写变了的那几项。
        /// 商店与结算里模拟已经不推进，战斗数值全都冻住，这时只让金币跟着走
        /// （买入、刷新会改它），其余一概不写——否则面板背后还在跳动的数字会像"这一局还在跑"。
        /// </summary>
        void Refresh()
        {
            HudValues next = HudValues.From(_player, _run, _runController, _enemies);

            HudField dirty = _hasShown ? next.Diff(_shown) : HudField.All;

            // 冻结只在已经有基准之后才收窄：第一次写就是整屏基线，没有基准可保留
            if (_hasShown && FrozenToBattleData) dirty &= HudField.Coins;

            if (dirty == HudField.None) return;

            ApplyValues(next, dirty);

            // 只把真写进去的那几项记成“已写”：冻结期间没写的战斗项继续留在旧值里，
            // 解冻那一帧才会算出差值、把血条与倒计时补上
            _shown = _hasShown ? _shown.Merge(next, dirty) : next;
            _hasShown = true;
        }

        /// <summary>商店 / 结算里战斗数据不该继续刷。Idle 不算冻结：那时一局还没开始，刷的是占位值。</summary>
        bool FrozenToBattleData
        {
            get
            {
                if (_runController == null) return false;
                return _runController.Phase == RunPhase.Shop || _runController.Phase == RunPhase.GameOver;
            }
        }

        void ApplyValues(in HudValues values, HudField dirty)
        {
            if ((dirty & HudField.Hp) != 0)
            {
                if (_hpFill != null)
                {
                    _hpFill.fillAmount = values.HpRatio;
                    _hpFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.25f), new Color(0.35f, 0.8f, 0.45f),
                        values.HpRatio);
                }

                if (_hpText != null)
                {
                    _hpText.text = _player != null
                        ? values.Hp + " / " + values.MaxHp
                        : "-- / --";
                }
            }

            if ((dirty & HudField.Coins) != 0 && _coinText != null && _run != null)
            {
                _coinText.text = "金币  " + values.Coins;
            }

            if ((dirty & HudField.Wave) != 0 && _waveText != null && _run != null)
            {
                _waveText.text = "第 " + values.WaveIndex + " / " + values.WavesTotal + " 波";
            }

            if ((dirty & HudField.Dash) != 0)
            {
                if (_dashFill != null)
                {
                    float ratio = 1f;
                    if (_player != null && _player.DashCooldown > 0f)
                    {
                        ratio = 1f - Mathf.Clamp01(_player.DashCooldownTimer / _player.DashCooldown);
                    }

                    _dashFill.fillAmount = ratio;
                }

                if (_dashText != null)
                {
                    if (_player == null)
                    {
                        _dashText.text = "冲刺";
                    }
                    else if (values.DashTenths > 0)
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

            if ((dirty & (HudField.WaveTime | HudField.AliveEnemies | HudField.Kills)) != 0 &&
                _statusText != null)
            {
                _statusText.text = "剩余 " + values.WaveSecondsLeft + "s    场上 " + values.AliveEnemies +
                                   "    击杀 " + values.Kills;
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
