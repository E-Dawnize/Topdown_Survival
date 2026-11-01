using System;
using System.Collections.Generic;
using Game.Config;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;

namespace Game.Controllers
{
    /// <summary>
    /// 升级抽卡规则：过滤掉已经拿满/没有槽位的卡，再按"卡权重 × 类别权重 × 品质倍率"
    /// 不放回地抽 N 张。这里只产出 UpgradeDef，不写卡面文案，也不改任何系统状态。
    ///
    /// 稀有度随等级上浮，幸运属性也参与上浮：越到后期越容易看到紫/金，但前期不会被金卡定死。
    /// 卡池不够时少给几张，不放回重复卡——给一张已经满层的卡比少一张更没有意义。
    /// </summary>
    public sealed class DraftRoller
    {
        readonly PlayerModel _player;
        readonly RunState _run;
        readonly CompanionSystem _companions;
        readonly WeaponSystem _weapons;
        readonly BuildState _state;
        readonly IRandom01 _random;

        readonly IReadOnlyList<UpgradeDef> _upgradePool;
        readonly IReadOnlyDictionary<string, WeaponDef> _weaponTable;
        readonly IReadOnlyDictionary<string, CompanionDef> _companionTable;
        readonly IReadOnlyDictionary<string, BuffDef> _buffTable;

        readonly int _weaponSlots;
        readonly float _luckRarityWeight;
        readonly float _weightWeapon;
        readonly float _weightCompanion;
        readonly float _weightStat;
        readonly float _weightBuff;

        readonly List<UpgradeDef> _eligible = new List<UpgradeDef>(64);

        public DraftRoller(PlayerModel player, RunState run, CompanionSystem companions, WeaponSystem weapons,
            BuildState state, ConfigDatabase config, IRandom01 random)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _companions = companions ?? throw new ArgumentNullException(nameof(companions));
            _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            if (config == null) throw new ArgumentNullException(nameof(config));

            _upgradePool = SortedUpgrades(config.Upgrades);
            _weaponTable = config.Weapons;
            _companionTable = config.Companions;
            _buffTable = config.Buffs;

            _weaponSlots = Math.Max(1, config.GetBalanceInt("WeaponSlots", 4));
            _luckRarityWeight = config.GetBalance("LuckRarityWeight", 0.01f);
            _weightWeapon = config.GetBalance("BuildWeightWeapon", 30f);
            _weightCompanion = config.GetBalance("BuildWeightCompanion", 18f);
            _weightStat = config.GetBalance("BuildWeightStat", 38f);
            _weightBuff = config.GetBalance("BuildWeightBuff", 14f);
        }

        /// <summary>
        /// 抽这一轮的候选卡。into 会被清空；每张卡在一轮里最多出现一次。
        /// 数量 = min(等级曲线的 OfferCount, 可抽卡数, MaxDraftOptions)。
        /// </summary>
        public void Roll(int level, List<UpgradeDef> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();

            _eligible.Clear();
            for (int i = 0; i < _upgradePool.Count; i++)
            {
                UpgradeDef def = _upgradePool[i];
                if (IsEligible(def)) _eligible.Add(def);
            }

            int offerCount = Math.Min(Math.Min(_run.Curve.OfferCount(level), _eligible.Count),
                BuildController.MaxDraftOptions);
            float bonus = RarityBonus(level);

            for (int pick = 0; pick < offerCount; pick++)
            {
                int chosen = PickWeighted(bonus);
                if (chosen < 0) break;

                into.Add(_eligible[chosen]);
                _eligible.RemoveAt(chosen);
            }
        }

        bool IsEligible(UpgradeDef def)
        {
            switch (def.Category)
            {
                case UpgradeCategory.Weapon:
                {
                    if (string.IsNullOrEmpty(def.WeaponId) || !_weaponTable.ContainsKey(def.WeaponId)) return false;
                    WeaponModel owned = _weapons.Find(def.WeaponId);
                    return owned != null ? owned.CanLevelUp : _weapons.Count < _weaponSlots;
                }

                case UpgradeCategory.Companion:
                {
                    if (string.IsNullOrEmpty(def.CompanionId) || !_companionTable.ContainsKey(def.CompanionId))
                    {
                        return false;
                    }

                    int level = _companions.LevelOf(def.CompanionId);
                    return level > 0
                        ? level < CompanionModel.MaxLevel
                        : _companions.Count < CompanionSystem.MaxCompanions;
                }

                case UpgradeCategory.Buff:
                    return !string.IsNullOrEmpty(def.BuffId)
                           && _buffTable.ContainsKey(def.BuffId)
                           && _state.BuffCardsOf(def.Id) < Math.Max(1, def.MaxStacks);

                default:
                    return _state.StatStacksOf(def.Id) < Math.Max(1, def.MaxStacks);
            }
        }

        /// <summary>
        /// 权重 = 卡权重 × 类别权重 × 品质倍率。品质倍率的相对关系：
        /// 白 1、蓝 0.6、紫 0.2×(1+2×bonus)、金 0.04×(1+4×bonus)。
        /// </summary>
        int PickWeighted(float bonus)
        {
            if (_eligible.Count == 0) return -1;

            float total = 0f;
            for (int i = 0; i < _eligible.Count; i++)
            {
                total += WeightOf(_eligible[i], bonus);
            }

            if (total <= 0f) return 0;

            float roll = (float)_random.Next01() * total;
            float cursor = 0f;
            for (int i = 0; i < _eligible.Count; i++)
            {
                cursor += WeightOf(_eligible[i], bonus);
                if (roll < cursor) return i;
            }

            return _eligible.Count - 1;   // 浮点边界：把最后一点概率算给最后一张
        }

        float WeightOf(UpgradeDef def, float bonus)
        {
            float category = CategoryWeight(def.Category);
            float rarity = RarityWeight(def.Rarity, bonus);
            return Math.Max(0.01f, def.Weight) * category * rarity;
        }

        float CategoryWeight(UpgradeCategory category)
        {
            switch (category)
            {
                case UpgradeCategory.Weapon: return Math.Max(0f, _weightWeapon);
                case UpgradeCategory.Companion: return Math.Max(0f, _weightCompanion);
                case UpgradeCategory.Buff: return Math.Max(0f, _weightBuff);
                default: return Math.Max(0f, _weightStat);
            }
        }

        static float RarityWeight(Rarity rarity, float bonus)
        {
            switch (rarity)
            {
                case Rarity.Rare: return 0.60f;
                case Rarity.Epic: return 0.20f * (1f + 2f * bonus);
                case Rarity.Legendary: return 0.04f * (1f + 4f * bonus);
                default: return 1f;
            }
        }

        float RarityBonus(int level)
        {
            float luck = _player.Stats[StatType.Luck] * _luckRarityWeight;
            return Math.Max(0f, _run.Curve.RarityBonus(level) + luck);
        }

        static IReadOnlyList<UpgradeDef> SortedUpgrades(IReadOnlyDictionary<string, UpgradeDef> upgrades)
        {
            var list = new List<UpgradeDef>(upgrades.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }
    }
}
