using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 刷怪导演：一局只有一条时间轴，不再有"波次"。
    ///
    /// 每段时间配置自己的刷怪间隔、单批数量、怪种池、精英概率、同屏上限与成长倍率。
    /// 时间越往后，数量变多、间隔变短、怪种池从 Crawler 一路扩到 Warden/Ravager，
    /// 同时血量/伤害/速度倍率连续上升——这就是"随时间加难度"的唯一出口。
    ///
    /// 最后一段的 EndTime 就是 BOSS 时间。BOSS 战（出场、召唤、狂暴、计时、击杀状态）
    /// 由 BossFight 这个内聚对象负责；本类只保留时间轴刷怪，并把 BOSS 属性转发给调用方。
    /// 时间轴与 BOSS 召唤共用 SpawnSequence，固定种子下角度序列仍可复现。
    ///
    /// 算法只依赖 DirectorSettings（时间轴 + 5 个精英倍率）；接收 ConfigDatabase 的构造
    /// 只是给 AppRoot / RunComposition / TestRun 的装配便利。
    /// </summary>
    public sealed class DirectorSystem
    {
        readonly EnemySystem _enemies;
        readonly IReadOnlyDictionary<string, EnemyDef> _enemyTable;
        readonly DirectorSettings _settings;
        readonly IReadOnlyList<DirectorDef> _directors;
        readonly IRandom01 _random;
        readonly IEventBus _events;

        readonly SpawnSequence _spawnSequence;
        readonly BossFight _bossFight;

        float _elapsed;
        float _nextSpawnAt;
        int _currentPhaseIndex = -1;
        int _highestTier;

        /// <summary>装配便利构造：从整个配置库取时间轴与 5 个精英倍率，算法本身只依赖窄配置。</summary>
        public DirectorSystem(EnemySystem enemies, IReadOnlyDictionary<string, EnemyDef> enemyTable,
            IReadOnlyList<DirectorDef> directors, IRandom01 random, ConfigDatabase config,
            IEventBus events = null)
            : this(enemies, enemyTable, DirectorSettings.FromConfig(directors, config), random, events)
        {
        }

        public DirectorSystem(EnemySystem enemies, IReadOnlyDictionary<string, EnemyDef> enemyTable,
            DirectorSettings settings, IRandom01 random, IEventBus events = null)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _enemyTable = enemyTable ?? throw new ArgumentNullException(nameof(enemyTable));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _directors = settings.Directors;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _events = events;

            _spawnSequence = new SpawnSequence(random);
            _bossFight = new BossFight(enemies, enemyTable, LastDirector(), random, _spawnSequence, events,
                ReportTier);
        }

        /// <summary>BOSS 出场时间 = 最后一段的结束时间。RunController 读它决定什么时候切阶段。</summary>
        public float BossTime => LastDirector().EndTime;

        public float Elapsed => _elapsed;
        public int CurrentThreat => Current == null ? 1 : Current.Threat;
        public int HighestTier => _highestTier;

        /// <summary>当前时间段的配置。开战前返回第一段，越界时夹到两端。</summary>
        public DirectorDef Current
        {
            get
            {
                int index = PhaseIndexAt(_elapsed);
                return index >= 0 && index < _directors.Count ? _directors[index] : null;
            }
        }

        /// <summary>BOSS 状态的唯一来源。RunController/HUD 需要更细的状态时读它。</summary>
        public BossFight BossFight => _bossFight;

        public bool BossActive => _bossFight.Active;
        public bool BossEnraged => _bossFight.Enraged;
        public bool BossDefeated => _bossFight.Defeated;
        public UnitId BossUnitId => _bossFight.UnitId;
        public string BossId => _bossFight.Id;
        public string BossName => _bossFight.Name;
        public float BossMaxHp => _bossFight.MaxHp;
        public float BossHp => _bossFight.Hp;
        public float BossHpRatio => _bossFight.HpRatio;
        public float BossFightSeconds => _bossFight.FightSeconds;

        /// <summary>开一局：清空计时与 BOSS 状态。敌人的清理由场景重载负责。</summary>
        public void Begin()
        {
            _elapsed = 0f;
            _nextSpawnAt = 0f;
            _spawnSequence.Reset();
            _currentPhaseIndex = -1;
            _highestTier = 1;

            _bossFight.Begin();
        }

        /// <summary>
        /// 推进普通刷怪。只在战斗阶段调用；BOSS 阶段由 BossFight 接管。
        /// 时间超过 BossTime 后这里不再刷，等 RunController 切阶段。
        /// </summary>
        public void Tick(float dt, Vector2 playerPosition)
        {
            if (dt <= 0f) return;

            _elapsed += dt;
            if (_bossFight.Active)
            {
                _bossFight.Tick(dt, playerPosition);
                return;
            }

            float bossTime = BossTime;
            if (_elapsed >= bossTime) return;

            DirectorDef phase = Current;
            if (phase == null) return;

            // 固定步长下 while 一般只走一次；dt 被外部放大时按间隔补齐，刷怪节奏不会因为一次卡顿整体推后。
            while (_elapsed >= _nextSpawnAt && _elapsed < bossTime)
            {
                DirectorDef current = Current;
                if (current == null) break;
                SpawnBatch(playerPosition, current);
                _nextSpawnAt += current.SpawnInterval;
            }
        }

        /// <summary>
        /// 时间到，BOSS 出场。调用方只调一次；重复调用直接返回。
        /// 实际出场与后续状态归 BossFight，本类只做兼容转发。
        /// </summary>
        public bool BeginBoss(Vector2 playerPosition)
        {
            return _bossFight.Start(playerPosition);
        }

        /// <summary>BOSS 死亡时由 RunController 通知，导演停止召唤并记录击杀状态。</summary>
        public void NotifyBossDefeated()
        {
            _bossFight.NotifyDefeated();
        }

        void ReportTier(int tier)
        {
            _highestTier = Math.Max(_highestTier, tier);
        }

        void SpawnBatch(Vector2 origin, DirectorDef phase)
        {
            if (_currentPhaseIndex != PhaseIndexAt(_elapsed))
            {
                _currentPhaseIndex = PhaseIndexAt(_elapsed);
                _highestTier = Math.Max(_highestTier, phase.Threat);
            }

            if (phase.EnemyPool == null || phase.EnemyPool.Length == 0) return;

            int room = phase.ConcurrentCap - _enemies.AliveCount;
            if (room <= 0) return;

            int count = SpawnCount(phase);
            if (count > room) count = room;

            float batchPhase = _spawnSequence.Angle;
            for (int i = 0; i < count; i++)
            {
                EnemyDef def = PickEnemy(phase.EnemyPool);
                if (def == null) continue;

                Vector2 offset = SpawnRing.Offset(i, count, batchPhase);
                SpawnOne(def, origin + offset, false, phase.EliteChance);
            }

            _spawnSequence.Advance();
        }

        void SpawnOne(EnemyDef def, Vector2 position, bool forceBoss, float eliteChance)
        {
            DirectorDef phase = Current;
            bool elite = !forceBoss && eliteChance > 0f && _random.Next01() < eliteChance;
            EliteScaling scaling = _settings.Elite;

            float hp = phase.HpScale * (elite ? scaling.Hp : 1f);
            float damage = phase.DamageScale * (elite ? scaling.Damage : 1f);
            float speed = phase.SpeedScale * (elite ? scaling.Speed : 1f);
            float visual = elite ? scaling.VisualScale : 1f;
            float xp = elite ? scaling.Xp : 1f;

            var options = new EnemySpawnOptions(hp, damage, speed, visual, xp, elite, forceBoss);
            _enemies.Spawn(def, position, options);
            _highestTier = Math.Max(_highestTier, def.Tier);
        }

        int SpawnCount(DirectorDef phase)
        {
            int min = phase.SpawnCountMin;
            int max = phase.SpawnCountMax;
            if (max <= min) return min;

            int span = max - min + 1;
            int offset = (int)(_random.Next01() * span);
            return min + (offset < span ? offset : span - 1);
        }

        EnemyDef PickEnemy(string[] pool)
        {
            if (pool == null || pool.Length == 0) return null;

            int index = (int)(_random.Next01() * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            return _enemyTable.TryGetValue(pool[index], out EnemyDef def) ? def : null;
        }

        int PhaseIndexAt(float time)
        {
            for (int i = 0; i < _directors.Count; i++)
            {
                if (time < _directors[i].EndTime) return i;
            }
            return _directors.Count - 1;
        }

        DirectorDef LastDirector() => _directors[_directors.Count - 1];
    }
}
