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
    /// 顺序（技术方案 7.1 的 3/6/7/4/10/11 六步，其余系统还没做）：
    ///   1 PlayerController.Tick   —— 玩家先动，本步的敌人追击与开火都用移动后的位置
    ///   2 EnemySystem.Tick        —— 追击 + 分离 + 接触伤害，顺带记录上一步位置供插值
    ///   3 SpatialGrid.Rebuild     —— 必须在敌人移动之后、武器结算之前，
    ///                                否则武器读到的是上一 tick 的敌人位置
    ///   4 WeaponSystem.Tick       —— 投射物先走完这一段再开火，命中判定读的是第 3 步的快照
    ///   5 清理死亡               —— 武器这一步才可能打死人，不能在它之前清
    ///   6 Events.Flush            —— 整步结束再分发，订阅方不会读到半更新的状态
    /// </summary>
    public sealed class RunSimulation
    {
        readonly PlayerModel _player;
        readonly PlayerController _playerController;
        readonly EnemySystem _enemies;
        readonly WeaponSystem _weapons;
        readonly IEventBus _events;

        // 开火者加成每个模拟步从 StatBlock 重算一次就够了。性状是只读结构，
        // 每帧 new 一个数组式的东西在这里是白搭的分配，所以只留一个字段复用。
        ShooterStats _shooterStats;

        public RunSimulation(PlayerModel player, PlayerController playerController, EnemySystem enemies,
            WeaponSystem weapons, IEventBus events)
        {
            _player = player;
            _playerController = playerController;
            _enemies = enemies;
            _weapons = weapons;
            _events = events;
        }

        public PlayerModel Player => _player;
        public PlayerController PlayerController => _playerController;
        public EnemySystem Enemies => _enemies;
        public WeaponSystem Weapons => _weapons;

        /// <summary>推进一个固定步长。input 按引用传，消费过的按钮位会被清掉。</summary>
        public void Tick(float dt, ref InputSnapshot input)
        {
            _playerController.Tick(dt, ref input);

            Vector2 playerPos = _player.Position;
            _enemies.Tick(playerPos, dt);
            _enemies.Grid.Rebuild(playerPos, _enemies);

            _shooterStats = new ShooterStats(
                _player.Stats[StatType.Attack],
                _player.Stats[StatType.CritChance],
                _player.Stats[StatType.CritDamage],
                _player.Stats[StatType.AttackSpeed]);

            _weapons.Tick(playerPos, ViewSnapshot.DirectionOf(_player.Facing), _shooterStats, dt);

            CleanupDeadEnemies();

            _events?.Flush();
        }

        /// <summary>
        /// 把这一步被打死的敌人从系统里摘掉。
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

                _events?.Publish(new UnitDied(EnemySystem.UnitIdOf(slot), enemy.LastDamageSource,
                    enemy.Position.x, enemy.Position.y));

                _enemies.Remove(slot);
            }
        }
    }
}
