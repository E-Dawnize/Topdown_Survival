using System;
using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Systems;

namespace Game.Core
{
    public enum RunPhase
    {
        /// <summary>还没开局，等外部调 StartRun。</summary>
        Idle,

        /// <summary>战斗：导演按时间刷怪，经验拾取排队升级。</summary>
        Battle,

        /// <summary>升级选卡：模拟暂停，等外部调 ChooseUpgrade。</summary>
        LevelUp,

        /// <summary>时间到，BOSS 战。普通刷怪停止，导演只负责 BOSS 与召唤。</summary>
        Boss,

        /// <summary>手动暂停：战斗系统不推进，等 Resume 回到暂停前的阶段。</summary>
        Paused,

        /// <summary>这一局结束（击杀 BOSS 或死亡）。之后不再推进任何状态。</summary>
        GameOver
    }

    /// <summary>
    /// 一局的流程状态机：Idle → Battle → (LevelUp ⇄ Battle) → Boss → GameOver。
    ///
    /// 与旧版的区别：没有波次、没有商店、没有金币。
    ///   - 难度由 DirectorSystem 的时间轴推进，本类只负责计时与阶段切换；
    ///   - 升级由经验触发，本类在 Battle/Boss 里检查 RunState.PendingLevelUps，
    ///     一次只开一轮，选完还有欠账就接着开下一轮；
    ///   - 时间到（最后一段导演的 EndTime）切 Boss 阶段，BOSS 死亡即通关。
    ///
    /// BOSS 的出场/血量/狂暴/击杀状态只有一份，存在 BossFight 里；本类的 BossActive 等属性
    /// 是给 HUD 的只读转发，不再自己维护一份需要互相同步的 defeated 标记。
    ///
    /// 升级与结算都只发事件，不直接改视图：HUD/UpgradeView/ResultView 靠订阅，
    /// 单测也能只凭事件顺序验状态机。
    /// </summary>
    public sealed class RunController
    {
        readonly PlayerModel _player;
        readonly RunState _run;
        readonly IReadOnlyList<DirectorDef> _directors;
        readonly IEventBus _events;
        readonly ulong _seed;
        readonly string _characterId;
        readonly BuildController _build;
        readonly DirectorSystem _director;
        readonly BossFight _bossFight;

        float _runElapsed;
        bool _bossStarted;
        RunPhase _phaseBeforePause = RunPhase.Battle;

        public RunController(PlayerModel player, RunState run, IReadOnlyList<DirectorDef> directors,
            IEventBus events, ulong seed, string characterId, BuildController build, DirectorSystem director,
            CompanionSystem companions = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _directors = directors ?? throw new ArgumentNullException(nameof(directors));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _director = director ?? throw new ArgumentNullException(nameof(director));
            _bossFight = director.BossFight;
            _seed = seed;
            _characterId = characterId;

            if (_directors.Count == 0)
            {
                throw new ArgumentException("导演时间轴是空的，一局没有敌人可刷", nameof(directors));
            }

            // companions 参数保留给现有装配签名（AppRoot/RunComposition/TestRun）。
            // 旅伴与羁绊的换局重置归 BuildController.ResetForNewRun，本类不再重置第二次。
            _ = companions;

            _events.Subscribe<UnitDied>(OnUnitDied);
        }

        public RunPhase Phase { get; private set; } = RunPhase.Idle;

        /// <summary>本局击杀数。HUD 与结算面板都读它，不在视图里再数一遍 UnitDied。</summary>
        public int Kills => _run.Kills;

        /// <summary>这一局的总时长（到 BOSS 的时间）。配置里最后一段的 EndTime。</summary>
        public float BossTime => _director.BossTime;

        /// <summary>距离 BOSS 还有几秒。升级暂停时冻结，Boss/结束态返回 0。</summary>
        public float RunSecondsLeft
        {
            get
            {
                if (Phase != RunPhase.Battle) return 0f;
                float left = BossTime - _runElapsed;
                return left > 0f ? left : 0f;
            }
        }

