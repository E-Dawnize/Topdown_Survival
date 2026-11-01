using System.Collections.Generic;
using Game.Config;
using Game.Model;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 旅伴的移动 AI 与两两分离。只读写 CompanionModel 与敌人网格，不碰武器、不碰羁绊。
    ///
    /// 行为规则（GDD 8）：
    ///   - Follow：固定环绕位（槽位号 × 90°），目标点 = 玩家位置 + 单位方向 × FollowRadius；
    ///     目标点会按玩家速度往身后偏一点，并用 SmoothDamp 追，所以起步、转向、停下时会有拖尾和回位；
    ///   - Aggro：找 AggroRadius 内最近的敌人，贴到 PreferredRange 后绕目标走；
    ///   - Kiter：维持 PreferredRange，敌人贴脸就后退；
    ///   - Guard：只打靠近玩家的敌人，不追远目标；
    ///   - 所有模式都受 LeashDistance 限制，离玩家超过活动半径就回位；Follow 仍沿用 4 米瞬移阈值。
    ///   - 不做寻路：目标点直线移动，靠两两松解避免旅伴互相重叠。
    ///
    /// 为什么选固定角度环绕而不是敌人那套邻格分离力：
    ///   旅伴最多 4 个，90° 一格的位置本身就是互不重叠的（相邻两格的间距 = √(r1² + r2²) ≥ √2 × MinRingRadius），
    ///   不需要空间索引，也不需要每步查邻居。敌人那套是为"几十上百个、没有天然位置"的群体准备的，
    ///   搬过来要给旅伴再建一张网格（敌人网格的槽位是敌人的，混用会让敌人的分离力把旅伴算进去）。
    ///   环形位置负责稳态，SmoothDamp 负责起步/转向的拖尾，另外加一道 O(n²)、n ≤ 4 的两两位置松解，
    ///   负责追赶途中与瞬移瞬间不叠在一起。
    ///
    /// 旅伴不参与敌人的分离力，也不会被敌人推着走：敌人系统只改 EnemyModel，从不读写 CompanionModel，
    /// 旅伴的位置每步只由"环绕目标 + 自己的移速 + 两两松解"决定。敌人也不会攻击旅伴（接触伤害只对玩家结算）。
    /// </summary>
    internal sealed class CompanionMovement
    {
        /// <summary>两两重合时（理论上瞬移后才可能）取固定方向散开用的黄金角，与 EnemySystem 同一个值。</summary>
        const float GoldenAngle = 2.39996323f;

        readonly CompanionModel[] _slots;
        readonly PlayerModel _player;
        readonly EnemySystem _enemies;

        // 每步索敌结果复用同一个容器：这条路径每 tick 调"旅伴数"次，自己 new 就是稳定 GC。
        // 容器逐调用者持有：EnemySystem 的共享索敌只借用，不缓存调用方的容器。
        readonly List<int> _targets = new List<int>(16);

        public CompanionMovement(CompanionModel[] slots, PlayerModel player, EnemySystem enemies)
        {
            _slots = slots;
            _player = player;
            _enemies = enemies;
        }

        /// <summary>
        /// 把旅伴放到它的环绕位上（出生与换局回位用）。PrevPosition 一起写：
        /// 渲染层在 PrevPosition 与 Position 之间插值，留着旧位置会把归位画成一条滑线。
        /// </summary>
        public void PlaceOnRing(CompanionModel companion, Vector2 playerPos)
        {
            Vector2 target = RingTarget(playerPos, companion.Slot, companion.Def.FollowRadius);
            companion.Position = target;
            companion.PrevPosition = target;
            companion.Velocity = Vector2.zero;
        }

        /// <summary>
        /// 跟随移动。排在玩家移动之后、敌人移动之前（RunSimulation 第 2.5 步）：
        /// 旅伴追的是玩家本步的新位置，而敌人的追击与分离力完全不看旅伴，先后都不影响它们。
        /// 稳态不分配：只读模型、写模型，外加一个长度 ≤ 4 的两两松解。
        /// </summary>
        public void Tick(Vector2 playerPos, float dt)
        {
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                CompanionModel companion = _slots[slot];
                if (companion == null) continue;

                Vector2 target = DesiredPoint(companion, playerPos, slot);
                float teleport = TeleportThreshold(companion);

                if ((playerPos - companion.Position).sqrMagnitude > teleport * teleport)
                {
                    // 超过活动半径就不跑了，直接回位：玩家冲刺 + 加速道具以后，靠跑是追不上的。
                    // 两边都写目标点：渲染层在 PrevPosition 与 Position 之间插值，留着旧位置
                    // 会把瞬移画成一条横穿战场的滑线
                    target = RingTarget(FollowAnchor(companion, playerPos), slot, companion.Def.FollowRadius);
                    companion.Position = target;
                    companion.PrevPosition = target;
                    companion.Velocity = Vector2.zero;
                    continue;
                }

                // 正常走：移动前记一笔，渲染层拿 PrevPosition 与 Position 插值。
                // 不用 MoveTowards：那会让旅伴和玩家保持完全相同的速度，看起来像焊在环上；
                // SmoothDamp 从当前速度连续加减速，起步/转向会落后一点再追上来。
                companion.PrevPosition = companion.Position;
                companion.Position = Vector2.SmoothDamp(companion.Position, target, ref companion.Velocity,
                    CompanionSystem.FollowSmoothTime, companion.MoveSpeed, dt);
            }

            ResolveOverlap();
        }

        /// <summary>
        /// 旅伴的 AI 目标点。Follow 是阻尼环绕；Aggro/Kiter/Guard 会从敌人网格里找最近目标：
        ///   - Aggro：贴到 PreferredRange 内，再绕着目标走；
        ///   - Kiter：保持 PreferredRange，敌人太近就后退；
        ///   - Guard：只打靠近玩家的敌人，不追远目标。
        /// 目标点最后会被 LeashDistance 夹回玩家周围，防止追怪追到天边。
        /// </summary>
        Vector2 DesiredPoint(CompanionModel companion, Vector2 playerPos, int slot)
        {
            Vector2 ring = RingTarget(FollowAnchor(companion, playerPos), slot, companion.Def.FollowRadius);

            CompanionAI ai = companion.Def.AI;
            if (ai == CompanionAI.Follow) return ring;

            float aggro = companion.Def.AggroRadius > 0.5f ? companion.Def.AggroRadius : 6f;
            EnemyModel enemy = FindNearestEnemy(companion.Position, aggro);
            if (enemy == null) return ring;

            if (ai == CompanionAI.Guard &&
                (enemy.Position - playerPos).sqrMagnitude > aggro * aggro)
            {
                return ring;
            }

            Vector2 toEnemy = enemy.Position - companion.Position;
            float distance = toEnemy.magnitude;
            float preferred = companion.Def.PreferredRange > 0.4f ? companion.Def.PreferredRange : 1.5f;
            Vector2 direction = distance > 1e-4f ? toEnemy / distance : Vector2.up;
            Vector2 point;

            if (ai == CompanionAI.Kiter)
            {
                if (distance < preferred)
                {
                    // 敌人近了：沿反方向退到 preferred 距离
                    point = companion.Position - direction * (preferred - distance);
                }
                else if (distance > preferred * 1.6f)
                {
                    // 敌人太远：往 target 靠近，但只到 preferred
                    point = enemy.Position - direction * preferred;
                }
                else
                {
                    point = companion.Position;
                }
            }
            else
            {
                if (distance > preferred)
                {
                    point = enemy.Position - direction * preferred;
                }
                else
                {
                    // 贴到距离内后绕着目标走，避免所有旅伴挤在同一个点上
                    var tangent = new Vector2(-direction.y, direction.x);
                    point = enemy.Position + tangent * preferred;
                }
            }

            float leash = companion.Def.LeashDistance > 0.5f ? companion.Def.LeashDistance : 8f;
            Vector2 toPoint = point - playerPos;
            float pointDistance = toPoint.magnitude;
            if (pointDistance > leash && pointDistance > 1e-4f)
            {
                point = playerPos + toPoint / pointDistance * leash;
            }

            return point;
        }

        /// <summary>
        /// 射程内最近的活敌人。扫描走 EnemySystem 的共享索敌：快照里可能还留着已移除/已死的槽位，
        /// 共享 API 会按模型当前位置与存活标记复核，和武器系统用的是同一套语义。
        /// </summary>
        EnemyModel FindNearestEnemy(Vector2 origin, float radius)
        {
            return _enemies.FindNearestAlive(origin, radius, _targets);
        }

        static float TeleportThreshold(CompanionModel companion)
        {
            if (companion.Def.AI == CompanionAI.Follow) return CompanionSystem.TeleportDistance;
            float leash = companion.Def.LeashDistance > 0.5f ? companion.Def.LeashDistance : 8f;
            return Mathf.Max(CompanionSystem.TeleportDistance, leash + 2f);
        }

        /// <summary>
        /// Follow 的目标锚点。玩家匀速移动时，整队目标会略微落到玩家身后（最多 FollowTrailTime × 速度），
        /// 配合 SmoothDamp 形成“追着跑”的观感；Aggro/Kiter/Guard 不拖尾，接敌/撤退保持即时。
        /// </summary>
        Vector2 FollowAnchor(CompanionModel companion, Vector2 playerPos)
        {
            if (companion.Def.AI != CompanionAI.Follow) return playerPos;
            return playerPos - _player.Velocity * CompanionSystem.FollowTrailTime;
        }

        Vector2 RingTarget(Vector2 playerPos, int slot, float followRadius)
        {
            // 90° 一格：4 个旅伴正好占满四个象限，任意两个相邻槽位都差 90°，间距下界由 MinRingRadius 保证。
            // 目标点不夹场地边界：夹了以后玩家贴着墙走时几位旅伴会被同一条边挤成一排，
            // 而旅伴没有物理碰撞（不撞墙也不撞敌人），追出去半个身位比在墙边重叠好。
            float angle = slot * (Mathf.PI * 0.5f);
            float radius = followRadius > CompanionSystem.MinRingRadius
                ? followRadius
                : CompanionSystem.MinRingRadius;
            return playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>
        /// 两两位置松解。环形位置已经保证了稳态不重叠，这一道管的是追赶途中与瞬移瞬间：
        /// 一个旅伴还在半路、另一个已经到位时，两个身体可能短暂插在一起。
        /// 每个重叠对沿连线各让一半，n ≤ 4 所以是固定 6 次比较，比再建一张空间网格便宜得多。
        /// </summary>
        void ResolveOverlap()
        {
            float minDistance = CompanionSystem.BodyRadius * 2f * CompanionSystem.SeparationMargin;
            float minSqr = minDistance * minDistance;

            for (int i = 0; i < _slots.Length; i++)
            {
                CompanionModel a = _slots[i];
                if (a == null) continue;

                for (int j = i + 1; j < _slots.Length; j++)
                {
                    CompanionModel b = _slots[j];
                    if (b == null) continue;

                    Vector2 delta = b.Position - a.Position;
                    float sqr = delta.sqrMagnitude;
                    if (sqr >= minSqr) continue;

                    float distance = Mathf.Sqrt(sqr);
                    Vector2 direction = distance > 1e-4f
                        ? delta / distance
                        : new Vector2(Mathf.Cos((i * CompanionSystem.MaxCompanions + j) * GoldenAngle),
                            Mathf.Sin((i * CompanionSystem.MaxCompanions + j) * GoldenAngle));   // 完全重合才走这里

                    float push = (minDistance - distance) * 0.5f;   // 一人让一半
                    a.Position -= direction * push;
                    b.Position += direction * push;
                }
            }
        }
    }
}
