using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 刷怪节奏、同屏上限与出生位置。刷怪出口用 IEnemySpawnTarget 的假实现代替 EnemySystem：
    /// 这类断言要的是"多久刷、刷几个、刷在哪"，与 EnemyModel 怎么建无关，
    /// 换成假出口才能把"刷怪循环自己分配不分配"称出来。
    /// </summary>
    public class WaveControllerTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void SpawnIntervalMatchesConfigWithinOneStep()
        {
            var target = new CountingSpawnTarget();
            var waves = new WaveController(target, EnemyTable(), new DeterministicRandom(7));
            waves.BeginWave(Wave(180f, 1.2f, 2, 2, 400, "Crawler"));

            // 记下"哪几步刷了新的一批"。数量固定 2，所以刷两个一批只算一次。
            var batchSteps = new List<int>();
            int lastSpawned = 0;
            for (int step = 1; step <= 600; step++)   // 10 秒
            {
                waves.Tick(Dt, Vector2.zero);
                if (target.Spawns == lastSpawned) continue;

                lastSpawned = target.Spawns;
                batchSteps.Add(step);
            }

            int expected = Mathf.RoundToInt(1.2f / Dt);   // 1.2 秒 = 72 个模拟步
            Assert.GreaterOrEqual(batchSteps.Count, 8, "10 秒里 1.2 秒一批，该刷 8 批：" + batchSteps.Count);
            for (int i = 1; i < batchSteps.Count; i++)
            {
                int gap = batchSteps[i] - batchSteps[i - 1];
                Assert.LessOrEqual(Mathf.Abs(gap - expected), 1,
                    "第 " + i + " 批与第 " + (i + 1) + " 批相隔 " + gap + " 步，期望 " + expected + "±1 步");
            }
        }

        [Test]
        public void ConcurrentCapIsNeverExceeded()
        {
            var target = new FixedAliveTarget();
            var waves = new WaveController(target, EnemyTable(), new DeterministicRandom(11));
            waves.BeginWave(Wave(180f, 0.5f, 1, 3, 10, "Crawler"));

            for (int step = 1; step <= 1200; step++)   // 20 秒
            {
                waves.Tick(Dt, Vector2.zero);

                if (target.Alive > 10)
                {
                    Assert.Fail("第 " + step + " 步同屏 " + target.Alive + " 个，突破了上限 10");
                }
            }

            Assert.AreEqual(10, target.Alive, "上限没填满说明刷怪提前停了，这批怪等不到人打");
        }

        [Test]
        public void SpawnsLandInsideTheAnnulusAndOnlyUseTheWavePool()
        {
            var target = new RecordingSpawnTarget();
            var waves = new WaveController(target, EnemyTable(), new DeterministicRandom(3));
            waves.BeginWave(Wave(180f, 0.5f, 2, 3, 100, "Crawler", "Thrower"));

            var origin = new Vector2(12f, -5f);
            for (int step = 1; step <= 300; step++)   // 5 秒
            {
                waves.Tick(Dt, origin);
            }

            Assert.Greater(target.Positions.Count, 10, "5 秒该刷出十来个");

            bool sawCrawler = false;
            bool sawThrower = false;
            for (int i = 0; i < target.Positions.Count; i++)
            {
                float radius = (target.Positions[i] - origin).magnitude;
                Assert.GreaterOrEqual(radius, WaveController.SpawnRadiusMin - 1e-3f, "第 " + i + " 个刷进了内圈");
                Assert.LessOrEqual(radius, WaveController.SpawnRadiusMax + 1e-3f, "第 " + i + " 个刷出了外圈");

                string id = target.Ids[i];
                Assert.IsTrue(id == "Crawler" || id == "Thrower", "刷出了怪种池以外的 '" + id + "'");
                if (id == "Crawler") sawCrawler = true;
                else sawThrower = true;
            }

            Assert.IsTrue(sawCrawler && sawThrower, "怪种池里两种都该被抽到，一直只出一种说明选怪没走随机");
        }

        [Test]
        public void DurationStopsTheSpawning()
        {
            var target = new CountingSpawnTarget();
            var waves = new WaveController(target, EnemyTable(), new DeterministicRandom(5));
            waves.BeginWave(Wave(2f, 0.25f, 1, 1, 100, "Crawler"));

            // 2 秒的波，每 0.25 秒刷 1 个：第一批在第一步就刷，之后每 15 步一批，一共 8 个
            for (int step = 1; step <= 600; step++)
            {
                waves.Tick(Dt, Vector2.zero);
            }

            Assert.AreEqual(8, target.Spawns, "时长用完以后不该再刷：2 秒 / 0.25 秒 = 8 批");
        }

        /// <summary>
        /// 三波 180 秒 = 540 秒 × 60Hz 的整局，刷怪循环自己的分配必须是 0 字节。
        /// 假出口里全是 int 字段，所以这里量到的就是波次循环的开销（环计算、选怪、上限判断、批量记账）；
        /// 每个敌人一个 EnemyModel 是模型层的既成分配，不在这条断言的范围内。
        /// </summary>
        [Test]
        public void SpawnLoopDoesNotAllocateOverAFullRun()
        {
            var target = new CountingSpawnTarget();
            var waves = new WaveController(target, EnemyTable(), new DeterministicRandom(9));

            // 与 Wave.json 一致的三波
            WaveDef[] config =
            {
                Wave(180f, 1.2f, 1, 2, 40, "Crawler"),
                Wave(180f, 1.0f, 2, 3, 70, "Crawler", "Thrower"),
                Wave(180f, 0.9f, 2, 3, 100, "Crawler", "Charger", "Thrower"),
            };

            // 预热一分钟：JIT、Mathf 的静态初始化这类一次性开销不该记在刷怪循环头上
            waves.BeginWave(config[0]);
            for (int i = 0; i < 3600; i++)
            {
                waves.Tick(Dt, Vector2.zero);
            }

            int spawnedBefore = target.Spawns;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

            const int StepsPerWave = 10800;   // 180 秒 × 60Hz
            for (int wave = 0; wave < config.Length; wave++)
            {
                waves.BeginWave(config[wave]);
                for (int i = 0; i < StepsPerWave; i++)
                {
                    waves.Tick(Dt, Vector2.zero);
                }
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.Greater(target.Spawns, spawnedBefore, "一个怪都没刷出来，说明零分配是空跑量出来的");
            Assert.AreEqual(0L, allocated,
                "540 秒 × 60Hz 的刷怪循环分配了 " + allocated + " 字节（" + config.Length * StepsPerWave + " 步，" +
                (target.Spawns - spawnedBefore) + " 个敌人）");
        }

        // ---- 装配 ----

        /// <summary>照抄 Enemy.json 的三个怪种：这里只用到 Id 与 Id 表里的引用。</summary>
        static Dictionary<string, EnemyDef> EnemyTable()
        {
            return new Dictionary<string, EnemyDef>
            {
                ["Crawler"] = new EnemyDef
                {
                    Id = "Crawler", Name = "爬虫", Behaviour = EnemyBehaviourType.Chase,
                    HP = 12f, Damage = 6f, Speed = 3f, AttackRange = 0.6f, AttackInterval = 1f,
                    CoinDrop = 1, BodyRadius = 0.35f
                },
                ["Charger"] = new EnemyDef
                {
                    Id = "Charger", Name = "冲锋兵", Behaviour = EnemyBehaviourType.Charge,
                    HP = 18f, Damage = 10f, Speed = 2.5f, AttackRange = 4f, AttackInterval = 3f,
                    CoinDrop = 2, BodyRadius = 0.4f
                },
                ["Thrower"] = new EnemyDef
                {
                    Id = "Thrower", Name = "投手", Behaviour = EnemyBehaviourType.Ranged,
                    HP = 15f, Damage = 8f, Speed = 2f, AttackRange = 5f, AttackInterval = 2.5f,
                    CoinDrop = 2, BodyRadius = 0.35f
                },
            };
        }

        static WaveDef Wave(float duration, float interval, int min, int max, int cap, params string[] pool)
        {
            return new WaveDef
            {
                Id = "test_wave", Index = 1, Duration = duration, SpawnInterval = interval,
                SpawnCountMin = min, SpawnCountMax = max, EnemyPool = pool, ConcurrentCap = cap,
                CoinReward = 0, BossId = ""
            };
        }

        /// <summary>
        /// 只记账、不分配。AliveCount 恒为 0，上限永远不挡刷怪，所以每一批都真的走完刷怪路径。
        /// </summary>
        sealed class CountingSpawnTarget : IEnemySpawnTarget
        {
            public int Spawns;

            public int AliveCount => 0;

            public int Spawn(EnemyDef def, Vector2 position) => ++Spawns;
        }

        /// <summary>同屏数由测试摆布的出口：每生成一个就 +1，用来验 ConcurrentCap。</summary>
        sealed class FixedAliveTarget : IEnemySpawnTarget
        {
            public int Alive;
            public int Spawns;

            public int AliveCount => Alive;

            public int Spawn(EnemyDef def, Vector2 position)
            {
                Spawns++;
                Alive++;
                return Spawns;
            }
        }

        /// <summary>记录出生点与怪种，只在位置/怪种两条断言里用（会分配，不进零分配测试）。</summary>
        sealed class RecordingSpawnTarget : IEnemySpawnTarget
        {
            public readonly List<Vector2> Positions = new List<Vector2>();
            public readonly List<string> Ids = new List<string>();

            public int AliveCount => 0;

            public int Spawn(EnemyDef def, Vector2 position)
            {
                Positions.Add(position);
                Ids.Add(def.Id);
                return Positions.Count;
            }
        }
    }
}
