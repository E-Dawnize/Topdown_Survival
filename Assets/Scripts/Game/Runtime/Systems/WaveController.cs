using System.Collections.Generic;
using Game.Config;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 一波敌人：什么时候刷、刷几个、刷什么、刷在哪。
    ///
    /// 不管"这一波该不该结束"——波次计时归 RunController，它一离开战斗态就不再 tick 这里，
    /// 刷怪自然停。两份时钟各管一件事，不会出现"波次已经收尾而刷怪间隔还挂在上一波"。
    /// </summary>
    public sealed class WaveController
    {
        /// <summary>
        /// 黄金角（弧度）。同一批里逐个转 137.5°：等角铺开的一串点不会落在同几条射线上，
        /// 比每个怪各随机一个角度均匀得多（同样 200 个点，这里的最近两点 1.5 米，
        /// 随机撒点的最近两点中位数只有 0.08 米）。同屏数量大时，这一条决定开局是一群还是一团。
        /// </summary>
        const float GoldenAngleRad = 2.39996323f;

        /// <summary>
        /// 刷怪环：玩家周围 8~15 米。太近会一开局就贴身，太远在 8 米射程外看不见开火。
        /// 公开给测试：环带装不装得下 ConcurrentCap 个敌人是按这两个半径算出来的。
        /// </summary>
        public const float SpawnRadiusMin = 8f;

        public const float SpawnRadiusMax = 15f;

        readonly IEnemySpawnTarget _enemies;
        readonly IReadOnlyDictionary<string, EnemyDef> _enemyTable;

        /// <summary>
        /// 刷怪用的随机流。按 I2-2 的要求走战斗那条流：一局只有一条随机源，
        /// 刷怪抽的怪种与暴击、闪避同源，"固定种子 + 固定输入 = 相同结果"就只需守一处。
        /// 代价是刷怪多抽一次也会挪动暴击序列——这是这一版明确接受的取舍。
        /// </summary>
        readonly IRandom01 _random;

        WaveDef _wave;
        float _elapsed;
        float _nextSpawnAt;
        float _phase;
        int _batchIndex;

        public WaveController(IEnemySpawnTarget enemies, IReadOnlyDictionary<string, EnemyDef> enemyTable,
            IRandom01 random)
        {
            _enemies = enemies ?? throw new System.ArgumentNullException(nameof(enemies));
            _enemyTable = enemyTable ?? throw new System.ArgumentNullException(nameof(enemyTable));
            _random = random ?? throw new System.ArgumentNullException(nameof(random));
        }

        /// <summary>当前这一波。BeginWave 之前是 null（也就不会刷任何东西）。</summary>
        public WaveDef CurrentWave => _wave;

        /// <summary>
        /// 换波：重置本波计时与刷怪环相位。相位每波从随机流里取一次，
        /// 所以连着两波不会刷在同样的几个角度上。
        /// </summary>
        public void BeginWave(WaveDef wave)
        {
            _wave = wave ?? throw new System.ArgumentNullException(nameof(wave));
            _elapsed = 0f;
            _nextSpawnAt = 0f;   // 波次一开始就刷第一批，不留空场
            _phase = _random.Next01() * Mathf.PI * 2f;
            _batchIndex = 0;
        }

        /// <summary>
        /// 推进刷怪。spawnOrigin 是本步的玩家位置，刷怪环以它为圆心。
        /// 每步只有加法与比较，不分配内存：整局 540 秒 × 60Hz 跑下来这条路径是 0 B。
        /// </summary>
        public void Tick(float dt, Vector2 spawnOrigin)
        {
            if (_wave == null) return;

            _elapsed += dt;

            // 时长用完就不再刷。RunController 到点就离开战斗态、根本不会再 tick 这里，
            // 这条挡板是给"以后换了驱动顺序"留的护栏，不是第二份计时。
            if (_elapsed >= _wave.Duration) return;

            // 固定步长下一次 Tick 最多补一批：配置里最小间隔 0.7 秒、一步 1/60 秒，远不及一批。
            // 写成 while 是为了 dt 被外部放大时按间隔补齐，刷怪节奏不会因为一次卡顿被整体推后。
            // 间隔为 0 时直接不刷：配置校验会拦住它，这里不给自己留死循环。
            while (_wave.SpawnInterval > 0f && _elapsed >= _nextSpawnAt)
            {
                _nextSpawnAt += _wave.SpawnInterval;
                SpawnBatch(spawnOrigin);
            }
        }

        void SpawnBatch(Vector2 origin)
        {
            string[] pool = _wave.EnemyPool;
            if (pool == null || pool.Length == 0) return;

            // 上限按"当前存活数"算，而且每批刷之前重算一次：RunSimulation 把刷怪排在本步清理之后，
            // 这一步刚被打死的怪已经不在场上了，所以卡的是真实同屏数。
            int room = _wave.ConcurrentCap - _enemies.AliveCount;
            if (room <= 0) return;   // 已经堆到上限：这一批直接不刷，宁可少刷也不突破上限

            int count = SpawnCount();
            if (count > room) count = room;

            // 整批一起再转一个黄金角：连续两批不会刷在同样的几个角度上
            float phase = _phase + _batchIndex * GoldenAngleRad;

            for (int i = 0; i < count; i++)
            {
                EnemyDef def = PickEnemy(pool);
                if (def == null) continue;   // 池里引用了敌人表没有的 Id：跳过这一个，不停掉整波

                Vector2 offset = SpawnRingOffset(i, count, phase, SpawnRadiusMin, SpawnRadiusMax);
                _enemies.Spawn(def, origin + offset);
            }

            _batchIndex++;
        }

        /// <summary>单批数量：SpawnCountMin..SpawnCountMax 闭区间。</summary>
        int SpawnCount()
        {
            int min = _wave.SpawnCountMin;
            int max = _wave.SpawnCountMax;
            if (max <= min) return min;

            int span = max - min + 1;
            int offset = (int)(_random.Next01() * span);
            return min + (offset < span ? offset : span - 1);   // Next01 的浮点边界兜一下
        }

        EnemyDef PickEnemy(string[] pool)
        {
            int index = (int)(_random.Next01() * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;

            return _enemyTable.TryGetValue(pool[index], out EnemyDef def) ? def : null;
        }

        /// <summary>
        /// 刷怪环上的第 index 个点（相对玩家）：角度按黄金角逐个转开，半径按面积均匀分布。
        ///
        /// 不按"每个敌人各随机一个角度与半径"撒点：200 个点里会有十几对撞在一起（中位数 12 对相距 0.3 米内，
        /// 最近的一对 0.01 米），出生那一刻就重叠成一团，分离力要跑几百步才推得开——压测一开局看到的就是一团。
        /// 铺均匀以后最小间距约 1.5 米，比两个身体加起来还宽；之后它们自己挤向玩家、挤成一圈，
        /// 那才是压力测试要的稳态（围在玩家身边互相推挤），而不是出生就叠在一起。
        /// </summary>
        /// <param name="index">0..count-1。</param>
        /// <param name="count">这一批刷多少。</param>
        /// <param name="phase">整圈的角度偏移，调用方从随机流里取，保证同一种子复现同一批位置。</param>
        public static Vector2 SpawnRingOffset(int index, int count, float phase, float radiusMin,
            float radiusMax)
        {
            if (count <= 0) return Vector2.zero;

            // 半径按 t 的平方根映射：这样点在环带上的面密度才均匀，
            // 直接按 t 线性插值会让点朝内圈挤（外圈周长更大、站位更多）。
            // +0.5 让首尾两个点也各占半格，不贴到环带的两个极端上。
            float t = (index + 0.5f) / count;
            float radius = Mathf.Sqrt(Mathf.Lerp(radiusMin * radiusMin, radiusMax * radiusMax, t));

            float angle = phase + index * GoldenAngleRad;
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }
}
