using Game.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 相机焦点是纯数据：屏幕矩形死区 + 两轴阻尼。
    /// 这里不碰 Cinemachine，只验"什么时候该动、往哪根轴动、动多快"。
    /// </summary>
    public class CameraFocusTests
    {
        const float Dt = 1f / 60f;
        const float HalfWidth = 10f;
        const float HalfDepth = 8f;
        const float Dead = 0.14f;
        const float Smooth = 0.32f;

        // 0.14 × 10 = 1.4，0.14 × 8 = 1.12
        const float DeadX = Dead * HalfWidth;
        const float DeadZ = Dead * HalfDepth;

        static CameraFocus NewFocus(float smooth = Smooth)
        {
            return new CameraFocus(Dead, smooth);
        }

        [Test]
        public void TargetInsideRectangleDoesNotMoveFocus()
        {
            CameraFocus focus = NewFocus();
            focus.Reset(Vector2.zero);

            for (int i = 0; i < 120; i++)
            {
                focus.Tick(new Vector2(DeadX - 0.05f, DeadZ - 0.05f), Dt, HalfWidth, HalfDepth);
            }

            Assert.AreEqual(0f, focus.Focus.x, 1e-5f);
            Assert.AreEqual(0f, focus.Focus.y, 1e-5f);
        }

        [Test]
        public void VerticalExitOnlyMovesVerticalAxis()
        {
            CameraFocus focus = NewFocus();
            focus.Reset(Vector2.zero);

            // x 还在死区内，y 已经出了上边：只有纵向应当动
            var target = new Vector2(DeadX - 0.2f, 3f);
            for (int i = 0; i < 120; i++) focus.Tick(target, Dt, HalfWidth, HalfDepth);

            Assert.AreEqual(0f, focus.Focus.x, 1e-4f, "横向还在框里，焦点 x 不该动");
            Assert.AreEqual(3f - DeadZ, focus.Focus.y, 0.02f, "纵向应当停在死区上边");
        }

        [Test]
        public void HorizontalExitOnlyMovesHorizontalAxis()
        {
            CameraFocus focus = NewFocus();
            focus.Reset(Vector2.zero);

            var target = new Vector2(3f, DeadZ - 0.2f);
            for (int i = 0; i < 120; i++) focus.Tick(target, Dt, HalfWidth, HalfDepth);

            Assert.AreEqual(3f - DeadX, focus.Focus.x, 0.02f, "横向应当停在死区右边");
            Assert.AreEqual(0f, focus.Focus.y, 1e-4f, "纵向还在框里，焦点 y 不该动");
        }

        [Test]
        public void DiagonalExitMovesBothAxes()
        {
            CameraFocus focus = NewFocus();
            focus.Reset(Vector2.zero);

            var target = new Vector2(3f, 3f);
            for (int i = 0; i < 180; i++) focus.Tick(target, Dt, HalfWidth, HalfDepth);

            Assert.AreEqual(3f - DeadX, focus.Focus.x, 0.02f);
            Assert.AreEqual(3f - DeadZ, focus.Focus.y, 0.02f);
        }

        [Test]
        public void DampingStartsSlowThenCatchesUp()
        {
            CameraFocus focus = NewFocus();
            focus.Reset(Vector2.zero);

            var target = new Vector2(3f, 0f);
            focus.Tick(target, Dt, HalfWidth, HalfDepth);
            float firstStep = focus.Focus.x;

            Assert.Greater(firstStep, 0f, "出框后应当朝目标动");
            Assert.Less(firstStep, 3f - DeadX, "第一帧不能直接贴到死区边，否则没有阻尼");

            for (int i = 0; i < 180; i++) focus.Tick(target, Dt, HalfWidth, HalfDepth);
            Assert.AreEqual(3f - DeadX, focus.Focus.x, 0.01f, "阻尼最终应当收敛到死区边");
        }

        [Test]
        public void ZeroDampingSnapsToDeadZoneEdge()
        {
            CameraFocus focus = NewFocus(0f);
            focus.Reset(Vector2.zero);

            focus.Tick(new Vector2(3f, 0f), Dt, HalfWidth, HalfDepth);

            Assert.AreEqual(3f - DeadX, focus.Focus.x, 1e-3f);
        }

        [Test]
        public void ReenteringDeadZoneStopsWithoutDrift()
        {
            CameraFocus focus = NewFocus();
            focus.Reset(Vector2.zero);

            var target = new Vector2(3f, 0f);
            for (int i = 0; i < 10; i++) focus.Tick(target, Dt, HalfWidth, HalfDepth);
            Assert.Greater(focus.Focus.x, 0f);

            Vector2 before = focus.Focus;

            // 目标回到框内：焦点保持不动，而且把残余速度清掉
            for (int i = 0; i < 30; i++) focus.Tick(Vector2.zero, Dt, HalfWidth, HalfDepth);

            Assert.AreEqual(before.x, focus.Focus.x, 1e-5f);
            Assert.AreEqual(before.y, focus.Focus.y, 1e-5f);
        }
    }
}