        public float RunElapsed => _runElapsed;

        /// <summary>流程上是否已经切进 BOSS 阶段（阶段状态，不是 BOSS 存活状态）。</summary>
        public bool BossStarted => _bossStarted;

        /// <summary>BOSS 状态的单一来源。更细的状态（Enraged/Id/UnitId…）直接读它。</summary>
        public BossFight BossFight => _bossFight;

        /// <summary>只有战斗与 BOSS 阶段能被暂停；升级/结算/Idle 不再叠一个暂停层。</summary>
        public bool CanPause => Phase == RunPhase.Battle || Phase == RunPhase.Boss;

        public bool IsPaused => Phase == RunPhase.Paused;

        public bool BossDefeated => _bossFight.Defeated;
        public bool BossActive => _bossFight.Active;
        public string BossName => _bossFight.Name;
        public float BossHpRatio => _bossFight.HpRatio;
        public int CurrentThreat => _director.CurrentThreat;
        public BuildController Build => _build;

        /// <summary>
        /// 开始一局。只允许在 Idle 状态调用一次。重开一局是场景重载，
        /// 同一个对象上只重置一半状态比直接拒绝更危险（旧版已经踩过这个坑）。
        /// </summary>
        public void StartRun()
        {
            if (Phase != RunPhase.Idle)
            {
                throw new InvalidOperationException(
                    "StartRun 只能在新装配的 Idle 状态调用；重开一局必须重载场景。当前状态：" + Phase);
            }

            _runElapsed = 0f;
            _bossStarted = false;
            _phaseBeforePause = RunPhase.Battle;
            _player.ContactTimer = 0f;

            _run.ResetForNewRun();
            _build.ResetForNewRun();
            _director.Begin();
            _run.ReportThreat(_director.CurrentThreat);

            _events.Publish(new RunStarted(_seed, _characterId));
            Phase = RunPhase.Battle;
        }

        /// <summary>推进一个固定步长。只做本阶段该做的事。</summary>
        public void Tick(float dt)
        {
            switch (Phase)
            {
                case RunPhase.Battle:
                    TickBattle(dt);
                    return;
                case RunPhase.Boss:
                    TickBoss(dt);
                    return;
                default:
                    return;   // Idle 等 StartRun；LevelUp / Paused 暂停；GameOver 之后不再推进
            }
        }

        /// <summary>
        /// 手动暂停。只允许 Battle/Boss；重复 Pause 不重复发事件。
        /// 暂停不改 _runElapsed，恢复后从原时刻继续。
        /// </summary>
        public void Pause()
        {
            if (!CanPause) return;

            _phaseBeforePause = Phase;
            Phase = RunPhase.Paused;
            _events.Publish(new RunPaused());
        }

        /// <summary>从暂停恢复。只允许在 Paused 状态调用。</summary>
        public void Resume()
        {
            if (Phase != RunPhase.Paused) return;

            Phase = _phaseBeforePause == RunPhase.Boss ? RunPhase.Boss : RunPhase.Battle;
            _events.Publish(new RunResumed());
        }

        /// <summary>
        /// 选一张升级卡。只有 LevelUp 阶段有效。
        /// 选完如果还有欠账（一次拾取跨了两级），立刻开下一轮，不回到战斗再暂停一次。
        /// </summary>
        public bool ChooseUpgrade(int index)
        {
            if (Phase != RunPhase.LevelUp) return false;
            if (!_build.TryChoose(index)) return false;

            if (_run.PendingLevelUps > 0)
            {
                OpenNextLevelUp();
                return true;
            }

            Phase = _bossStarted ? RunPhase.Boss : RunPhase.Battle;
            _events.Publish(new LevelUpClosed(_run.Level));
            return true;
        }

