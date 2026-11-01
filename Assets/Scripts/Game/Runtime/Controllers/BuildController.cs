using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;

namespace Game.Controllers
{
    /// <summary>
    /// 升级构筑的编排入口。一局里唯一能给玩家"变强"的地方：
    ///   武器  —— 拿新武器或升已有武器（最多 WeaponSlots 把，每把 WeaponMaxLevel 级）
    ///   旅伴  —— 招募或升级（最多 4 个，每个 3 级，阵营羁绊沿用 CompanionSystem）
    ///   数值  —— 写进 StatBlock 的永久属性（按 UpgradeDef.MaxStacks 叠层）
    ///   buff  —— 带触发条件的道具（击杀叠攻速、受击叠甲、低血加攻、周期回复……）
    ///
    /// 本类只做编排：什么时候开一轮抽卡、选中哪张卡生效、重抽与换局重置。
    ///   - 候选过滤/权重/不放回抽卡归 DraftRoller；
    ///   - 数值层数/buff 卡/标签/组合的持有与重置归 BuildState；
    ///   - 卡面文案归 DraftDescriber。
    /// RunController 决定什么时候 OpenDraft，UpgradeView 只读 Options 并把选择回传 TryChoose。
    /// </summary>
    public sealed class BuildController
    {
        /// <summary>一次升级最多展示几张卡。UpgradeView 的卡片容量也是 4，两处必须一致。</summary>
        public const int MaxDraftOptions = 4;

        /// <summary>一张升级卡的只读展示数据。UI 不直接读 Def，避免把"这张卡现在几级"的规则复制到视图里。</summary>
        public readonly struct DraftOption
        {
            public readonly UpgradeDef Def;
            public readonly string Title;
            public readonly string Detail;
            public readonly string RarityLabel;
            public readonly bool IsNew;
            public readonly int CurrentLevel;
            public readonly int NextLevel;

            public DraftOption(UpgradeDef def, string title, string detail, string rarityLabel, bool isNew,
                int currentLevel, int nextLevel)
            {
                Def = def;
                Title = title;
                Detail = detail;
                RarityLabel = rarityLabel;
                IsNew = isNew;
                CurrentLevel = currentLevel;
                NextLevel = nextLevel;
            }
        }

        readonly PlayerModel _player;
        readonly CompanionSystem _companions;
        readonly WeaponSystem _weapons;
        readonly BuffSystem _buffs;
        readonly IReadOnlyDictionary<string, WeaponDef> _weaponTable;
        readonly IReadOnlyDictionary<string, CompanionDef> _companionTable;
        readonly BuildState _state;
        readonly DraftRoller _roller;
        readonly DraftDescriber _describer;
        readonly IEventBus _events;
        readonly WeaponDef _startingWeapon;
        readonly int _weaponSlots;
        readonly int _rerollsPerLevel;

        readonly List<DraftOption> _options = new List<DraftOption>(MaxDraftOptions);
        readonly List<UpgradeDef> _rolled = new List<UpgradeDef>(MaxDraftOptions);

        int _currentLevel;
        int _rerollsLeft;
        int _loadoutRevision;

        public BuildController(PlayerModel player, RunState run, CompanionSystem companions, WeaponSystem weapons,
            BuffSystem buffs, ConfigDatabase config, IEventBus events, IRandom01 random, WeaponDef startingWeapon)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _companions = companions ?? throw new ArgumentNullException(nameof(companions));
            _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
            _buffs = buffs ?? throw new ArgumentNullException(nameof(buffs));
            _events = events;
            _startingWeapon = startingWeapon;

            if (run == null) throw new ArgumentNullException(nameof(run));
            if (config == null) throw new ArgumentNullException(nameof(config));
            _weaponTable = config.Weapons;
            _companionTable = config.Companions;

            _weaponSlots = Math.Max(1, config.GetBalanceInt("WeaponSlots", 4));
            _rerollsPerLevel = Math.Max(0, config.GetBalanceInt("RerollsPerLevel", 1));

            _state = new BuildState(player, config.Combos, config.Buffs, buffs, events);
            _roller = new DraftRoller(player, run, companions, weapons, _state, config, random);
            _describer = new DraftDescriber(config.Weapons, config.Companions, config.Buffs, companions, weapons,
                _state);
        }

        public IReadOnlyList<DraftOption> Options => _options;
        public int CurrentLevel => _currentLevel;
        public int RerollsLeft => _rerollsLeft;
        public int WeaponCount => _weapons.Count;
        public int CompanionCount => _companions.Count;
        public int BuffCardCount => _buffs.Count;
        public int WeaponSlots => _weaponSlots;
        public int StatUpgradeCount => _state.StatUpgradeCount;
        public int ActiveComboCount => _state.ActiveComboCount;
        public IReadOnlyList<ComboDef> ActiveCombos => _state.ActiveCombos;

        /// <summary>
        /// 当前持有的武器与旅伴。HUD 的装备栏直接遍历它，不另外在视图层再存一份名单；
        /// 等级变化由 LoadoutRevision 驱动，视图不需要每帧拼字符串。
        /// </summary>
        public IReadOnlyList<WeaponModel> Weapons => _weapons.Weapons;

