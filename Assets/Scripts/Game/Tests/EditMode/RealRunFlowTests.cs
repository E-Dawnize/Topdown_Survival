using System.Diagnostics;
using System.IO;
using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 用仓库里真实导出的配置跑完整的时间轴（10 分钟 + BOSS）。
    /// 玩家血量与拾取范围都拉满：目的是验证刷怪、经验、升级暂停、BOSS 切换这条链，
    /// 不是验证数值平衡。BOSS 出场后直接打空它的血，确认胜利收尾。
    /// </summary>
    public class RealRunFlowTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;

        [Test]
        public void TenMinutesOfDirectorThenBossEndsInVictory()
        {
            string root = FindConfigDirectory();
            if (root == null) Assert.Ignore("找不到配置目录");
            ConfigDatabase config = ConfigDatabase.Load(new DiskSource(root));

            var bus = new EventBus();
            var player = new PlayerModel(100000000f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            // 让经验自动飞到玩家身上，否则站桩测试永远升不了级。
            player.Stats.SetBase(StatType.PickupRadius, 100f);

            var state = new RunState(new ExperienceCurve(config.Levels));
            var random = new DeterministicRandom(0x5EED1A2B3C4D5E6Ful);
            var ids = new UnitIdAllocator(2);

            var enemies = new EnemySystem(player, ArenaHalf, random, ids, bus);
            var weapons = new WeaponSystem(enemies, random, AppRoot.PlayerUnit, 512, bus,
                WeaponUpgradeRules.FromConfig(config));
            var companions = new CompanionSystem(player, enemies, config.Weapons, random, bus, ids);
            var buffs = new BuffSystem(player, bus);
            var director = new DirectorSystem(enemies, config.Enemies, config.Directors, random, config, bus);
            var pickups = new PickupSystem(player, state, bus, random,
                config.GetBalance("HeartDropChance", 0.12f), config.GetBalance("HeartHeal", 8f));

            string weaponId = config.Characters["Scavenger"].StartingWeaponId;
            config.Weapons.TryGetValue(weaponId, out WeaponDef startingWeapon);

            var build = new BuildController(player, state, companions, weapons, buffs, config, bus, random,
                startingWeapon);
            var run = new RunController(player, state, config.Directors, bus, TestRun.Seed, "Scavenger", build,
                director, companions);
            var simulation = new RunSimulation(player, new PlayerController(player, bus), enemies, weapons, bus,
                run, director, pickups, companions, buffs);

            run.StartRun();
            bus.Flush();

            var stopwatch = Stopwatch.StartNew();
            var input = new InputSnapshot();
            int steps = 0;
            int maxAlive = 0;
            int levels = 0;

            while (run.Phase != RunPhase.Boss && run.Phase != RunPhase.GameOver && steps < 60 * 620)
            {
                simulation.Tick(Dt, ref input);
                steps++;
                if (enemies.AliveCount > maxAlive) maxAlive = enemies.AliveCount;

                while (run.Phase == RunPhase.LevelUp)
                {
                    levels++;
                    Assert.IsTrue(run.ChooseUpgrade(0), "升级卡应当能选中");
                    bus.Flush();
                }
            }

            stopwatch.Stop();
            TestContext.Progress.WriteLine("[真实配置全流程] " + steps + " 步，" + stopwatch.ElapsedMilliseconds +
                                          " ms，等级 " + state.Level + "，升级次数 " + levels +
                                          "，最高同屏 " + maxAlive + "，击杀 " + state.Kills +
                                          "，武器 " + build.WeaponCount + "，旅伴 " + build.CompanionCount +
                                          "，组合 " + build.ActiveComboCount + "，buff " + buffs.Count);

            Assert.AreEqual(RunPhase.Boss, run.Phase, "600 秒后必须进入 BOSS 阶段");
            Assert.IsTrue(run.BossStarted);
            Assert.Greater(state.Level, 1, "站桩吸经验也应该升级");
            Assert.Greater(state.Kills, 0);
            Assert.Greater(build.ActiveComboCount, 0, "真实配置下至少应触发一个组合效果");
            Assert.LessOrEqual(maxAlive, config.Directors[config.Directors.Count - 1].ConcurrentCap + 12,
                "同屏数量不应大幅超过导演上限（BOSS 召唤的少量额外单位除外）");

            // BOSS 出场后测试不需要真的磨血，直接清空并走同一条死亡/清理/结算路径。
            Assert.IsTrue(enemies.TryGetSlot(director.BossUnitId, out int bossSlot));
            enemies.Get(bossSlot).HP = 0f;
            simulation.Tick(Dt, ref input);
            simulation.Tick(Dt, ref input);
            bus.Flush();

            Assert.AreEqual(RunPhase.GameOver, run.Phase);
            Assert.IsTrue(run.BossDefeated);
        }

        static string FindConfigDirectory()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Config");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        sealed class DiskSource : IConfigSource
        {
            readonly string _root;

            public DiskSource(string root)
            {
                _root = root;
            }

            public bool TryRead(string fileName, out string text)
            {
                string path = Path.Combine(_root, fileName);
                if (!File.Exists(path))
                {
                    text = null;
                    return false;
                }

                text = File.ReadAllText(path);
                return true;
            }
        }
    }
}
