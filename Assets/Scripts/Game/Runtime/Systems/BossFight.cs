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
    /// 刷怪环的角度序列。时间轴刷怪与 BOSS 召唤共用同一条序列：
    /// 每批怪按黄金角错开，批次计数每刷一批加一，所以固定种子下两条路径的角度可复现。
    /// 相位与批次计数必须只有一份——拆成两份就会让 BOSS 召唤和普通刷怪各转各的。
    /// </summary>
    public sealed class SpawnSequence
    {
        readonly IRandom01 _random;
        float _phase;
        int _batchIndex;

        public SpawnSequence(IRandom01 random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>下一批刷怪使用的基准角度。</summary>
        public float Angle => _phase + _batchIndex * SpawnRing.GoldenAngleRad;

        /// <summary>开一局：用本局随机流重新取相位，批次从 0 开始。</summary>
        public void Reset()
        {
            _phase = _random.Next01() * Mathf.PI * 2f;
            _batchIndex = 0;
        }

        /// <summary>一批刷完，角度转到下一位。</summary>
        public void Advance()
        {
            _batchIndex++;
        }
    }

    /// <summary>
    /// BOSS 战的内聚状态：出场、召唤、半血狂暴、计时与"已被击杀"。
    ///
    /// 时间轴刷怪留在 DirectorSystem；本类只拿最后一段导演配置（BOSS 与召唤参数）和
    /// 共享的刷怪环序列。它是 BOSS 状态的唯一来源：DirectorSystem / RunController 的
    /// BossActive、BossName、BossHpRatio 等都只读这里，不再各存一份再互相同步。
    /// </summary>
    public sealed class BossFight
    {
        readonly EnemySystem _enemies;
        readonly IReadOnlyDictionary<string, EnemyDef> _enemyTable;
        readonly DirectorDef _config;
        readonly IRandom01 _random;
        readonly SpawnSequence _spawnSequence;
        readonly IEventBus _events;

        /// <summary>把新召唤出来的怪的最高 Tier 报给导演的 HUD 统计。可为空。</summary>
        readonly Action<int> _reportTier;

        bool _active;
        bool _enraged;
        bool _defeated;
        UnitId _unitId = UnitId.None;
        int _slot = -1;
        EnemyDef _def;
        float _maxHp;
        float _elapsed;
        float _nextAddAt;

        public BossFight(EnemySystem enemies, IReadOnlyDictionary<string, EnemyDef> enemyTable,
            DirectorDef lastDirector, IRandom01 random, SpawnSequence spawnSequence, IEventBus events = null,
            Action<int> reportTier = null)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _enemyTable = enemyTable ?? throw new ArgumentNullException(nameof(enemyTable));
            _config = lastDirector ?? throw new ArgumentNullException(nameof(lastDirector));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _spawnSequence = spawnSequence ?? throw new ArgumentNullException(nameof(spawnSequence));
            _events = events;
            _reportTier = reportTier;
        }

        public bool Active => _active;
        public bool Enraged => _enraged;
        public bool Defeated => _defeated;
        public UnitId UnitId => _unitId;
        public string Id => _def != null ? _def.Id : string.Empty;
        public string Name => _def != null ? _def.Name : string.Empty;
        public float MaxHp => _maxHp;
        public float FightSeconds => _elapsed;

        /// <summary>BOSS 当前血量。没出场或已死亡时返回 0。</summary>
        public float Hp
        {
            get
            {
                EnemyModel boss = BossInstance();
                return boss != null && !boss.IsDead ? boss.HP : 0f;
            }
        }

        public float HpRatio
        {
            get
            {
                if (_maxHp <= 0f) return 0f;
                return Mathf.Clamp01(Hp / _maxHp);
            }
        }

        /// <summary>开一局清空 BOSS 状态（不重取随机流；刷怪环相位由 SpawnSequence 负责）。</summary>
        public void Begin()
        {
            _active = false;
            _enraged = false;
            _defeated = false;
            _unitId = UnitId.None;
            _slot = -1;
            _def = null;
            _maxHp = 0f;
            _elapsed = 0f;
            _nextAddAt = 0f;
        }

        /// <summary>
        /// BOSS 出场。位置在玩家外侧一个刷怪环点上，出场即进入战斗。
        /// 返回 false 表示最后一段没配 BOSS 或敌人表里找不到它；此时不改任何状态。
        /// </summary>
        public bool Start(Vector2 playerPosition)
        {
            if (_active) return false;
            if (string.IsNullOrEmpty(_config.BossId)) return false;
            if (!_enemyTable.TryGetValue(_config.BossId, out EnemyDef def)) return false;

            _active = true;
            _enraged = false;
            _defeated = false;
            _def = def;
            _elapsed = 0f;
            _nextAddAt = _config.BossAddInterval;

            float angle = _spawnSequence.Angle;
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12f;
            var options = new EnemySpawnOptions(_config.HpScale, _config.DamageScale, _config.SpeedScale, 1f, 1f,
                false, true);
            _slot = _enemies.Spawn(def, playerPosition + offset, options);

            EnemyModel boss = _enemies.Get(_slot);
            _unitId = boss != null ? boss.Id : UnitId.None;
            _maxHp = boss != null ? boss.MaxHP : 0f;
            _reportTier?.Invoke(def.Tier);
            return true;
        }

        /// <summary>
        /// BOSS 阶段每步推进：计时、半血狂暴一次、按间隔召唤。
        /// BOSS 已死亡时只继续累计战斗时长，与旧导演行为一致。
        /// </summary>
        public void Tick(float dt, Vector2 playerPosition)
        {
            if (!_active) return;

            _elapsed += dt;

            EnemyModel boss = BossInstance();
            if (boss == null || boss.IsDead) return;

            // 半血狂暴：只触发一次。提高的是已经写进 StatBlock 的基线，追击速度与接触伤害立刻生效。
            if (!_enraged && boss.HP <= _maxHp * 0.5f)
            {
                _enraged = true;
                boss.Stats.SetBase(StatType.MoveSpeed, boss.Def.Speed * _config.SpeedScale * 1.2f);
                boss.Stats.SetBase(StatType.ContactDamage, boss.Def.Damage * _config.DamageScale * 1.35f);
                SpawnAdds(playerPosition, _config.BossAddCount * 2);
                _events?.Publish(new BossEnraged(Id));
            }

            if (_elapsed < _nextAddAt) return;

            _nextAddAt += Mathf.Max(1f, _config.BossAddInterval);
            SpawnAdds(playerPosition, _config.BossAddCount);
        }

        /// <summary>BOSS 死亡时由 RunController 通知：停止召唤并记录已击杀。</summary>
        public void NotifyDefeated()
        {
            if (_defeated) return;
            _defeated = true;
            _active = false;
        }

        EnemyModel BossInstance()
        {
            if (!_unitId.IsValid) return null;
            EnemyModel boss = _enemies.Get(_slot);
            if (boss == null || !boss.IsAlive || boss.Id != _unitId) return null;
            return boss;
        }

        void SpawnAdds(Vector2 playerPosition, int count)
        {
            if (count <= 0) return;

            // BOSS 召唤同样受最后一段的 ConcurrentCap 约束。
            // 否则玩家拖长战斗时，召唤物会无限累积，既没有玩法收益，也会把压力测试的基线破坏掉。
            int room = _config.ConcurrentCap - _enemies.AliveCount;
            if (room <= 0) return;
            if (count > room) count = room;

            for (int i = 0; i < count; i++)
            {
                EnemyDef def = PickEnemy(_config.EnemyPool);
                if (def == null) continue;

                Vector2 offset = SpawnRing.Offset(i, count, _spawnSequence.Angle, 6f, 12f);
                SpawnAdd(def, playerPosition + offset);
            }

            _spawnSequence.Advance();
        }

        void SpawnAdd(EnemyDef def, Vector2 position)
        {
            var options = new EnemySpawnOptions(_config.HpScale, _config.DamageScale, _config.SpeedScale, 1f, 1f,
                false, false);
            _enemies.Spawn(def, position, options);
            _reportTier?.Invoke(def.Tier);
        }

        EnemyDef PickEnemy(string[] pool)
        {
            if (pool == null || pool.Length == 0) return null;

            int index = (int)(_random.Next01() * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            return _enemyTable.TryGetValue(pool[index], out EnemyDef def) ? def : null;
        }
    }
}