        public IReadOnlyList<CompanionModel> Companions => _companions.Companions;

        /// <summary>已获得的 buff/机制道具。HUD 的装备栏把名字列出来，玩家才看得见自己为什么有范围伤害。</summary>
        public IReadOnlyList<BuffRuntime> Buffs => _buffs.Buffs;

        /// <summary>
        /// 构筑内容每变一次 +1（拿卡、升级、换局）。HUD 用它判断"装备栏文字要不要重拼"；
        /// 只比数量的话，武器升级、旅伴升级、组合触发都不会刷新。
        /// </summary>
        public int LoadoutRevision => _loadoutRevision;

        /// <summary>构筑持有状态。抽卡与卡面规则之外的层数/组合测试可以直接读它。</summary>
        public BuildState State => _state;

        /// <summary>
        /// 换局清空。武器回到角色的起始武器，数值修正按 OwnedStat 退干净，buff 全部退掉，
        /// 旅伴与羁绊也在这里一起清空——换局状态的归属只有本方法一处。
        /// 正式重开是场景重载；这条路径给同一进程里的调试重开与测试兜底。
        /// </summary>
        public void ResetForNewRun()
        {
            _state.Reset();
            _buffs.ResetForNewRun();
            _companions.ResetForNewRun();

            _weapons.ResetForNewRun();
            if (_startingWeapon != null) _weapons.AddWeapon(_startingWeapon);

            _options.Clear();
            _rolled.Clear();
            _currentLevel = 1;
            _rerollsLeft = 0;
            _loadoutRevision++;
        }

        /// <summary>开一轮升级抽卡。由 RunController 在进入 LevelUp 阶段时调用。</summary>
        public void OpenDraft(int level)
        {
            _currentLevel = level;
            _rerollsLeft = _rerollsPerLevel;
            RollOptions();
        }

        /// <summary>
        /// 重抽。每次升级有 RerollsPerLevel 次免费重抽，用完返回 false。
        /// 成功后发 UpgradeOptionsChanged，UI 只刷新卡片，不重播面板打开动画。
        /// </summary>
        public bool TryReroll()
        {
            if (_rerollsLeft <= 0) return false;

            _rerollsLeft--;
            RollOptions();
            _events?.Publish(new UpgradeOptionsChanged(_currentLevel));
            return true;
        }

        /// <summary>
        /// 选中一张卡。返回 false 表示下标无效或这一轮已经关掉；调用方不用先判 Options.Count。
        /// 生效后就清空选项，同一轮不能选第二张。
        /// </summary>
        public bool TryChoose(int index)
        {
            if (index < 0 || index >= _options.Count) return false;

            UpgradeDef def = _options[index].Def;

            switch (def.Category)
            {
                case UpgradeCategory.Weapon:
                    ApplyWeapon(def);
                    break;
                case UpgradeCategory.Companion:
                    ApplyCompanion(def);
                    break;
                case UpgradeCategory.Buff:
                    _state.ApplyBuff(def);
                    break;
                default:
                    _state.ApplyStat(def);
                    break;
            }

            _state.RecordTags(def.Tags);
            _state.RecomputeCombos();

            // 任何一张卡都可能改变装备/数值/buff/组合，HUD 的装备栏统一按这一版号重拼。
            _loadoutRevision++;

            _options.Clear();
            _rolled.Clear();
            if (_player.HP > _player.MaxHP) _player.HP = _player.MaxHP;
            return true;
        }

        /// <summary>同名数值卡现在拿了几层。HUD/卡面用，测试也读它。</summary>
        public int StatStacksOf(string upgradeId) => _state.StatStacksOf(upgradeId);

        /// <summary>同名 buff 卡现在拿了几张。</summary>
        public int BuffCardsOf(string upgradeId) => _state.BuffCardsOf(upgradeId);

        public int WeaponLevelOf(string weaponId)
        {
            WeaponModel weapon = _weapons.Find(weaponId);
            return weapon?.Level ?? 0;
        }

        void RollOptions()
        {
            _options.Clear();
            _roller.Roll(_currentLevel, _rolled);
            for (int i = 0; i < _rolled.Count; i++)
            {
                _options.Add(_describer.Describe(_rolled[i]));
            }
        }

        int ApplyWeapon(UpgradeDef def)
        {
            if (!_weaponTable.TryGetValue(def.WeaponId, out WeaponDef weaponDef)) return 0;

            WeaponModel owned = _weapons.Find(def.WeaponId);
            if (owned != null)
            {
                owned.UpgradeLevel();
                return owned.Level;
            }

            WeaponModel added = _weapons.AddWeapon(weaponDef);
            return added.Level;
        }

        int ApplyCompanion(UpgradeDef def)
        {
            if (!_companionTable.TryGetValue(def.CompanionId, out CompanionDef companionDef)) return 0;

            _companions.Recruit(companionDef);
            return _companions.LevelOf(def.CompanionId);
        }
    }
}
