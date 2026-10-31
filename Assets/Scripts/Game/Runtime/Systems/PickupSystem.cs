using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>掉落物种类。金币与心就这两种，不做通用的"物品"抽象。</summary>
    public enum PickupKind
    {
        Coin,
        Heart
    }

    /// <summary>
    /// 掉落物：金币与心。敌人死在清理阶段时由 RunSimulation 调 DropEnemyLoot 生成，
    /// 之后每一步由 Tick 推进"静止 → 磁吸 → 结算"。
    ///
    /// 两个半径分工明确：
    ///   - 吸附半径读玩家的 StatBlock[PickupRadius]（GDD 5.1 默认 2.5）。读属性而不是写常量，
    ///     拾取范围道具与 buff 以后加在这个属性上就能直接生效。
    ///   - 拾取半径 0.5 是纯粹的手感数字（"贴到身上才算吃到"），不进属性表。
    ///
    /// 容量固定 512，稳态零分配：数组 + 一个 count，活跃项永远是 0..count-1，
    /// 回收用末尾换位。场上从不多于 512 个掉落物，因为满的时候顶掉最旧的（见 Spawn）。
    /// </summary>
    public sealed class PickupSystem
    {
        /// <summary>
        /// 掉落物上限。单个波次的击杀量在这个量级以内（同屏上限 100，一波 180 秒），
        /// 512 够玩家捡不过来时也不至于无限涨；满了的处理见 Spawn。
        /// </summary>
        public const int Capacity = 512;

        /// <summary>拾取半径。不读属性：它是"贴到身上"的手感阈值，不是玩家强度。</summary>
        public const float CollectRadius = 0.5f;

        /// <summary>
        /// 一只怪掉 1 枚金币，价值 1。
        /// Enemy.json 里还有一个 CoinDrop 列（普通怪 1/2/2，GDD 9.1 还给精英 12、BOSS 50），这里先不用它：
        /// I2-3 的验收口径是"每只怪掉 1 枚价值 1 的金币"，而精英与 BOSS 要等 I3 才有，
        /// 现在就把普通怪的 2 枚接上去，等于在没有对应敌人的情况下先改了一次经济。接 CoinDrop 跟数值调优一起做。
        /// </summary>
        public const int CoinValuePerKill = 1;

        /// <summary>心掉落概率，GDD 9.1：金币 100%，心 25%。</summary>
        public const float HeartDropChance = 0.25f;

        /// <summary>一颗心回 5 点血，GDD 9.1。</summary>
        public const float HeartHeal = 5f;

        /// <summary>
        /// 磁吸加速度（米/秒²）。从吸附边缘 2.5 米起吸引时约 0.4 秒到手、末速 12 米/秒，
        /// 看得出是"被吸过去"而不是瞬移。
        /// </summary>
        const float MagnetAcceleration = 30f;

        /// <summary>磁吸速度上限。不封顶的话长距离吸附会快过玩家，看着像掉落物在追人。</summary>
        const float MagnetMaxSpeed = 18f;

        /// <summary>池里的一格。放在数组里按值保存，每步只读改写字段，不装箱。</summary>
        struct Slot
        {
            public PickupKind Kind;
            public int Value;
            public float Heal;
            public Vector2 Position;
            public float MagnetSpeed;
            public bool Magnetized;

            /// <summary>生成序号。容量满时靠它找出最旧的那一个，不靠数组下标（回收会换位）。</summary>
            public int Serial;
        }

        /// <summary>对外只读快照。视图层拿它摆 GameObject，测试拿它验"有没有动"。</summary>
        public readonly struct Snapshot
        {
            /// <summary>池内槽位号。同一个掉落物在它的生命周期里不变，视图按它对齐 GameObject。</summary>
            public readonly int Slot;

            public readonly PickupKind Kind;
            public readonly Vector2 Position;
            public readonly bool Magnetized;

            public Snapshot(int slot, PickupKind kind, Vector2 position, bool magnetized)
            {
                Slot = slot;
                Kind = kind;
                Position = position;
                Magnetized = magnetized;
            }
        }

        readonly Slot[] _slots;
        readonly PlayerModel _player;
        readonly RunState _run;

        /// <summary>拾取时发 CoinPicked / CoinChanged。可空：只测磁吸的用例不需要事件总线。</summary>
        readonly IEventBus _events;

        /// <summary>掉心的骰子。按要求走战斗那条流（一局只有一条随机源），不新开一条。</summary>
        readonly IRandom01 _random;

        /// <summary>活跃槽位数，活跃项是 _slots[0.._count-1]（稠密，回收时末尾换位）。</summary>
        int _count;

        int _nextSerial = 1;

        public PickupSystem(PlayerModel player, RunState run, IEventBus events, IRandom01 random)
        {
            _player = player ?? throw new System.ArgumentNullException(nameof(player));
            _run = run ?? throw new System.ArgumentNullException(nameof(run));
            _events = events;
            _random = random ?? throw new System.ArgumentNullException(nameof(random));
            _slots = new Slot[Capacity];
        }

        /// <summary>槽位上限，视图层同步时可以拿它当数组上界。</summary>
        public int SlotCapacity => _slots.Length;

        /// <summary>场上现有的掉落物数。</summary>
        public int Count => _count;

        /// <summary>按池内顺序取第 ordinal 个掉落物。视图层同步与测试用。</summary>
        public bool TryGetActive(int ordinal, out Snapshot snapshot)
        {
            if (ordinal < 0 || ordinal >= _count)
            {
                snapshot = default;
                return false;
            }

            Slot slot = _slots[ordinal];
            snapshot = new Snapshot(ordinal, slot.Kind, slot.Position, slot.Magnetized);
            return true;
        }

        /// <summary>
        /// 敌人死亡掉落：金币 100%、心 25%。
        /// 只在 RunSimulation 的清理阶段调：命中路径上生成掉落会让"谁还能看见这个敌人"变成隐式时序问题，
        /// 和 Remove 放在一起就没有"打死了但还没掉"的中间态。
        /// </summary>
        public void DropEnemyLoot(Vector2 position)
        {
            Spawn(PickupKind.Coin, CoinValuePerKill, 0f, position);

            // 心也掷一次，而不是"金币之外再掉一件"：一条随机流抽一次，掉落序列与礼物特效无关
            if (_random.Next01() < HeartDropChance) Spawn(PickupKind.Heart, 0, HeartHeal, position);
        }

        /// <summary>
        /// 推进所有掉落物：远处的原地待着，进了吸附半径就加速飞向玩家，贴身到拾取半径就结算。
        /// 纯加法与比较，稳态 0 B（事件是结构体，入队走预先分配的队列）。
        /// </summary>
        public void Tick(float dt)
        {
            Vector2 playerPos = _player.Position;

            // 吸附半径从属性读：拾取范围道具加在 PickupRadius 上就生效，这里不存副本
            float magnetRadius = _player.Stats[StatType.PickupRadius];

            // 倒序遍历：回收时把末尾换到当前格子，末尾一定已经处理过，不会跳过谁、也不会重复处理
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
                    // 远处不动。停在原地是刻意的：玩家看得见掉落物，就得为它走过去一段
                    if (distance > magnetRadius) continue;
                    slot.Magnetized = true;
                }

                // 一旦被吸住就一直追：玩家跑远了它也不回到静止态，否则贴脸捡的路上会被反复"松手"
                slot.MagnetSpeed = Mathf.Min(slot.MagnetSpeed + MagnetAcceleration * dt, MagnetMaxSpeed);

                // 单步位移不超过剩余距离：飞过头会让它在拾取半径两侧来回抖一步
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
                // 满了顶掉最旧的，不拒绝生成：
                // 被顶掉的必然是玩家一直没去捡的那一个（多半留在已经离开的那片战场上），
                // 而拒绝生成会悄悄少掉"这只怪该掉的金币"——从场面上完全看不出来，
                // 调数值时会变成查不清的收支差。
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

        /// <summary>
        /// 结算一次拾取。金币记进 RunState 并发 CoinPicked / CoinChanged；心回血并 clamp 到 MaxHP。
        /// </summary>
        void Collect(in Slot slot)
        {
            if (slot.Kind == PickupKind.Coin)
            {
                _run.AddCoins(slot.Value);
                _events?.Publish(new CoinPicked(slot.Value, slot.Position.x, slot.Position.y));
                _events?.Publish(new CoinChanged(_run.Coins, slot.Value));
                return;
            }

            // 满血时这颗心照样被吃掉：留着的话它会粘在玩家身上，满血的人踩过去就白攒一堆心
            float maxHp = _player.MaxHP;
            if (_player.HP >= maxHp) return;

            // clamp 到上限：回 5 血的规则不能把 98 血的人推到 103
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
