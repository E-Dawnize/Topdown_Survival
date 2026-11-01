using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// 局内 HUD：血条、等级/经验、生存计时/威胁、装备栏（武器/旅伴/组合/道具）、击杀/场上、冲刺冷却、BOSS 血条。
    /// 布局在 HudCanvas 预制体里摆；经验条、BOSS 条、装备栏、状态行、受击伤害数字与受伤闪屏是运行时补的
    /// （见 HudWidgets），因为它们不值得为一次玩法调整去改长期资产。
    ///
    /// 刷新走 HudValues 的差值：只有变了的那一组才写 text / fillAmount。
    /// 升级选卡与结算期间战斗项冻结（HudValues.Frozen），避免面板背后还在跳数字；
    /// 冻结时不推进 _shown，解冻后第一帧自然会把积压的差异整块重写。
    ///
    /// 生命周期：控件在 Bind 里建（而不是等 Awake），因为 HUD 预制体根节点是 inactive，
    /// 装配根会在 SetActive 之前就调用 Bind。Awake 里再建一次只是兜底，Build 是幂等的。
    /// </summary>
    public sealed class HudView : UiView
    {
        [SerializeField] Image _hpFill;
        [SerializeField] Text _hpText;
        [SerializeField] Text _progressText;
        [SerializeField] Text _threatText;
        [SerializeField] Image _dashFill;
        [SerializeField] Text _dashText;
        [SerializeField] GameObject _hintBackdrop;
        [SerializeField] Text _hintText;

        const float DamageFlashPeak = 0.22f;
        const float DamageFlashFade = 0.35f;
        const float HintSeconds = 1.2f;

        HudWidgets _widgets;

        readonly StringBuilder _equipment = new StringBuilder(256);

        float _hintAlpha;
        float _flashAlpha;
        float _damageAlpha;
        int _lastHp;

        bool _hasShown;
        HudValues _shown;

        /// <summary>狂暴是事件驱动的表现状态，不在 Model 里；快照带上它，名字变化就能走同一条 Diff。</summary>
        bool _bossEnraged;

        // 只用于每帧取快照：HudView 不直接读这些对象的字段（见 HudValues）。
        PlayerModel _player;
        RunState _run;
        RunController _runController;
        EnemySystem _enemies;
        DirectorSystem _director;
        BuildController _build;

        void Awake()
        {
            // 预制体若是激活的，Awake 会先于 Bind 跑；先建控件，Bind 时就不会对着空引用刷新。
            EnsureWidgets();
        }

        public void Bind(PlayerModel player, RunState run, RunController runController, EnemySystem enemies,
            DirectorSystem director, BuildController build)
        {
            _player = player;
            _run = run;
            _runController = runController;
            _enemies = enemies;
            _director = director;
            _build = build;

            EnsureWidgets();

            // 绑定即刷新一次整屏：先建控件再写值，顺序与 UpgradeView / ResultView 一致。
            HudValues initial = HudValues.From(_player, _run, _runController, _enemies, _build, _director,
                _bossEnraged);
            _lastHp = initial.Hp;
            Refresh(initial, force: true);
        }

        protected override void SubscribeAll()
        {
            // 只有“副作用”事件需要订阅：数值变化一律由 LateUpdate 的 Diff 发现。
            // （经验、构筑、BOSS 血量、升级关闭都只是数值变，订阅了也只会白调一次 Refresh。）
            On<ComboActivated>(OnComboActivated);
            On<BossFightStarted>(OnBossStarted);
            On<BossEnraged>(OnBossEnraged);
            On<PlayerDamaged>(OnPlayerDamaged);
            On<ExperienceCollectPulse>(OnExperienceCollectPulse);
        }

        void LateUpdate()
        {
            if (_widgets == null) return;

            HudValues next = HudValues.From(_player, _run, _runController, _enemies, _build, _director,
                _bossEnraged);

            CheckDamageFlash(next);
            FadeHint();
            FadeDamageFlash();
            FadeDamageText();
            Refresh(next, force: false);
        }

        void EnsureWidgets()
        {
            if (_widgets != null) return;

            _widgets = HudWidgets.Build(transform, _hpFill, _hpText, _progressText, _threatText, _dashFill,
                _dashText, _hintBackdrop, _hintText);
        }

        void OnComboActivated(ComboActivated e)
        {
            ShowHint("组合触发：" + e.Name);
        }

        void OnBossStarted(BossFightStarted _)
        {
            // BOSS 换人/重开时清掉上一个的狂暴标记；数值本身由下一帧 Diff 带出来。
            _bossEnraged = false;
        }

        void OnBossEnraged(BossEnraged _)
        {
            _bossEnraged = true;
            ShowHint("BOSS 狂暴");
        }

        void OnExperienceCollectPulse(ExperienceCollectPulse e)
        {
            ShowHint("全屏吸取 " + e.Orbs + " 枚经验　+" + e.TotalExperience + " XP");
        }

        /// <summary>
        /// 每次实际掉血都在血条右侧弹一个数字。以前只有屏幕红闪和血条下降，后排敌人/远程弹丸打来时
        /// 玩家分不清是“被谁打了”还是“血条自己掉了”；数字至少把“什么时候掉了多少”钉住。
        /// </summary>
        void OnPlayerDamaged(PlayerDamaged e)
        {
            EnsureWidgets();

            // 世界飘字放在玩家头上；HUD 左上角的数字保留，作为屏幕边缘也能看到的兜底。
            if (_player != null)
            {
                DamageNumbers.Show(new Vector3(_player.Position.x, 1.1f, _player.Position.y), e.Amount,
                    false, true);
            }

            if (_widgets.DamageText == null || e.Amount <= 0f) return;

            _widgets.DamageText.text = "-" + Mathf.CeilToInt(e.Amount);
            _widgets.DamageText.enabled = true;
            _damageAlpha = 1f;

            Color c = _widgets.DamageText.color;
            c.a = 1f;
            _widgets.DamageText.color = c;
        }

        void FadeDamageText()
        {
            if (_widgets.DamageText == null || !_widgets.DamageText.enabled) return;

            _damageAlpha = Mathf.Max(0f, _damageAlpha - Time.unscaledDeltaTime / 0.9f);
            Color c = _widgets.DamageText.color;
            c.a = _damageAlpha;
            _widgets.DamageText.color = c;

            if (_damageAlpha <= 0f) _widgets.DamageText.enabled = false;
        }

        void CheckDamageFlash(in HudValues values)
        {
            if (values.Hp < _lastHp) _flashAlpha = DamageFlashPeak;
            _lastHp = values.Hp;
        }

        void FadeDamageFlash()
        {
            Image flash = _widgets.DamageFlash;
            if (flash == null || _flashAlpha <= 0f) return;

            _flashAlpha = Mathf.Max(0f, _flashAlpha - DamageFlashPeak / DamageFlashFade * Time.unscaledDeltaTime);

            Color c = flash.color;
            c.a = _flashAlpha;
            flash.color = c;
            flash.enabled = _flashAlpha > 0f;
        }

        void ShowHint(string message)
        {
            if (_widgets.HintText == null || _widgets.HintBackdrop == null) return;

            _widgets.HintText.text = message;
            _hintAlpha = HintSeconds;

            Color c = _widgets.HintText.color;
            c.a = 1f;
            _widgets.HintText.color = c;
            _widgets.HintBackdrop.SetActive(true);
        }

        void FadeHint()
        {
            if (_widgets.HintBackdrop == null || !_widgets.HintBackdrop.activeSelf) return;

            _hintAlpha -= Time.unscaledDeltaTime;

            Color c = _widgets.HintText.color;
            c.a = Mathf.Clamp01(_hintAlpha);
            _widgets.HintText.color = c;

            if (_hintAlpha <= 0f) _widgets.HintBackdrop.SetActive(false);
        }

        void Refresh(in HudValues next, bool force)
        {
            HudField dirty = _hasShown && !force ? next.Diff(_shown) : HudField.All;
            if (dirty == HudField.None) return;

            // 冻结期间不写战斗项，也不推进 _shown：解冻后第一帧的 Diff 会把这些项全部算脏，
            // 于是面板关掉的那一刻自然整块重写，不需要额外记一份“暂停期间发生了什么”。
            if (next.Frozen && _hasShown) return;

            ApplyValues(next, dirty);

            // 写过的字段才推进基线；dirty 之外的字段本来就与 _shown 相等，所以直接整份覆盖即可
            // （旧实现用 Merge 逐字段搬，等价但多一份代码）。
            _shown = next;
            _hasShown = true;
        }

        void ApplyValues(in HudValues values, HudField dirty)
        {
            HudWidgets w = _widgets;

            if ((dirty & HudField.Hp) != 0)
            {
                if (w.HpFill != null)
                {
                    w.HpFill.fillAmount = values.HpRatio;
                    w.HpFill.color = Color.Lerp(new Color(0.85f, 0.25f, 0.25f), new Color(0.35f, 0.8f, 0.45f),
                        values.HpRatio);
                }

                if (w.HpText != null)
                {
                    w.HpText.text = values.HasPlayer ? values.Hp + " / " + values.MaxHp : "-- / --";
                }
            }

            if ((dirty & HudField.Progress) != 0)
            {
                if (w.ProgressText != null)
                {
                    w.ProgressText.text = "Lv " + values.Level + "　经验 " + values.Xp + " / " + values.XpToNext;
                }

                if (w.XpFill != null) w.XpFill.fillAmount = values.XpRatio;
            }

            // BOSS 是否开着属于 Boss 组，但它也决定威胁行的文案，所以两组任一脏都要重写这一行。
            if ((dirty & (HudField.RunTime | HudField.Boss)) != 0 && w.ThreatText != null)
            {
                w.ThreatText.text = ThreatLine(values);
            }

            if ((dirty & HudField.Dash) != 0)
            {
                if (w.DashFill != null) w.DashFill.fillAmount = values.DashRatio;

                if (w.DashText != null)
                {
                    if (!values.HasPlayer)
                    {
                        w.DashText.text = "冲刺";
                    }
                    else if (values.DashTenths > 0)
                    {
                        w.DashText.text = "冲刺  " + (values.DashTenths / 10f).ToString("0.0") + "s";
                        w.DashText.color = new Color(0.75f, 0.78f, 0.85f, 0.8f);
                    }
                    else
                    {
                        w.DashText.text = "冲刺就绪";
                        w.DashText.color = new Color(0.55f, 0.9f, 1f, 1f);
                    }
                }
            }

            if ((dirty & HudField.Collect) != 0)
            {
                if (w.CollectFill != null) w.CollectFill.fillAmount = values.CollectRatio;

                if (w.CollectText != null)
                {
                    if (!values.HasPlayer)
                    {
                        w.CollectText.text = "E 全屏吸取";
                    }
                    else if (values.CollectTenths > 0)
                    {
                        w.CollectText.text = "吸取  " + (values.CollectTenths / 10f).ToString("0.0") + "s";
                        w.CollectText.color = new Color(0.72f, 0.78f, 0.86f, 0.85f);
                    }
                    else
                    {
                        w.CollectText.text = "E 全屏吸取就绪";
                        w.CollectText.color = new Color(0.55f, 0.92f, 1f, 1f);
                    }
                }
            }

            if ((dirty & (HudField.RunTime | HudField.AliveEnemies | HudField.Kills | HudField.Build)) != 0 &&
                w.StatusLine != null)
            {
                w.StatusLine.text = "击杀 " + values.Kills + "　　场上 " + values.AliveEnemies +
                                    "　　武器 " + values.Weapons + "/" + values.WeaponSlots +
                                    "　　旅伴 " + values.Companions + "/" + values.CompanionSlots +
                                    "　　组合 " + values.Combos;
            }

            if ((dirty & HudField.Build) != 0) RebuildEquipment(values);

            if ((dirty & HudField.Boss) != 0)
            {
                if (w.BossRoot != null) w.BossRoot.SetActive(values.BossActive);
                if (w.BossFill != null) w.BossFill.fillAmount = values.BossHpRatio;

                if (w.BossName != null)
                {
                    w.BossName.text = values.BossEnraged && !string.IsNullOrEmpty(values.BossName)
                        ? values.BossName + " · 狂暴"
                        : values.BossName;
                }
            }
        }

        static string ThreatLine(in HudValues values)
        {
            return values.BossActive
                ? "BOSS 战"
                : "威胁 Lv " + values.Threat + "　　BOSS 倒计时 " + TimeFormat.Clock(values.RunSecondsLeft);
        }

        /// <summary>
        /// 重拼左上角装备栏。只在 HudField.Build 变脏时调（拿卡/升级/组合触发/换局），
        /// 不在每帧的 LateUpdate 里拼字符串。
        /// </summary>
        void RebuildEquipment(in HudValues values)
        {
            if (_widgets.EquipmentText == null) return;

            StringBuilder sb = _equipment;
            sb.Clear();

            sb.Append("<b>装备</b>　　武器 ").Append(values.Weapons).Append('/').Append(values.WeaponSlots)
                .Append("　旅伴 ").Append(values.Companions).Append('/').Append(values.CompanionSlots);

            sb.Append("\n武器：");
            if (_build != null && _build.Weapons.Count > 0)
            {
                for (int i = 0; i < _build.Weapons.Count; i++)
                {
                    if (i > 0) sb.Append("　");
                    var weapon = _build.Weapons[i];
                    sb.Append(string.IsNullOrEmpty(weapon.Def.Name) ? weapon.Id : weapon.Def.Name)
                        .Append(" Lv").Append(weapon.Level);
                }
            }
            else
            {
                sb.Append("无");
            }

            sb.Append("\n旅伴：");
            if (_build != null && _build.Companions.Count > 0)
            {
                for (int i = 0; i < _build.Companions.Count; i++)
                {
                    if (i > 0) sb.Append("　");
                    var companion = _build.Companions[i];
                    sb.Append(string.IsNullOrEmpty(companion.Def.Name) ? companion.Def.Id : companion.Def.Name)
                        .Append(" Lv").Append(companion.Level);
                }
            }
            else
            {
                sb.Append("无");
            }

            sb.Append("\n组合：");
            if (_build != null && _build.ActiveCombos.Count > 0)
            {
                for (int i = 0; i < _build.ActiveCombos.Count; i++)
                {
                    if (i > 0) sb.Append("　");
                    sb.Append(_build.ActiveCombos[i].Name);
                }
            }
            else
            {
                sb.Append("无");
            }

            if (_build != null)
            {
                sb.Append("\n数值卡 ").Append(_build.StatUpgradeCount)
                    .Append("　　机制/buff ").Append(_build.BuffCardCount);

                if (_build.Buffs.Count > 0)
                {
                    sb.Append("\n道具：");
                    for (int i = 0; i < _build.Buffs.Count; i++)
                    {
                        if (i > 0) sb.Append("·");
                        var buff = _build.Buffs[i];
                        sb.Append(string.IsNullOrEmpty(buff.Def.Name) ? buff.Def.Id : buff.Def.Name);
                    }
                }
            }

            _widgets.EquipmentText.text = sb.ToString();
        }

    }
}
