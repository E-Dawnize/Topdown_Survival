using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 敌人追击 + 邻格分离力 + 接触伤害。只管推进行为，不刷怪也不删怪：
    /// 生成与移除由外部（波次、清理阶段）调 Spawn / Remove；打死后也得外部调 Remove，
    /// 系统不自己清尸体——免得"谁该看到尸体"这种时序问题隐式藏在系统里。
    ///
    /// 位置快照：分离力读的是上一次 Rebuild 落进格子的位置，不是别的敌人刚写进去的新位置。
    /// 这样同一 tick 里谁先被处理都不会改变结果——固定步长 + 固定种子的复现性靠这一条。
    /// 敌人按槽位号从小到大推进，纯为确定性，和格子里的顺序无关。
    /// </summary>
    public sealed class EnemySystem : ISpatialGridSource
    {
        /// <summary>
        /// 分离速度相对追击速度的权重。小于 1 时挤在一起的敌人推不开彼此、会顶成一坨；
        /// 过大则玩家身边的敌人互相弹开、看起来在抖。1.6 是 50 敌群 600 步实测的稳定值。
        /// </summary>
        const float SeparationWeight = 1.6f;

        /// <summary>黄金角。两个敌人完全重合时按槽位号取一个固定方向散开，避免除零也避免永远叠在一起。</summary>
        const float GoldenAngle = 2.39996323f;

        readonly SpatialGrid _grid = new SpatialGrid();
        readonly PlayerModel _player;
        readonly float _arenaHalfExtent;

        // 每步的邻格查询结果复用同一个容器：这条路径每 tick 调「敌人数」次，自己 new 就是稳定 GC
        readonly List<int> _neighbors = new List<int>(16);

        EnemyModel[] _slots = new EnemyModel[64];
        int _aliveCount;

        public EnemySystem(PlayerModel player, float arenaHalfExtent)
        {
            _player = player ?? throw new System.ArgumentNullException(nameof(player));
            _arenaHalfExtent = arenaHalfExtent;
        }

        /// <summary>
        /// 敌人槽位号到 UnitId 的唯一换算。事件与伤害里都走这里，免得各处自己写 slot + 1
        /// 之后偏移量对不上（0 是 UnitId.None，所以从 1 开始）。
        /// </summary>
        public static UnitId UnitIdOf(int slot) => new UnitId(slot + 1);

        /// <summary>UnitIdOf 的反函数。视图层收到 UnitDamaged 时靠它找回槽位。</summary>
        public static int SlotOf(UnitId id) => id.Value - 1;

        /// <summary>外部按主循环第 7 步调 Rebuild(玩家位置, 本系统)：格子快照必须晚于移动。</summary>
        public SpatialGrid Grid => _grid;

        public int AliveCount => _aliveCount;

        /// <summary>已分配的槽位上限。视图层同步时可以拿它当数组上界。</summary>
        public int SlotCapacity => _slots.Length;

        public EnemyModel Get(int slot)
        {
            return slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        }

        /// <summary>外部生成。返回槽位号（= 空间网格的索引），敌人数组直接按它下标存。</summary>
        public int Spawn(EnemyDef def, Vector2 position)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));

            int slot = _grid.Allocate();
            if (slot >= _slots.Length)
            {
                // 只在容量不够时扩容，稳态刷怪不产生分配
                int capacity = _slots.Length;
                while (capacity <= slot) capacity *= 2;
                var grown = new EnemyModel[capacity];
                System.Array.Copy(_slots, grown, _slots.Length);
                _slots = grown;
            }

            _slots[slot] = new EnemyModel(def, ClampToArena(position));
            _aliveCount++;
            return slot;
        }

        /// <summary>外部移除。先从空间索引摘掉再清数据：Rebuild 拿到的槽位必须还指得到对象。</summary>
        public void Remove(int slot)
        {
            EnemyModel enemy = Get(slot);
            if (enemy == null || !enemy.IsAlive) return;

            _grid.Release(slot);
            enemy.IsAlive = false;
            _slots[slot] = null;
            _aliveCount--;
        }

        /// <summary>ISpatialGridSource：Rebuild 从这里读本轮位置。</summary>
        public Vector2 PositionOf(int unitIndex) => _slots[unitIndex].Position;

        /// <summary>
        /// 固定步长调用一次。追击玩家 + 邻格分离 + 接触伤害结算。
        /// 玩家位置从参数进（本轮玩家已经移动完），模型引用只用来扣血和读无敌帧。
        /// </summary>
        public void Tick(Vector2 playerPos, float dt)
        {
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                EnemyModel enemy = _slots[slot];
                if (enemy == null || !enemy.IsAlive) continue;
                Step(slot, enemy, playerPos, dt);
            }
        }

        void Step(int slot, EnemyModel enemy, Vector2 playerPos, float dt)
        {
            Vector2 toPlayer = playerPos - enemy.Position;
            float distance = toPlayer.magnitude;

            // 到了接触距离就不再往里挤：再挤只会把自己人压成一坨，并且把玩家顶穿
            Vector2 chase = distance > enemy.Def.AttackRange && distance > 1e-4f
                ? toPlayer * (enemy.Speed / distance)
                : Vector2.zero;

            Vector2 separationVelocity;
            Vector2 separationOffset;
            ComputeSeparation(slot, enemy, out separationVelocity, out separationOffset);

            enemy.Velocity = chase + separationVelocity;

            // 移动前记一笔，渲染层拿 PrevPosition 与 Position 插值
            enemy.PrevPosition = enemy.Position;

            // 位置 = 速度积分 + 位置松解。后者必要：几十个敌人围住玩家时推速有上限，
            // 只靠速度分离总残余一点互相插进去（实测最小间距只有身体直径的 6 成），
            // 松解把这点残余重叠每步直接推开一半，稳态下身体不互相插。
            enemy.Position = ClampToArena(enemy.Position + enemy.Velocity * dt + separationOffset);

            if (enemy.AttackTimer > 0f)
            {
                enemy.AttackTimer -= dt;
                if (enemy.AttackTimer < 0f) enemy.AttackTimer = 0f;
            }

            // 冷却结束才结算，贴着站再久也是每 AttackInterval 掉一次血
            Vector2 contact = playerPos - enemy.Position;
            if (enemy.AttackTimer <= 0f && contact.sqrMagnitude <= enemy.Def.AttackRange * enemy.Def.AttackRange)
            {
                Vector2 direction = contact.sqrMagnitude > 1e-8f ? contact.normalized : Vector2.zero;
                ApplyContactDamage(slot, enemy, direction);
                enemy.AttackTimer = enemy.Def.AttackInterval;
            }
        }

        /// <summary>
        /// 邻居分离。只查自己周围一小片（半径 = 两个身体半径），不遍历全表：
        /// 挤在一起时邻居才相关，远处的敌人对"别叠成一坨"没有贡献。
        /// 一次遍历同时得到两项：velocity 是推开的速度分量，offset 是本步的位置修正量。
        /// </summary>
        void ComputeSeparation(int slot, EnemyModel enemy, out Vector2 velocity, out Vector2 offset)
        {
            velocity = Vector2.zero;
            offset = Vector2.zero;

            float reach = enemy.Radius * 2f;
            _neighbors.Clear();
            _grid.QueryCircle(enemy.Position, reach, _neighbors);

            Vector2 push = Vector2.zero;
            Vector2 correction = Vector2.zero;
            for (int i = 0; i < _neighbors.Count; i++)
            {
                int otherSlot = _neighbors[i];
                if (otherSlot == slot) continue;

                // 快照里可能还留着刚被移除的槽位，用前问一下
                EnemyModel other = Get(otherSlot);
                if (other == null || !other.IsAlive) continue;

                Vector2 away = enemy.Position - _grid.SnapshotPosition(otherSlot);
                float pairReach = enemy.Radius + other.Radius;
                float sqr = away.sqrMagnitude;
                if (sqr >= pairReach * pairReach) continue;

                if (sqr < 1e-8f)
                {
                    // 完全重合（同点生成）：按槽位号取固定方向，结果仍然是确定的
                    float angle = slot * GoldenAngle;
                    away = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    sqr = 1f;
                }

                float length = Mathf.Sqrt(sqr);
                float overlap = pairReach - length;                  // 重叠越深推得越狠
                push += away * (overlap / pairReach / length);

                // 一人让一半：对方那半边由它自己那一步补，两个方向合起来正好补满一个重叠量
                correction += away * (overlap * 0.5f / length);
            }

            float magnitude = push.magnitude;
            if (magnitude > 1e-6f)
            {
                // 邻居再多也只推到"一个单位向量"的量级：不夹住的话速度会随邻居数线性涨上去
                if (magnitude > 1f) push *= 1f / magnitude;
                velocity = push * (enemy.Speed * SeparationWeight);
            }

            // 修正量封顶到一个身体半径：挤成一片时邻居会有七八个，不夹住的话一帧能被推出去半个身位，
            // 极端堆叠（同点刷 50 个）时看起来像瞬移
            float correctionLength = correction.magnitude;
            offset = correctionLength > enemy.Radius ? correction * (enemy.Radius / correctionLength) : correction;
        }

        void ApplyContactDamage(int slot, EnemyModel enemy, Vector2 direction)
        {
            // 护甲与闪避仍按 0 传：闪避骰子该由调用方用 runRng 掷，系统内部不许自己开随机流。
            // 真按 _player.Stats[StatType.Dodge] 传而骰子恒为 0，会变成"只要有闪避属性就必定闪避"。
            var damage = new DamageInfo(enemy.ContactDamage, DamageType.Physical, UnitIdOf(slot), direction,
                0f, false, DamageFlags.Dodgeable);
            var target = new DamageTarget(_player.HP, 0f, 0f, _player.IsInvulnerable);

            DamageResult result = DamageResolver.Resolve(damage, target, 0f);
            _player.HP = result.RemainingHp;
        }

        Vector2 ClampToArena(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, -_arenaHalfExtent, _arenaHalfExtent);
            position.y = Mathf.Clamp(position.y, -_arenaHalfExtent, _arenaHalfExtent);
            return position;
        }
    }
}
