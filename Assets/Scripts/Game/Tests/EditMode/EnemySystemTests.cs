using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
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
                XpValue = 1,
                BodyRadius = BodyRadius, Scale = 1f
            };
        }

        static EnemyDef RangedDef(float hp = 100f, float damage = 8f, float speed = 2f, float attackRange = 5f,
            float attackInterval = 2f)
        {
            return new EnemyDef
            {
                Id = "test_thrower",
                Name = "测试投手",
                Behaviour = EnemyBehaviourType.Ranged,
                HP = hp,
                Damage = damage,
                Speed = speed,
                AttackRange = attackRange,
                AttackInterval = attackInterval,
                XpValue = 1,
                BodyRadius = BodyRadius, Scale = 1f
            };
        }

        static EnemyDef ChargeDef(float hp = 100f, float damage = 10f, float speed = 2.5f, float attackRange = 4f,
            float attackInterval = 3f)
        {
            return new EnemyDef
            {
                Id = "test_charger",
                Name = "测试冲锋兵",
                Behaviour = EnemyBehaviourType.Charge,
                HP = hp,
                Damage = damage,
                Speed = speed,
                AttackRange = attackRange,
                AttackInterval = attackInterval,
                XpValue = 1,
                BodyRadius = BodyRadius, Scale = 1f
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

        /// <summary>骰子恒为 0 的随机源：配 Dodge = 1 时 0 &lt; 1 必然走闪避分支，骰子本身钉死。</summary>
        sealed class ZeroRoll : IRandom01
        {
            public float Next01() => 0f;
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

            // 伤害 5、间隔 0.5 秒，敌人就站在玩家身上：不会被追击推力带走，纯粹考结算节奏。
            // 现在每次出手前有 0.32 秒起手预警，伤害只在起手结束那一步结算。
            EnemyDef def = ChaseDef(damage: 5f, attackInterval: 0.5f, attackRange: 1.5f);
            system.Spawn(def, Vector2.zero);

            Vector2 playerPos = Vector2.zero;

            RunSteps(system, playerPos, 1);
            Assert.AreEqual(1000f, player.HP, 1e-3f, "起手阶段不该扣血");

            RunSteps(system, playerPos, 19);
            Assert.AreEqual(1000f, player.HP, 1e-3f, "起手没结束前都不该扣血");

            RunSteps(system, playerPos, 1);
            Assert.AreEqual(995f, player.HP, 1e-3f, "起手结束那一步才结算第一次伤害");

            // 再走 10 步（合计 0.18 秒）仍没到（0.5 - 0.32）秒的冷却，不能重复扣血
            RunSteps(system, playerPos, 10);
            Assert.AreEqual(995f, player.HP, 1e-3f, "冷却没到就重复扣血了");

            // 10 秒里按 0.5 秒一次应当是 20 次上下（首步起手也算一次），绝不是每步都扣
            RunSteps(system, playerPos, 600);
            float hits = (1000f - player.HP) / 5f;
            Assert.GreaterOrEqual(hits, 18f, "10 秒内结算次数 " + hits + " 太少");
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

        [Test]
        public void HitKnockbackDecaysBackIntoChase()
        {
            var player = MakePlayer();
            var system = new EnemySystem(player, ArenaHalf);

            // 放在 30 米外，冲量衰减完以后还有足够的距离看它继续追
            EnemyDef def = ChaseDef(speed: 3f, attackRange: 1.5f);
            int slot = system.Spawn(def, new Vector2(30f, 0f));
            EnemyModel enemy = system.Get(slot);

            // 一次 10 点伤害的命中：冲量朝 +x，把敌人往远离玩家的方向推
            float knockback = WeaponDamage.KnockbackSpeed(10f);
            DamageInfo damage = WeaponDamage.Compose(10f, AppRoot.PlayerUnit, Vector2.right, false, knockback);
            WeaponDamage.ApplyToEnemy(enemy, damage, 1f);

            Assert.Greater(enemy.Knockback.x, 0f, "命中方向是 +x，冲量也该朝 +x");

            Vector2 playerPos = Vector2.zero;
            system.Tick(playerPos, Dt);

            Assert.Less(enemy.Velocity.x, 0f, "追击速度不能被冲量覆盖：Velocity 仍然指着玩家");
            Assert.Greater(enemy.Position.x, 30f, "这一步应该被冲量推得离玩家更远");

            // 冲量按固定速率线性衰减，0.2 秒上下归零
            RunSteps(system, playerPos, 60);
            Assert.AreEqual(0f, enemy.Knockback.sqrMagnitude, 1e-6f,
                "冲量该衰减到 0，不能一直留着把人越推越远");

            Vector2 before = enemy.Position;
            system.Tick(playerPos, Dt);
            Assert.Less(enemy.Position.x, before.x, "冲量没了以后该继续朝玩家移动");
        }

        [Test]
        public void DodgeOneAvoidsEveryContactHit()
        {
            var player = MakePlayer(1000f);
            player.Stats.SetBase(StatType.Dodge, 1f);

            var system = new EnemySystem(player, ArenaHalf, new ZeroRoll());
            system.Spawn(ChaseDef(damage: 20f, attackInterval: 0.2f), Vector2.zero);

            // 5 秒里按 0.2 秒的间隔本该挨二十多下
            RunSteps(system, Vector2.zero, 300);

            Assert.AreEqual(1000f, player.HP, 1e-3f, "Dodge = 1 时接触伤害必须每次都被闪掉");
        }

        [Test]
        public void ZeroDodgeStillTakesContactDamageAgainstSameRoll()
        {
            // 同一条零骰子，Dodge = 0 时该照常扣血：证明“开始掷骰”本身没把结算改坏。
            // 近战现在先起手 0.32 秒，第 21 个固定步才真正结算。
            var player = MakePlayer(1000f);
            var system = new EnemySystem(player, ArenaHalf, new ZeroRoll());
            system.Spawn(ChaseDef(damage: 20f, attackInterval: 0.2f), Vector2.zero);

            RunSteps(system, Vector2.zero, 21);

            Assert.AreEqual(980f, player.HP, 1e-3f, "Dodge = 0 时起手结束就该结第一次 20 点近战伤害");
        }

        [Test]
        public void MeleeAttackTelegraphsAndMissesIfPlayerLeaves()
        {
            var player = MakePlayer(1000f);
            var system = new EnemySystem(player, ArenaHalf);
            int slot = system.Spawn(ChaseDef(damage: 20f, attackRange: 1.5f, attackInterval: 2f),
                new Vector2(1f, 0f));

            RunSteps(system, Vector2.zero, 1);

            Assert.IsTrue(system.Get(slot).IsWindingUp, "进入攻击距离后应该先起手，不能立刻掉血");
            Assert.AreEqual(1000f, player.HP, 1e-3f, "起手阶段不该扣血");

            // 玩家侧移离开：敌人锁定的是起手瞬间的方向，这一刀应该落空
            RunSteps(system, new Vector2(10f, 0f), 30);
            Assert.AreEqual(1000f, player.HP, 1e-3f, "玩家离开后近战应该落空");
        }

        [Test]
        public void RangedEnemyFiresAVisibleProjectileThatMustReachThePlayer()
        {
            var player = MakePlayer(1000f);
            var system = new EnemySystem(player, ArenaHalf);
            system.Spawn(RangedDef(damage: 8f, attackInterval: 3f), new Vector2(4f, 0f));

            Vector2 playerPos = Vector2.zero;

            // 起手 0.55 秒：这期间玩家不该掉血
            RunSteps(system, playerPos, 40);
            Assert.AreEqual(1000f, player.HP, 1e-3f, "远程起手期间不该扣血");
            Assert.Greater(system.Projectiles.Count, 0, "起手结束应该发射一发可见弹丸");

            // 弹丸 9 米/秒，4 米距离要飞 0.45 秒左右；落地才扣血
            RunSteps(system, playerPos, 60);
            Assert.AreEqual(992f, player.HP, 1e-3f, "弹丸飞到玩家身上才扣一次血");
        }

        [Test]
        public void ChargeEnemyOnlyDamagesWhenItsBodyReachesThePlayer()
        {
            var player = MakePlayer(1000f);
            var system = new EnemySystem(player, ArenaHalf);
            int slot = system.Spawn(ChargeDef(damage: 10f, attackRange: 4f, attackInterval: 3f),
                new Vector2(4f, 0f));

            Vector2 playerPos = Vector2.zero;

            RunSteps(system, playerPos, 1);
            Assert.IsTrue(system.Get(slot).IsWindingUp, "冲锋前应该先蓄力");
            Assert.AreEqual(1000f, player.HP, 1e-3f, "蓄力阶段不该扣血");

            // 蓄力 0.5 秒 + 冲锋 0.3 秒：撞到玩家身体才结算一次
            RunSteps(system, playerPos, 60);
            Assert.AreEqual(990f, player.HP, 1e-3f, "冲锋撞到玩家后只结算一次");
        }

        static EnemyModel FindAlive(EnemySystem system, int index)
        {
            // 槽位号是稳定的，但测试里按“第几个生成”数，所以扫一遍存活槽位
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

        // ---- 共享索敌（武器 / 旅伴移动 / 旅伴开火共用） ----

        [Test]
        public void FindNearestAliveSkipsDeadAndRemovedSlots()
        {
            var system = new EnemySystem(MakePlayer(), ArenaHalf);
            int far = system.Spawn(ChaseDef(), new Vector2(4f, 0f));
            int near = system.Spawn(ChaseDef(), new Vector2(1f, 0f));
            system.Grid.Rebuild(Vector2.zero, system);

            // 调用方容器里的旧内容必须被清掉：不能把上一轮遗留的槽位号当成这一轮的结果
            var scratch = new List<int> { 999 };
            Assert.AreEqual(near, system.FindNearestAliveSlot(Vector2.zero, 6f, scratch));
            CollectionAssert.DoesNotContain(scratch, 999);

            // 死亡但还没被清理（HP 已是 0）：快照里还在，索敌必须跳过它
            system.Get(near).HP = 0f;
            Assert.AreEqual(far, system.FindNearestAliveSlot(Vector2.zero, 6f, scratch));

            // 已移除：快照里还留着旧槽位，但 Get 已经拿不到模型
            system.Remove(far);
            Assert.AreEqual(-1, system.FindNearestAliveSlot(Vector2.zero, 6f, scratch), "没有活目标时返回 -1");
            Assert.IsNull(system.FindNearestAlive(Vector2.zero, 6f, scratch));
        }

        [Test]
        public void FindNearestAliveUsesCurrentPositionAndRespectsRadius()
        {
            var system = new EnemySystem(MakePlayer(), ArenaHalf);
            int moved = system.Spawn(ChaseDef(), new Vector2(1f, 0f));
            int behind = system.Spawn(ChaseDef(), new Vector2(3f, 0f));
            system.Grid.Rebuild(Vector2.zero, system);

            // 快照里 moved 在 1 米处；模型这一步被推到了 5 米外（还没 Rebuild）。
            // 索敌要按模型当前位置算，选 3 米的那个，而不是按旧快照选 1 米的。
            system.Get(moved).Position = new Vector2(5f, 0f);

            var scratch = new List<int>();
            Assert.AreEqual(behind, system.FindNearestAliveSlot(Vector2.zero, 6f, scratch));

            // 半径边界：快照里 behind 在 3 米处，半径 2.9 时不应该入选
            Assert.AreEqual(-1, system.FindNearestAliveSlot(Vector2.zero, 2.9f, scratch));
            Assert.AreEqual(behind, system.FindNearestAliveSlot(Vector2.zero, 3f, scratch), "恰好在半径上也算命中");
        }
    }
}
