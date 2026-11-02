using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 一个固定步长的完整模拟。从 RunLoop 里拆出来，是为了让"每步跑哪些系统、按什么顺序"
    /// 只有一份实现：主循环和 EditMode 集成测试走同一段代码。
    ///
    /// 顺序：
    ///   1 RunController.Tick        —— 计时、死亡、升级暂停、BOSS 切换
    ///   2 PlayerController.Tick     —— 玩家先动
    ///   3 CompanionSystem.Move      —— 旅伴追玩家本步的新位置
    ///   4 EnemySystem.Tick          —— 追击/保持距离 + 起手预警 + 近战/冲锋/远程弹丸
    ///   5 SpatialGrid.Rebuild       —— 必须在敌人移动之后、武器结算之前
    ///   6 WeaponSystem.Tick         —— 投射物推进 → 开火
    ///   7 CompanionSystem.Weapons   —— 旅伴开火
    ///   8 Cleanup                   —— 移除死亡敌人，生成经验/心
    ///   9 PickupSystem.Tick         —— 经验磁吸与升级排队
    ///   10 BuffSystem.Tick          —— 低血判定、周期回复
    ///   11 DirectorSystem.Tick      —— 按时间刷怪 / BOSS 召唤
    ///   12 Events.Flush             —— 整步结束再分发
    ///
    /// 升级与 BOSS 结算期间（LevelUp / GameOver）只有第 1 步与第 12 步跑，
    /// 战斗系统不再推进。玩家死了以后每步只补发一次 PlayerDied。
    /// </summary>
    public sealed class RunSimulation
    {
        readonly PlayerModel _player;
        readonly PlayerController _playerController;
        readonly EnemySystem _enemies;
        readonly WeaponSystem _weapons;
        readonly IEventBus _events;
        readonly RunController _run;
        readonly DirectorSystem _director;
        readonly PickupSystem _pickups;
        readonly CompanionSystem _companions;
        readonly BuffSystem _buffs;

        bool _playerDiedPublished;
        ShooterStats _shooterStats;
        readonly System.Collections.Generic.List<int> _contactTargets = new System.Collections.Generic.List<int>(16);

        public RunSimulation(PlayerModel player, PlayerController playerController, EnemySystem enemies,
            WeaponSystem weapons, IEventBus events, RunController run = null, DirectorSystem director = null,
            PickupSystem pickups = null, CompanionSystem companions = null, BuffSystem buffs = null)
        {
            _player = player;
            _playerController = playerController;
            _enemies = enemies;
            _weapons = weapons;
            _events = events;
            _run = run;
            _director = director;
            _pickups = pickups;
            _companions = companions;
            _buffs = buffs;
        }

        public PlayerModel Player => _player;
        public PlayerController PlayerController => _playerController;
        public EnemySystem Enemies => _enemies;
        public WeaponSystem Weapons => _weapons;
        public RunController Run => _run;
        public DirectorSystem Director => _director;
        public PickupSystem Pickups => _pickups;
        public CompanionSystem Companions => _companions;
        public BuffSystem Buffs => _buffs;

        public void Tick(float dt, ref InputSnapshot input)
        {
            _run?.Tick(dt);

            bool combatPhase = _run == null || _run.Phase == RunPhase.Battle || _run.Phase == RunPhase.Boss;

            if (combatPhase && !_player.IsDead)
            {
                Step(dt, ref input);
            }

            PublishPlayerDied();
            _events?.Flush();
        }

        void Step(float dt, ref InputSnapshot input)
        {
            _playerController.Tick(dt, ref input);
            TickExperienceCollect(dt, ref input);
            _companions?.TickMovement(_player.Position, dt);

            Vector2 playerPos = _player.Position;
            _enemies.Tick(playerPos, dt);

            if (_player.IsDead) return;

            _enemies.Grid.Rebuild(playerPos, _enemies);

            _shooterStats = new ShooterStats(
                _player.Stats[StatType.Attack],
                _player.Stats[StatType.CritChance],
                _player.Stats[StatType.CritDamage],
                _player.Stats[StatType.AttackSpeed],
                Mathf.RoundToInt(_player.Stats[StatType.ProjectileCount]),
                _player.Stats[StatType.Range]);

            TickPlayerRegen(dt);
            TickPlayerContact(dt, playerPos);

            _weapons.Tick(playerPos, _player.Forward, _shooterStats, dt);
            _companions?.TickWeapons(playerPos, dt);

            CleanupDeadEnemies();

            _pickups?.Tick(dt);
            _buffs?.Tick(dt);

            // 刷怪排在清理之后：本步刚被打死的怪已经腾出槽位与存活数，
            // 同屏上限算的是这一帧真实的存活数。升级阶段整步不跑，导演时间也随之暂停。
            _director?.Tick(dt, playerPos);

        }

        /// <summary>
        /// 全屏吸取经验（E/RB）。冷却每步推进，只有真的吸到经验才进入冷却；
        /// 输入边沿在这里消费，和冲刺一样只让一个模拟步看到这次按下。
        /// </summary>
        void TickExperienceCollect(float dt, ref InputSnapshot input)
        {
            if (_player.ExperienceCollectTimer > 0f)
            {
                _player.ExperienceCollectTimer -= dt;
                if (_player.ExperienceCollectTimer < 0f) _player.ExperienceCollectTimer = 0f;
            }

            if (!input.CollectPressed) return;
            input.CollectPressed = false;

            if (_pickups == null || !_player.CanCollectExperience) return;

            int orbs = _pickups.CollectAllExperience(out int totalExperience);
            if (orbs <= 0) return;

            _player.ExperienceCollectTimer = _player.ExperienceCollectCooldown;
            _events?.Publish(new ExperienceCollectPulse(_player.Position.x, _player.Position.y, orbs,
                totalExperience));
        }

        /// <summary>生命回复属性每个固定步结算一次。负数回复也允许，但不会把血量降到 0 以下。</summary>
        void TickPlayerRegen(float dt)
        {
            float regen = _player.Stats[StatType.Regen];
            if (regen == 0f || _player.IsDead) return;

            float maxHp = _player.MaxHP;
            _player.HP = Mathf.Clamp(_player.HP + regen * dt, 0f, maxHp);
        }

        /// <summary>
        /// 碰撞伤害。每 0.5 秒对贴身敌人结算一次，伤害来自 StatBlock[ContactDamage]。
        /// 走与武器相同的 ApplyToEnemy：护甲、击退、击杀归属都在同一条路径上。
        /// 闪避骰子固定传 1（敌人表没有闪避来源），以后敌人有 Dodge 时再从这里接随机流。
        /// </summary>
        void TickPlayerContact(float dt, Vector2 playerPos)
        {
            float contact = _player.Stats[StatType.ContactDamage];
            if (contact <= 0f)
            {
                _player.ContactTimer = 0f;
                return;
            }

            _player.ContactTimer -= dt;
            if (_player.ContactTimer > 0f) return;
            _player.ContactTimer = 0.5f;

            _contactTargets.Clear();
            _enemies.Grid.QueryCircle(playerPos, 1.2f, _contactTargets);
            bool hitAny = false;
            for (int i = 0; i < _contactTargets.Count; i++)
            {
                EnemyModel enemy = _enemies.Get(_contactTargets[i]);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                Vector2 toEnemy = enemy.Position - playerPos;
                float reach = enemy.Radius + 0.5f;
                if (toEnemy.sqrMagnitude > reach * reach) continue;

                Vector2 direction = toEnemy.sqrMagnitude > 1e-8f ? toEnemy.normalized : Vector2.up;
                var damage = new DamageInfo(contact, DamageType.Physical, UnitId.Player, direction,
                    WeaponDamage.KnockbackSpeed(contact), false,
                    DamageFlags.Dodgeable);
                DamageResult result = WeaponDamage.ApplyAndPublish(_events, enemy, damage, 1f);
                if (result.Applied > 0f) hitAny = true;
            }

            // 碰撞伤害是“贴身自动掉血”，没表现时会被当成刀在隔空砍人；给一个短暂的黄色环。
            if (hitAny)
            {
                _events?.Publish(new CombatAreaEffect(UnitId.Player, playerPos.x, playerPos.y, 1.2f,
                    CombatAreaEffectKind.Contact));
            }
        }

        void PublishPlayerDied()
        {
            if (_playerDiedPublished || !_player.IsDead) return;

            _playerDiedPublished = true;
            _events?.Publish(new PlayerDied(_player.Position.x, _player.Position.y));
        }

        /// <summary>
        /// 把这一步被打死的敌人从系统里摘掉，顺手掉一份经验。
        /// UnitDied 在 Remove 之前发：事件带 IsElite/IsBoss 与击杀归属，Remove 后模型就没了。
        /// </summary>
        void CleanupDeadEnemies()
        {
            for (int slot = 0; slot < _enemies.SlotCapacity; slot++)
            {
                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || !enemy.IsDead) continue;

                Vector2 position = enemy.Position;
                int xp = enemy.XpValue;

                _events?.Publish(new UnitDied(enemy.Id, enemy.LastDamageSource,
                    position.x, position.y, enemy.IsElite, enemy.IsBoss));

                _pickups?.DropEnemyLoot(position, xp, PickupSystem.TierOf(enemy));
                _enemies.Remove(slot);
            }
        }
    }
}
