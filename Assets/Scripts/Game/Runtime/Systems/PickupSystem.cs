using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>掉落物种类：经验与心。金币已经随商店一起从这一版移除。</summary>
    public enum PickupKind
    {
        Experience,
        Heart
    }

    /// <summary>
    /// 掉落物：经验与心。敌人死在清理阶段时由 RunSimulation 调 DropEnemyLoot 生成，
    /// 之后每一步由 Tick 推进"静止 → 磁吸 → 结算"。
    ///
    /// 经验是这一版唯一的经济：每只怪掉一枚经验，精英/BOSS 的 XpValue 在 EnemyModel 里已经放大。
    /// 拾取到经验只做两件事：写 RunState、发 ExperienceGained；升级抽卡由 RunController 下次 Tick 接管。
    ///
    /// 容量固定 512，稳态零分配：数组 + 一个 count，活跃项永远是 0..count-1，回收用末尾换位。
    /// </summary>
    public sealed class PickupSystem
    {
        public const int Capacity = 512;
        public const float CollectRadius = 0.5f;

        const float MagnetAcceleration = 30f;
        const float MagnetMaxSpeed = 18f;

        struct Slot
        {
            public PickupKind Kind;
            public int Value;
            public float Heal;
            public Vector2 Position;
            public float MagnetSpeed;
            public bool Magnetized;
            public int Serial;
        }

        public readonly struct Snapshot
        {
            /// <summary>
            /// 活跃列表里的稠密下标（0..Count-1），不是稳定身份：回收时会末尾换位，同一个槽位下一帧可能已经是另一件掉落物。
            /// 视图层可以按它复用实例，但跨帧对齐要配合 Kind / Position，不要拿它当“这件掉落物的 ID”。
            /// </summary>
            public readonly int Slot;
            public readonly PickupKind Kind;
            public readonly Vector2 Position;
            public readonly bool Magnetized;
            public readonly int Value;

            public Snapshot(int slot, PickupKind kind, Vector2 position, bool magnetized, int value)
            {
                Slot = slot;
                Kind = kind;
                Position = position;
                Magnetized = magnetized;
                Value = value;
            }
        }

        readonly Slot[] _slots;
        readonly PlayerModel _player;
        readonly RunState _run;
        readonly IEventBus _events;
        readonly IRandom01 _random;
        readonly float _heartDropChance;
        readonly float _heartHeal;

        int _count;
        int _nextSerial = 1;

        public PickupSystem(PlayerModel player, RunState run, IEventBus events, IRandom01 random,
            float heartDropChance = 0.12f, float heartHeal = 8f)
        {
            _player = player ?? throw new System.ArgumentNullException(nameof(player));
            _run = run ?? throw new System.ArgumentNullException(nameof(run));
            _events = events;
            _random = random ?? throw new System.ArgumentNullException(nameof(random));
            _heartDropChance = Mathf.Clamp01(heartDropChance);
            _heartHeal = Mathf.Max(1f, heartHeal);
            _slots = new Slot[Capacity];
        }

        public int SlotCapacity => _slots.Length;
        public int Count => _count;

        public bool TryGetActive(int ordinal, out Snapshot snapshot)
        {
            if (ordinal < 0 || ordinal >= _count)
            {
                snapshot = default;
                return false;
            }

            Slot slot = _slots[ordinal];
            snapshot = new Snapshot(ordinal, slot.Kind, slot.Position, slot.Magnetized, slot.Value);
            return true;
        }

        /// <summary>
        /// 敌人死亡掉落：经验 100%，心按 HeartDropChance。
        /// 只在 RunSimulation 的清理阶段调：命中路径上生成掉落会让"谁还能看见这个敌人"变成隐式时序问题。
        /// </summary>
        public void DropEnemyLoot(Vector2 position, int experience)
        {
            if (experience > 0) Spawn(PickupKind.Experience, experience, 0f, position);

            if (_random.Next01() < _heartDropChance)
            {
                Spawn(PickupKind.Heart, 0, _heartHeal, position);
            }
        }

        /// <summary>
        /// 推进所有掉落物：远处的原地待着，进了吸附半径就加速飞向玩家，贴身到拾取半径就结算。
        /// 纯加法与比较，稳态 0 B（事件是结构体，入队走预先分配的队列）。
        /// </summary>
        public void Tick(float dt)
        {
            Vector2 playerPos = _player.Position;
            float magnetRadius = _player.Stats[StatType.PickupRadius];

            for (int i = _count - 1; i >= 0; i--)
            {
                ref Slot slot = ref _slots[i];
                Vector2 toPlayer = playerPos - slot.Position;
                float distance = toPlayer.magnitude;

                if (distance <= CollectRadius)
                {
                    Collect(in slot);
                    RemoveAt(i);
                    continue;
                }

                if (!slot.Magnetized)
                {
                    if (distance > magnetRadius) continue;
                    slot.Magnetized = true;
                }

                slot.MagnetSpeed = Mathf.Min(slot.MagnetSpeed + MagnetAcceleration * dt, MagnetMaxSpeed);
                float stepLength = Mathf.Min(slot.MagnetSpeed * dt, distance);
                slot.Position += toPlayer * (stepLength / distance);
            }
        }

        void Spawn(PickupKind kind, int value, float heal, Vector2 position)
        {
            int index;
            if (_count < _slots.Length)
            {
                index = _count;
                _count++;
            }
            else
            {
                index = OldestIndex();
            }

            ref Slot slot = ref _slots[index];
            slot.Kind = kind;
            slot.Value = value;
            slot.Heal = heal;
            slot.Position = position;
            slot.MagnetSpeed = 0f;
            slot.Magnetized = false;
            slot.Serial = _nextSerial++;
        }

        void Collect(in Slot slot)
        {
            if (slot.Kind == PickupKind.Experience)
            {
                _run.AddExperience(slot.Value);
                _events?.Publish(new ExperienceGained(slot.Value, _run.TotalXp, _run.Level, _run.Xp,
                    _run.XpToNext, _run.PendingLevelUps));
                return;
            }

            float maxHp = _player.MaxHP;
            if (_player.HP >= maxHp) return;
            _player.HP = Mathf.Min(_player.HP + slot.Heal, maxHp);
        }

        void RemoveAt(int index)
        {
            _count--;
            if (index != _count) _slots[index] = _slots[_count];
        }

        int OldestIndex()
        {
            int oldest = 0;
            int oldestSerial = _slots[0].Serial;
            for (int i = 1; i < _count; i++)
            {
                if (_slots[i].Serial >= oldestSerial) continue;
                oldest = i;
                oldestSerial = _slots[i].Serial;
            }

            return oldest;
        }
    }
}
