using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class PlayerMovementTests
    {
        const float Speed = 5f;
        const float DashMultiplier = 3f;
        const float DashDuration = 0.15f;
        const float DashCooldown = 2f;
        const float Half = 67f;
        const float Dt = 1f / 60f;

        static PlayerController Make(out PlayerModel model, out EventBus bus)
        {
            model = new PlayerModel(100f, Speed, Speed * DashMultiplier, DashDuration, DashCooldown, Half);
            bus = new EventBus();
            return new PlayerController(model, bus);
        }

        static void Step(PlayerController controller, ref InputSnapshot input, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                controller.Tick(Dt, ref input);
            }
        }

        [Test]
        public void MovesAlongInputAtMoveSpeed()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var input = new InputSnapshot { Move = new Vector2(1f, 0f) };
            Step(c, ref input, 60);

            Assert.AreEqual(Speed, m.Position.x, 0.05f);
            Assert.AreEqual(0f, m.Position.y, 0.001f);
            Assert.AreEqual(0f, m.Facing, 0.001f);
        }

        [Test]
        public void DiagonalIsNormalized()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var input = new InputSnapshot { Move = new Vector2(1f, 1f) };
            c.Tick(Dt, ref input);

            Assert.AreEqual(Speed, m.Velocity.magnitude, 0.001f);
        }

        [Test]
        public void ClampedToArena()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var input = new InputSnapshot { Move = new Vector2(1f, 0f) };
            Step(c, ref input, 60 * 60);

            Assert.AreEqual(Half, m.Position.x, 0.001f);
        }

        [Test]
        public void DashFiresEventAndEntersDashState()
        {
            PlayerController c = Make(out PlayerModel m, out EventBus bus);
            int dashed = 0;
            bus.Subscribe<PlayerDashed>(_ => dashed++);

            var input = new InputSnapshot { Move = new Vector2(1f, 0f), DashPressed = true };
            c.Tick(Dt, ref input);
            bus.Flush();

            Assert.AreEqual(1, dashed);
            Assert.IsTrue(m.IsDashing);
            Assert.AreEqual(Speed * DashMultiplier, m.Velocity.magnitude, 0.001f);
        }

        [Test]
        public void DashPressedIsConsumedOncePerFrame()
        {
            PlayerController c = Make(out PlayerModel m, out EventBus bus);
            int dashed = 0;
            bus.Subscribe<PlayerDashed>(_ => dashed++);

            var input = new InputSnapshot { Move = new Vector2(1f, 0f), DashPressed = true };
            Step(c, ref input, 3);   // 一帧跑 3 步
            bus.Flush();

            Assert.AreEqual(1, dashed, "同一帧的多步只该触发一次冲刺");
        }

        [Test]
        public void CannotDashDuringCooldown()
        {
            PlayerController c = Make(out PlayerModel m, out EventBus bus);
            int dashed = 0;
            bus.Subscribe<PlayerDashed>(_ => dashed++);

            var input = new InputSnapshot { Move = new Vector2(1f, 0f), DashPressed = true };
            c.Tick(Dt, ref input);

            input.DashPressed = true;
            Step(c, ref input, 20);
            bus.Flush();

            Assert.AreEqual(1, dashed, "冷却期间不该再冲");
        }

        [Test]
        public void DashEndsAndReturnsToWalkSpeed()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var input = new InputSnapshot { Move = new Vector2(1f, 0f), DashPressed = true };
            c.Tick(Dt, ref input);
            Assert.IsTrue(m.IsDashing);

            Step(c, ref input, 12);   // 0.2s，超过 0.15s 的冲刺时长
            Assert.IsFalse(m.IsDashing);
            Assert.AreEqual(Speed, m.Velocity.magnitude, 0.001f);
        }

        [Test]
        public void DashWithoutInputUsesFacing()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var walk = new InputSnapshot { Move = new Vector2(0f, 1f) };
            Step(c, ref walk, 10);

            var dash = new InputSnapshot { Move = Vector2.zero, DashPressed = true };
            c.Tick(Dt, ref dash);

            Assert.AreEqual(0f, m.DashDirection.x, 0.001f);
            Assert.AreEqual(1f, m.DashDirection.y, 0.001f);
        }

        [Test]
        public void FacingFollowsVelocity()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var input = new InputSnapshot { Move = new Vector2(0f, 1f) };
            c.Tick(Dt, ref input);

            Assert.AreEqual(Mathf.PI / 2f, m.Facing, 0.001f);
        }

        [Test]
        public void DashCoversExpectedDistance()
        {
            PlayerController c = Make(out PlayerModel m, out _);
            var input = new InputSnapshot { Move = new Vector2(1f, 0f), DashPressed = true };
            Step(c, ref input, 12);

            // 冲刺 0.15s × 15m/s = 2.25m，之后剩 0.05s 按 5m/s 走 0.25m
            Assert.Greater(m.Position.x, 2.2f);
            Assert.Less(m.Position.x, 2.9f);
        }
    }
}
