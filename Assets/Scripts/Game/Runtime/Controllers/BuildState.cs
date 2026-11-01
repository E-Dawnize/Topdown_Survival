using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems;

namespace Game.Controllers
{
    /// <summary>
    /// 构筑状态：玩家已经拿到的数值卡层数、buff 卡张数、构筑标签与已激活组合。
    ///
    /// 抽卡规则（DraftRoller）与卡面文案（DraftDescriber）都不写这里；武器与旅伴的等级
    /// 也归 WeaponSystem / CompanionSystem。本类只回答"同名卡拿了几层"以及"这些卡对玩家做了什么"，
    /// 所以规则、状态、文案三块能各读各的、各自单独测。
    ///
    /// 所有写入都走 ApplyStat / ApplyBuff / RecordTags + RecomputeCombos；Reset 会把写进
    /// StatBlock 的修正按同一个 OwnedStat 源全部退掉，状态与玩家属性不会各说各话。
    /// </summary>
    public sealed class BuildState
    {
        /// <summary>数值卡的一次持有。同一张卡反复拿只增加 Stacks，StatBlock 里的修正 Source 始终是它。</summary>
        sealed class OwnedStat
        {
            public readonly UpgradeDef Def;
            public int Stacks;

            public OwnedStat(UpgradeDef def)
            {
                Def = def;
            }
        }

        readonly PlayerModel _player;
        readonly IReadOnlyDictionary<string, ComboDef> _comboTable;
        readonly IReadOnlyDictionary<string, BuffDef> _buffTable;
        readonly BuffSystem _buffs;
        readonly IEventBus _events;

        readonly Dictionary<string, OwnedStat> _stats = new Dictionary<string, OwnedStat>(16);
        readonly Dictionary<string, int> _buffCards = new Dictionary<string, int>(16);
        readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>(16);
        readonly List<ComboDef> _activeCombos = new List<ComboDef>(8);

        public BuildState(PlayerModel player, IReadOnlyDictionary<string, ComboDef> comboTable,
            IReadOnlyDictionary<string, BuffDef> buffTable, BuffSystem buffs, IEventBus events)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _comboTable = comboTable ?? throw new ArgumentNullException(nameof(comboTable));
            _buffTable = buffTable ?? throw new ArgumentNullException(nameof(buffTable));
            _buffs = buffs ?? throw new ArgumentNullException(nameof(buffs));
            _events = events;
        }

        public int StatUpgradeCount => _stats.Count;
        public int ActiveComboCount => _activeCombos.Count;
        public IReadOnlyList<ComboDef> ActiveCombos => _activeCombos;

        /// <summary>同名数值卡现在拿了几层。HUD/卡面用，测试也读它。</summary>
        public int StatStacksOf(string upgradeId)
        {
            return !string.IsNullOrEmpty(upgradeId) && _stats.TryGetValue(upgradeId, out OwnedStat owned)
                ? owned.Stacks
                : 0;
        }

        /// <summary>同名 buff 卡现在拿了几张。</summary>
        public int BuffCardsOf(string upgradeId)
        {
            return !string.IsNullOrEmpty(upgradeId) && _buffCards.TryGetValue(upgradeId, out int count)
                ? count
                : 0;
        }

        /// <summary>
        /// 拿一层数值卡：第一次拿时新建 OwnedStat 作为 Source，之后每层都往同一份 StatBlock 加修正。
        /// 返回这张卡当前层数。
        /// </summary>
        public int ApplyStat(UpgradeDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            if (!_stats.TryGetValue(def.Id, out OwnedStat owned))
            {
                owned = new OwnedStat(def);
                _stats.Add(def.Id, owned);
            }

            owned.Stacks++;
            if (def.HasStatA)
            {
                _player.Stats.Add(new StatModifier(def.StatA, def.ValueA, ToModOp(def.OpA), owned));
            }
            if (def.HasStatB)
            {
                _player.Stats.Add(new StatModifier(def.StatB, def.ValueB, ToModOp(def.OpB), owned));
            }

            return owned.Stacks;
        }

        /// <summary>拿一张 buff 卡：先让 BuffSystem 生效，再记一张卡面计数。返回这张卡的张数。</summary>
        public int ApplyBuff(UpgradeDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrEmpty(def.BuffId)) return 0;
            if (!_buffTable.TryGetValue(def.BuffId, out BuffDef buffDef)) return 0;

            _buffs.Acquire(buffDef);
            _buffCards.TryGetValue(def.Id, out int count);
            _buffCards[def.Id] = count + 1;
            return count + 1;
        }

        /// <summary>把一张卡的标签记进构筑。组合只认累计数量，不认具体哪张卡。</summary>
        public void RecordTags(string[] tags)
        {
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                if (string.IsNullOrEmpty(tag)) continue;
                _tagCounts.TryGetValue(tag, out int count);
                _tagCounts[tag] = count + 1;
            }
        }

        /// <summary>
        /// 组合效果：标签累计后逐个检查未激活的组合，满足条件就通过 BuffSystem 获得组合 buff
        /// （不占构筑槽位）；每个组合一局只触发一次。
        /// </summary>
        public void RecomputeCombos()
        {
            foreach (ComboDef combo in _comboTable.Values)
            {
                if (ContainsCombo(combo.Id)) continue;
                if (!MeetsCombo(combo)) continue;
                if (!_buffTable.TryGetValue(combo.BuffId, out BuffDef buff)) continue;

                _buffs.Acquire(buff);
                _activeCombos.Add(combo);
                _events?.Publish(new ComboActivated(combo.Id, combo.Name));
            }
        }

        /// <summary>换局清空：先按 Source 退掉数值修正，再清卡片计数、标签与组合。</summary>
        public void Reset()
        {
            foreach (OwnedStat owned in _stats.Values)
            {
                _player.Stats.RemoveBySource(owned);
            }

            _stats.Clear();
            _buffCards.Clear();
            _tagCounts.Clear();
            _activeCombos.Clear();
        }

        bool ContainsCombo(string comboId)
        {
            for (int i = 0; i < _activeCombos.Count; i++)
            {
                if (_activeCombos[i].Id == comboId) return true;
            }
            return false;
        }

        bool MeetsCombo(ComboDef combo)
        {
            if (!TagCountAtLeast(combo.TagA, combo.CountA)) return false;
            if (!string.IsNullOrEmpty(combo.TagB) && !TagCountAtLeast(combo.TagB, combo.CountB)) return false;
            return true;
        }

        bool TagCountAtLeast(string tag, int count)
        {
            if (count <= 0) return true;
            return !string.IsNullOrEmpty(tag) && _tagCounts.TryGetValue(tag, out int value) && value >= count;
        }

        static ModOp ToModOp(StatOp op)
        {
            switch (op)
            {
                case StatOp.Percent: return ModOp.Percent;
                case StatOp.Multiply: return ModOp.Multiply;
                default: return ModOp.Flat;
            }
        }
    }
}
