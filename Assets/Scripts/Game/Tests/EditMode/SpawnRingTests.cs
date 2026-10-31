using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// Test_Arena 临时刷怪环的分布（I1-11 的起点条件）。
    ///
    /// 要验的就一件事：200 个敌人出生时不能叠在一起。出生就重叠的话，分离力要跑几百步才推得开，
    /// 压测开局看到的那一团"不是压力测试要测的稳态"，量出来的帧时间也没意义。
    /// </summary>
    public class SpawnRingTests
    {
        const int EnemyCount = AppRoot.TestArenaEnemyCount;   // 200

        /// <summary>
        /// 两个敌人不重叠的最短距离：敌人表里最大的身体半径是 0.4（Charger），两个加起来 0.8。
        /// 判定用 1.0 米，比身体直径更宽一点——出生时就互相推挤说明分布不够开。
        /// </summary>
        const float MinSpawnDistance = 1f;

        static Vector2[] Ring(int count, float phase)
        {
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = AppRoot.TestSpawnRingOffset(i, count, phase, AppRoot.SpawnRadiusMin,
                    AppRoot.SpawnRadiusMax);
            }

            return points;
        }

        [Test]
        public void TwoHundredSpawnsAreFarEnoughApartToNotStartOverlapping()
        {
            Vector2[] points = Ring(EnemyCount, 0.37f);

            float worst = float.MaxValue;
            int worstA = -1;
            int worstB = -1;
            for (int a = 0; a < points.Length; a++)
            {
                for (int b = a + 1; b < points.Length; b++)
                {
                    float distance = Vector2.Distance(points[a], points[b]);
                    if (distance >= worst) continue;
                    worst = distance;
                    worstA = a;
                    worstB = b;
                }
            }

            Assert.GreaterOrEqual(worst, MinSpawnDistance,
                "第 " + worstA + " 与第 " + worstB + " 个敌人只隔 " + worst.ToString("F3") +
                " 米，200 个敌人出生就挤在一起了");
        }

        [Test]
        public void EverySpawnLandsInsideTheAnnulus()
        {
            Vector2[] points = Ring(EnemyCount, 1.7f);

            for (int i = 0; i < points.Length; i++)
            {
                float radius = points[i].magnitude;
                Assert.GreaterOrEqual(radius, AppRoot.SpawnRadiusMin - 1e-3f, "第 " + i + " 个敌人刷进了内圈");
                Assert.LessOrEqual(radius, AppRoot.SpawnRadiusMax + 1e-3f, "第 " + i + " 个敌人刷出了外圈");
            }
        }

        [Test]
        public void SpawnRingIsDeterministicAndPhaseOnlyRotatesIt()
        {
            Vector2[] first = Ring(EnemyCount, 0.4f);
            Vector2[] again = Ring(EnemyCount, 0.4f);
            CollectionAssert.AreEqual(first, again, "同样的种子与下标必须给出同样的出生点");

            // phase 只整圈转一下，不改变点与点的相对位置：换种子时刷怪圈的样子变了，均匀度不能跟着变
            const float phaseShift = 0.9f;
            Vector2[] shifted = Ring(EnemyCount, 0.4f + phaseShift);
            float cos = Mathf.Cos(phaseShift);
            float sin = Mathf.Sin(phaseShift);
            for (int i = 0; i < first.Length; i++)
            {
                // 转 phaseShift 后的点 = 原点绕 Y 轴转相位的那个角（二维平面上的旋转）
                var rotated = new Vector2(first[i].x * cos - first[i].y * sin, first[i].x * sin + first[i].y * cos);
                Assert.AreEqual(rotated.x, shifted[i].x, 1e-3f, "第 " + i + " 个敌人的 x 对不上旋转结果");
                Assert.AreEqual(rotated.y, shifted[i].y, 1e-3f, "第 " + i + " 个敌人的 y 对不上旋转结果");
            }
        }

        [Test]
        public void EmptyRingRequestDoesNotThrow()
        {
            // 数量为 0 时调用方仍然会走一遍循环（count 来自配置/调试面板），不能在这里除零
            Assert.AreEqual(Vector2.zero, AppRoot.TestSpawnRingOffset(0, 0, 0f, 8f, 15f));
        }
    }
}
