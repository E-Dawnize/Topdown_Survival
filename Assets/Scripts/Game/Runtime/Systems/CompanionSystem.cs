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
    /// 旅伴门面：花名册 + 阵营羁绊 + 招募/离队，并把每步的移动与开火转给两个内部部件：
    ///   - <see cref="CompanionMovement"/>：跟随/接敌移动、瞬移回位、两两分离；
    ///   - <see cref="CompanionArmory"/>：每个槽位一套 WeaponSystem 的装配与开火。
    ///
    /// 花名册与羁绊留在一起，是因为羁绊的唯一输入就是这支队伍：招进 / 失去一个旅伴的同一步里必须重算羁绊，
    /// 拆成两个类以后"谁负责在队伍变化后重算"就没有答案了。Recruit / Remove 是唯一的重算触发点，
    /// 外部不需要（也不应该）在改队伍后自己补一次 RecomputeBonds。
    ///
    /// 行为规则（GDD 8）见 <see cref="CompanionMovement"/>；武器归属见 <see cref="CompanionArmory"/>。
    /// 主循环顺序：TickMovement 在玩家移动之后、敌人移动之前；TickWeapons 在玩家开火之后、清理敌人之前。
    /// </summary>
    public sealed class CompanionSystem
    {
        /// <summary>
        /// GDD 7：一局最多同时拥有 4 个。它与羁绊二级阈值（4 件）当前数值相同，但那是两条独立规则：
        /// 队伍上限改这里，羁绊阈值在 CompanionBonds.SecondTierCount，两边不互相绑定。
        /// </summary>
        public const int MaxCompanions = 4;

        /// <summary>
        /// 旅伴默认 ID 起点。正式装配由 AppRoot 传入与 EnemySystem 共用的 UnitIdAllocator，
        /// 这里只给单独构造 CompanionSystem 的测试一个不与敌人（从 2 开始）撞号的起点。
        /// 旅伴不用"基址 + 槽位"编码，槽位复用不会复用 UnitId。
        /// </summary>
        public const int UnitIdBase = 1000;

        /// <summary>离玩家超过这个距离就瞬移回环绕位（ITERATION_PLAN I2-6 的验收：超过 4 米瞬移回位）。</summary>
        public const float TeleportDistance = 4f;

        /// <summary>
        /// 旅伴的身体半径。Companion 表没有这个字段，取与敌人占位身体同量级的 0.35：
        /// 验收口径"4 个旅伴的最小间距大于身体直径"按 0.7 米算。
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

        /// <summary>
        /// 跟随阻尼（SmoothDamp 的 smoothTime，秒）。0.22 在 5 米/秒时约落后 1 米，
        /// 起步/停下能看出拖尾，但不会掉出 4 米瞬移阈值。
        /// </summary>
        public const float FollowSmoothTime = 0.22f;

        /// <summary>
        /// Follow 模式的目标点相对玩家往速度反方向偏的秒数。玩家匀速跑时整队会略微拖在后面，
        /// 不是四个点焊死在玩家身上；只影响 Follow，Aggro/Kiter/Guard 的接敌仍然即时。
        /// </summary>
        public const float FollowTrailTime = 0.10f;

        /// <summary>每个旅伴自己的投射物池容量。旅伴的弹道不共用玩家的池，池爆掉不会互相拖累。</summary>
        public const int ProjectileCapacity = 256;

        /// <summary>阵营枚举的成员数。0 是 None（不算羁绊），其余下标与 CompanionFaction 一一对应。</summary>
        static readonly int FactionCount = Enum.GetValues(typeof(CompanionFaction)).Length;

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
        readonly IReadOnlyDictionary<string, WeaponDef> _weaponTable;

        /// <summary>稳定实体 ID 分配器。正式装配与 EnemySystem 共用同一个实例。</summary>
        readonly UnitIdAllocator _ids;

        // 槽位与队伍两条索引：槽位号是固定的（决定环绕角度与武器绑定），_roster 只放还在队里的那几个，
        // 重算羁绊与按购买顺序遍历都用它，省掉每步扫空槽位。UnitId 按招募顺序分配，不跟槽位绑定。
        readonly CompanionModel[] _slots = new CompanionModel[MaxCompanions];
        readonly List<CompanionModel> _roster = new List<CompanionModel>(MaxCompanions);

        // 羁绊状态。_bondSources 按阵营下标存当前生效的源对象，重算时按它逐条回滚。
        readonly BondSource[] _bondSources = new BondSource[FactionCount];
        readonly int[] _factionCounts = new int[FactionCount];
        readonly List<ActiveBond> _activeBonds = new List<ActiveBond>(FactionCount);

        readonly CompanionMovement _movement;
        readonly CompanionArmory _armory;

        public CompanionSystem(PlayerModel player, EnemySystem enemies, IReadOnlyDictionary<string, WeaponDef> weapons,
            IRandom01 rng, IEventBus events = null, UnitIdAllocator ids = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _weaponTable = weapons ?? throw new ArgumentNullException(nameof(weapons));
            _ids = ids ?? new UnitIdAllocator(UnitIdBase);

            _movement = new CompanionMovement(_slots, _player, _enemies);
            _armory = new CompanionArmory(_slots, _enemies, rng, events);
        }

        /// <summary>
        /// 兼容旧装配点的重载。旅伴在本阶段没有武器成长：升级走 CompanionModel 的属性修正，
        /// WeaponModel 永远停在 1 级，因此 WeaponUpgradeRules 对旅伴武器的数值是恒等变换、没有任何可观察效果。
        /// 这份规则被显式忽略（不悄悄给旅伴加武器成长），保留参数只为不改动 AppRoot 等既有调用点；
        /// 以后真要做旅伴武器升级时，应该在这里接上，而不是让调用方以为已经生效。
        /// </summary>
        public CompanionSystem(PlayerModel player, EnemySystem enemies, IReadOnlyDictionary<string, WeaponDef> weapons,
            IRandom01 rng, IEventBus events, UnitIdAllocator ids, WeaponUpgradeRules weaponRules)
            : this(player, enemies, weapons, rng, events, ids)
        {
        }

        /// <summary>按槽位号从小到大排列的槽位数组长度（= 队伍上限）。视图层同步时可以拿它当上界。</summary>
        public int SlotCapacity => _slots.Length;

        public int Count => _roster.Count;

        /// <summary>还在队伍里的旅伴，按购买顺序。羁绊与开火顺序都走它，结果与购买顺序无关的那部分另行排序。</summary>
        public IReadOnlyList<CompanionModel> Companions => _roster;

        /// <summary>当前生效的羁绊，按 CompanionFaction 的枚举顺序。空列表表示一条都没触发。</summary>
        public IReadOnlyList<ActiveBond> ActiveBonds => _activeBonds;

        /// <summary>当前槽位对应的稳定 UnitId。槽位空时返回 None。</summary>
        public UnitId UnitIdOf(int slot)
        {
            CompanionModel companion = Get(slot);
            return companion != null ? companion.UnitId : UnitId.None;
        }

        /// <summary>按稳定 ID 找回旅伴槽位。不是旅伴或已经离队返回 -1。</summary>
        public int SlotOf(UnitId id)
        {
            if (!id.IsValid) return -1;

            for (int slot = 0; slot < _slots.Length; slot++)
            {
                CompanionModel companion = _slots[slot];
                if (companion != null && companion.UnitId == id) return slot;
            }

            return -1;
        }

        public CompanionModel Get(int slot)
        {
            return slot >= 0 && slot < _slots.Length ? _slots[slot] : null;
        }

        /// <summary>正拿着这把武器的那套武器系统。每个旅伴一套，Shooter 是旅伴自己的 UnitId。</summary>
        public WeaponSystem WeaponsOf(int slot) => _armory.WeaponsOf(slot);

        /// <summary>队伍里某个旅伴的等级，没有这个旅伴返回 0。</summary>
        public int LevelOf(string companionId)
        {
            CompanionModel existing = Find(companionId);
            return existing == null ? 0 : existing.Level;
        }

        /// <summary>
        /// 现在能不能再招这个旅伴：已有的看等级（满级买不了），没有的看队伍有没有满。
        /// 构筑抽卡的 IsEligible 与生效路径共用这一条判据，界面上"看着能拿、按下去没反应"就不会出现。
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
            UnitId unitId = _ids.Allocate(UnitKind.Companion);

            // Shooter 用旅伴自己的 UnitId：这条武器系统打出去的伤害、发的受击事件都记在旅伴头上
            _armory.Equip(slot, weapon, unitId);

            var model = new CompanionModel(def, weapon, slot, unitId, FollowSpeed);

            // 出生就站在环绕位上：否则第一个模拟步会从原点滑过来，看起来像"刚被召唤出来"
            _movement.PlaceOnRing(model, _player.Position);

            _slots[slot] = model;
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
            _armory.Unequip(slot);
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
            for (int i = 0; i < _slots.Length; i++) _slots[i] = null;

            _armory.Clear();
            _roster.Clear();
            RecomputeBonds();
        }

        /// <summary>
        /// 跟随移动。排在玩家移动之后、敌人移动之前（RunSimulation 第 2.5 步）：
        /// 旅伴追的是玩家本步的新位置，而敌人的追击与分离力完全不看旅伴，先后都不影响它们。
        /// 稳态不分配：只读模型、写模型，外加一个长度 ≤ 4 的两两松解。
        /// </summary>
        public void TickMovement(Vector2 playerPos, float dt) => _movement.Tick(playerPos, dt);

        /// <summary>
        /// 开火。排在玩家开火之后（RunSimulation 第 5 步的末尾），前提是敌人网格已经 Rebuild 过：
        /// 旅伴的武器和玩家用的是同一个 EnemySystem 与同一份位置快照，不另开线程也不用协程。
        /// 稳态不分配：ShooterStats 是结构体，索敌结果由武器部件复用。
        /// </summary>
        public void TickWeapons(Vector2 playerPos, float dt) => _armory.Tick(playerPos, dt);

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
