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

        float _hintAlpha;

        PlayerModel _player;
        RunState _run;

        void Awake()
        {
            // 预制体里的字体是内置的 LegacyRuntime，没有中文字形；运行时换成系统字体
            Font font = ResolveFont();
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
            Refresh();   // 先刷一次，避免激活后第一帧显示占位值
        }

        protected override void SubscribeAll()
        {
            On<CoinChanged>(OnCoinChanged);
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
            Color c = _hintText.color;
            c.a = Mathf.Clamp01(_hintAlpha);
            _hintText.color = c;
            if (_hintAlpha <= 0f)
            {
                _hintBackdrop.SetActive(false);
            }
        }

        static Font ResolveFont()
        {
            Font f = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 18);

            if (f != null) return f;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
