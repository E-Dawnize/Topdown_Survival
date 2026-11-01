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
    /// 场景里的装配根。负责加载配置、实例化预制体、创建视图，然后把对象图交给 RunComposition。
    /// 纯 C# 的模拟对象图只有一份实现（RunComposition），测试夹具与正式装配共用，构造参数不会两处漂移。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AppRoot : MonoBehaviour
    {
        public const string PlayerPrefabId = "Player";
        public const string HudPrefabId = "HudCanvas";

        /// <summary>投射物池容量。验收要求 200 敌 + 400 发同时在场，取 512 留一倍余量。</summary>
        public const int ProjectilePoolCapacity = RunComposition.DefaultProjectilePoolCapacity;

        /// <summary>RunSeed 没被设置过（0）时用的世界种子。固定常量保证同一输入可复现同一局。</summary>
        public const ulong DefaultWorldSeed = 0x5EED1A2B3C4D5E6Ful;

        /// <summary>手动压测入口的流与战斗流分开：按一下它不改变这一局的暴击序列。</summary>
        const ulong ManualSpawnSeedSalt = 0x9E3779B97F4A7C15ul;

        /// <summary>玩家固定占用 1 号 ID。保留这个别名是为了兼容旧调用点。</summary>
        public static readonly UnitId PlayerUnit = UnitId.Player;

        public static AppRoot Instance { get; private set; }

        [SerializeField] bool _loadConfigOnAwake = true;
        [SerializeField] string _characterId = "Scavenger";
        [SerializeField] UnitPrefabMap _prefabMap;

        /// <summary>本局种子。0 表示使用 DefaultWorldSeed。种子属于启动配置，不在运行中途修改。</summary>
        [SerializeField] ulong _runSeed;

        public EventBus Events => _composition != null ? _composition.Events : null;
        public RunState Run => _composition != null ? _composition.Run : null;
        public ConfigDatabase Config { get; private set; }
        public InputService Input { get; private set; }
        public UnitPrefabMap PrefabMap => _prefabMap;
        public ulong RunSeed { get; private set; }

        public PlayerModel Player => _composition != null ? _composition.Player : null;
        public PlayerController PlayerController => _composition != null ? _composition.PlayerController : null;
        public PlayerView PlayerView { get; private set; }
        public HudView Hud { get; private set; }
        public UpgradeView UpgradeUi { get; private set; }
        public ResultView ResultUi { get; private set; }
        public CameraRig Camera { get; private set; }

        public EnemySystem Enemies => _composition != null ? _composition.Enemies : null;
        public WeaponSystem Weapons => _composition != null ? _composition.Weapons : null;
        public CompanionSystem Companions => _composition != null ? _composition.Companions : null;
        public RunController RunController => _composition != null ? _composition.RunController : null;
        public DirectorSystem Director => _composition != null ? _composition.Director : null;
        public PickupSystem Pickups => _composition != null ? _composition.Pickups : null;
        public BuildController Build => _composition != null ? _composition.Build : null;
        public BuffSystem Buffs => _composition != null ? _composition.Buffs : null;
        public RunSimulation Simulation => _composition != null ? _composition.Simulation : null;
        public EnemyViewPool EnemyViews { get; private set; }
        public EnemyProjectileViewPool EnemyProjectileViews { get; private set; }
        public CombatEffectView CombatEffectsView { get; private set; }
        public CompanionViewPool CompanionViews { get; private set; }
        public ProjectileViewPool ProjectileViews { get; private set; }
        public PickupViewPool PickupViews { get; private set; }

        public bool ConfigOk { get; private set; }
        public string ConfigError { get; private set; }

        RunComposition _composition;

        // 手动压测入口（SpawnTestEnemies）用的怪种快照与随机流。自动刷怪走 DirectorSystem。
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
            Input = GetComponent<InputService>();
            RunSeed = _runSeed != 0 ? _runSeed : DefaultWorldSeed;

            // 没有 RunLoop 时继续装配只会得到“状态机开了、世界不动”的半成品。
            RunLoop runLoop = FindObjectOfType<RunLoop>();
            if (runLoop == null)
            {
                ShowStartupError("缺少模拟驱动",
                    "当前场景没有 RunLoop。请从 MainMenu 进入 Run；旧 Boot 场景不要再挂 AppRoot。");
                return;
            }

            if (!_loadConfigOnAwake)
            {
                ShowStartupError("配置未加载", "AppRoot 的 _loadConfigOnAwake 被关闭，当前场景不会装配战局。");
                return;
            }

            if (!LoadConfig())
            {
                ShowStartupError("配置加载失败", ConfigError);
                return;
            }

            if (!ValidateContent())
            {
                ShowStartupError("资源映射不完整", "UnitPrefabMap 缺失或不可用，无法创建玩家与敌人。");
                return;
            }

            try
            {
                _composition = RunComposition.Create(Config, _characterId, RunSeed, ProjectilePoolCapacity);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AppRoot] 对象图装配失败:\n" + e.Message);
                ShowStartupError("装配失败", e.Message);
                return;
            }

            _manualSpawnRandom = new DeterministicRandom(RunSeed ^ ManualSpawnSeedSalt);
            _manualSpawnPool = BuildManualSpawnPool();

            if (!CreatePlayerView()) return;
            CreateHud();
            CreateUpgradeUi();
            CreateResultUi();
            CreateViewPools();
            BindSceneCamera();
            runLoop.Bind(this);

            // 一局只在这里开始。重开走场景重载，不允许同一个 RunController 二次 StartRun。
            RunController.StartRun();

            Debug.Log("[AppRoot] 开局就绪 起始武器 " +
                      (_composition.StartingWeapon != null ? _composition.StartingWeapon.Id : "无") +
                      $"，BOSS {Director.BossTime:0} 秒后出现");
        }

        /// <summary>
        /// 场景卸载。实体与视图跟着场景一起销毁，这里只收两样跨场景的东西：
        /// 事件总线与 Instance；视图池若有显式退订也在这里断开。
        /// </summary>
        void OnDestroy()
        {
            if (Instance != this) return;

            EnemyViews?.Dispose();
            EnemyProjectileViews?.Dispose();
            CombatEffectsView?.Bind(null);
            CombatEffects.Clear();
            _composition?.Events.Clear();
            _composition = null;
            Instance = null;
        }

        bool LoadConfig()
        {
            try
            {
                Config = ConfigDatabase.Load(new FileConfigSource());
                ConfigOk = true;
                ConfigError = null;
                Debug.Log($"[Config] 加载成功 版本 {Config.Version}｜角色 {Config.Characters.Count}" +
                          $"｜武器 {Config.Weapons.Count}｜旅伴 {Config.Companions.Count}" +
                          $"｜敌人 {Config.Enemies.Count}｜导演段 {Config.Directors.Count}" +
                          $"｜等级 {Config.Levels.Count}｜构筑卡 {Config.Upgrades.Count}｜buff {Config.Buffs.Count}" +
                          $"｜组合 {Config.Combos.Count}");
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
        /// 敌人缺失先给警告——刷到没有预制体的敌人时只会有看不见的怪。
        /// </summary>
        bool ValidateContent()
        {
            if (_prefabMap == null)
            {
                Debug.LogError("[AppRoot] 没有指定 UnitPrefabMap，玩家与敌人都实例化不出来。" +
                               "先跑一次 Tools/Game/生成预制体");
                return false;
            }

            var missing = new List<string>();
            foreach (EnemyDef enemy in Config.Enemies.Values)
            {
                string prefabId = string.IsNullOrEmpty(enemy.PrefabId) ? enemy.Id : enemy.PrefabId;
                if (!_prefabMap.Has(prefabId)) missing.Add("Enemy/" + enemy.Id + " -> " + prefabId);
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning("[AppRoot] 以下 Id 没有对应预制体，刷到时会实例化失败:\n  " +
                                 string.Join("\n  ", missing));
            }

            return true;
        }

        /// <summary>
        /// 启动失败时给玩家看得见的结果，而不是留下一个半装配、只会在 Console 里报错的场景。
        /// 面板属于装配根，不依赖 HUD；HUD 创建失败时同样能显示。
        /// </summary>
        void ShowStartupError(string title, string detail)
        {
            UiFactory.CreateStartupError(transform, title, detail);
        }

        bool CreatePlayerView()
        {
            if (Config == null || _prefabMap == null) return false;

            GameObject prefab = _prefabMap.Find(PlayerPrefabId);
            if (prefab == null)
            {
                Debug.LogError($"[AppRoot] UnitPrefabMap 里没有 '{PlayerPrefabId}' 预制体，" +
                               "先跑一次 Tools/Game/生成预制体");
                return false;
            }

            GameObject instance = Instantiate(prefab);
            instance.name = PlayerPrefabId;

            PlayerView view = instance.GetComponent<PlayerView>();
            if (view == null)
            {
                Debug.LogError("[AppRoot] 玩家预制体上没有 PlayerView");
                return false;
            }

            PlayerView = view;
            view.Bind(Player, Weapons, Events);

            Debug.Log($"[AppRoot] 玩家就绪 生命 {Player.MaxHP} 移速 {Player.MoveSpeed}" +
                      $" 冲刺 {Player.DashSpeed} 边界 ±{Player.ArenaHalfExtent}");
            return true;
        }

        void CreateViewPools()
        {
            var effectGo = new GameObject("CombatEffectView");
            effectGo.transform.SetParent(transform, false);
            CombatEffectsView = effectGo.AddComponent<CombatEffectView>();
            CombatEffectsView.Bind(Events);

            EnemyViews = new EnemyViewPool(_prefabMap, transform, Enemies, Events);
            EnemyProjectileViews = new EnemyProjectileViewPool(Enemies.Projectiles, transform,
                _prefabMap != null ? _prefabMap.ProjectileArrow : null);
            CompanionViews = new CompanionViewPool(_prefabMap, Companions, transform);
            ProjectileViews = new ProjectileViewPool(Weapons.Projectiles, Companions, transform,
                _prefabMap != null ? _prefabMap.ProjectileArrow : null);
            PickupViews = new PickupViewPool(_prefabMap, Pickups, transform);
        }

        /// <summary>
        /// Test_Arena 的手动压测入口（I1-11：同屏 200 敌不掉帧）。自动刷怪由 DirectorSystem 接管，
        /// 这里只留给手动把同屏堆到 200 的场景。出生点公式用 SpawnRing.Offset，与正式刷怪同一套。
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
            float phase = _manualSpawnRandom.Range(0f, Mathf.PI * 2f);

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = SpawnRing.Offset(i, count, phase);
                Enemies.Spawn(_manualSpawnPool[_manualSpawnRandom.NextInt(_manualSpawnPool.Length)], origin + offset);
            }

            Debug.Log($"[AppRoot] 手动刷了 {count} 个测试敌人（压测入口：不进导演时间轴，也不受同屏上限管）");
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
            view.Bind(Player, Run, RunController, Enemies, Director, Build);
            instance.SetActive(true);
            Hud = view;
        }

        /// <summary>
        /// 升级选卡界面。它订阅 LevelUpStarted / UpgradeOptionsChanged / LevelUpClosed，
        /// 自己在开面板时切 UI Map、关面板时切回 Player Map；RunController 只负责阶段与抽卡。
        /// </summary>
        void CreateUpgradeUi()
        {
            var go = new GameObject("UpgradeCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var view = go.AddComponent<UpgradeView>();
            view.Initialize(Events);
            view.Bind(Build, RunController, Input);
            go.SetActive(true);
            UpgradeUi = view;
        }

        /// <summary>
        /// 结算面板。重开与回主菜单都走场景切换，所以把 UnitySceneFlow 注进去——面板不直接调 SceneManager。
        /// </summary>
        void CreateResultUi()
        {
            var go = new GameObject("ResultCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var view = go.AddComponent<ResultView>();
            view.Initialize(Events);
            view.Bind(Input, new UnitySceneFlow());
            go.SetActive(true);
            ResultUi = view;
        }

        void BindSceneCamera()
        {
            Camera = FindObjectOfType<CameraRig>();
        }
    }
}
