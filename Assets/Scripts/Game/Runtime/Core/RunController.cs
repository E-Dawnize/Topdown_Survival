using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;

namespace Game.Core
{
    public enum RunPhase
    {
        /// <summary>还没开局，等外部调 StartRun。</summary>
        Idle,

        /// <summary>战斗中：推进波次计时，时间到就 WaveCleared 并转商店。</summary>
        Battle,

        /// <summary>波间商店：等外部调 CloseShop 开下一波。</summary>
        Shop,

        /// <summary>这一局结束（通关或死亡）。之后不再推进任何状态。</summary>
        GameOver
    }

    /// <summary>
    /// 一局的流程状态机：Idle → Battle → Shop → Battle … → GameOver。
    /// 只决定"现在该是哪个阶段"：刷什么怪归 WaveController，打多少伤害归各战斗系统。
    ///
    /// 每次状态切换只往事件总线投事件（RunStarted / WaveStarted / WaveCleared / ShopOpened /
    /// ShopClosed / RunEnded），不直接改视图与 UI 状态：HUD、商店、结算面板以后都靠订阅，
    /// 单测也就能只凭"收到的顺序"验状态机，不必去读一堆中间字段。
    ///
    /// 商店与结束态里 Tick 不做任何推进。继续下一波只有一个入口 CloseShop：
    /// 商店界面的 Esc 与测试调的是同一个方法。
    /// </summary>
    public sealed class RunController
    {
        readonly PlayerModel _player;
        readonly RunState _run;
        readonly IReadOnlyList<WaveDef> _waves;
        readonly IEventBus _events;
        readonly ulong _seed;
        readonly string _characterId;

        /// <summary>
        /// 波间商店。可空：只测状态机的用例不需要它，那样进商店只停在 Shop 态、没有货可买。
        /// 有它的时候本类只负责"什么时候开、什么时候关"，抽卡与买卖归 ShopController。
        /// </summary>
        readonly ShopController _shop;

        float _waveElapsed;
        float _runElapsed;
        int _kills;
        int _wavesCleared;

        public RunController(PlayerModel player, RunState run, IReadOnlyList<WaveDef> waves, IEventBus events,
            ulong seed, string characterId, ShopController shop = null)
        {
            _player = player ?? throw new System.ArgumentNullException(nameof(player));
            _run = run ?? throw new System.ArgumentNullException(nameof(run));
            _waves = waves ?? throw new System.ArgumentNullException(nameof(waves));
            _events = events ?? throw new System.ArgumentNullException(nameof(events));
            _seed = seed;
            _characterId = characterId;
            _shop = shop;

            if (_waves.Count == 0)
            {
                throw new System.ArgumentException("波次表是空的，一局没有波次可跑", nameof(waves));
            }

            // 一局有几波只从配置表来：RunState 自己不给默认波数，写死 5 会让配置改成 3 波时
            // HUD 显示与结束判定一起错。
            _run.WavesTotal = _waves.Count;
            _run.WaveIndex = 1;

            _events.Subscribe<UnitDied>(OnUnitDied);
        }

        public RunPhase Phase { get; private set; } = RunPhase.Idle;

        /// <summary>配置里这一局的波数。</summary>
        public int WavesTotal => _waves.Count;

        /// <summary>当前波次号，从 1 开始。与 Run.WaveIndex 是同一份数据，这里是给流程内的系统用的入口。</summary>
        public int WaveIndex => _run.WaveIndex;

        /// <summary>本局击杀数。HUD 与结算面板都读它，不要在视图里再数一遍 UnitDied。</summary>
        public int Kills => _kills;

        /// <summary>
        /// 当前这一波还剩几秒。取整与钳位都放在这里：HUD 只负责显示，不自己算"哪一波的时长减哪个计时"。
        /// 商店与结束态返回 0——波次计时在那些态里不推进，显示成"还剩 30 秒"会让人以为还在打。
        /// </summary>
        public float WaveSecondsLeft
        {
            get
            {
                WaveDef wave = CurrentWave;
                if (Phase != RunPhase.Battle || wave == null) return 0f;

                float left = wave.Duration - _waveElapsed;
                return left > 0f ? left : 0f;
            }
        }

        /// <summary>Run.WaveIndex 指向的那一波。索引越界时返回 null（不会拿越界的下标去取）。</summary>
        public WaveDef CurrentWave
        {
            get
            {
                int index = _run.WaveIndex;
                return index >= 1 && index <= _waves.Count ? _waves[index - 1] : null;
            }
        }

