using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Controllers
{
    /// <summary>
    /// 商店里的一格候选。Item 与 Companion 至多有一个非空，两个都为空表示这一格没有东西可买（已买走，或道具表填不满）。
    /// 买的是什么只看格子里装的是哪种 Def：道具走道具那条路（StatBlock 修正 + 叠层），旅伴走进队伍。
    /// 不在商店里写任何旅伴 Id——加一个旅伴只改配置，不改代码。
    /// </summary>
    public readonly struct ShopSlot
    {
        public readonly ItemDef Item;

        /// <summary>旅伴货。非空时这一格买的是旅伴（重复购买即升级），Item 一定是 null。</summary>
        public readonly CompanionDef Companion;

        /// <summary>按开这一间商店时的波次算好的价格。翻牌那一刻就定死，之后波次不再变它。</summary>
        public readonly int Price;

        public ShopSlot(ItemDef item, int price)
        {
            Item = item;
            Companion = null;
            Price = price;
        }

        public ShopSlot(CompanionDef companion, int price)
        {
            Item = null;
            Companion = companion;
            Price = price;
        }

        public bool IsEmpty => Item == null && Companion == null;
    }

    /// <summary>
    /// 买到的一件道具实例。它同时是两件事：
    ///   - StatBlock 修正的 Source：以后卖掉 / 羁绊失效时按它 RemoveBySource 精确回滚这一件，
    ///     不会把同名道具的另一层也一起退掉；
    ///   - 同名叠加的计数单位：买了几件、有没有到 MaxStack 都靠 _owned 里的实例数。
    /// </summary>
    public sealed class OwnedItem
    {
        public readonly ItemDef Def;

        public OwnedItem(ItemDef def)
        {
            Def = def;
        }
    }

    /// <summary>
    /// 波间商店的纯逻辑：抽卡、保底、价格、刷新、买入。不碰 UI，也不自己判断"现在是不是在商店里"——
    /// 开 / 关由 RunController 驱动（进商店时 OpenShop，CloseShop 时 Close）。
    ///
    /// 6 格里固定有一格卖旅伴（见 CompanionSlotIndex）：道具与旅伴共用货位与价格公式，
    /// 区分只看格子里装的是 ItemDef 还是 CompanionDef，不在代码里写旅伴 Id。
    ///
    /// 抽卡随机流用 runRng（技术方案 7.5：战斗与商店同一条）：同一种子下，买不买、刷不刷新
    /// 都会改变后续暴击序列，这是明确接受的取舍，好处是一局只有一条随机源要复现。
    ///
    /// 这个类每个商店只在一帧里被调几次命令，不在每步模拟路径上，所以买一件道具时 new 一个
    /// OwnedItem / 改字典都是可以接受的；模拟层（Tick）零分配的要求不落在它头上。
    /// </summary>
    public sealed class ShopController
    {
        /// <summary>每间商店的格数。GDD 10.1 说波 2 后 5 格、波 4 后 6 格，I2-4 的验收口径是先一律 6 格。</summary>
        public const int SlotCount = 6;

        /// <summary>
        /// 旅伴货的固定货位（0 基下标 2 = GDD 10.1 的"第 3 个货位"）。
        /// 有旅伴可招时这一格换成旅伴，没有就保留原来抽到的道具：这一格是唯一的旅伴入口，
        /// 所以"旅伴和道具同处 6 格"不会变成随机事件。
        /// </summary>
        public const int CompanionSlotIndex = 2;

        /// <summary>
        /// 旅伴的基准价。Companion.json 没有价格列，而本阶段不许改配置表，所以价格只能写在代码里：
        /// 取 70（蓝色道具 45 与紫色道具 80 之间），与道具共用同一条"每波上浮"公式。
        /// 表里补了价格列以后，把这里换成读 CompanionDef 即可。
        /// </summary>
        public const int CompanionBasePrice = 70;

        /// <summary>
        /// 刷新价格。GDD 10.1 写的是"首次 5 金币，每次 +3"，I2-4 改成一间商店首次免费、之后固定 10：
        /// 免费那一次让玩家每波都愿意看一眼手牌，固定价则让"刷几次"成为可以算的取舍。
        /// </summary>
        public const int RefreshPrice = 10;

        /// <summary>保底阈值：连续这么多次没出紫以上，下一抽强制紫。</summary>
        public const int PityThreshold = 8;

        /// <summary>权重表的上端点幸运值（GDD 8.1 的"幸运 50"列）。</summary>
        const float LuckAtMaxWeight = 50f;

        static readonly ItemDef[] EmptyPool = new ItemDef[0];

        readonly PlayerModel _player;
        readonly RunState _run;
        readonly IEventBus _events;
        readonly IRandom01 _random;

        /// <summary>价格每波上浮比例，来自 Balance 表的 PriceGrowthPerWave。没有它价格会与数值表脱钩。</summary>
        readonly float _priceGrowthPerWave;

        /// <summary>按品质分好组的道具表，每组按 Id 排序：字典遍历顺序不是契约，排序后同一种子抽到同一间商店。</summary>
        readonly ItemDef[][] _itemsByRarity;

        /// <summary>全表平铺（同样按 Id 排序）。抽到的品质在表里一件都没有时，从这里兜一件出来。</summary>
        readonly ItemDef[] _allItems;

        /// <summary>
        /// 旅伴队伍。可空：只测道具的用例不需要它，那样旅伴货位永远不会出现，行为与 I2-4 完全一致。
        /// 商店只通过它问"能不能再招"与"招进来"，等级、羁绊、武器都归 CompanionSystem。
        /// </summary>
        readonly CompanionSystem _companions;

        /// <summary>旅伴表，按 Id 排序：字典遍历顺序不是契约，排序后同一种子抽到同一间商店的同一个旅伴。</summary>
        readonly CompanionDef[] _companionPool;

        /// <summary>本次可以招的旅伴（没招过或没满级的），也是按 Id 顺序。滚动货位时复用，不每间商店 new。</summary>
        readonly List<CompanionDef> _companionOffers = new List<CompanionDef>(CompanionSystem.MaxCompanions);

        readonly ShopSlot[] _slots = new ShopSlot[SlotCount];
        readonly List<OwnedItem> _owned = new List<OwnedItem>(8);
        readonly Dictionary<string, int> _stacks = new Dictionary<string, int>(8);

        int _waveIndex = 1;
        int _pity;
        int _spent;

        /// <summary>本次商店的免费刷新是否已经用掉。每间商店重置一次。</summary>
        bool _refreshUsed;

        bool _opened;

        public ShopController(PlayerModel player, RunState run, IReadOnlyDictionary<string, ItemDef> items,
            IEventBus events, IRandom01 random, float priceGrowthPerWave,
            CompanionSystem companions = null, IReadOnlyDictionary<string, CompanionDef> companionDefs = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _events = events;
            _priceGrowthPerWave = priceGrowthPerWave;

            int rarityCount = Enum.GetValues(typeof(ItemRarity)).Length;
            _itemsByRarity = new ItemDef[rarityCount][];
            var buckets = new List<ItemDef>[rarityCount];
            for (int i = 0; i < rarityCount; i++) buckets[i] = new List<ItemDef>(4);

            if (items != null)
            {
                foreach (ItemDef def in items.Values)
                {
                    int index = (int)def.Rarity;
                    if (index < 0 || index >= rarityCount) continue;
                    buckets[index].Add(def);
                }
            }

            int total = 0;
            for (int i = 0; i < rarityCount; i++)
            {
                List<ItemDef> bucket = buckets[i];
                bucket.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                _itemsByRarity[i] = bucket.ToArray();
                total += bucket.Count;
            }

            _allItems = new ItemDef[total];
            int cursor = 0;
            for (int i = 0; i < rarityCount; i++)
            {
                ItemDef[] bucket = _itemsByRarity[i];
                Array.Copy(bucket, 0, _allItems, cursor, bucket.Length);
                cursor += bucket.Length;
            }

            _companions = companions;
            _companionPool = BuildCompanionPool(companionDefs);
        }

        /// <summary>旅伴按 Id 排序平铺。没有队伍或没有表时是空数组，旅伴货位因此永远不会出现。</summary>
        static CompanionDef[] BuildCompanionPool(IReadOnlyDictionary<string, CompanionDef> companionDefs)
        {
            if (companionDefs == null || companionDefs.Count == 0) return new CompanionDef[0];

            var pool = new List<CompanionDef>(companionDefs.Values);
            pool.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return pool.ToArray();
        }

        /// <summary>当前商店对应的波次（进商店时刚打完的那一波），价格按它算。</summary>
        public int WaveIndex => _waveIndex;

        /// <summary>这一间商店花掉的金币。RunController.CloseShop 用它填 ShopClosed。</summary>
        public int Spent => _spent;

        /// <summary>连续没出紫以上的次数。0 表示上一抽就是紫或金。</summary>
        public int Pity => _pity;

        /// <summary>是不是有一间开着商店（OpenShop 之后、Close 之前）。</summary>
        public bool IsOpen => _opened;

        /// <summary>已经买到的道具实例，按购买顺序。</summary>
        public IReadOnlyList<OwnedItem> Owned => _owned;

        /// <summary>本次刷新的价格：每间商店的第一次免费，之后固定 10。</summary>
        public int RefreshPriceNow => _refreshUsed ? RefreshPrice : 0;

        /// <summary>取一格。下标越界返回空格（Item 为 null），调用方不用自己判。</summary>
        public ShopSlot Slot(int index)
        {
            return index >= 0 && index < _slots.Length ? _slots[index] : default;
        }

        /// <summary>同一件道具已经买了几层。HUD / UI 显示与 MaxStack 判定都用它。</summary>
        public int StackOf(string itemId)
        {
            return itemId != null && _stacks.TryGetValue(itemId, out int count) ? count : 0;
        }

        /// <summary>
        /// 开一间新商店：重置本次花费与免费刷新、按波次重新抽 6 格。
        /// 保底计数不在这里重置——它跨商店累计，只在换局时归零（ResetForNewRun）。
        /// </summary>
        public void OpenShop(int waveIndex)
        {
            _waveIndex = waveIndex < 1 ? 1 : waveIndex;
            _spent = 0;
            _refreshUsed = false;
            _opened = true;
            RollSlots();
        }

        /// <summary>关商店：格子清空，之后 TryBuy / TryRefresh 一律返回 false，不会在战斗中买到上一间的货。</summary>
        public void Close()
        {
            _opened = false;
            for (int i = 0; i < _slots.Length; i++) _slots[i] = default;
        }

        /// <summary>
        /// 换局时清空：保底计数、商店状态、已买道具计数、以及买过的道具在属性上留下的修正。
        /// 由 RunController.StartRun 直接调，而不是订阅 RunStarted：RunStarted 要到下一次 Flush 才分发，
        /// 中间万一有人先开了商店，就会带着上一局攒的保底进场。
        ///
        /// 属性回滚必须做：OwnedItem 实例同时是 StatBlock 修正的 Source，只清列表不退修正，
        /// 在同一个进程里重开一局就会得到"金币归零、属性却还是上一局叠加后的"——
        /// 这正是 I2-10 的"重开一局状态干净"要卡住的东西。
        /// </summary>
        public void ResetForNewRun()
        {
            for (int i = 0; i < _owned.Count; i++) _player.Stats.RemoveBySource(_owned[i]);

            _pity = 0;
            _spent = 0;
            _refreshUsed = false;
            _waveIndex = 1;
            _owned.Clear();
            _stacks.Clear();
            Close();
        }

        /// <summary>刷新 6 格。第一次免费，之后每次 10 金币。返回 false 表示没开商店或金币不够。</summary>
        public bool TryRefresh()
        {
            if (!_opened) return false;

            int price = RefreshPriceNow;
            if (!_run.TrySpend(price)) return false;

            if (price > 0)
            {
                _spent += price;
                _events?.Publish(new CoinChanged(_run.Coins, -price));
            }

            _refreshUsed = true;
            RollSlots();
            return true;
        }

        /// <summary>
        /// 这一格现在能不能买：开着一间商店、格子有货、金币够、同名没叠满。
        /// TryBuy 与商店界面共用这一条判据——界面拿它决定"置灰 / 方向键跳过"，
        /// 两处各写一遍的话，改了规则以后会出现"看着能买、按下去没反应"。
        /// </summary>
        public bool CanBuy(int index)
        {
            if (!_opened) return false;
            if (index < 0 || index >= _slots.Length) return false;
            if (_slots[index].Price > _run.Coins) return false;

            // 旅伴货：能不能再招（队伍未满、未满级）由 CompanionSystem 说话，商店不自己数队伍
            CompanionDef companion = _slots[index].Companion;
            if (companion != null) return _companions != null && _companions.CanRecruit(companion);

            ItemDef def = _slots[index].Item;
            if (def == null) return false;

            // MaxStack <= 0 的配置当 1 处理：不许叠的写成 0 是笔误，不该变成"无限叠"
            int maxStack = def.MaxStack > 0 ? def.MaxStack : 1;
            return StackOf(def.Id) < maxStack;
        }

        /// <summary>
        /// 买第 index 格。买入后这一格清空（Item 与 Companion 都变 null），同一格不可重复购买。
        /// 道具：属性走 StatBlock.Add，Source 是这一次购买的 OwnedItem 实例；同名道具受 MaxStack 限制。
        /// 旅伴：交给 CompanionSystem 招进来（已拥有则升 1 级），不记进道具的 Owned/叠层——
        /// 旅伴的属性修正由它自己的模型与羁绊源持有，与道具回滚是两套。
        /// </summary>
        public bool TryBuy(int index)
        {
            if (!CanBuy(index)) return false;

            ShopSlot slot = _slots[index];
            int price = slot.Price;

            // 先判钱再改任何状态：金币不够时属性、叠层、格子都不能动
            if (!_run.TrySpend(price)) return false;

            if (slot.Companion != null)
            {
                // CanBuy 已经问过 CanRecruit，正常不会走到失败；真失败了就把钱退回去，
                // 不让人花金币买到一格空的货位
                if (_companions == null || _companions.Recruit(slot.Companion) == null)
                {
                    _run.AddCoins(price);
                    return false;
                }
            }
            else
            {
                ItemDef def = slot.Item;
                var owned = new OwnedItem(def);
                _owned.Add(owned);
                _stacks[def.Id] = StackOf(def.Id) + 1;
                ApplyStats(owned);
            }

            _spent += price;
            _events?.Publish(new CoinChanged(_run.Coins, -price));
            _slots[index] = default;
            return true;
        }

        /// <summary>
        /// 价格 = BasePrice × (1 + 上浮比例 × (波次−1))，四舍五入到整数。
        /// 用 Floor(x + 0.5) 而不是 Mathf.RoundToInt：后者是银行家舍入，49.5 会往偶数走成 48，
        /// 价格表里出现半值时"涨了还是没涨"就说不清了。
        /// </summary>
        public int PriceOf(ItemDef def, int waveIndex)
        {
            return def == null ? 0 : PriceOf(def.BasePrice, waveIndex);
        }

        /// <summary>旅伴的价格。Companion 表没有价格列，基准价写在 CompanionBasePrice，上浮公式与道具同一条。</summary>
        public int CompanionPriceOf(int waveIndex)
        {
            return PriceOf(CompanionBasePrice, waveIndex);
        }

        int PriceOf(int basePrice, int waveIndex)
        {
            int wave = waveIndex < 1 ? 1 : waveIndex;
            float raw = basePrice * (1f + _priceGrowthPerWave * (wave - 1));
            return Mathf.FloorToInt(raw + 0.5f);
        }

        /// <summary>
        /// 品质权重（GDD 8.1）：幸运 0 → 白 55 / 蓝 30 / 紫 12 / 金 3；幸运 50 → 白 30 / 蓝 40 / 紫 22 / 金 8，
        /// 中间按幸运线性插值，两端各之和都是 1，插值后仍是 1，不需要再归一化。
        ///
        /// GDD 正文还有一句"幸运每 1 点把 1% 白概率转给紫"，它与这两个端点对不上：从 0 到 50 白只降 25 个点、
        /// 紫只涨 10 个点。这里按端点表实现——只有表里的数带验收口径（分布误差 &lt; 2%），那句话按设计意图读。
        /// </summary>
        public static void WeightsFor(float luck, out float common, out float rare, out float epic,
            out float legendary)
        {
            float t = Mathf.Clamp(luck, 0f, LuckAtMaxWeight) / LuckAtMaxWeight;
            common = Mathf.Lerp(0.55f, 0.30f, t);
            rare = Mathf.Lerp(0.30f, 0.40f, t);
            epic = Mathf.Lerp(0.12f, 0.22f, t);
            legendary = Mathf.Lerp(0.03f, 0.08f, t);
        }

        void RollSlots()
        {
            // 先清空再抽：去重要看"这一轮已经出现了什么"，上一间的残留会被误判成已用
            for (int i = 0; i < _slots.Length; i++) _slots[i] = default;

            for (int i = 0; i < _slots.Length; i++)
            {
                ItemDef def = RollItem();
                _slots[i] = def == null ? default : new ShopSlot(def, PriceOf(def, _waveIndex));
            }

            OfferCompanion();
        }

        /// <summary>
        /// 把固定货位换成旅伴。没有旅伴表、队伍已满或全员满级时留着原来抽到的道具：
        /// 宁可只卖道具，也不要摆一格点了没反应的货。
        /// 抽哪一个用抽卡那条 runRng，并按 Id 顺序排候选，同一种子得到同一个旅伴。
        /// </summary>
        void OfferCompanion()
        {
            if (_companions == null || _companionPool.Length == 0) return;

            _companionOffers.Clear();
            for (int i = 0; i < _companionPool.Length; i++)
            {
                CompanionDef def = _companionPool[i];
                if (_companions.CanRecruit(def)) _companionOffers.Add(def);
            }

            if (_companionOffers.Count == 0) return;

            CompanionDef offer = _companionOffers[RandomIndex(_companionOffers.Count)];
            _slots[CompanionSlotIndex] = new ShopSlot(offer, CompanionPriceOf(_waveIndex));
        }

        ItemDef RollItem()
        {
            ItemRarity rarity = RollRarity();

            // 同一轮不重复：先在抽到的品质里挑一件本轮还没出现过的
            ItemDef def = PickUnused(rarity);

            // 这个品质的本轮都用过了：先保证"6 格不重复"再谈品质
            if (def == null) def = PickAnyUnused();

            // 整张表都不够 6 件（当前配置只有 4 件，一种品质一件）：只能重复，
            // 但品质仍按抽卡结果取，别让"表太小"把品质分布也一起改掉
            if (def == null) def = PickAny(rarity);

            // 抽到的品质在表里一件都没有：从全表兜一件，宁可品质不对也不留空格
            if (def == null) def = PickAnyFromWholeTable();

            return def;
        }

        ItemRarity RollRarity()
        {
            // 保底：上一段已经连续 8 次没出紫以上，这一抽直接给紫。
            // 强制紫自己也算出货，所以顺手把计数清零。
            // 前提是道具表里至少有一件紫：一件紫都没有时 RollItem 的兜底会给别的品质，
            // 那时该补的是道具表（Item.json 现在有香水，I3 还会再加），不是在这里造一件紫出来。
            if (_pity >= PityThreshold)
            {
                _pity = 0;
                return ItemRarity.Epic;
            }

            // 幸运没设过基线时是 0（StatBlock 构造清零），正好是权重表的低端点
            WeightsFor(_player.Stats[StatType.Luck], out float common, out float rare, out float epic, out _);

            float roll = _random.Next01();

            ItemRarity rarity;
            if (roll < common) rarity = ItemRarity.Common;
            else if (roll < common + rare) rarity = ItemRarity.Rare;
            else if (roll < common + rare + epic) rarity = ItemRarity.Epic;
            else rarity = ItemRarity.Legendary;   // 尾部：浮点累加剩下的那一点也算金，不把概率丢掉

            if (rarity == ItemRarity.Legendary || rarity == ItemRarity.Epic) _pity = 0;
            else _pity++;

            return rarity;
        }

        /// <summary>在某个品质里随机挑一件本轮没出现过的。随机起点 + 顺序扫描：既均匀又只抽一次骰子。</summary>
        ItemDef PickUnused(ItemRarity rarity)
        {
            ItemDef[] pool = Pool(rarity);
            if (pool.Length == 0) return null;

            int start = RandomIndex(pool.Length);
            for (int offset = 0; offset < pool.Length; offset++)
            {
                ItemDef def = pool[(start + offset) % pool.Length];
                if (!UsedInThisShop(def)) return def;
            }

            return null;
        }

        ItemDef PickAny(ItemRarity rarity)
        {
            ItemDef[] pool = Pool(rarity);
            return pool.Length == 0 ? null : pool[RandomIndex(pool.Length)];
        }

        ItemDef PickAnyFromWholeTable()
        {
            return _allItems.Length == 0 ? null : _allItems[RandomIndex(_allItems.Length)];
        }

        /// <summary>不重复优先的兜底：从低品质到高品质顺序找。只在道具表凑不满 6 件时走到。</summary>
        ItemDef PickAnyUnused()
        {
            for (int i = 0; i < _itemsByRarity.Length; i++)
            {
                ItemDef def = PickUnused((ItemRarity)i);
                if (def != null) return def;
            }

            return null;
        }

        /// <summary>
        /// 取一个 [0, count) 的下标。IRandom01 只承诺 [0,1) 浮点，不额外加接口方法：
        /// 这里换算并兜住浮点边界，做法与 DeterministicRandom.NextInt 一致。
        /// </summary>
        int RandomIndex(int count)
        {
            int value = (int)(_random.Next01() * count);
            return value < count ? value : count - 1;
        }

        ItemDef[] Pool(ItemRarity rarity)
        {
            int index = (int)rarity;
            return index >= 0 && index < _itemsByRarity.Length ? _itemsByRarity[index] : EmptyPool;
        }

        bool UsedInThisShop(ItemDef def)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (ReferenceEquals(_slots[i].Item, def)) return true;
            }

            return false;
        }

        void ApplyStats(OwnedItem owned)
        {
            ItemDef def = owned.Def;

            // Item 表还没有"修正方式"列，现有 4 件道具的数值都是平加（生命回复 −2、碰撞伤害 +10），
            // 所以一律按 Flat 生效。等 I3 出现百分比道具时必须先在表里补一列，
            // 靠 StatType 猜"这条该是百分比"迟早会把某件道具算错 100 倍。
            //
            // EffectType（CoinHeal / OnHurtAttackUp / Endless）这一步不做：I2-4 的验收只到
            // "属性生效 + 同名叠层"，触发型效果要挂在命中/受伤/拾取的回调上，归 I2-8 与 I5。
            // 所以香水与无限路牌现在买得到、只生效它们的 StatA/StatB（这两件都是 Unknown，等于只扣钱），
            // 这不是遗漏，是还没轮到的部分。
            if (def.HasStatA) _player.Stats.Add(new StatModifier(def.StatA, def.ValueA, ModOp.Flat, owned));
            if (def.HasStatB) _player.Stats.Add(new StatModifier(def.StatB, def.ValueB, ModOp.Flat, owned));
        }
    }
}
