using System;
using System.Collections.Generic;
using Game.Config;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;

namespace Game.Controllers
{
    /// <summary>
    /// 升级卡面文案：标题、说明、稀有度、当前/下一级。
    ///
    /// 文案里的成长数字必须来自真正执行升级的那份规则：
    ///   武器 —— WeaponUpgradeRules（WeaponModel 开火用的是同一份）；
    ///   旅伴 —— CompanionModel 的每级伤害修正（用模型自己的 SetLevel 量一次，见 CompanionDamagePerLevel）。
    /// 这样卡面不会和实际生效各写一份常量，也不会出现"卡上写 +40%、模型改成了 +50%"的漂移。
    /// </summary>
    public sealed class DraftDescriber
    {
        readonly IReadOnlyDictionary<string, WeaponDef> _weaponTable;
        readonly IReadOnlyDictionary<string, CompanionDef> _companionTable;
        readonly IReadOnlyDictionary<string, BuffDef> _buffTable;
        readonly CompanionSystem _companions;
        readonly WeaponSystem _weapons;
        readonly BuildState _state;

        public DraftDescriber(IReadOnlyDictionary<string, WeaponDef> weaponTable,
            IReadOnlyDictionary<string, CompanionDef> companionTable,
            IReadOnlyDictionary<string, BuffDef> buffTable, CompanionSystem companions,
            WeaponSystem weapons, BuildState state)
        {
            _weaponTable = weaponTable ?? throw new ArgumentNullException(nameof(weaponTable));
            _companionTable = companionTable ?? throw new ArgumentNullException(nameof(companionTable));
            _buffTable = buffTable ?? throw new ArgumentNullException(nameof(buffTable));
            _companions = companions ?? throw new ArgumentNullException(nameof(companions));
            _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>一张升级卡的只读展示数据。UI 不直接读 Def，避免把"这张卡现在几级"的规则复制到视图里。</summary>
        public BuildController.DraftOption Describe(UpgradeDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            string rarity = RarityLabel(def.Rarity);

            switch (def.Category)
            {
                case UpgradeCategory.Weapon:
                {
                    int current = _weapons.Find(def.WeaponId)?.Level ?? 0;
                    int next = current <= 0 ? 1 : current + 1;
                    string detail = WeaponDetail(def, current);
                    return new BuildController.DraftOption(def, DisplayName(def), detail, rarity, current <= 0,
                        current, next);
                }

                case UpgradeCategory.Companion:
                {
                    int current = _companions.LevelOf(def.CompanionId);
                    int next = current <= 0 ? 1 : current + 1;
                    string detail = CompanionDetail(def, current);
                    return new BuildController.DraftOption(def, DisplayName(def), detail, rarity, current <= 0,
                        current, next);
                }

                case UpgradeCategory.Buff:
                {
                    int count = _state.BuffCardsOf(def.Id);
                    return new BuildController.DraftOption(def, DisplayName(def), def.Description, rarity,
                        count == 0, count, count + 1);
                }

                default:
                {
                    int stacks = _state.StatStacksOf(def.Id);
                    return new BuildController.DraftOption(def, DisplayName(def), def.Description, rarity,
                        stacks == 0, stacks, stacks + 1);
                }
            }
        }

        string DisplayName(UpgradeDef def)
        {
            switch (def.Category)
            {
                case UpgradeCategory.Weapon:
                    return _weaponTable.TryGetValue(def.WeaponId, out WeaponDef weapon) ? weapon.Name : def.Name;
                case UpgradeCategory.Companion:
                    return _companionTable.TryGetValue(def.CompanionId, out CompanionDef companion)
                        ? companion.Name
                        : def.Name;
                case UpgradeCategory.Buff:
                    return _buffTable.TryGetValue(def.BuffId, out BuffDef buff) ? buff.Name : def.Name;
                default:
                    return def.Name;
            }
        }

        string WeaponDetail(UpgradeDef def, int currentLevel)
        {
            string baseText = def.Description;
            if (!_weaponTable.TryGetValue(def.WeaponId, out WeaponDef weapon)) return baseText;

            if (currentLevel <= 0)
            {
                return baseText + "　·　" + (weapon.Kind == WeaponKind.Ranged ? "远程" : "近战") +
                       "　伤害 " + weapon.BaseDamage.ToString("0.#") +
                       "　间隔 " + weapon.Cooldown.ToString("0.##") + "s";
            }

            WeaponUpgradeRules rules = _weapons.Rules;
            int next = currentLevel + 1;
            string text = baseText + "　·　Lv" + currentLevel + " → Lv" + next +
                          "　伤害 +" + (rules.DamagePerLevel * 100f).ToString("0") + "%";

            // ProjectileCount / Pierce 只被 RangedBehaviour 消费。
            // 近战也显示“弹道 +1 / 穿透 +1”会让玩家以为近战升级有额外弹道，
            // 实际 MeleeBehaviour 只做一次扇形判定。卡面必须和真正生效的规则一致。
            if (weapon.Kind == WeaponKind.Ranged)
            {
                if (next == rules.ProjectileMilestone) text += "　弹道 +1";
                if (next == rules.PierceMilestone) text += "　穿透 +1";
            }

            return text;
        }

        string CompanionDetail(UpgradeDef def, int currentLevel)
        {
            if (!_companionTable.TryGetValue(def.CompanionId, out CompanionDef companion)) return def.Description;

            string faction = FactionLabel(companion.Faction);
            string ai = AILabel(companion.AI);
            if (currentLevel <= 0) return def.Description + "　·　" + faction + "阵营　·　" + ai;
            return def.Description + "　·　Lv" + currentLevel + " → Lv" + (currentLevel + 1) +
                   "　伤害 +" + (CompanionModel.DamagePerLevel * 100f).ToString("0") + "%　·　" + ai;
        }

        static string RarityLabel(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Rare: return "蓝";
                case Rarity.Epic: return "紫";
                case Rarity.Legendary: return "金";
                default: return "白";
            }
        }

        static string AILabel(CompanionAI ai)
        {
            switch (ai)
            {
                case CompanionAI.Aggro: return "主动接敌";
                case CompanionAI.Kiter: return "保持距离";
                case CompanionAI.Guard: return "护卫";
                default: return "跟随";
            }
        }

        static string FactionLabel(CompanionFaction faction)
        {
            switch (faction)
            {
                case CompanionFaction.Machine: return "机械";
                case CompanionFaction.Ghost: return "幽灵";
                case CompanionFaction.Beast: return "野兽";
                case CompanionFaction.Doll: return "玩偶";
                default: return "无";
            }
        }
    }
}
