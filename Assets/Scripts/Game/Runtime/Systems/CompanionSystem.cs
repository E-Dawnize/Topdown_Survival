using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 旅伴（I2-6）与阵营羁绊（I2-7）。两部分放在一起，是因为羁绊的唯一输入就是这支队伍本身：
    /// 招进 / 失去一个旅伴的同一步里必须重算羁绊，拆成两个类以后"谁负责在队伍变化后重算"就没有答案了。
    ///
    /// 跟随规则（GDD 7 / 17：不做寻路，只用"跟随玩家 + 距离弹性"）：
    ///   - 每个旅伴有一个固定的环绕位（槽位号 × 90°），目标点 = 玩家位置 + 单位方向 × FollowRadius；
    ///   - 每步按自己的移速朝目标点直线移动，没有寻路、没有避障；
    ///   - 离玩家超过 4 米直接瞬移回目标点（ITERATION_PLAN I2-6 的验收口径），
    ///     这样玩家用冲刺或加速道具拉开距离时旅伴不会永远挂在后面。
    ///
    /// 为什么选固定角度环绕而不是敌人那套邻格分离力：
    ///   旅伴最多 4 个，90° 一格的位置本身就是互不重叠的（相邻两格的间距 = √(r1² + r2²) ≥ √2 × MinRingRadius），
    ///   不需要空间索引，也不需要每步查邻居。敌人那套是为"几十上百个、没有天然位置"的群体准备的，
    ///   搬过来要给旅伴再建一张网格（敌人网格的槽位是敌人的，混用会让敌人的分离力把旅伴算进去）。
    ///   环形位置负责稳态，另外加一道 O(n²)、n ≤ 4 的两两位置松解，负责追赶途中与瞬移瞬间不叠在一起。
    ///
    /// 旅伴不参与敌人的分离力，也不会被敌人推着走：敌人系统只改 EnemyModel，从不读写 CompanionModel，
    /// 旅伴的位置每步只由"环绕目标 + 自己的移速 + 两两松解"决定。敌人也不会攻击旅伴（接触伤害只对玩家结算）。
    ///
    /// 武器：每个旅伴一个 WeaponSystem 实例，Shooter 是旅伴自己的 UnitId（见 UnitIdOf）。
    /// 所以伤害、受击反馈与击杀归属记在旅伴头上，不会算成玩家的输出。
    /// 开火在同一步里跟在玩家开火之后（RunSimulation 第 5 步），不开线程也不用协程。
    /// </summary>
    public sealed class CompanionSystem
    {
        /// <summary>GDD 7：一局最多同时拥有 4 个。它同时也正好是 4 件羁绊的上限，两个规则用同一个数。</summary>
        public const int MaxCompanions = CompanionBonds.SecondTierCount;

        /// <summary>
        /// 旅伴 UnitId 的起点。敌人用"槽位号 + 1"、玩家固定 1（AppRoot.PlayerUnit），
        /// 三者共用同一个小整数空间；旅伴把自己放在 1000 之后，UnitDamaged / UnitDied 里才能一眼分出
        /// "这一下是谁打的、这只怪是谁杀的"。同屏敌人上限是 200（I1-11），离 1000 还很远。
        /// </summary>
        public const int UnitIdBase = 1000;

        /// <summary>离玩家超过这个距离就瞬移回环绕位（ITERATION_PLAN I2-6 的验收：超过 4 米瞬移回位）。</summary>
        public const float TeleportDistance = 4f;

        /// <summary>
        /// 旅伴的身体半径。表里没有这个字段（Companion.json 只有 FollowRadius 与 MaxHP），
        /// 取与敌人占位身体同量级的 0.35：验收口径"4 个旅伴的最小间距大于身体直径"按 0.7 米算。
        /// </summary>
        public const float BodyRadius = 0.35f;

        /// <summary>两两间距的身体直径余量。1.1 表示"比身体直径再宽 10%"，让验收断言有可量的空隙。</summary>
        public const float SeparationMargin = 1.1f;

        /// <summary>
        /// 环绕半径下限。相邻两格相差 90°，两点的间距 ≥ √2 × 半径；要让这个间距 ≥ 1.1 × 身体直径，
        /// 半径至少要 √2 × 身体半径 × 1.1。配置里的 FollowRadius（1.4 起）都远大于它，
        /// 这一条只是兜住"表里填了个很小的 FollowRadius"时 4 个旅伴不会叠成一坨。
        /// </summary>
        public static readonly float MinRingRadius = Mathf.Sqrt(2f) * BodyRadius * SeparationMargin;

        /// <summary>
        /// 跟随移速。CompanionDef 没有移速列（本阶段不许改 JSON），
        /// 取 6 米/秒：玩家基础移速 5（Character.json），留 20% 余量让旅伴追得上而不是一路靠瞬移；
        /// 玩家被移速道具拉到 6 以上时，4 米瞬移兜底。
        /// </summary>
        public const float FollowSpeed = 6f;

        /// <summary>每个旅伴自己的投射物池容量。旅伴的弹道不共用玩家的池，池爆掉不会互相拖累。</summary>
        public const int ProjectileCapacity = 256;

        /// <summary>阵营枚举的成员数。0 是 None（不算羁绊），其余下标与 CompanionFaction 一一对应。</summary>
        static readonly int FactionCount = Enum.GetValues(typeof(CompanionFaction)).Length;

        /// <summary>两两重合时（理论上瞬移后才可能）取固定方向散开用的黄金角，与 EnemySystem 同一个值。</summary>
        const float GoldenAngle = 2.39996323f;

        /// <summary>索敌扫描的兜底半径：武器 Range 填了 0 或负数时，至少给朝向一个有意义的方向来源。</summary>
        const float MinAimRadius = 1f;

        /// <summary>羁绊源的身份。Source 靠引用相等批量移除，所以一条羁绊每次生效都要有自己的实例。</summary>
        sealed class BondSource
        {
            public readonly CompanionFaction Faction;

            public BondSource(CompanionFaction faction)
            {
                Faction = faction;
            }
        }

        readonly PlayerModel _player;
        readonly EnemySystem _enemies;
        readonly IRandom01 _rng;
        readonly IReadOnlyDictionary<string, WeaponDef> _weaponTable;
        readonly IEventBus _events;

        // 槽位与队伍两条索引：槽位号是固定的（决定环绕角度与 UnitId），_roster 只放还活着的那几个，
        // 重算羁绊与按购买顺序遍历都用它，省掉每步扫空槽位。
        readonly CompanionModel[] _slots = new CompanionModel[MaxCompanions];
        readonly WeaponSystem[] _weaponSystems = new WeaponSystem[MaxCompanions];
        readonly List<CompanionModel> _roster = new List<CompanionModel>(MaxCompanions);

        // 羁绊状态。_bondSources 按阵营下标存当前生效的源对象，重算时按它逐条回滚。
        readonly BondSource[] _bondSources = new BondSource[FactionCount];
        readonly int[] _factionCounts = new int[FactionCount];
        readonly List<ActiveBond> _activeBonds = new List<ActiveBond>(FactionCount);

        // 每步索敌结果复用同一个容器：这条路径每 tick 调"旅伴数"次，自己 new 就是稳定 GC
        readonly List<int> _targets = new List<int>(16);

        public CompanionSystem(PlayerModel player, EnemySystem enemies, IReadOnlyDictionary<string, WeaponDef> weapons,
            IRandom01 rng, IEventBus events = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _weaponTable = weapons ?? throw new ArgumentNullException(nameof(weapons));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _events = events;
        }

        /// <summary>按槽位号从小到大排列的槽位数组长度（= 队伍上限）。视图层同步时可以拿它当上界。</summary>
        public int SlotCapacity => _slots.Length;

        public int Count => _roster.Count;

        /// <summary>还在队伍里的旅伴，按购买顺序。羁绊与开火顺序都走它，结果与购买顺序无关的那部分另行排序。</summary>
        public IReadOnlyList<CompanionModel> Companions => _roster;

        /// <summary>当前生效的羁绊，按 CompanionFaction 的枚举顺序。空列表表示一条都没触发。</summary>
        public IReadOnlyList<ActiveBond> ActiveBonds => _activeBonds;

        /// <summary>旅伴槽位号到 UnitId 的唯一换算，与 EnemySystem.UnitIdOf 同一个思路。</summary>
        public static UnitId UnitIdOf(int slot) => new UnitId(UnitIdBase + slot + 1);

        /// <summary>反向换算：这个 UnitId 是不是旅伴，以及是哪个槽位。不是旅伴返回 -1。</summary>
        public static int SlotOf(UnitId id)
        {
            int slot = id.Value - UnitIdBase - 1;
            return slot >= 0 && slot < MaxCompanions ? slot : -1;
        }

        public CompanionModel Get(int slot)
        {
            return slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        }

        /// <summary>正拿着这把武器的那套武器系统。每个旅伴一套，Shooter 是旅伴自己的 UnitId。</summary>
        public WeaponSystem WeaponsOf(int slot)
        {
            return slot >= 0 && slot < _weaponSystems.Length ? _weaponSystems[slot] : null;
        }

        /// <summary>队伍里某个旅伴的等级，没有这个旅伴返回 0。</summary>
        public int LevelOf(string companionId)
        {
            CompanionModel existing = Find(companionId);
            return existing == null ? 0 : existing.Level;
        }

        /// <summary>
        /// 现在能不能再招这个旅伴：已有的看等级（满级买不了），没有的看队伍有没有满。
        /// 商店的 CanBuy 与 TryBuy 共用这一条判据，界面上"看着能买、按下去没反应"就不会出现。
        /// </summary>
        public bool CanRecruit(CompanionDef def)
        {
            if (def == null) return false;

            CompanionModel existing = Find(def.Id);
            if (existing != null) return existing.CanLevelUp;

            return _roster.Count < MaxCompanions;
        }

        /// <summary>
        /// 招一个旅伴。已经拥有则升 1 级（GDD 7.3：重复购买 +1 级，上限 3 级），
        /// 否则占一个空槽位、按 CompanionDef.WeaponId 绑一把武器。
        /// 队伍已满且是新的旅伴时返回 null，且不改任何状态（调用方应当先用 CanRecruit 判断）。
        ///
        /// 这个方法不在每步模拟路径上（买一次调一次），所以这里 new 模型、加修正都是可以接受的。
        /// </summary>
        public CompanionModel Recruit(CompanionDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            CompanionModel existing = Find(def.Id);
            if (existing != null)
            {
                if (!existing.CanLevelUp) return null;
                existing.SetLevel(existing.Level + 1);
                return existing;
            }

            int slot = FindFreeSlot();
            if (slot < 0) return null;

            WeaponDef weapon = ResolveWeapon(def);

            // Shooter 用旅伴自己的 UnitId：这条武器系统打出去的伤害、发的受击事件都记在旅伴头上
            var weapons = new WeaponSystem(_enemies, _rng, UnitIdOf(slot), ProjectileCapacity, _events);
            weapons.AddWeapon(weapon);

            var model = new CompanionModel(def, weapon, slot, UnitIdOf(slot), FollowSpeed);

            // 出生就站在环绕位上：否则第一个模拟步会从原点滑过来，看起来像"刚被召唤出来"
            model.Position = RingTarget(_player.Position, slot, def.FollowRadius);
            model.PrevPosition = model.Position;

            _slots[slot] = model;
            _weaponSystems[slot] = weapons;
            _roster.Add(model);

            // 队伍变了：先让新旅伴吃到已有的羁绊，再看这一手有没有凑出新的羁绊
            RecomputeBonds();
            return model;
        }

        /// <summary>
        /// 失去一个旅伴（祭坛献祭、以后的卖出）。羁绊的数量掉到阈值以下时，属性就在这里回滚。
        /// 旅伴的等级修正与武器随模型一起丢掉，不需要单独退。
        /// </summary>
        public bool Remove(int slot)
        {
            CompanionModel model = Get(slot);
            if (model == null) return false;

            _slots[slot] = null;
            _weaponSystems[slot] = null;
            _roster.Remove(model);

            RecomputeBonds();
            return true;
        }

        /// <summary>
        /// 重算羁绊，返回当前生效的列表。队伍变化后由本类自己调用；测试与以后的"卖掉道具"也可以直接调。
        ///
        /// 顺序是"先整表回滚、再按当前数量重建"：数量掉到 1 件时如果只在旧修正上加新的，
        /// 属性会留下上一档的一半（GDD 的验收口径就是"无残留"）。回滚靠 RemoveBySource，
        /// Source 是一条羁绊一个实例，所以不会连带把等级修正或道具修正退掉。
        /// </summary>
        public IReadOnlyList<ActiveBond> RecomputeBonds()
        {
            RollbackBonds();
            _activeBonds.Clear();

            for (int i = 0; i < FactionCount; i++) _factionCounts[i] = 0;

            for (int i = 0; i < _roster.Count; i++)
            {
                int index = (int)_roster[i].Faction;
                if (index > 0 && index < FactionCount) _factionCounts[index]++;
            }

            for (int index = 1; index < FactionCount; index++)   // 0 是 None，不成羁绊
            {
                int count = _factionCounts[index];
                int tier = CompanionBonds.TierOf(count);
                if (tier <= 0) continue;

                var faction = (CompanionFaction)index;
                var source = new BondSource(faction);
                _bondSources[index] = source;

                // 全队 = 玩家 + 每个旅伴。同一份 Source 可以安全地挂在多份 StatBlock 上，
                // RemoveBySource 是逐份表调的，彼此不影响。
                CompanionBonds.Apply(_player.Stats, faction, tier, source);
                for (int i = 0; i < _roster.Count; i++)
                {
                    CompanionBonds.Apply(_roster[i].Stats, faction, tier, source);
                }

                _activeBonds.Add(new ActiveBond(source.Faction, count, tier));
            }

            return _activeBonds;
        }

        /// <summary>
        /// 换局清空。整局重开走场景重载，所以这条路径是给"同一个进程里重开"（测试与调试）用的：
        /// 队伍清空后必须把羁绊在玩家属性上留下的修正一起退掉，否则新的一局会带着上一局的加成开场。
        /// </summary>
        public void ResetForNewRun()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = null;
                _weaponSystems[i] = null;
            }

            _roster.Clear();
            RecomputeBonds();
        }

        /// <summary>
        /// 跟随移动。排在玩家移动之后、敌人移动之前（RunSimulation 第 2.5 步）：
        /// 旅伴追的是玩家本步的新位置，而敌人的追击与分离力完全不看旅伴，先后都不影响它们。
        /// 稳态不分配：只读模型、写模型，外加一个长度 ≤ 4 的两两松解。
        /// </summary>
        public void TickMovement(Vector2 playerPos, float dt)
        {
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                CompanionModel companion = _slots[slot];
                if (companion == null) continue;

                Vector2 target = RingTarget(playerPos, slot, companion.Def.FollowRadius);

                if ((playerPos - companion.Position).sqrMagnitude > TeleportDistance * TeleportDistance)
                {
                    // 超过 4 米就不跑了，直接回位：玩家冲刺 + 加速道具以后，靠跑是追不上的。
                    // 两边都写目标点：渲染层在 PrevPosition 与 Position 之间插值，留着旧位置
                    // 会把瞬移画成一条横穿战场的滑线
                    companion.Position = target;
                    companion.PrevPosition = target;
                    continue;
                }

                // 正常走：移动前记一笔，渲染层拿 PrevPosition 与 Position 插值
                companion.PrevPosition = companion.Position;
                companion.Position = Vector2.MoveTowards(companion.Position, target, companion.MoveSpeed * dt);
            }

            ResolveOverlap();
        }

        /// <summary>
        /// 开火。排在玩家开火之后（RunSimulation 第 5 步的末尾），前提是敌人网格已经 Rebuild 过：
        /// 旅伴的武器和玩家用的是同一个 EnemySystem 与同一份位置快照，不另开线程也不用协程。
        /// 稳态不分配：ShooterStats 是结构体，索敌结果复用 _targets。
        /// </summary>
        public void TickWeapons(Vector2 playerPos, float dt)
        {
            for (int slot = 0; slot < _slots.Length; slot++)
            {
                CompanionModel companion = _slots[slot];
                if (companion == null) continue;

                WeaponSystem weapons = _weaponSystems[slot];

                // 加成从旅伴自己的 StatBlock 现读：等级修正与羁绊都写在那里面，这里不再算一遍
                var stats = new ShooterStats(
                    companion.Stats[StatType.Attack],
                    companion.Stats[StatType.CritChance],
                    companion.Stats[StatType.CritDamage],
                    companion.Stats[StatType.AttackSpeed],
                    Mathf.RoundToInt(companion.Stats[StatType.ProjectileCount]));

                // 远程只在贴身重合时用朝向兜底，近战的扇形判定必须有朝向：一律朝射程内最近的敌人
                Vector2 facing = FacingTowardNearest(companion, playerPos);

                weapons.Tick(companion.Position, facing, stats, dt);
            }
        }

        Vector2 RingTarget(Vector2 playerPos, int slot, float followRadius)
        {
            // 90° 一格：4 个旅伴正好占满四个象限，任意两个相邻槽位都差 90°，间距下界由 MinRingRadius 保证。
            // 目标点不夹场地边界：夹了以后玩家贴着墙走时几位旅伴会被同一条边挤成一排，
            // 而旅伴没有物理碰撞（不撞墙也不撞敌人），追出去半个身位比在墙边重叠好。
            float angle = slot * (Mathf.PI * 0.5f);
            float radius = followRadius > MinRingRadius ? followRadius : MinRingRadius;
            return playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>
        /// 两两位置松解。环形位置已经保证了稳态不重叠，这一道管的是追赶途中与瞬移瞬间：
        /// 一个旅伴还在半路、另一个已经到位时，两个身体可能短暂插在一起。
        /// 每个重叠对沿连线各让一半，n ≤ 4 所以是固定 6 次比较，比再建一张空间网格便宜得多。
        /// </summary>
        void ResolveOverlap()
        {
            float minDistance = BodyRadius * 2f * SeparationMargin;
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
                        : new Vector2(Mathf.Cos((i * MaxCompanions + j) * GoldenAngle),
                            Mathf.Sin((i * MaxCompanions + j) * GoldenAngle));   // 完全重合才走这里

                    float push = (minDistance - distance) * 0.5f;   // 一人让一半
                    a.Position -= direction * push;
                    b.Position += direction * push;
                }
            }
        }

        /// <summary>
        /// 朝射程内最近的活目标。近战的扇形判定直接拿它当方向，远程只在目标与射手重合时兜底用。
        /// 扫不到目标时朝玩家：没有目标就没有可打的方向，这个朝向不会造成误伤。
        /// </summary>
        Vector2 FacingTowardNearest(CompanionModel companion, Vector2 playerPos)
        {
            float range = companion.Weapon.Range > MinAimRadius ? companion.Weapon.Range : MinAimRadius;

            _targets.Clear();
            _enemies.Grid.QueryCircle(companion.Position, range, _targets);

            int best = -1;
            float bestSqr = range * range;
            for (int i = 0; i < _targets.Count; i++)
            {
                int slot = _targets[i];
                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                float sqr = (enemy.Position - companion.Position).sqrMagnitude;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                best = slot;
            }

            if (best >= 0)
            {
                EnemyModel target = _enemies.Get(best);
                Vector2 toTarget = target.Position - companion.Position;
                if (toTarget.sqrMagnitude > 1e-8f) return toTarget.normalized;
            }

            Vector2 toPlayer = playerPos - companion.Position;
            return toPlayer.sqrMagnitude > 1e-8f ? toPlayer.normalized : Vector2.up;
        }

        void RollbackBonds()
        {
            for (int index = 0; index < _bondSources.Length; index++)
            {
                BondSource source = _bondSources[index];
                if (source == null) continue;

                _player.Stats.RemoveBySource(source);
                for (int i = 0; i < _roster.Count; i++) _roster[i].Stats.RemoveBySource(source);

                _bondSources[index] = null;
            }
        }

        int FindFreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null) return i;
            }
            return -1;
        }

        CompanionModel Find(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return null;

            for (int i = 0; i < _roster.Count; i++)
            {
                if (_roster[i].Def.Id == companionId) return _roster[i];
            }
            return null;
        }

        /// <summary>
        /// 按 CompanionDef.WeaponId 从武器表取一把。ConfigDatabase 加载时已经校验过引用，
        /// 走到这里取不到只可能是装配时给错了表，直接抛比"旅伴站在那里不开火"更容易发现。
        /// </summary>
        WeaponDef ResolveWeapon(CompanionDef def)
        {
            if (!string.IsNullOrEmpty(def.WeaponId) && _weaponTable.TryGetValue(def.WeaponId, out WeaponDef weapon))
            {
                return weapon;
            }

            throw new ArgumentException(
                "旅伴 " + def.Id + " 的 WeaponId '" + def.WeaponId + "' 在武器表里找不到", nameof(def));
        }
    }
}