        /// <summary>重抽当前这一轮。免费次数由 BuildController 管。</summary>
        public bool RerollUpgrade()
        {
            return Phase == RunPhase.LevelUp && _build.TryReroll();
        }

        void TickBattle(float dt)
        {
            TickCombat(dt);
        }

        void TickBoss(float dt)
        {
            TickCombat(dt);
        }

        /// <summary>
        /// Battle 与 Boss 共用的每步检查：计时 → 死亡 → 升级 → 阶段专属收尾。
        /// 死亡必须排在升级与 BOSS 结算之前：人已经死了就不该再弹升级面板。
        /// </summary>
        void TickCombat(float dt)
        {
            _runElapsed += dt;
            _run.ReportThreat(_director.CurrentThreat);

            if (_player.IsDead)
            {
                EndRun(false);
                return;
            }

            if (_run.PendingLevelUps > 0)
            {
                OpenNextLevelUp();
                return;
            }

            if (Phase == RunPhase.Boss)
            {
                // BOSS 死亡会在清理阶段发 UnitDied，本类在事件回调里收尾。
                // 这里再判一次 BossFight，兜住"事件晚一步而 BOSS 已经被移除"的边界。
                if (_bossFight.Defeated)
                {
                    EndRun(true);
                }
                return;
            }

            if (_runElapsed >= BossTime)
            {
                StartBossFight();
            }
        }

        void OpenNextLevelUp()
        {
            if (!_run.ConsumePendingLevelUp()) return;

            _build.OpenDraft(_run.Level);
            if (_build.Options.Count == 0)
            {
                // 卡池被拿空（极端的满构筑）：没有可选的卡就直接把欠账清掉回到战斗，
                // 不让玩家卡在一个空白面板上。
                while (_run.PendingLevelUps > 0) _run.ConsumePendingLevelUp();
                Phase = _bossStarted ? RunPhase.Boss : RunPhase.Battle;
                _events.Publish(new LevelUpClosed(_run.Level));
                return;
            }

            Phase = RunPhase.LevelUp;
            _events.Publish(new LevelUpStarted(_run.Level, _build.Options.Count));
        }

        void StartBossFight()
        {
            if (_bossStarted) return;

            _bossStarted = true;
            Phase = RunPhase.Boss;
            _run.ReportThreat(_director.CurrentThreat);

            if (!_director.BeginBoss(_player.Position))
            {
                // 配置校验会拦住没有 BossId 的情况；真走到这里说明装配给错了表。
                // 没有 BOSS 可打时直接判通关，不让玩家卡在一个空阶段。
                EndRun(true);
                return;
            }

            _events.Publish(new BossFightStarted(_bossFight.Id, _bossFight.Name, _bossFight.MaxHp));
        }

        void EndRun(bool victory)
        {
            if (Phase == RunPhase.GameOver) return;
            Phase = RunPhase.GameOver;

            var summary = new RunSummary(
                _run.Level,
                _run.Kills,
                _run.EliteKills,
                _runElapsed,
                _seed,
                _build.WeaponCount,
                _build.CompanionCount);

            _events.Publish(new RunEnded(summary, victory));
        }

        /// <summary>
        /// 击杀计数与 BOSS 收尾。只认 Enemy，旅伴与玩家自己的死亡不算击杀。
        /// BOSS 的 UnitId 与 BossFight 记录的一致时才算击杀，避免同名怪或槽位复用误判。
        /// </summary>
        void OnUnitDied(UnitDied e)
        {
            if (e.Unit.Kind != UnitKind.Enemy) return;

            _run.AddKill(e.IsElite, e.IsBoss);

            if (!e.IsBoss || _bossFight.Defeated) return;
            if (e.Unit != _bossFight.UnitId) return;

            _director.NotifyBossDefeated();
            _events.Publish(new BossDefeated(_bossFight.Id, _bossFight.FightSeconds));
            EndRun(true);
        }
    }
}
