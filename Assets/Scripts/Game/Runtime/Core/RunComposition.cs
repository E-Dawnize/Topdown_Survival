using System;
using Game.Config;
using Game.Controllers;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;

namespace Game.Core
{
    /// <summary>
    /// 一局的纯 C# 对象图。场景装配与测试共用这一份，避免系统构造参数在两处漂移。
    /// 本类不创建 GameObject，也不读取场景；预制体、视图与输入仍由 AppRoot 装配。
    /// </summary>
    public sealed class RunComposition
    {
        public const int DefaultProjectilePoolCapacity = 512;

        public ConfigDatabase Config { get; }
        public CharacterDef Character { get; }
        public WeaponDef StartingWeapon { get; }
        public ulong Seed { get; }
        public string CharacterId { get; }

        public EventBus Events { get; }
        public RunState Run { get; }
        public PlayerModel Player { get; }
        public PlayerController PlayerController { get; }
        public EnemySystem Enemies { get; }
        public WeaponSystem Weapons { get; }
        public CompanionSystem Companions { get; }
        public BuffSystem Buffs { get; }
        public DirectorSystem Director { get; }
        public PickupSystem Pickups { get; }
        public BuildController Build { get; }
        public RunController RunController { get; }
        public RunSimulation Simulation { get; }

        RunComposition(ConfigDatabase config, string characterId, ulong seed,
            int projectilePoolCapacity, float? playerMaxHp)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
            Seed = seed;

            if (!config.Characters.TryGetValue(characterId, out CharacterDef character))
            {
                throw new ArgumentException("角色表里没有 '" + characterId + "'", nameof(characterId));
            }

            Character = character;
            config.Weapons.TryGetValue(character.StartingWeaponId ?? string.Empty, out WeaponDef startingWeapon);
            StartingWeapon = startingWeapon;

            Events = new EventBus();

            float arenaHalf = config.GetBalance("ArenaHalfExtent", 67f);
            float dashMultiplier = config.GetBalance("DashSpeedMultiplier", 3f);
            float dashDuration = config.GetBalance("DashDuration", 0.15f);
            float dashCooldown = config.GetBalance("DashCooldown", 2f);
            float maxHp = playerMaxHp ?? character.BaseHP;
            float collectCooldown = config.GetBalance("ExperienceCollectCooldown",
                PlayerModel.DefaultExperienceCollectCooldown);

            Player = new PlayerModel(maxHp, character.BaseMoveSpeed,
                character.BaseMoveSpeed * dashMultiplier, dashDuration, dashCooldown, arenaHalf, collectCooldown);
            PlayerController = new PlayerController(Player, Events);
            Run = new RunState(new ExperienceCurve(config.Levels));

            // 同一种子 + 同一输入必须复现同一局。武器、刷怪、掉落共用这条战斗随机流。
            var combatRandom = new DeterministicRandom(seed);
            var unitIds = new UnitIdAllocator(2);
            var weaponRules = WeaponUpgradeRules.FromConfig(config);

            Enemies = new EnemySystem(Player, Player.ArenaHalfExtent, combatRandom, unitIds, Events);
            Weapons = new WeaponSystem(Enemies, combatRandom, UnitId.Player, projectilePoolCapacity,
                Events, weaponRules);
            Companions = new CompanionSystem(Player, Enemies, config.Weapons, combatRandom, Events, unitIds);
            Buffs = new BuffSystem(Player, Events, Enemies, UnitId.Player);
            Director = new DirectorSystem(Enemies, config.Enemies, config.Directors, combatRandom, config, Events);
            Pickups = new PickupSystem(Player, Run, Events, combatRandom,
                config.GetBalance("HeartDropChance", 0.09f), config.GetBalance("HeartHeal", 8f));

            Build = new BuildController(Player, Run, Companions, Weapons, Buffs, config, Events,
                combatRandom, startingWeapon);
            RunController = new RunController(Player, Run, config.Directors, Events, seed, characterId,
                Build, Director, Companions);
            Simulation = new RunSimulation(Player, PlayerController, Enemies, Weapons, Events,
                RunController, Director, Pickups, Companions, Buffs);
        }

        /// <summary>
        /// playerMaxHp 只给流程测试提高生存上限；正式装配传 null，完全使用角色表。
        /// </summary>
        public static RunComposition Create(ConfigDatabase config, string characterId, ulong seed,
            int projectilePoolCapacity = DefaultProjectilePoolCapacity, float? playerMaxHp = null)
        {
            if (projectilePoolCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(projectilePoolCapacity));
            }

            return new RunComposition(config, characterId, seed, projectilePoolCapacity, playerMaxHp);
        }
    }
}
