using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 敌人远程攻击的弹丸。投手起手结束后从这里发射一发，弹丸真的飞到玩家身上才扣血；
    /// 以前投手在 5 米外站着就能掉玩家的血，既没有弹道也没有攻击动作，读起来就是“全屏伤害”。
    ///
    /// 只做三件事：直线移动、线段扫掠命中玩家、命中后走 PlayerDamage。
    /// 与玩家的 ProjectileSystem 分开：目标阵营、命中规则、池子容量都不同，
    /// 硬塞进同一个池只会让“这一发是谁的”变成每个 tick 都要判断的分支。
    /// 稳态不分配：槽位数组 + 活跃列表，与 ProjectileSystem 同一套做法。
    /// </summary>
    public sealed class EnemyProjectileSystem
    {
        /// <summary>弹丸自己的体积。与玩家半径相加后作为命中距离，看起来擦到就算打中。</summary>
        public const float BodyRadius = 0.12f;

        /// <summary>弹丸池容量。投手同时在场最多几十只，128 足够，池满时新弹丸直接丢弃并能在调试层看见。</summary>
        public const int DefaultCapacity = 128;

        struct Slot
        {
            public Vector2 Position;
            public Vector2 PrevPosition;
            public Vector2 Velocity;
            public float Damage;
            public float RemainingDistance;
            public float TimeToLive;
            public UnitId Source;
        }

        /// <summary>表现层用的只读快照。Slot 号在一次弹丸生命周期里稳定。</summary>
        public readonly struct Snapshot
        {
            public readonly int Slot;
            public readonly Vector2 Position;
            public readonly Vector2 PrevPosition;
            public readonly Vector2 Velocity;

            public Snapshot(int slot, Vector2 position, Vector2 prevPosition, Vector2 velocity)
            {
                Slot = slot;
                Position = position;
                PrevPosition = prevPosition;
                Velocity = velocity;
            }

            public Vector2 Direction => Velocity.sqrMagnitude > 1e-8f ? Velocity.normalized : Vector2.zero;
        }

        readonly Slot[] _slots;
        readonly List<int> _active = new List<int>(64);
        readonly List<int> _free = new List<int>(64);
        readonly PlayerModel _player;
        readonly IRandom01 _dodgeRng;
        readonly IEventBus _events;

        public EnemyProjectileSystem(PlayerModel player, IRandom01 dodgeRng = null, IEventBus events = null,
            int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _player = player ?? throw new ArgumentNullException(nameof(player));
            _dodgeRng = dodgeRng;
            _events = events;
            _slots = new Slot[capacity];

            for (int i = capacity - 1; i >= 0; i--) _free.Add(i);
        }

        public int Capacity => _slots.Length;

        public int Count => _active.Count;

        public void Clear()
        {
            _active.Clear();
            _free.Clear();
            for (int i = _slots.Length - 1; i >= 0; i--) _free.Add(i);
        }

        /// <summary>
        /// 发射一发。池满返回 false；调用方不需要为了一次丢弹丸改变冷却，
        /// 这一发就是没有出膛，下一次攻击照常按表里的间隔来。
        /// </summary>
        public bool Spawn(Vector2 position, Vector2 direction, float speed, float maxDistance, float damage,
            UnitId source)
        {
            if (_free.Count == 0 || speed <= 0f || maxDistance <= 0f || damage <= 0f) return false;
            if (direction.sqrMagnitude < 1e-8f) return false;

            int index = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);

            Vector2 velocity = direction.normalized * speed;
            ref Slot slot = ref _slots[index];
            slot.Position = position;
            slot.PrevPosition = position;
            slot.Velocity = velocity;
            slot.Damage = damage;
            slot.RemainingDistance = maxDistance;
            slot.TimeToLive = maxDistance / speed;
            slot.Source = source;

            _active.Add(index);
            return true;
        }

        /// <summary>
        /// 推进所有弹丸并结算对玩家的命中。调用方必须已经更新过玩家本步的位置。
        /// 命中用“上一步 → 这一步”的线段，避免高速弹丸在一帧内从玩家身体里穿过去。
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f || _player.IsDead) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                int index = _active[i];
                ref Slot slot = ref _slots[index];

                slot.PrevPosition = slot.Position;
                Vector2 step = slot.Velocity * dt;
                Vector2 to = slot.Position + step;
                float length = step.magnitude;

                if (IntersectsPlayer(slot.Position, to))
                {
                    Vector2 direction = slot.Velocity.sqrMagnitude > 1e-8f
                        ? slot.Velocity.normalized
                        : Vector2.zero;
                    PlayerDamage.Strike(_player, slot.Damage, slot.Source, direction, _dodgeRng, _events);
                    Release(index, i);
                    continue;
                }

                slot.Position = to;
                slot.RemainingDistance -= length;
                slot.TimeToLive -= dt;

                if (slot.RemainingDistance <= 0f || slot.TimeToLive <= 0f) Release(index, i);
            }
        }

        public bool TryGetActive(int ordinal, out Snapshot snapshot)
        {
            if (ordinal < 0 || ordinal >= _active.Count)
            {
                snapshot = default;
                return false;
            }

            int index = _active[ordinal];
            Slot slot = _slots[index];
            snapshot = new Snapshot(index, slot.Position, slot.PrevPosition, slot.Velocity);
            return true;
        }

        bool IntersectsPlayer(Vector2 from, Vector2 to)
        {
            Vector2 player = _player.Position;
            float radius = PlayerModel.BodyRadius + BodyRadius;
            float radiusSqr = radius * radius;

            Vector2 segment = to - from;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr < 1e-8f) return (player - from).sqrMagnitude <= radiusSqr;

            float t = Vector2.Dot(player - from, segment) / lengthSqr;
            t = Mathf.Clamp01(t);
            Vector2 closest = from + segment * t;
            return (player - closest).sqrMagnitude <= radiusSqr;
        }

        void Release(int index, int activeIndex)
        {
            _active.RemoveAt(activeIndex);
            _free.Add(index);
        }
    }
}
