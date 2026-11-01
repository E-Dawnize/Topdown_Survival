using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 刷怪环分布。出生点不能叠在一起，否则分离力要跑几百步才推得开，
    /// 压力测试量到的不是稳态。200 个点是 I1-11 的验收量级。
    /// </summary>
    public class SpawnRingTests
    {
        const int EnemyCount = 200;
        const float MinSpawnDistance = 0.8f;

        static Vector2[] Ring(int count, float phase)
        {
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = SpawnRing.Offset(i, count, phase);
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
                Assert.GreaterOrEqual(radius, SpawnRing.RadiusMin - 1e-3f, "第 " + i + " 个敌人刷进了内圈");
                Assert.LessOrEqual(radius, SpawnRing.RadiusMax + 1e-3f, "第 " + i + " 个敌人刷出了外圈");
            }
        }

        [Test]
        public void SpawnRingIsDeterministicAndPhaseOnlyRotatesIt()
        {
            Vector2[] first = Ring(EnemyCount, 0.4f);
            Vector2[] again = Ring(EnemyCount, 0.4f);
            CollectionAssert.AreEqual(first, again, "同样的相位与下标必须给出同样的出生点");

            const float phaseShift = 0.9f;
            Vector2[] shifted = Ring(EnemyCount, 0.4f + phaseShift);
            float cos = Mathf.Cos(phaseShift);
            float sin = Mathf.Sin(phaseShift);
            for (int i = 0; i < first.Length; i++)
            {
                var rotated = new Vector2(first[i].x * cos - first[i].y * sin, first[i].x * sin + first[i].y * cos);
                Assert.AreEqual(rotated.x, shifted[i].x, 1e-3f, "第 " + i + " 个敌人的 x 对不上旋转结果");
                Assert.AreEqual(rotated.y, shifted[i].y, 1e-3f, "第 " + i + " 个敌人的 y 对不上旋转结果");
            }
        }

        [Test]
        public void EmptyRingRequestDoesNotThrow()
        {
            Assert.AreEqual(Vector2.zero, SpawnRing.Offset(0, 0, 0f, 8f, 15f));
        }
    }
}
