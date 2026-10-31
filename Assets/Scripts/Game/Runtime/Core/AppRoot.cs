using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Services;
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

        public bool ConfigOk { get; private set; }
        public string ConfigError { get; private set; }

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

            if (ConfigOk)
            {
                ValidateContent();
                CreatePlayer();
                CreateHud();
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
        /// 敌人缺失先给警告——I1 阶段还有两个敌人没有预制体。
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

        void CreatePlayer()
        {
            if (Config == null || _prefabMap == null) return;

            if (!Config.Characters.TryGetValue(_characterId, out CharacterDef def))
            {
                Debug.LogError($"[AppRoot] 角色表里没有 '{_characterId}'");
                return;
            }

            GameObject prefab = _prefabMap.Find(PlayerPrefabId);
            if (prefab == null)
            {
                Debug.LogError($"[AppRoot] UnitPrefabMap 里没有 '{PlayerPrefabId}' 预制体，" +
                               "先跑一次 Tools/Game/生成预制体");
                return;
            }

            float arenaHalf = Config.GetBalance("ArenaHalfExtent", 67f);
            float dashMultiplier = Config.GetBalance("DashSpeedMultiplier", 3f);
            float dashDuration = Config.GetBalance("DashDuration", 0.15f);
            float dashCooldown = Config.GetBalance("DashCooldown", 2f);

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

            Debug.Log($"[AppRoot] 玩家就绪 生命 {Player.MaxHP} 移速 {def.BaseMoveSpeed}" +
                      $" 冲刺 {Player.DashSpeed} 边界 ±{arenaHalf}");
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
