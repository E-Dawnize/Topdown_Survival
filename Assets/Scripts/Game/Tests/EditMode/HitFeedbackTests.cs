using Game.Core;
using Game.Systems.Weapons;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 命中反馈：定格时钟与"值得强调"的门槛。RunClock 从 RunLoop 里拆出来就是为了能在命令行跑这几条——
    /// 定格期间模拟一步都不推进，恢复以后也不许把暂停掉的时间补跑回来。
    /// 击退的衰减与闪避骰子在 EnemySystemTests，死亡事件在 RunSimulationTests。
    /// </summary>
    public class HitFeedbackTests
    {
        [Test]
        public void OnlyCritOrBigDamageIsEmphasized()
        {
            Assert.IsTrue(HitFeedback.IsEmphasized(1f, true), "暴击一律强调，哪怕伤害很小");

            Assert.IsTrue(HitFeedback.IsEmphasized(HitFeedback.BigHitThreshold, false),
                "到阈值的普通命中算大伤害");
            Assert.IsFalse(HitFeedback.IsEmphasized(HitFeedback.BigHitThreshold - 1f, false),
                "不到阈值的普通命中不该卡一下：连射时会一直抽");
        }

        [Test]
        public void HitStopPausesSteppingAndDoesNotCatchUp()
        {
            var clock = new RunClock();
            clock.RequestHitStop(HitFeedback.HitStopSeconds);

            const int frames = 120;   // 2 秒，一帧正好一个固定步长
            int steps = 0;
            int stoppedFrames = 0;

            for (int i = 0; i < frames; i++)
            {
                if (clock.IsHitStopped) stoppedFrames++;

                int advanced = clock.Advance(RunClock.FixedStep);
                steps += advanced;

                Assert.LessOrEqual(advanced, 1,
                    "一帧的时间只够一步：定格期间与恢复之后都不该一次跑好几步");
            }

            Assert.Greater(stoppedFrames, 0, "0.05 秒的定格应该真的挡掉了几帧");
            Assert.AreEqual(frames - stoppedFrames, steps,
                "总步数 = 没被定格的帧数：定格期间一步不跑，恢复后也没有把暂停的时间补回来");
        }

        [Test]
        public void AlphaStaysOnLastStepWhileHitStopped()
        {
            var clock = new RunClock();
            clock.Advance(RunClock.FixedStep);          // 先正常走一步，和“一开局就定格”区分开
            float before = clock.Alpha;

            clock.RequestHitStop(HitFeedback.HitStopSeconds);
            clock.Advance(RunClock.FixedStep);

            Assert.AreEqual(1f, clock.Alpha, 1e-6f,
                "定格时画面要停在最后一步的模拟结果上（alpha 1），按余量算会退回上一步");
            Assert.Less(before, 1f);
        }

        [Test]
        public void KnockbackSpeedScalesWithDamageAndIsCapped()
        {
            Assert.AreEqual(0f, WeaponDamage.KnockbackSpeed(0f), 1e-6f, "没伤害就没击退");
            Assert.AreEqual(2f, WeaponDamage.KnockbackSpeed(4f), 1e-4f, "每点伤害 0.5 米/秒");

            Assert.Greater(WeaponDamage.KnockbackSpeed(20f), WeaponDamage.KnockbackSpeed(4f),
                "大伤害该推得更远");

            Assert.AreEqual(WeaponDamage.MaxKnockbackSpeed, WeaponDamage.KnockbackSpeed(1000f), 1e-4f,
                "单次命中的冲量要封顶，否则一箭能把人推出半个场地");
        }
    }
}
