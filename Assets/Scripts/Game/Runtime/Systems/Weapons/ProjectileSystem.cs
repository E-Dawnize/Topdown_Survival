using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 投射物：移动、命中、穿透、回收。整条线只用一个定长池，稳态不分配——
    /// 400 发投射物每帧 new 一个对象，1 秒就是 24000 个垃圾，GC 一响帧率就掉。
    ///
    /// 命中判定用"上一位置 → 当前位置"的线段，不是只判当前点：18 米/秒在 60Hz 下一步走 0.3 米，
    /// 敌人半径 0.3 米，只判当前点会在身体边上擦过去（帧率掉到 30 时整段穿过去）。
    /// 去重靠"每发投射物一个自增戳"：敌人槽位上记着本发的戳，说明这发已经打过它。
    /// 没这一条，同一个身体会在相邻两步里被同一发箭打两次，白吃一层穿透。
    /// </summary>
    public sealed class ProjectileSystem
    {
        /// <summary>投射物自己的身体半径。判定用"敌人半径 + 它"，比只算敌人半径宽容一点，看着像打中了就算打中。</summary>
        public const float BodyRadius = 0.1f;

        /// <summary>
        /// 网格粗筛的半径余量。查询要一次问够，而查的时候还不知道圈里有哪些敌人；
        /// 敌人表里 BodyRadius 最大 0.5 量级，1 米留够余量，精确判定仍按每个敌人自己的半径算。
        /// </summary>
        const float QueryBodyRadius = 1f;

        /// <summary>池里的一格。放在数组里按值遍历，不装箱。</summary>
        struct Slot
        {
            public Vector2 Position;
            public Vector2 PrevPosition;
            public Vector2 Velocity;
            public DamageInfo Damage;
            public int RemainingHits;
            public int Stamp;
            public float Traveled;
            public float MaxDistance;
        }

        /// <summary>对外只读快照。视图层拿它渲染，测试拿它验弹道方向。</summary>
        public readonly struct Snapshot
        {
            /// <summary>池内槽位号。它在一个投射物的生命周期里不变，视图层按它对齐 GameObject。</summary>
            public readonly int Slot;

            public readonly Vector2 Position;

            /// <summary>本步移动前的位置。渲染层用它和 Position 插值，不直接吃阶梯位移。</summary>
            public readonly Vector2 PrevPosition;

            public readonly Vector2 Velocity;
            public readonly bool IsCrit;

            public Snapshot(int slot, Vector2 position, Vector2 prevPosition, Vector2 velocity, bool isCrit)
            {
                Slot = slot;
                Position = position;
                PrevPosition = prevPosition;
                Velocity = velocity;
                IsCrit = isCrit;
            }

            public Vector2 Direction => Velocity.sqrMagnitude > 1e-8f ? Velocity.normalized : Vector2.zero;
        }

        readonly Slot[] _slots;
        readonly List<int> _active = new List<int>(64);
        readonly List<int> _free = new List<int>(64);

        // 每步的查询结果与排序结果复用同一批容器，这条路径每帧会跑"投射物数"次
        readonly List<int> _candidates = new List<int>(32);
        readonly List<int> _ordered = new List<int>(32);
        readonly List<float> _orderedDistance = new List<float>(32);

        readonly IRandom01 _rng;

        /// <summary>命中事件往哪发。可空：武器单测不需要事件总线，传 null 就只扣血不发事件。</summary>
        readonly IEventBus _events;

        // 按敌人槽位号索引的"最后一发打中它的戳"。0 表示没被打过，戳从 1 开始发。
        int[] _hitStamp = new int[64];
        int _nextStamp = 1;

        public ProjectileSystem(IRandom01 rng, int capacity = 256, IEventBus events = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _events = events;
            _slots = new Slot[capacity];

            // 倒序压栈，先租出去的是 0 号，池内顺序与分配顺序一致，看调试信息时好对上
            for (int i = capacity - 1; i >= 0; i--) _free.Add(i);
        }

        /// <summary>池容量。达到容量后 Spawn 返回 false，不扩容——池被打爆要在调试层看见，而不是悄悄吃内存掉帧。</summary>
        public int Capacity => _slots.Length;

        /// <summary>当前存活的投射物数。</summary>
        public int Count => _active.Count;

        /// <summary>
        /// 生成一发。池满返回 false，调用方要处理（本次开火剩下的弹道就没了，日志里能看见池不够）。
        /// direction 必须是单位向量；pierce &lt;= 0 当 1 处理（至少要能命中一个目标，否则这发等于空气）。
        /// </summary>
        public bool Spawn(Vector2 position, Vector2 direction, float speed, float maxDistance, in DamageInfo damage,
            int pierce)
        {
            if (_free.Count == 0) return false;

            int index = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);

            ref Slot slot = ref _slots[index];
            slot.Position = position;
            slot.PrevPosition = position;   // 刚出膛这一帧不插值，否则会从上一个用了这个槽位的弹道飞过来
            slot.Velocity = direction * speed;
            slot.Damage = damage;
            slot.RemainingHits = pierce > 0 ? pierce : 1;
            slot.Stamp = NextStamp();
            slot.Traveled = 0f;
            slot.MaxDistance = maxDistance;

            _active.Add(index);
            return true;
        }

        /// <summary>
        /// 推进所有投射物并结算命中。读的是网格里上一次 Rebuild 的位置，调用方必须先 Rebuild 再调它，
        /// 顺序与主循环一致：先移动、再重建、再判定。
        /// </summary>
        public void Tick(float dt, EnemySystem enemies)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));

            // 倒序遍历：回收时把末尾那发换到当前下标，而末尾一定已经处理过了，不会跳过谁
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                int index = _active[i];
                ref Slot slot = ref _slots[index];
                if (!Step(ref slot, dt, enemies)) Release(index, i);
            }
        }

        /// <summary>按池内顺序取第 ordinal 发存活投射物。视图层同步与测试用。</summary>
        public bool TryGetActive(int ordinal, out Snapshot snapshot)
        {
            if (ordinal < 0 || ordinal >= _active.Count)
            {
                snapshot = default;
                return false;
            }

            Slot slot = _slots[_active[ordinal]];
            snapshot = new Snapshot(_active[ordinal], slot.Position, slot.PrevPosition, slot.Velocity,
                slot.Damage.IsCrit);
            return true;
        }

        /// <summary>返回 false 表示这发该回收了（打穿次数用尽或飞满射程）。</summary>
        bool Step(ref Slot slot, float dt, EnemySystem enemies)
        {
            Vector2 from = slot.Position;
            slot.PrevPosition = from;
            Vector2 step = slot.Velocity * dt;
            Vector2 to = from + step;
            float stepLength = step.magnitude;

            ResolveHits(ref slot, from, to, enemies);

            // 穿透次数用尽就地回收，不再往前走——这一发的存在意义已经结束了
            if (slot.RemainingHits <= 0) return false;

            // 速度为 0 的投射物永远飞不到任何地方，留着只会占槽位
            if (slot.Velocity.sqrMagnitude < 1e-8f) return false;

            slot.Position = to;
            slot.Traveled += stepLength;
            return slot.Traveled < slot.MaxDistance;
        }

        /// <summary>
        /// 结算这一小段上的命中。先粗筛（线段中点附近一圈格子），再按"沿飞行方向走了多远"排序，
        /// 从近到远逐个命中：穿透次数用尽时留下的是更远的目标，符合"箭穿人"的直觉。
        /// 一步之内可能扫到多个目标（敌人挨在一起时），排序也保证了顺序与距离一致。
        /// </summary>
        void ResolveHits(ref Slot slot, Vector2 from, Vector2 to, EnemySystem enemies)
        {
            float stepLength = (to - from).magnitude;
            Vector2 midpoint = (from + to) * 0.5f;
            float queryRadius = stepLength * 0.5f + QueryBodyRadius;

            _candidates.Clear();
            _ordered.Clear();
            _orderedDistance.Clear();
            enemies.Grid.QueryCircle(midpoint, queryRadius, _candidates);
            if (_candidates.Count == 0) return;

            Vector2 travelDirection = stepLength > 1e-6f ? (to - from) / stepLength : slot.Velocity.normalized;
            EnsureStampCapacity(enemies);

            for (int i = 0; i < _candidates.Count; i++)
            {
                int enemySlot = _candidates[i];

                // 快照里可能残留已移除的槽位，用前问一下
                EnemyModel enemy = enemies.Get(enemySlot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;
                if (_hitStamp[enemySlot] == slot.Stamp) continue;   // 本发已经打过这个身体

                float reach = enemy.Radius + BodyRadius;
                if (SegmentDistanceSqr(enemy.Position, from, to, stepLength) > reach * reach) continue;

                float along = Vector2.Dot(enemy.Position - from, travelDirection);
                InsertOrdered(enemySlot, along);
            }

            if (_ordered.Count == 0) return;

            for (int i = 0; i < _ordered.Count; i++)
            {
                if (slot.RemainingHits <= 0) return;

                int enemySlot = _ordered[i];
                EnemyModel enemy = enemies.Get(enemySlot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                _hitStamp[enemySlot] = slot.Stamp;
                DamageResult result = WeaponDamage.ApplyToEnemy(enemy, slot.Damage, _rng.Next01());
                slot.RemainingHits--;

                // 只在真扣到血时发：闪避、无敌帧这种"打到了但没扣"不该让受击表现亮一下。
                // 死亡事件不在这里发：存活标记与清理归外部的清理阶段，发在这里就成了隐式时序。
                if (result.Applied > 0f)
                {
                    _events?.Publish(new UnitDamaged(EnemySystem.UnitIdOf(enemySlot), slot.Damage.Source,
                        result.Applied, slot.Damage.IsCrit, result.RemainingHp));
                }
            }
        }

        /// <summary>把候选按沿飞行方向的距离插进有序表。一步内的候选只有几个，插入排序比排序库更省。</summary>
        void InsertOrdered(int enemySlot, float distance)
        {
            int position = _ordered.Count;
            while (position > 0 && _orderedDistance[position - 1] > distance) position--;

            _ordered.Insert(position, enemySlot);
            _orderedDistance.Insert(position, distance);
        }

        /// <summary>回收：池内位置末尾交换 + 槽位压回空闲栈，O(1)，不挪动其它投射物。</summary>
        void Release(int index, int activePosition)
        {
            int last = _active[_active.Count - 1];
            _active[activePosition] = last;
            _active.RemoveAt(_active.Count - 1);
            _free.Add(index);
        }

        int NextStamp()
        {
            // 戳用完了就整表清零重来。2^31 次开火才会走到这里（几小时的局），
            // 但不清零会让"戳等于 0"的旧命中记录突然对上新发次，白丢一层穿透。
            if (_nextStamp == int.MaxValue)
            {
                Array.Clear(_hitStamp, 0, _hitStamp.Length);
                _nextStamp = 1;
                return _nextStamp++;
            }
            return _nextStamp++;
        }

        void EnsureStampCapacity(EnemySystem enemies)
        {
            int needed = enemies.SlotCapacity;
            if (_hitStamp.Length >= needed) return;

            int size = _hitStamp.Length;
            while (size < needed) size *= 2;

            var grown = new int[size];
            Array.Copy(_hitStamp, grown, _hitStamp.Length);
            _hitStamp = grown;
        }

        /// <summary>点到线段的最短距离平方。线段退化成点时按点算，避免除零。</summary>
        static float SegmentDistanceSqr(Vector2 point, Vector2 from, Vector2 to, float length)
        {
            Vector2 offset = point - from;
            if (length < 1e-6f) return offset.sqrMagnitude;

            Vector2 direction = (to - from) / length;
            float along = Vector2.Dot(offset, direction);
            if (along <= 0f) return offset.sqrMagnitude;
            if (along >= length) return (point - to).sqrMagnitude;

            Vector2 closest = from + direction * along;
            return (point - closest).sqrMagnitude;
        }
    }
}
