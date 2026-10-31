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
using UnityEngine.SceneManagement;

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

        /// <summary>战斗测试场。I2 之后这里不再自动刷怪，靠波次系统。</summary>
        public const string TestArenaSceneName = "Test_Arena";

        /// <summary>Test_Arena 一进来刷多少个敌人。50 个是 I1-11 压测的基线量级。</summary>
        public const int TestArenaEnemyCount = 50;

        /// <summary>临时刷怪环：玩家周围 8~15 米。太近会一开始就贴身，太远在 8 米射程外看不见开火。</summary>
        const float SpawnRadiusMin = 8f;
        const float SpawnRadiusMax = 15f;

        /// <summary>
        /// RunSeed 没被设置过（0）时用的世界种子。用固定常量而不是读系统时间：
        /// 同一份输入要能复现同一局，随机源必须能从外部钉死。
        /// </summary>
        public const ulong DefaultWorldSeed = 0x5EED1A2B3C4D5E6Ful;

        /// <summary>刷怪的流与战斗的流分开：以后加一段刷怪逻辑不会改变暴击序列。</summary>
        const ulong SpawnSeedSalt = 0x9E3779B97F4A7C15ul;

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
        public CameraRig Camera { get; private set; }

        public EnemySystem Enemies { get; private set; }
        public WeaponSystem Weapons { get; private set; }
        public RunSimulation Simulation { get; private set; }
        public EnemyViewPool EnemyViews { get; private set; }
        public ProjectileViewPool ProjectileViews { get; private set; }

        public bool ConfigOk { get; private set; }
        public string ConfigError { get; private set; }

        CharacterDef _character;
        DeterministicRandom _spawnRandom;

        // 从敌人表拍下来的有序快照。字典的遍历顺序不是契约，先按 Id 排一次，
        // 同一种子才会刷出同一批怪，而不是换台机器怪种就变了
        EnemyDef[] _spawnPool;

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

            // 临时入口：I2 的波次系统会取代它。现在只在 Test_Arena 里自动撒一批敌人，
            // Run 场景不刷——正式流程里刷怪归 WaveController，这里抢着刷会掩盖波次系统的行为。
            if (SceneManager.GetActiveScene().name == TestArenaSceneName)
            {
                SpawnTestEnemies(TestArenaEnemyCount);
            }
        }

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
            _spawnRandom = new DeterministicRandom(worldSeed ^ SpawnSeedSalt);
            _spawnPool = BuildSpawnPool();

            Enemies = new EnemySystem(Player, Player.ArenaHalfExtent);

            // 武器、暴击、闪避用同一条战斗流：同一种子 + 同一输入 = 同一次暴击
            var combatRandom = new DeterministicRandom(worldSeed);
            Weapons = new WeaponSystem(Enemies, combatRandom, PlayerUnit, 256, Events);

            AddStartingWeapon();

            Simulation = new RunSimulation(Player, PlayerController, Enemies, Weapons, Events);
            EnemyViews = new EnemyViewPool(_prefabMap, transform, Enemies, Events);
            ProjectileViews = new ProjectileViewPool(_prefabMap, Weapons.Projectiles, transform);
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

        EnemyDef[] BuildSpawnPool()
        {
            var pool = new List<EnemyDef>(Config.Enemies.Values);
            pool.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return pool.ToArray();
        }

        /// <summary>
        /// 临时入口，I2 的波次系统会取代它：正式流程里刷什么、刷多少、什么时候刷归 WaveController。
        /// 这里手动撒一批，好让"玩家自动射箭、敌群追人、被打掉血"在一个场景里就能看到。
        /// 位置取玩家周围 8~15 米的一圈，怪种从敌人表里随机；随机走固定种子的流，可复现。
        /// </summary>
        public void SpawnTestEnemies(int count)
        {
            if (Enemies == null || _spawnRandom == null || _spawnPool == null || _spawnPool.Length == 0)
            {
                Debug.LogWarning("[AppRoot] 刷怪条件没就绪（配置或敌人系统缺失），跳过");
                return;
            }

            Vector2 origin = Player.Position;
            for (int i = 0; i < count; i++)
            {
                float angle = _spawnRandom.Range(0f, Mathf.PI * 2f);
                float radius = _spawnRandom.Range(SpawnRadiusMin, SpawnRadiusMax);
                var offset = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

                EnemyDef def = _spawnPool[_spawnRandom.NextInt(_spawnPool.Length)];
                Enemies.Spawn(def, origin + offset);
            }

            Debug.Log($"[AppRoot] 刷了 {count} 个测试敌人（{TestArenaSceneName} 临时入口，I2 由波次系统接管）");
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
            view.Bind(Player, Run);
            instance.SetActive(true);
            Hud = view;
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
