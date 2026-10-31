using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Services;
using Game.Systems;
using Game.Systems.Weapons;
using Game.Views;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 场景里的装配根。只有它加载配置、实例化预制体、持有游戏级引用。
    /// 其余代码通过构造注入拿到依赖，不查静态单例。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AppRoot : MonoBehaviour
    {
        public const string PlayerPrefabId = "Player";
        public const string HudPrefabId = "HudCanvas";

        /// <summary>
        /// 投射物池容量。验收要求 200 敌 + 400 发同时在场，取 512 留一倍余量：
        /// 多弹道武器一次开火就是好几发，容量卡在 400 会让"刚好够用"变成偶发丢弹（丢弹只打一条日志，很难发现）。
        /// </summary>
        public const int ProjectilePoolCapacity = 512;

        /// <summary>
        /// RunSeed 没被设置过（0）时用的世界种子。用固定常量而不是读系统时间：
        /// 同一份输入要能复现同一局，随机源必须能从外部钉死。
        /// </summary>
        public const ulong DefaultWorldSeed = 0x5EED1A2B3C4D5E6Ful;

        /// <summary>手动压测入口的流与战斗流分开：按一下它不改变这一局的暴击序列。</summary>
        const ulong ManualSpawnSeedSalt = 0x9E3779B97F4A7C15ul;

        /// <summary>
        /// 玩家固定用这个 UnitId。敌人的 id 是槽位号 + 1（见 EnemySystem.UnitIdOf），
        /// 两套编码现在共用同一个小整数空间；I2 接击杀统计时统一，在那之前不要假定 id 跨阵营唯一。
        /// </summary>
        public static readonly UnitId PlayerUnit = new UnitId(1);

        public static AppRoot Instance { get; private set; }

        [SerializeField] bool _loadConfigOnAwake = true;
        [SerializeField] string _characterId = "Scavenger";
        [SerializeField] UnitPrefabMap _prefabMap;

        public EventBus Events { get; private set; }
        public RunState Run { get; private set; }
        public ConfigDatabase Config { get; private set; }
        public InputService Input { get; private set; }
        public UnitPrefabMap PrefabMap => _prefabMap;
        public ulong RunSeed { get; private set; }

        public PlayerModel Player { get; private set; }
        public PlayerController PlayerController { get; private set; }
        public PlayerView PlayerView { get; private set; }
        public HudView Hud { get; private set; }
        public ShopView ShopUi { get; private set; }
        public ResultView ResultUi { get; private set; }
        public CameraRig Camera { get; private set; }

        public EnemySystem Enemies { get; private set; }
        public WeaponSystem Weapons { get; private set; }
        public CompanionSystem Companions { get; private set; }
        public RunController RunController { get; private set; }
        public WaveController Waves { get; private set; }
        public PickupSystem Pickups { get; private set; }
        public ShopController Shop { get; private set; }
        public RunSimulation Simulation { get; private set; }
        public EnemyViewPool EnemyViews { get; private set; }
        public CompanionViewPool CompanionViews { get; private set; }
        public ProjectileViewPool ProjectileViews { get; private set; }
        public PickupViewPool PickupViews { get; private set; }

        public bool ConfigOk { get; private set; }
        public string ConfigError { get; private set; }

        CharacterDef _character;

        // 手动压测入口（SpawnTestEnemies）用的怪种快照与随机流。字典遍历顺序不是契约，先按 Id 排一次，
        // 同一种子才刷出同一批怪；这条流与战斗流分开，按一下压测入口不该改变这一局的暴击序列。
        // 自动刷怪走 WaveController 与战斗流，与这两样无关。
        DeterministicRandom _manualSpawnRandom;
        EnemyDef[] _manualSpawnPool;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AppRoot] 场景里有多个 AppRoot，销毁多余的那个");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Events = new EventBus();
            Run = new RunState();
            Input = GetComponent<InputService>();

            if (_loadConfigOnAwake)
            {
                TryLoadConfig();
            }

            if (!ConfigOk) return;

            ValidateContent();
            if (!CreatePlayer()) return;

            CreateCombat();
            CreateHud();
            CreateShopUi();
            CreateResultUi();

            // 两个场景同一个入口：一进来就开局，波次系统自己刷怪。
            // RunStarted / WaveStarted(1) 只是被投进队列，第一次 Flush（RunSimulation.Tick 末尾）才分发，
            // 那时 HUD 已经订阅好了，一个事件都不会丢。
            if (RunController != null) RunController.StartRun();
        }

        /// <summary>
        /// 场景卸载。重开一局就是一次场景重载（见 ResultView），所以这里必须把跨场景的只有两样东西收干净：
        ///   - 事件总线：Clear 会把所有订阅与未分发的队列一起丢掉，SubscriptionCount 归零；
        ///   - Instance：不置空的话新场景的装配根会把自己当成重复的那个，直接自毁。
        /// 实体（玩家、敌人、掉落物、视图池、HUD/商店/结算面板）都挂在本对象或场景里，跟着场景一起销毁，
        /// 不需要手动逐个清：手动清才是这个迭代里最容易漏项的一块。
        /// 本对象自己的订阅（RunController 的 UnitDied）也在这条总线上，一样被 Clear 带走。
        /// </summary>
        void OnDestroy()
        {
            if (Instance != this) return;
            Events?.Clear();
            Events = null;
            Instance = null;
        }

        public void SetSeed(ulong seed)
        {
            RunSeed = seed;
        }

        public bool TryLoadConfig()
        {
            try
            {
                Config = ConfigDatabase.Load(new FileConfigSource());
                ConfigOk = true;
                ConfigError = null;
                Debug.Log($"[Config] 加载成功 版本 {Config.Version}｜角色 {Config.Characters.Count}" +
                          $"｜武器 {Config.Weapons.Count}｜旅伴 {Config.Companions.Count}" +
                          $"｜道具 {Config.Items.Count}｜敌人 {Config.Enemies.Count}｜波次 {Config.Waves.Count}");
                return true;
            }
            catch (System.Exception e)
            {
                ConfigOk = false;
                ConfigError = e.Message;
                Config = null;
                Debug.LogError("[Config] 加载失败:\n" + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 配置表里的 Id 必须能在预制体映射里找到。玩家缺失是致命错误，
        /// 敌人缺失先给警告——刷到没有预制体的敌人时只会有看不见的怪，不该连玩家一起拖死。
        /// </summary>
        void ValidateContent()
        {
            if (_prefabMap == null)
            {
                Debug.LogError("[AppRoot] 没有指定 UnitPrefabMap，玩家与敌人都实例化不出来。" +
                               "先跑一次 Tools/Game/生成预制体");
                return;
            }

            var missing = new List<string>();
            foreach (EnemyDef enemy in Config.Enemies.Values)
            {
                if (!_prefabMap.Has(enemy.Id)) missing.Add("Enemy/" + enemy.Id);
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning("[AppRoot] 以下 Id 没有对应预制体，刷到时会实例化失败:\n  " +
                                 string.Join("\n  ", missing));
            }
        }

        bool CreatePlayer()
        {
            if (Config == null || _prefabMap == null) return false;

            if (!Config.Characters.TryGetValue(_characterId, out CharacterDef def))
            {
                Debug.LogError($"[AppRoot] 角色表里没有 '{_characterId}'");
                return false;
            }

            GameObject prefab = _prefabMap.Find(PlayerPrefabId);
            if (prefab == null)
            {
                Debug.LogError($"[AppRoot] UnitPrefabMap 里没有 '{PlayerPrefabId}' 预制体，" +
                               "先跑一次 Tools/Game/生成预制体");
                return false;
            }

            float arenaHalf = Config.GetBalance("ArenaHalfExtent", 67f);
            float dashMultiplier = Config.GetBalance("DashSpeedMultiplier", 3f);
            float dashDuration = Config.GetBalance("DashDuration", 0.15f);
            float dashCooldown = Config.GetBalance("DashCooldown", 2f);

            _character = def;

            Player = new PlayerModel(
                def.BaseHP,
                def.BaseMoveSpeed,
                def.BaseMoveSpeed * dashMultiplier,
                dashDuration,
                dashCooldown,
                arenaHalf);

            PlayerController = new PlayerController(Player, Events);

            GameObject instance = Instantiate(prefab);
            instance.name = PlayerPrefabId;

            PlayerView view = instance.GetComponent<PlayerView>();
            if (view != null)
            {
                PlayerView = view;
            }

            Debug.Log($"[AppRoot] 玩家就绪 生命 {Player.MaxHP} 移速 {Player.MoveSpeed}" +
                      $" 冲刺 {Player.DashSpeed} 边界 ±{arenaHalf}");
            return true;
        }

        /// <summary>
        /// 战斗装配：敌人系统、武器系统、一步模拟、两层视图。
        /// 必须在玩家之后：武器每步从玩家的 StatBlock 取加成，敌人追的也是玩家。
        /// </summary>
        void CreateCombat()
        {
            ulong worldSeed = RunSeed != 0 ? RunSeed : DefaultWorldSeed;
            _manualSpawnRandom = new DeterministicRandom(worldSeed ^ ManualSpawnSeedSalt);
            _manualSpawnPool = BuildManualSpawnPool();

            // 武器、暴击、闪避、接触伤害用同一条战斗流：同一种子 + 同一输入 = 同一次暴击、同一次闪避。
            // 这条流必须在敌人系统之前建好：接触伤害的闪避骰子从它里掷。
            var combatRandom = new DeterministicRandom(worldSeed);

            Enemies = new EnemySystem(Player, Player.ArenaHalfExtent, combatRandom);

            Weapons = new WeaponSystem(Enemies, combatRandom, PlayerUnit, ProjectilePoolCapacity, Events);

            AddStartingWeapon();

            // 旅伴在商店之前建：商店的固定货位要问它"能不能再招"，买进来以后羁绊也归它重算。
            // 旅伴的武器与暴击闪避共用战斗那条流（一局只有一条随机源）。
            Companions = new CompanionSystem(Player, Enemies, Config.Weapons, combatRandom, Events);

            // 刷怪不要自己的随机流：按 I2-2 的要求走战斗那条流，一局只有一条随机源，
            // "固定种子 + 固定输入 = 相同结果"只需守一处。
            Waves = new WaveController(Enemies, Config.Enemies, combatRandom);

            // 掉落与商店也共用这一条 runRng（技术方案 7.5）：代价是商店抽卡会挪动后续的暴击序列，
            // 换来的是"一局只有一条随机源要复现"。
            Pickups = new PickupSystem(Player, Run, Events, combatRandom);

            float priceGrowth = Config.GetBalance("PriceGrowthPerWave", 0.1f);
            Shop = new ShopController(Player, Run, Config.Items, Events, combatRandom, priceGrowth, Companions,
                Config.Companions);

            RunController = new RunController(Player, Run, Config.Waves, Events, worldSeed, _characterId, Shop,
                Companions);

            Simulation = new RunSimulation(Player, PlayerController, Enemies, Weapons, Events, RunController, Waves,
                Pickups, Companions);
            EnemyViews = new EnemyViewPool(_prefabMap, transform, Enemies, Events);

            // 旅伴视图与敌人视图分开建：几何体是代码建的胶囊（不走 UnitPrefabMap），
            // 槽位也属于 CompanionSystem 而不是 EnemySystem。
            CompanionViews = new CompanionViewPool(_prefabMap, Companions, transform);

            // 投射物视图把旅伴的投射物一起接进来：一个池按持有者换颜色与大小，
            // 不为每个旅伴各建一套池（旅伴的武器系统是招募时才建出来的，池自己也每帧对齐）
            ProjectileViews = new ProjectileViewPool(_prefabMap, Weapons.Projectiles, Companions, transform);
            PickupViews = new PickupViewPool(_prefabMap, Pickups, transform);
        }

        /// <summary>
        /// 起始武器从角色表取（Scavenger 指向 Bow），代码里不写死武器 Id。
        /// 指向不存在的武器说明配置坏了：报错并跳过，没有武器还能跑，抛异常就什么都不剩。
        /// </summary>
        void AddStartingWeapon()
        {
            string weaponId = _character == null ? null : _character.StartingWeaponId;
            if (string.IsNullOrEmpty(weaponId)) return;

            if (Config.Weapons.TryGetValue(weaponId, out WeaponDef def))
            {
                Weapons.AddWeapon(def);
                Debug.Log($"[AppRoot] 武器就绪 {def.Id} 伤害 {def.BaseDamage}" +
                          $" 冷却 {def.Cooldown} 射程 {def.Range}");
                return;
            }

            Debug.LogError($"[AppRoot] 武器表里没有 '{weaponId}'（角色 {_character.Id} 的 StartingWeaponId），" +
                           "玩家不会攻击");
        }

        /// <summary>
        /// Test_Arena 的手动压测入口（I1-11：同屏 200 敌不掉帧）。波次系统接管自动刷怪以后这里不再自动调用，
        /// 只留给手动把同屏堆到 200 的场景（压测、试手感）。出生点公式用 WaveController.SpawnRingOffset，
        /// 与正式刷怪是同一套，AppRoot 不再自己算环。
        /// </summary>
        public void SpawnTestEnemies(int count)
        {
            if (Enemies == null || _manualSpawnRandom == null || _manualSpawnPool == null ||
                _manualSpawnPool.Length == 0)
            {
                Debug.LogWarning("[AppRoot] 刷怪条件没就绪（配置或敌人系统缺失），跳过");
                return;
            }

            Vector2 origin = Player.Position;

            // 整个环带一起转：换种子时刷怪圈的样子跟着变，但不改变点的分布均匀度
            float phase = _manualSpawnRandom.Range(0f, Mathf.PI * 2f);

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = WaveController.SpawnRingOffset(i, count, phase,
                    WaveController.SpawnRadiusMin, WaveController.SpawnRadiusMax);
                Enemies.Spawn(_manualSpawnPool[_manualSpawnRandom.NextInt(_manualSpawnPool.Length)], origin + offset);
            }

            Debug.Log($"[AppRoot] 手动刷了 {count} 个测试敌人（压测入口：不进波次流程，也不受同屏上限管）");
        }

        EnemyDef[] BuildManualSpawnPool()
        {
            var pool = new List<EnemyDef>(Config.Enemies.Values);
            pool.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return pool.ToArray();
        }

        void CreateHud()
        {
            if (_prefabMap == null) return;

            GameObject prefab = _prefabMap.Find(HudPrefabId);
            if (prefab == null)
            {
                Debug.LogError($"[AppRoot] UnitPrefabMap 里没有 '{HudPrefabId}'，屏幕上不会有 HUD");
                return;
            }

            if (prefab.activeSelf)
            {
                Debug.LogWarning("[AppRoot] HUD 预制体根节点是激活的，首次 OnEnable 会早于注入。" +
                                 "把预制体根节点设为 inactive");
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.name = HudPrefabId;

            HudView view = instance.GetComponent<HudView>();
            if (view == null)
            {
                Debug.LogError("[AppRoot] HUD 预制体上没有 HudView");
                return;
            }

            view.Initialize(Events);
            view.Bind(Player, Run, RunController, Enemies);
            instance.SetActive(true);
            Hud = view;
        }

        /// <summary>
        /// 商店界面（I2-5）。和其它 UI 一样先注入再激活：ShopView 在 OnEnable 里订阅
        /// ShopOpened / ShopClosed，跳过一次就永远不弹了。面板本身由代码搭（见 ShopView），
        /// 不占 UnitPrefabMap 的一条映射，也不往场景里放东西。
        /// 它自己负责开商店时把输入切到 UI Map、关商店时切回 Player。
        /// </summary>
        void CreateShopUi()
        {
            // 带 RectTransform 建：Overlay Canvas 的根节点就是一张全屏矩形，
            // 先建普通 Transform 再挂 Canvas 会被 Unity 换成 RectTransform，不如一开始就建对
            var go = new GameObject("ShopCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var view = go.AddComponent<ShopView>();
            view.Initialize(Events);
            view.Bind(Shop, Run, RunController, Input);
            go.SetActive(true);
            ShopUi = view;
        }

        /// <summary>
        /// 结算面板（I2-10）。与商店界面同一套做法：带 RectTransform 建根、先注入再激活（它订阅 RunEnded），
        /// 面板本身由代码搭（见 ResultView），不占 UnitPrefabMap 的一条映射。
        /// 重开与回主菜单都走场景切换，所以这里把 UnitySceneFlow 注进去——面板不直接调 SceneManager，
        /// 换实现（比如加一段淡出）时只改这一处。
        /// </summary>
        void CreateResultUi()
        {
            var go = new GameObject("ResultCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var view = go.AddComponent<ResultView>();
            view.Initialize(Events);
            view.Bind(RunController, Input, new UnitySceneFlow());
            go.SetActive(true);
            ResultUi = view;
        }

        public void BindPlayerView(PlayerView view)
        {
            if (view == null) return;
            PlayerView = view;
        }

        public void BindCameraRig(CameraRig rig)
        {
            Camera = rig;
        }
    }
}
