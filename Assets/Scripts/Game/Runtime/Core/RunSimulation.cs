using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using Game.Views;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 一个固定步长的完整模拟。从 RunLoop 里拆出来，是为了让"每步跑哪些系统、按什么顺序"
    /// 只有一份实现：主循环和 EditMode 集成测试走同一段代码，测试才真的在验接入顺序，
    /// 而不是各写一遍次序再互相印证。
    ///
    /// 顺序（技术方案 7.1 里已经实现的那几步）：
    ///   1 RunController.Tick      —— 局状态机先跑：波次计时、死亡收尾、商店。
    ///                                它不在这里推进就没有"现在是不是战斗中"这个前提
    ///   2 PlayerController.Tick   —— 玩家先动，本步的敌人追击与开火都用移动后的位置
    ///   2.5 CompanionSystem.TickMovement —— 旅伴追的是玩家本步的新位置。敌人不看旅伴，
    ///                               所以它只影响旅伴自己的位置（以及同一段中自己的开火）
    ///   3 EnemySystem.Tick        —— 追击 + 分离 + 接触伤害，顺带记录上一步位置供插值
    ///   4 SpatialGrid.Rebuild     —— 必须在敌人移动之后、武器结算之前，
    ///                                否则武器读到的是上一 tick 的敌人位置
    ///   5 WeaponSystem.Tick       —— 投射物先走完这一段再开火，命中判定读的是第 4 步的快照
    ///     CompanionSystem.TickWeapons —— 旅伴的武器接在玩家之后、同一步内开火，不开线程也不用协程
    ///   6 清理死亡               —— 武器这一步才可能打死人，不能在它之前清；掉落物也在这里生成
    ///   7 PickupSystem.Tick      —— 排在清理之后：这一步刚掉的金币当场就参与磁吸判定，
    ///                                玩家踩在尸体上不用等下一步
    ///   8 WaveController.Tick    —— 刷怪排在清理之后：本步刚被打死的怪已经腾出槽位与存活数，
    ///                                同屏上限算的是这一帧真实的存活数，不是上一步的
    ///   9 Events.Flush           —— 整步结束再分发，订阅方不会读到半更新的状态
    ///
    /// 没有战局控制器时（只装配战斗的单测）就是第 2~8 步一直跑；有的话只有 Battle 态才跑这七步，
    /// Shop 与 GameOver 态只剩第 1 步与第 9 步。RunController 在这些态里仍然要被 Tick：
    /// CloseShop / StartRun 是外部直接调的，它们发出的 ShopClosed / WaveStarted / RunStarted
    /// 也要靠末尾的 Flush 分发出去，否则"跳过商店"按下去会像没反应。
    ///
    /// 玩家死了以后整段不再跑，每步只把 PlayerDied 补发一次（并且只发一次）：
    /// 人已经没了还继续追击、继续开火，等于让一具尸体把这一局空转到结束。
    /// </summary>
    public sealed class RunSimulation
    {
        readonly PlayerModel _player;
        readonly PlayerController _playerController;
        readonly EnemySystem _enemies;
        readonly WeaponSystem _weapons;
        readonly IEventBus _events;
        readonly RunController _run;
        readonly WaveController _waves;
        readonly PickupSystem _pickups;

        /// <summary>旅伴队伍。可空：只装玩家战斗的单测不需要它；为空时这一步与 I2-5 完全一致。</summary>
        readonly CompanionSystem _companions;

        // 死亡事件只发一次：RunLoop 每帧都会再调 Tick，靠这个位挡住重复发布
        bool _playerDiedPublished;

        // 已经推给 WaveController 的波次号。0 表示还没推过任何一波。
        // 换波只认 RunController 的波次号，刷怪系统自己不去猜"现在该刷第几波"。
        int _activeWaveIndex;

        // 开火者加成每个模拟步从 StatBlock 重算一次就够了。性状是只读结构，
        // 每帧 new 一个数组式的东西在这里是白搭的分配，所以只留一个字段复用。
        ShooterStats _shooterStats;

        /// <summary>
        /// run / waves / pickups 可空：只测战斗的单测不必造一整套流程。run 与 waves 都给上才是完整的一局；
        /// 只给 run 时波次照常推进但没有敌人生成，只给 waves 时没有波次可刷；
        /// pickups 为空时敌人照旧会死，只是不掉落。
        /// </summary>
        public RunSimulation(PlayerModel player, PlayerController playerController, EnemySystem enemies,
            WeaponSystem weapons, IEventBus events, RunController run = null, WaveController waves = null,
            PickupSystem pickups = null, CompanionSystem companions = null)
        {
            _player = player;
            _playerController = playerController;
            _enemies = enemies;
            _weapons = weapons;
            _events = events;
            _run = run;
            _waves = waves;
            _pickups = pickups;
            _companions = companions;
        }

        public PlayerModel Player => _player;
        public PlayerController PlayerController => _playerController;
        public EnemySystem Enemies => _enemies;
        public WeaponSystem Weapons => _weapons;
        public RunController Run => _run;
        public WaveController Waves => _waves;
        public PickupSystem Pickups => _pickups;
        public CompanionSystem Companions => _companions;

        /// <summary>推进一个固定步长。input 按引用传，消费过的按钮位会被清掉。</summary>
        public void Tick(float dt, ref InputSnapshot input)
        {
            // 本步开始时是不是处于战斗态，只用来判断"这一步是不是刚换了一波"。
            // 商店里 CloseShop 之后 Phase 已经变成 Battle，而刷怪系统还没被通知过，
            // 光比波次号不足以区分"同一波的下一步"与"新一波的第一步"。
            bool wasBattle = _run != null && _run.Phase == RunPhase.Battle;

            // 1 局状态机。商店与结束态也照跑（见类注释）：它的 Tick 在那些态里只是计时或不做事，
            // 但 CloseShop / StartRun 发出的事件要能在本步末尾被分发。
            _run?.Tick(dt);

            // 2~7 只有战斗中才推进。商店里继续让敌人追击、武器开火，等于把商店当成一段免费输出时间。
            if (_run == null || _run.Phase == RunPhase.Battle)
            {
                SyncWave(wasBattle);

                if (!_player.IsDead)
                {
                    Step(dt, ref input);
                }
            }

            // 死掉的那一步也要把 PlayerDied 发出去，而且死了以后每步只做这件事。
            // Flush 放在最后：死亡事件不留在队列里过夜，HUD 当帧就能切到死亡提示。
            PublishPlayerDied();
            _events?.Flush();
        }

        /// <summary>
        /// 把这一波的定义推给刷怪系统。推进时机是"刚进战斗态"或"波次号变了"，
        /// 换波这件事从头到尾只有 RunController 说了算。
        /// </summary>
        void SyncWave(bool wasBattle)
        {
            if (_waves == null || _run == null || _run.Phase != RunPhase.Battle) return;

            int index = _run.WaveIndex;
            if (wasBattle && index == _activeWaveIndex) return;

            _activeWaveIndex = index;
            _waves.BeginWave(_run.CurrentWave);
        }

        void Step(float dt, ref InputSnapshot input)
        {
            _playerController.Tick(dt, ref input);

            // 2.5 旅伴跟随。排在玩家移动之后、敌人移动之前：追的是玩家本步的位置，
            // 而敌人系统只读写敌人自己的模型，旅伴的位置与它无关。
            _companions?.TickMovement(_player.Position, dt);

            Vector2 playerPos = _player.Position;
            _enemies.Tick(playerPos, dt);

            // 这一步被接触伤害打死了：后面的格子重建与开火不再跑。
            // 死亡在同一个模拟步里就发出去，不会拖到下一步才显示"你死了"。
            if (_player.IsDead) return;

            _enemies.Grid.Rebuild(playerPos, _enemies);

            _shooterStats = new ShooterStats(
                _player.Stats[StatType.Attack],
                _player.Stats[StatType.CritChance],
                _player.Stats[StatType.CritDamage],
                _player.Stats[StatType.AttackSpeed],
                Mathf.RoundToInt(_player.Stats[StatType.ProjectileCount]));

            _weapons.Tick(playerPos, ViewSnapshot.DirectionOf(_player.Facing), _shooterStats, dt);

            // 旅伴开火：与玩家同一步、玩家之后。它们的武器各自带自己的 Shooter（UnitId），
            // 所以伤害、击退、击杀归属都记在旅伴头上，不会算成玩家。
            _companions?.TickWeapons(playerPos, dt);

            CleanupDeadEnemies();

            // 7 掉落物。排在清理之后，本步刚掉的金币立刻参与磁吸判定
            _pickups?.Tick(dt);

            // 8 刷怪。排在清理之后：这一步被打死的怪已经不在场上了，同屏上限按真实存活数算。
            _waves?.Tick(dt, playerPos);
        }

        void PublishPlayerDied()
        {
            if (_playerDiedPublished || !_player.IsDead) return;

            _playerDiedPublished = true;
            _events?.Publish(new PlayerDied(_player.Position.x, _player.Position.y));
        }

        /// <summary>
        /// 把这一步被打死的敌人从系统里摘掉，顺手掉一份战利品。
        /// 放在清理而不是命中路径上：命中时删会让"谁还能看见这个敌人"变成隐式时序问题，
        /// 而按槽位顺序遍历、只删当前槽位（Remove 只把本槽位置空，不挪动别的槽位），不会边遍历边错位。
        /// 放在 Flush 之前：这样同一步里 UnitDamaged 与 UnitDied 一批发出去，视图当帧就能回收，
        /// 不会留一帧的尸体。
        /// </summary>
        void CleanupDeadEnemies()
        {
            for (int slot = 0; slot < _enemies.SlotCapacity; slot++)
            {
                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || !enemy.IsDead) continue;

                Vector2 position = enemy.Position;

                _events?.Publish(new UnitDied(EnemySystem.UnitIdOf(slot), enemy.LastDamageSource,
                    position.x, position.y));

                // 掉落与移除放在一起：只有这里知道"这个敌人这一步死了"，
                // 散到别处（订阅 UnitDied）会晚一步，还可能因为事件重入导致同一只怪掉两次
                _pickups?.DropEnemyLoot(position);

                _enemies.Remove(slot);
            }
        }
    }
}