        /// <summary>
        /// 开局。Idle 与 GameOver 可用；一局正在跑（Battle / Shop）时忽略，
        /// 免得外部误调把 WaveIndex 与计时往回拨。
        ///
        /// 这里只重置流程状态（波次、计时、击杀数、金币），实体不归它管：玩家、敌人、掉落物、视图池
        /// 全部挂在场景上，重开一局走的是场景重载（见 ResultView），由场景把上一局的东西一起带走。
        /// 反过来说，同一次运行里调 StartRun 只会把状态机拨回第一波，被清空的 PlayerModel（HP = 0）
        /// 不会复活——所以正式路径永远是重载场景，这个方法留给测试与调试。
        /// </summary>
        public void StartRun()
        {
            if (Phase == RunPhase.Battle || Phase == RunPhase.Shop) return;

            _runElapsed = 0f;
            _waveElapsed = 0f;
            _kills = 0;
            _wavesCleared = 0;

            // 金币与波次索引是跨局状态：重开一局要从 0 金币、第 1 波开始
            _run.ResetForNewRun();

            // 上一局攒的保底计数不能带到新的一局：连空 8 次是这一局的运气，不是下一局的起点
            _shop?.ResetForNewRun();

            _events.Publish(new RunStarted(_seed, _characterId));
            StartWave(1);
        }

        /// <summary>推进一个固定步长。只做本阶段该做的事。</summary>
        public void Tick(float dt)
        {
            switch (Phase)
            {
                case RunPhase.Battle:
                    TickBattle(dt);
                    return;
                case RunPhase.Shop:
                    _runElapsed += dt;   // 商店里的时间也算进这一局的总时长
                    return;
                default:
                    return;   // Idle 等 StartRun；GameOver 之后什么都不再推进
            }
        }

        /// <summary>
        /// 关掉商店、开下一波。返回 false 表示当前不在商店态（重复调用、或已经在战斗里），
        /// 调用方不用自己判 Phase。
        /// </summary>
        public bool CloseShop()
        {
            if (Phase != RunPhase.Shop) return false;

            // 花费从商店自己读，不让调用方报数：UI、调试键与测试调的是同一个入口，
            // 谁传都得对，而"实际花了多少"只有 ShopController 真的知道。
            int spent = _shop != null ? _shop.Spent : 0;
            _shop?.Close();

            _events.Publish(new ShopClosed(spent));
            StartWave(_run.WaveIndex + 1);
            return true;
        }

        void TickBattle(float dt)
        {
            _runElapsed += dt;
            _waveElapsed += dt;

            // 死亡判定排在"这一波时间到"前面：同一帧里两件事都成立时，人已经死了就不该再
            // 发 WaveCleared、更不该再开一间商店。
            // 玩家掉血发生在战斗步里（本 Tick 之后），所以死亡是在下一次 Tick 收尾的——晚一步，
            // 但全流程只有 PlayerModel.IsDead 一处判据，不必再听一遍 PlayerDied 事件。
            if (_player.IsDead)
            {
                EndRun(false);
                return;
            }

            WaveDef wave = CurrentWave;
            if (wave == null || _waveElapsed < wave.Duration) return;

            ClearWave(wave);
        }

        void ClearWave(WaveDef wave)
        {
            int index = _run.WaveIndex;
            _wavesCleared++;

            _run.AddCoins(wave.CoinReward);
            _events.Publish(new WaveCleared(index, wave.CoinReward));
            _events.Publish(new CoinChanged(_run.Coins, wave.CoinReward));

            // 最后一波打完直接结算：GDD 3 节的商店只开在两波之间，最后一波后面接的是结算。
            // 先判索引到顶，CloseShop 就永远只开得出配置里真有的波，不会越界。
            if (index >= _waves.Count)
            {
                EndRun(true);
                return;
            }

            Phase = RunPhase.Shop;

            // 先把货备好再发 ShopOpened：订阅方（将来的商店 UI）收到事件时读到的已经是这一间的 6 格
            _shop?.OpenShop(index);
            _events.Publish(new ShopOpened(index));
        }

        void StartWave(int index)
        {
            // 只有 CloseShop 会走到这里，而它上面的"最后一波"判断保证 index 不会超过配置波数
            _run.WaveIndex = index;
            _waveElapsed = 0f;
            Phase = RunPhase.Battle;
            _events.Publish(new WaveStarted(index, _waves[index - 1].Duration));
        }

        void EndRun(bool victory)
        {
            Phase = RunPhase.GameOver;

            var summary = new RunSummary(_wavesCleared, _kills, _run.Coins, _runElapsed, _seed);
            _events.Publish(new RunEnded(summary, victory));
        }

        /// <summary>
        /// 击杀计数。目前只有敌人会发 UnitDied（玩家死亡走 PlayerDied），所以直接当击杀数用；
        /// 以后有可被击杀的旅伴或道具时，这里要按阵营过滤。
        /// </summary>
        void OnUnitDied(UnitDied _)
        {
            _kills++;
        }
    }
}
