using Game.Core;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Controllers
{
    /// <summary>
    /// 玩家移动与冲刺。只吃 InputSnapshot，不认识 InputSystem，也不碰场景。
    /// 固定步长下每步调一次，一帧可能调多次。
    /// </summary>
    public sealed class PlayerController
    {
        readonly PlayerModel _model;
        readonly IEventBus _events;

        public PlayerModel Model => _model;

        public PlayerController(PlayerModel model, IEventBus events)
        {
            _model = model;
            _events = events;
        }

        public void Tick(float dt, ref InputSnapshot input)
        {
            if (_model.DashCooldownTimer > 0f)
            {
                _model.DashCooldownTimer = Mathf.Max(0f, _model.DashCooldownTimer - dt);
            }

            Vector2 move = input.Move;
            if (move.sqrMagnitude > 1f)
            {
                move = move.normalized;   // 八向，对角线不加速
            }

            if (_model.DashTimer > 0f)
            {
                _model.DashTimer -= dt;
                _model.Velocity = _model.DashDirection * _model.DashSpeed;
                if (_model.DashTimer <= 0f)
                {
                    _model.DashTimer = 0f;
                }
            }
            else if (input.DashPressed && _model.CanDash)
            {
                StartDash(move);
                input.DashPressed = false;
            }
            else
            {
                _model.Velocity = move * _model.MoveSpeed;
            }

            _model.Position += _model.Velocity * dt;
            ClampToArena();

            if (_model.Velocity.sqrMagnitude > 0.0001f)
            {
                _model.Facing = Mathf.Atan2(_model.Velocity.y, _model.Velocity.x);
            }

            _model.State = _model.DashTimer > 0f
                ? PlayerState.Dashing
                : _model.Velocity.sqrMagnitude > 0.0001f
                    ? PlayerState.Moving
                    : PlayerState.Idle;
        }

        void StartDash(Vector2 move)
        {
            Vector2 dir = move.sqrMagnitude > 0.0001f
                ? move.normalized
                : new Vector2(Mathf.Cos(_model.Facing), Mathf.Sin(_model.Facing));

            _model.DashDirection = dir;
            _model.DashTimer = _model.DashDuration;
            _model.DashCooldownTimer = _model.DashCooldown;
            _model.Velocity = dir * _model.DashSpeed;   // 立即生效，否则起步这一步白给
            _model.Facing = Mathf.Atan2(dir.y, dir.x);

            _events?.Publish(new PlayerDashed(_model.Position.x, _model.Position.y, _model.Facing));
        }

        void ClampToArena()
        {
            float half = _model.ArenaHalfExtent;
            _model.Position.x = Mathf.Clamp(_model.Position.x, -half, half);
            _model.Position.y = Mathf.Clamp(_model.Position.y, -half, half);
        }
    }
}
