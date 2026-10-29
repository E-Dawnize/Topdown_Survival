using UnityEngine;

namespace Game.Model
{
    public enum PlayerState
    {
        Idle,
        Moving,
        Dashing
    }

    /// <summary>
    /// 玩家的纯数据。只认 Vector2 的 (x, z)，不碰 Transform。
    /// 属性全部在构造时确定，I1-3 接入 StatBlock 后改为可修正。
    /// </summary>
    public sealed class PlayerModel
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 DashDirection;
        public float Facing;

        public PlayerState State;

        public readonly float MaxHP;
        public float HP;

        public readonly float MoveSpeed;
        public readonly float DashSpeed;
        public readonly float DashDuration;
        public readonly float DashCooldown;
        public readonly float ArenaHalfExtent;

        public float DashTimer;
        public float DashCooldownTimer;

        public PlayerModel(float maxHp, float moveSpeed, float dashSpeed, float dashDuration, float dashCooldown,
            float arenaHalfExtent)
        {
            MaxHP = maxHp;
            HP = maxHp;
            MoveSpeed = moveSpeed;
            DashSpeed = dashSpeed;
            DashDuration = dashDuration;
            DashCooldown = dashCooldown;
            ArenaHalfExtent = arenaHalfExtent;
        }

        public bool IsDead => HP <= 0f;
        public float HpRatio => MaxHP > 0f ? Mathf.Clamp01(HP / MaxHP) : 0f;
        public bool IsDashing => DashTimer > 0f;
        public bool IsInvulnerable => DashTimer > 0f;
        public bool CanDash => DashCooldownTimer <= 0f && DashTimer <= 0f;
    }
}
