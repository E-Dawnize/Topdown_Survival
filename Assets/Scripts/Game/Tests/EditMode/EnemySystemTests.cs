using Game.Config;
using Game.Model;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class EnemySystemTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;
        const float BodyRadius = 0.3f;

        static EnemyDef ChaseDef(float hp = 100f, float damage = 5f, float speed = 3f, float attackRange = 1.5f,
            float attackInterval = 0.5f)
        {
            return new EnemyDef
            {
                Id = "test_chaser",
                Name = "测试追击",
                Behaviour = EnemyBehaviourType.Chase,
                HP = hp,
                Damage = damage,
                Speed = speed,
                AttackRange = attackRange,
                AttackInterval = attackInterval,
                CoinDrop = 1,
                BodyRadius = BodyRadius
            };
        }

        static PlayerModel MakePlayer(float maxHp = 1000f)
        {
            return new PlayerModel(maxHp, 5f, 15f, 0.15f, 2f, ArenaHalf);
        }

        /// <summary>固定种子的小随机源。UnityEngine.Random 是全局状态，测试之间会互相影响。</summary>
        sealed class Lcg
        {
            uint _state;

            public Lcg(uint seed)
            {
                _state = seed;
            }

            public float Next01()
            {
                _state = _state * 1664525u + 1013904223u;
                return (_state >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>照主循环第 6、7 步走：先敌人移动，再重建格子。</summary>
        static void RunSteps(EnemySystem system, Vector2 playerPos, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                system.Tick(playerPos, Dt);
                system.Grid.Rebuild(playerPos, system);
            }
        }

        [Test]
        public void FiftyEnemiesChasePlayerWithoutStackingIntoOnePile()
        {
            // 基线（2026-10 本机 dotnet 实测）：minPair 0.387 / avgNearest 0.476 / 包围圈半径 2.03 米 / 重叠对 0。
            // 对照组：把分离力整个关掉，同样 600 步后 minPair 0.010、avgNearest 0.092、重叠对 93（一坨）。
            // 阈值都落在两组之间，以后调参可以漂一点，但不可能漂到"叠成一坨"还绿。
            const int enemyCount = 50;
            const int steps = 600;   // 10 秒，足够从 9 米外走到贴身再互相挤开

            var system = new EnemySystem(MakePlayer(), ArenaHalf);
            EnemyDef def = ChaseDef();
            var rng = new Lcg(20240521u);

            for (int i = 0; i < enemyCount; i++)
            {
                // 从半径 5~9 米的环上散着撒，避免一开始就人人重叠
                float angle = rng.Next01() * Mathf.PI * 2f;
                float radius = 5f + rng.Next01() * 4f;
                system.Spawn(def, new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }

            Vector2 playerPos = Vector2.zero;
            RunSteps(system, playerPos, steps);

            float spacing = BodyRadius * 2f;   // 两个身体刚好贴住的中心距
            float minPair = float.MaxValue;
            float sumNearest = 0f;
            float maxPlayerDistance = 0f;
            int overlappingPairs = 0;

            for (int i = 0; i < enemyCount; i++)
            {
                EnemyModel a = FindAlive(system, i);
                Assert.IsNotNull(a, "第 " + i + " 个敌人不该消失");

                float nearest = float.MaxValue;
                for (int j = 0; j < enemyCount; j++)
                {
                    if (i == j) continue;
                    EnemyModel b = FindAlive(system, j);
                    float distance = Vector2.Distance(a.Position, b.Position);
                    if (distance < nearest) nearest = distance;
                    if (i < j)
                    {
                        if (distance < minPair) minPair = distance;
                        if (distance < spacing * 0.5f) overlappingPairs++;
                    }
                }

                sumNearest += nearest;
                float playerDistance = Vector2.Distance(a.Position, playerPos);
                if (playerDistance > maxPlayerDistance) maxPlayerDistance = playerDistance;
            }

            float averageNearest = sumNearest / enemyCount;

            Assert.LessOrEqual(maxPlayerDistance, 4f,
                "敌人都该追到玩家身边（最远 " + maxPlayerDistance.ToString("F2") + " 米），不能留在出生点");
            Assert.GreaterOrEqual(minPair, spacing * 0.5f,
                "最近的一对不能叠在一起：最小间距 " + minPair.ToString("F3") + " 米，贴身距离 " + spacing.ToString("F2") + " 米");
            Assert.GreaterOrEqual(averageNearest, spacing * 0.6f,
                "平均最近邻距离 " + averageNearest.ToString("F3") + " 米太小，说明挤成了一坨");
            Assert.LessOrEqual(overlappingPairs, 2,
                "重叠对数 " + overlappingPairs + " 太多：分离力没生效");
        }

        [Test]
        public void EnemiesStayInsideArena()
        {
            // 场地故意设得比这群敌人还小：分离力会一直往外推，边界 clamp 必须兜住
            const float half = 1f;
            const int enemyCount = 20;

            var system = new EnemySystem(MakePlayer(), half);
            EnemyDef def = ChaseDef();
            var rng = new Lcg(7u);

            for (int i = 0; i < enemyCount; i++)
            {
                system.Spawn(def, new Vector2(rng.Next01() * 2f - 1f, rng.Next01() * 2f - 1f));
            }

            Vector2 playerPos = Vector2.zero;
            RunSteps(system, playerPos, 300);

            for (int i = 0; i < enemyCount; i++)
            {
                EnemyModel enemy = FindAlive(system, i);
                Assert.IsNotNull(enemy);
                Assert.LessOrEqual(Mathf.Abs(enemy.Position.x), half + 1e-3f, "第 " + i + " 个敌人从 x 方向跑出去了");
                Assert.LessOrEqual(Mathf.Abs(enemy.Position.y), half + 1e-3f, "第 " + i + " 个敌人从 z 方向跑出去了");
            }
        }

        [Test]
        public void ContactDamageRespectsAttackInterval()
        {
            var player = MakePlayer(1000f);
            var system = new EnemySystem(player, ArenaHalf);

            // 伤害 5、间隔 0.5 秒，敌人就站在玩家身上：不会被追击推力带走，纯粹考结算节奏
            EnemyDef def = ChaseDef(damage: 5f, attackInterval: 0.5f, attackRange: 1.5f);
            system.Spawn(def, Vector2.zero);

            Vector2 playerPos = Vector2.zero;

            RunSteps(system, playerPos, 1);
            Assert.AreEqual(995f, player.HP, 1e-3f, "第一步就该结第一次接触伤害");

            // 再走 10 步（合计 0.18 秒）仍远没到 0.5 秒的间隔，不能重复扣血
            RunSteps(system, playerPos, 10);
            Assert.AreEqual(995f, player.HP, 1e-3f, "间隔没到就重复扣血了");

            // 10 秒里按 0.5 秒一次应当是 20 次上下（首步也算一次），绝不是每步都扣
            RunSteps(system, playerPos, 600);
            float hits = (1000f - player.HP) / 5f;
            Assert.GreaterOrEqual(hits, 19f, "10 秒内结算次数 " + hits + " 太少");
            Assert.LessOrEqual(hits, 22f, "10 秒内结算次数 " + hits + " 太多");
        }

        [Test]
        public void ChaseStopsAtAttackRange()
        {
            var player = MakePlayer();
            var system = new EnemySystem(player, ArenaHalf);
            EnemyDef def = ChaseDef(attackRange: 1.5f);
            int slot = system.Spawn(def, new Vector2(5f, 0f));

            Vector2 playerPos = Vector2.zero;
            RunSteps(system, playerPos, 300);   // 5 秒，速度 3 米/秒，早该到位

            EnemyModel enemy = system.Get(slot);
            float distance = Vector2.Distance(enemy.Position, playerPos);

            // 一步最多走 3/60 = 0.05 米，所以会停在接触距离内侧一点点
            Assert.GreaterOrEqual(distance, 1.4f, "追过头了：距离 " + distance.ToString("F3"));
            Assert.LessOrEqual(distance, 1.55f, "没走到接触距离：距离 " + distance.ToString("F3"));
            Assert.AreEqual(1, system.AliveCount);
        }

        static EnemyModel FindAlive(EnemySystem system, int index)
        {
            // 槽位号是稳定的，但测试里按"第几个生成"数，所以扫一遍存活槽位
            int seen = 0;
            for (int slot = 0; slot < system.SlotCapacity; slot++)
            {
                EnemyModel enemy = system.Get(slot);
                if (enemy == null || !enemy.IsAlive) continue;
                if (seen == index) return enemy;
                seen++;
            }
            return null;
        }
    }
}
