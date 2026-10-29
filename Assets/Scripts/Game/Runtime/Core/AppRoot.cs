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
    /// 场景里的装配根。只有它能 new 系统、加载配置、持有游戏级引用。
    /// 其余代码通过构造注入拿到依赖，不查静态单例。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AppRoot : MonoBehaviour
    {
        public static AppRoot Instance { get; private set; }

        [SerializeField] bool _loadConfigOnAwake = true;
        [SerializeField] string _characterId = "Scavenger";

        public EventBus Events { get; private set; }
        public ConfigDatabase Config { get; private set; }
        public InputService Input { get; private set; }
        public ulong RunSeed { get; private set; }

        public PlayerModel Player { get; private set; }
        public PlayerController PlayerController { get; private set; }
        public PlayerView PlayerView { get; private set; }
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
            Input = GetComponent<InputService>();

            if (_loadConfigOnAwake)
            {
                TryLoadConfig();
            }

            CreatePlayer();
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

        void CreatePlayer()
        {
            if (Config == null) return;

            if (!Config.Characters.TryGetValue(_characterId, out CharacterDef def))
            {
                Debug.LogError($"[AppRoot] 角色表里没有 '{_characterId}'");
                return;
            }

            float arenaHalf = Config.GetBalance("ArenaHalfExtent", 67f);
            float dashMultiplier = Config.GetBalance("DashSpeedMultiplier", 3f);
            float dashDuration = Config.GetBalance("DashDuration", 0.15f);
            float dashCooldown = Config.GetBalance("DashCooldown", 2f);

            Player = new PlayerModel(
                def.BaseMoveSpeed,
                def.BaseMoveSpeed * dashMultiplier,
                dashDuration,
                dashCooldown,
                arenaHalf);

            PlayerController = new PlayerController(Player, Events);
            PlayerView?.Bind(Player);

            Debug.Log($"[AppRoot] 玩家就绪 移速 {def.BaseMoveSpeed} 冲刺 {Player.DashSpeed}" +
                      $" 边界 ±{arenaHalf}");
        }

        public void BindPlayerView(PlayerView view)
        {
            PlayerView = view;
            view.Bind(Player);
        }

        public void BindCameraRig(CameraRig rig)
        {
            Camera = rig;
        }
    }
}
