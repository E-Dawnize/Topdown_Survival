using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 敌人行为：追击 + 邻格分离 + 起手预警 + 近战/冲锋/远程出手 + 击退冲量衰减。
    /// 只管推进行为，不刷怪也不删怪：生成与移除由外部（波次、清理阶段）调 Spawn / Remove；
    /// 打死后也得外部调 Remove，系统不自己清尸体——免得“谁该看到尸体”这种时序问题隐式藏在系统里。
    ///
    /// 伤害只在“起手结束的那一下”结算：近战按锁定的扇形复核、冲锋靠身体撞到、
    /// 远程从 EnemyProjectileSystem 发射可见弹丸。没有“站在攻击距离内就每 AttackInterval 自动掉血”的隐形伤害。
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

        /// <summary>
        /// 击退冲量每秒衰减多少米/秒。线性衰减到 0：一次命中的冲量在 0.2 秒上下归零，
        /// 短到不会把敌人推出战团，也长到能看出“这一下是被打退的”。
        /// </summary>
        const float KnockbackDecay = 24f;

        // ---- 敌人出手参数 ----

        /// <summary>近战起手（预警）时长。够玩家看见红色预警环并做出一次侧移。</summary>
        const float MeleeWindupSeconds = 0.32f;

        /// <summary>BOSS 的近战起手更长：范围大、伤害高，必须留出更明显的躲避窗口。</summary>
        const float BossWindupSeconds = 0.55f;

        /// <summary>远程投掷的蓄力时长。</summary>
        const float RangedWindupSeconds = 0.55f;

        /// <summary>冲锋兵的蓄力时长。</summary>
        const float ChargeWindupSeconds = 0.50f;

        /// <summary>冲锋位移持续多久。速度是移速的 4.5 倍，0.3 秒冲出去 2–4 米。</summary>
        const float ChargeDuration = 0.30f;

        /// <summary>冲锋速度相对普通移速的倍数。太慢看不出冲锋，太快会直接穿过玩家。</summary>
        const float ChargeSpeedMultiplier = 4.5f;

        /// <summary>近战扇形的半角。敌人不是精确的刀客，给玩家一点侧面擦到的宽容。</summary>
        const float MeleeHalfAngleDeg = 55f;

        /// <summary>BOSS 的横扫范围。预警环与伤害共用同一个角度。</summary>
        const float BossHalfAngleDeg = 100f;

        /// <summary>投手弹丸速度。比玩家的箭慢，玩家能看到它飞过来并侧移躲开。</summary>
        const float RangedProjectileSpeed = 9f;

        readonly SpatialGrid _grid = new SpatialGrid();
        readonly PlayerModel _player;
        readonly float _arenaHalfExtent;

        /// <summary>
        /// 敌人对玩家伤害的闪避骰子。可空：没注入随机流时把玩家的 Dodge 按 0 处理，
        /// 而不是照 Dodge 传参再喂一个恒为 0 的骰——那样有闪避属性就会变成必定闪避。
        /// 近战、冲锋与远程弹丸共用这一条随机流。正式装配（AppRoot）一定注入。
        /// </summary>
        readonly IRandom01 _dodgeRng;

        /// <summary>
        /// 敌人远程攻击的弹丸。投手蓄力结束后从这里发射，弹丸真的碰到玩家才扣血。
        /// 暴露给视图层同步弹丸表现；正式装配由 RunLoop 每帧调 Sync。
        /// </summary>
        public EnemyProjectileSystem Projectiles { get; }

        /// <summary>稳定实体 ID 分配器。由 AppRoot 传入同一个实例，玩家、敌人、旅伴不会撞号。</summary>
        readonly UnitIdAllocator _ids;

        /// <summary>受击事件往哪发。可空：只测追击的用例不需要事件总线。</summary>
        readonly IEventBus _events;

        /// <summary>UnitId.Value 到槽位的索引。ID 不复用，所以延迟事件不会解析到新实体。</summary>
        readonly Dictionary<int, int> _slotById = new Dictionary<int, int>(64);

        // 每步的邻格查询结果复用同一个容器：这条路径每 tick 调「敌人数」次，自己 new 就是稳定 GC
        readonly List<int> _neighbors = new List<int>(16);

        EnemyModel[] _slots = new EnemyModel[64];
        int _aliveCount;

        public EnemySystem(PlayerModel player, float arenaHalfExtent, IRandom01 dodgeRng = null,
            UnitIdAllocator ids = null, IEventBus events = null)
        {
            _player = player ?? throw new System.ArgumentNullException(nameof(player));
            _arenaHalfExtent = arenaHalfExtent;
            _dodgeRng = dodgeRng;
            _ids = ids ?? new UnitIdAllocator(2);
            _events = events;
            Projectiles = new EnemyProjectileSystem(player, dodgeRng, events);
        }

        /// <summary>当前槽位对应的稳定 UnitId。槽位空时返回 None。</summary>
        public UnitId UnitIdOf(int slot)
        {
            EnemyModel enemy = Get(slot);
            return enemy != null ? enemy.Id : UnitId.None;
        }

        /// <summary>按稳定 ID 找回当前槽位。视图层收到 UnitDamaged 时用它，不再自己算 slot + 1。</summary>
        public bool TryGetSlot(UnitId id, out int slot)
        {
            if (id.IsValid && _slotById.TryGetValue(id.Value, out slot)) return true;
            slot = -1;
            return false;
        }

        public int SlotOf(UnitId id) => TryGetSlot(id, out int slot) ? slot : -1;

        /// <summary>已经分配出去的最大 ID，投射物命中记录按它扩容。</summary>
        public int MaxUnitIdValue => _ids.MaxAllocated;

        /// <summary>外部按主循环第 7 步调 Rebuild(玩家位置, 本系统)：格子快照必须晚于移动。</summary>
        public SpatialGrid Grid => _grid;

        public int AliveCount => _aliveCount;

        /// <summary>已分配的槽位上限。视图层同步时可以拿它当数组上界。</summary>
        public int SlotCapacity => _slots.Length;

        public EnemyModel Get(int slot)
        {
            return slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        }

        /// <summary>
        /// 以 origin 为圆心、radius 为半径，找最近的活敌人槽位；没有返回 -1。玩家武器、旅伴移动与旅伴开火
        /// 共用这一条索敌规则，避免“武器打最近的、旅伴朝另一个”这种两套口径。
        ///
        /// 用 QueryCircle 而不是逐环最近优先：网格里存的是上一次 Rebuild 的快照，可能还留着已死/已移除的槽位；
        /// 先扫一圈再按模型当前位置与存活标记复核，只多几次比较，不会因为最近的是尸体就整轮不开火。
        /// 距离用模型当前位置算（不是快照位置），所以本步移动过的敌人不会被旧位置误导。
        ///
        /// scratch 由调用方持有并复用（这条路径每 tick 会被调用多次）；本方法会先清空它，
        /// 只借用、不缓存，各调用方的容器互不影响。
        /// </summary>
        public int FindNearestAliveSlot(Vector2 origin, float radius, List<int> scratch)
        {
            if (scratch == null) throw new System.ArgumentNullException(nameof(scratch));

            scratch.Clear();
            _grid.QueryCircle(origin, radius, scratch);

            int best = -1;
            float bestSqr = radius * radius;
            for (int i = 0; i < scratch.Count; i++)
            {
                int slot = scratch[i];
                EnemyModel enemy = Get(slot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                float sqr = (enemy.Position - origin).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = slot;
            }
            return best;
        }

        /// <summary>同 <see cref="FindNearestAliveSlot"/>，直接返回模型；没有目标返回 null。</summary>
        public EnemyModel FindNearestAlive(Vector2 origin, float radius, List<int> scratch)
        {
            int slot = FindNearestAliveSlot(origin, radius, scratch);
            return slot >= 0 ? Get(slot) : null;
        }

        /// <summary>外部生成（也是 IEnemySpawnTarget 的实现）。返回槽位号（= 空间网格的索引），敌人数组直接按它下标存。</summary>
        public int Spawn(EnemyDef def, Vector2 position)
        {
            return Spawn(def, position, EnemySpawnOptions.Default);
        }

        /// <summary>
        /// 带强度参数的生成。DirectorSystem 用它把"当前时间段的血量/伤害/速度倍率"和精英标记写进敌人。
        /// 普通刷怪与精英/BOSS 共用这一条路径，避免两套生成逻辑在平衡改动时分叉。
        /// </summary>
        public int Spawn(EnemyDef def, Vector2 position, in EnemySpawnOptions options)
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

            EnemyModel enemy = new EnemyModel(def, ClampToArena(position), _ids.Allocate(UnitKind.Enemy), options);
            _slots[slot] = enemy;
            _slotById[enemy.Id.Value] = slot;
            _aliveCount++;
            return slot;
        }

        /// <summary>外部移除。先从空间索引摘掉再清数据：Rebuild 拿到的槽位必须还指得到对象。</summary>
        public void Remove(int slot)
        {
            EnemyModel enemy = Get(slot);
            if (enemy == null || !enemy.IsAlive) return;

            _grid.Release(slot);
            _slotById.Remove(enemy.Id.Value);
            enemy.IsAlive = false;
            _slots[slot] = null;
            _aliveCount--;
        }

        /// <summary>ISpatialGridSource：Rebuild 从这里读本轮位置。</summary>
        public Vector2 PositionOf(int unitIndex) => _slots[unitIndex].Position;

        /// <summary>
        /// 固定步长调用一次：追击/保持距离 + 邻格分离 + 近战起手与冲锋 + 远程弹丸。
        /// 玩家位置从参数进（本轮玩家已经移动完）。所有伤害都发生在起手结束的那一下，
        /// 不再有“站在攻击距离内每 AttackInterval 自动扣血”的隐形伤害。
        /// </summary>
        public void Tick(Vector2 playerPos, float dt)
        {
            // 先推进上一帧已经出膛的弹丸：本步敌人新发射的弹丸从下一步才开始移动，
            // 与玩家投射物同一条规则，免得「刚出手就吃掉一整步位移」。
            Projectiles.Tick(dt);

            for (int slot = 0; slot < _slots.Length; slot++)
            {
                EnemyModel enemy = _slots[slot];
                if (enemy == null || !enemy.IsAlive) continue;
                Step(slot, enemy, playerPos, dt);
            }
        }

        void Step(int slot, EnemyModel enemy, Vector2 playerPos, float dt)
        {
            if (enemy.AttackTimer > 0f)
            {
                enemy.AttackTimer -= dt;
                if (enemy.AttackTimer < 0f) enemy.AttackTimer = 0f;
            }

            // 冲锋是一段独立位移：不追击、不做分离，走完这一段才回到正常追击。
            if (enemy.IsCharging)
            {
                StepCharge(enemy, playerPos, dt);
                return;
            }

            Vector2 toPlayer = playerPos - enemy.Position;
            float distance = toPlayer.magnitude;
            Vector2 towardPlayer = distance > 1e-4f
                ? toPlayer / distance
                : (enemy.AttackDirection.sqrMagnitude > 1e-8f ? enemy.AttackDirection : Vector2.up);

            Vector2 chase = Vector2.zero;

            if (enemy.IsWindingUp)
            {
                // 起手期间不追击：玩家看到预警后侧移就能让近战落空，远程则躲开锁定的弹道。
                enemy.AttackWindup -= dt;
                if (enemy.AttackWindup <= 0f)
                {
                    enemy.AttackWindup = 0f;
                    Strike(enemy, playerPos);
                }
            }
            else
            {
                chase = DesiredVelocity(enemy, distance, towardPlayer);

                if (enemy.AttackTimer <= 0f && InAttackRange(enemy, distance))
                {
                    BeginWindup(enemy, towardPlayer);
                    chase = Vector2.zero;
                }
            }

            Vector2 separationVelocity;
            Vector2 separationOffset;
            ComputeSeparation(slot, enemy, out separationVelocity, out separationOffset);

            enemy.Velocity = chase + separationVelocity;

            // 冲量衰减在这里而不是命中时一次性算完：命中那一刻只给初速，
            // 每步按固定速率减到 0，所以同一发箭不管命中时帧率多少，退的距离都一样。
            enemy.Knockback = Vector2.MoveTowards(enemy.Knockback, Vector2.zero, KnockbackDecay * dt);

            // 移动前记一笔，渲染层拿 PrevPosition 与 Position 插值
            enemy.PrevPosition = enemy.Position;

            // 位置 = 速度积分 + 位置松解。后者必要：几十个敌人围住玩家时推速有上限，
            // 只靠速度分离总残余一点互相插进去（实测最小间距只有身体直径的 6 成），
            // 松解把这点残余重叠每步直接推开一半，稳态下身体不互相插。
            // 击退作为第三个分量直接加到位移上：追击与分离力那两份不受它影响，
            // 冲量过去了敌人就还是按原来的速度追人。
            enemy.Position = ClampToArena(
                enemy.Position + (enemy.Velocity + enemy.Knockback) * dt + separationOffset);
        }

        /// <summary>
        /// 不同出手方式想要的移动：近战走到贴身、冲锋走到冲锋距离、远程保持在自己射程内。
        /// 这里只返回追击速度，分离力仍然每步叠加，几十只怪不会因为“都不许追”而叠成一坨。
        /// </summary>
        Vector2 DesiredVelocity(EnemyModel enemy, float distance, Vector2 towardPlayer)
        {
            if (distance <= 1e-4f) return Vector2.zero;

            switch (enemy.AttackKind)
            {
                case EnemyAttackKind.Ranged:
                {
                    // 远程不出现在玩家脸上：太近就后退，太远就靠近，落在 Preferred 附近持续投掷。
                    float maxRange = Mathf.Max(0.5f, enemy.Def.AttackRange);
                    float preferred = Mathf.Clamp(enemy.Def.AttackRange * 0.8f, maxRange * 0.5f, maxRange);
                    if (distance > preferred + 0.15f) return towardPlayer * enemy.Speed;
                    if (distance < preferred - 0.15f) return -towardPlayer * enemy.Speed;
                    return Vector2.zero;
                }

                case EnemyAttackKind.Charge:
                {
                    // 冲锋需要一段助跑距离；进入 AttackRange 后停下蓄力，蓄力结束才冲出去。
                    float preferred = Mathf.Max(1.5f, enemy.Def.AttackRange * 0.8f);
                    return distance > preferred ? towardPlayer * enemy.Speed : Vector2.zero;
                }

                default:
                {
                    float reach = StrikeReach(enemy);
                    return distance > reach ? towardPlayer * enemy.Speed : Vector2.zero;
                }
            }
        }

        bool InAttackRange(EnemyModel enemy, float distance)
        {
            if (enemy.AttackKind == EnemyAttackKind.Melee)
            {
                // +0.05 的容差：追击在 reach 处停下，浮点误差不应该让它永远差一点不开火。
                return distance <= StrikeReach(enemy) + 0.05f;
            }

            return distance <= enemy.Def.AttackRange;
        }

        /// <summary>
        /// 近战的实际够到距离。至少是“两个身体半径相加”，否则体型大的怪会出现
        /// 身体已经贴住玩家、中心距离却超过表里 AttackRange 的怪事（BOSS 尤其明显）。
        /// </summary>
        float StrikeReach(EnemyModel enemy)
        {
            float body = enemy.Radius + PlayerModel.BodyRadius;
            return enemy.AttackKind == EnemyAttackKind.Melee
                ? Mathf.Max(enemy.Def.AttackRange, body)
                : enemy.Def.AttackRange;
        }

        float HalfAngleDeg(EnemyModel enemy)
        {
            return enemy.IsBoss ? BossHalfAngleDeg : MeleeHalfAngleDeg;
        }

        float WindupSeconds(EnemyModel enemy)
        {
            if (enemy.IsBoss) return BossWindupSeconds;

            switch (enemy.AttackKind)
            {
                case EnemyAttackKind.Ranged: return RangedWindupSeconds;
                case EnemyAttackKind.Charge: return ChargeWindupSeconds;
                default: return MeleeWindupSeconds;
            }
        }

        /// <summary>
        /// 开始起手：锁定方向、记下时长，并发一条 EnemyAttackStarted 给表现层画预警环。
        /// 锁定方向是“可见攻击”的核心——预警结束前玩家侧移，原来那一刀就该落空。
        /// </summary>
        void BeginWindup(EnemyModel enemy, Vector2 towardPlayer)
        {
            float duration = WindupSeconds(enemy);
            enemy.AttackWindupDuration = duration;
            enemy.AttackWindup = duration;
            enemy.AttackDirection = towardPlayer;

            float angle = Mathf.Atan2(towardPlayer.y, towardPlayer.x);
            _events?.Publish(new EnemyAttackStarted(enemy.Id, enemy.Position.x, enemy.Position.y,
                angle, duration, enemy.AttackKind));
        }

        /// <summary>
        /// 起手结束，真正出手。近战按锁定的扇形复核一次；冲锋进入位移；远程发射可见弹丸。
        /// 三种方式都先发 EnemyAttackStrike，表现层才能在同一个模拟步里画出“这一下打出来了”。
        /// </summary>
        void Strike(EnemyModel enemy, Vector2 playerPos)
        {
            Vector2 toPlayer = playerPos - enemy.Position;
            float distance = toPlayer.magnitude;
            Vector2 direction = enemy.AttackDirection.sqrMagnitude > 1e-8f
                ? enemy.AttackDirection
                : (distance > 1e-4f ? toPlayer / distance : Vector2.up);

            EnemyAttackKind kind = enemy.AttackKind;
            float reach = kind == EnemyAttackKind.Melee ? StrikeReach(enemy) : enemy.Def.AttackRange;
            float halfAngle = kind == EnemyAttackKind.Melee ? HalfAngleDeg(enemy) : 0f;

            if (kind == EnemyAttackKind.Ranged)
            {
                // 从身体外缘发射，避免弹丸一出生就落在自己身体里。
                Vector2 origin = enemy.Position + direction * (enemy.Radius + 0.15f);
                float maxDistance = Mathf.Max(enemy.Def.AttackRange + 4f, 8f);
                Projectiles.Spawn(origin, direction, RangedProjectileSpeed, maxDistance,
                    enemy.ContactDamage, enemy.Id);
            }
            else if (kind == EnemyAttackKind.Charge)
            {
                enemy.ChargeTimer = ChargeDuration;
                enemy.ChargeLanded = false;
            }
            else
            {
                if (distance <= reach + 0.1f && IsInsideArc(toPlayer, distance, direction, halfAngle))
                {
                    PlayerDamage.Strike(_player, enemy.ContactDamage, enemy.Id,
                        distance > 1e-4f ? toPlayer / distance : direction, _dodgeRng, _events);
                }
            }

            // 冲锋的冷却在位移结束时结算；近战/远程从这里开始等下一次攻击。
            if (kind != EnemyAttackKind.Charge)
            {
                enemy.AttackTimer = Mathf.Max(0.05f, enemy.Def.AttackInterval - enemy.AttackWindupDuration);
            }

            float angle = Mathf.Atan2(direction.y, direction.x);
            _events?.Publish(new EnemyAttackStrike(enemy.Id, enemy.Position.x, enemy.Position.y,
                angle, reach, halfAngle, kind));
        }

        /// <summary>
        /// 冲锋位移。锁定的方向 + 4.5 倍移速，撞到玩家才结算一次伤害；
        /// 玩家看到红色预警后往侧面走，冲锋会从旁边擦过去。
        /// </summary>
        void StepCharge(EnemyModel enemy, Vector2 playerPos, float dt)
        {
            enemy.ChargeTimer -= dt;
            enemy.Velocity = enemy.AttackDirection * (enemy.Speed * ChargeSpeedMultiplier);
            enemy.Knockback = Vector2.MoveTowards(enemy.Knockback, Vector2.zero, KnockbackDecay * dt);

            enemy.PrevPosition = enemy.Position;
            enemy.Position = ClampToArena(enemy.Position + (enemy.Velocity + enemy.Knockback) * dt);

            float hitDistance = enemy.Radius + PlayerModel.BodyRadius + 0.1f;
            if (!enemy.ChargeLanded &&
                (playerPos - enemy.Position).sqrMagnitude <= hitDistance * hitDistance)
            {
                enemy.ChargeLanded = true;
                PlayerDamage.Strike(_player, enemy.ContactDamage, enemy.Id, enemy.AttackDirection, _dodgeRng,
                    _events);
            }

            if (enemy.ChargeTimer > 0f) return;

            enemy.ChargeTimer = 0f;
            enemy.AttackTimer = Mathf.Max(0.1f,
                enemy.Def.AttackInterval - enemy.AttackWindupDuration - ChargeDuration);
        }

        /// <summary>扇形判定：与玩家近战同一套角度口径；半角 &gt;= 180° 当全向。</summary>
        static bool IsInsideArc(Vector2 toTarget, float distance, Vector2 facing, float halfAngleDeg)
        {
            if (halfAngleDeg >= 180f) return true;
            if (distance < 1e-4f) return true;

            float cosine = Vector2.Dot(toTarget / distance, facing);
            float angle = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
            return angle <= halfAngleDeg;
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


        Vector2 ClampToArena(Vector2 position)
        {
            position.x = Mathf.Clamp(position.x, -_arenaHalfExtent, _arenaHalfExtent);
            position.y = Mathf.Clamp(position.y, -_arenaHalfExtent, _arenaHalfExtent);
            return position;
        }
    }
}
