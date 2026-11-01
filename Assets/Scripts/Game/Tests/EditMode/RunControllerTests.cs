using System.Collections.Generic;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 新流程状态机：Idle → Battle → LevelUp ⇄ Battle → Boss → GameOver。
    /// 只测 RunController 与事件顺序；刷怪、伤害、抽卡细节由各自系统测。
    /// </summary>
    public class RunControllerTests
    {
        [Test]
        public void StartRunPublishesRunStartedAndEntersBattle()
        {
            var bus = new EventBus();
            var player = new Game.Model.PlayerModel(1000f, 5f, 15f, 0.15f, 2f, 67f);
            var state = TestContent.NewRun();
            var config = TestContent.Config();
            var random = new Game.Core.DeterministicRandom(TestRun.Seed);
            var ids = new UnitIdAllocator(2);
            var enemies = new Game.Systems.EnemySystem(player, 67f, random, ids, bus);
            var weapons = new Game.Systems.Weapons.WeaponSystem(enemies, random, AppRoot.PlayerUnit, 64, bus);
            var companions = new Game.Systems.CompanionSystem(player, enemies, config.Weapons, random, bus, ids);
            var buffs = new Game.Systems.BuffSystem(player, bus, enemies, AppRoot.PlayerUnit);
            var director = new Game.Systems.DirectorSystem(enemies, config.Enemies, config.Directors, random, config, bus);
            var build = new Game.Controllers.BuildController(player, state, companions, weapons, buffs, config, bus,
                random, TestContent.Weapon("Bow"));
            var run = new RunController(player, state, config.Directors, bus, TestRun.Seed, "Scavenger", build,
                director, companions);

            var events = new List<string>();
            bus.Subscribe<RunStarted>(_ => events.Add("RunStarted"));

            run.StartRun();
            bus.Flush();

            Assert.AreEqual(RunPhase.Battle, run.Phase);
            Assert.AreEqual(1, state.Level);
            Assert.AreEqual(10f, run.BossTime, 1e-4f);
            CollectionAssert.AreEqual(new[] { "RunStarted" }, events);
        }

        [Test]
        public void TickInIdleDoesNothing()
        {
            TestRun run = TestRun.Create();
            run.Controller.Tick(1f);

            Assert.AreEqual(RunPhase.Battle, run.Controller.Phase, "StartRun 已经进战斗，Tick 不该把它拉回 Idle");
        }

        [Test]
        public void ExperiencePausesBattleUntilUpgradeChosen()
        {
            TestRun run = TestRun.Create();
            int phaseChanges = 0;
            run.Bus.Subscribe<LevelUpStarted>(_ => phaseChanges++);
            run.Bus.Subscribe<LevelUpClosed>(_ => phaseChanges++);

            run.State.AddExperience(5);
            run.Controller.Tick(TestRun.Dt);

            Assert.AreEqual(RunPhase.LevelUp, run.Controller.Phase);
            Assert.AreEqual(1, run.Build.Options.Count > 0 ? 1 : 0);
            Assert.IsTrue(run.Controller.ChooseUpgrade(0));
            run.Bus.Flush();

            Assert.AreEqual(RunPhase.Battle, run.Controller.Phase);
            Assert.AreEqual(2, phaseChanges, "一次升级要发 Started 与 Closed 各一次");
        }

        [Test]
        public void MultipleLevelUpsOfferMoreThanOneDraft()
        {
            TestRun run = TestRun.Create();
            run.State.AddExperience(20);
            Assert.GreaterOrEqual(run.State.PendingLevelUps, 2);

            run.Controller.Tick(TestRun.Dt);
            Assert.AreEqual(RunPhase.LevelUp, run.Controller.Phase);

            // 20 点经验在这张测试曲线上会跨 3 级，所以要连选 3 次才回战斗。
            int picks = 0;
            while (run.Controller.Phase == RunPhase.LevelUp && picks < 5)
            {
                Assert.IsTrue(run.Controller.ChooseUpgrade(0));
                picks++;
            }

            Assert.AreEqual(3, picks, "20 点经验跨 3 级，应该连续开 3 轮");
            Assert.AreEqual(RunPhase.Battle, run.Controller.Phase);
        }

        [Test]
        public void BossStartsWhenTimelineEndsAndKillingItWins()
        {
            TestRun run = TestRun.Create();

            var events = new List<string>();
            run.Bus.Subscribe<BossFightStarted>(_ => events.Add("BossStarted"));
            run.Bus.Subscribe<BossDefeated>(_ => events.Add("BossDefeated"));
            run.Bus.Subscribe<RunEnded>(e => events.Add(e.Victory ? "Win" : "Lose"));

            // 直接推进到 10 秒：最后一段结束，切 BOSS 阶段并从外围生成 BOSS。
            run.AdvanceSeconds(10.2f);

            Assert.AreEqual(RunPhase.Boss, run.Controller.Phase);
            Assert.IsTrue(run.Controller.BossStarted);
            Assert.IsTrue(run.Controller.BossActive);
            Assert.AreEqual("缝线巨偶", run.Controller.BossName);
            CollectionAssert.AreEqual(new[] { "BossStarted" }, events);

            Assert.IsTrue(run.Enemies.TryGetSlot(run.Director.BossUnitId, out int slot));
            run.Enemies.Get(slot).HP = 0f;
            run.Step(2);

            Assert.AreEqual(RunPhase.GameOver, run.Controller.Phase);
            Assert.IsTrue(run.Controller.BossDefeated);
            CollectionAssert.AreEqual(new[] { "BossStarted", "BossDefeated", "Win" }, events);
        }

        [Test]
        public void PlayerDeathEndsTheRunWithoutBoss()
        {
            TestRun run = TestRun.Create(hp: 10f);
            var endings = new List<string>();
            run.Bus.Subscribe<RunEnded>(e => endings.Add(e.Victory ? "Win" : "Lose"));

            run.Player.HP = 0f;
            run.Controller.Tick(TestRun.Dt);
            run.Bus.Flush();

            Assert.AreEqual(RunPhase.GameOver, run.Controller.Phase);
            CollectionAssert.AreEqual(new[] { "Lose" }, endings);
        }

        [Test]
        public void ChooseUpgradeOutsideLevelUpIsIgnored()
        {
            TestRun run = TestRun.Create();
            Assert.IsFalse(run.Controller.ChooseUpgrade(0));
            Assert.AreEqual(RunPhase.Battle, run.Controller.Phase);
        }

        [Test]
        public void StartRunAfterGameOverIsRejected()
        {
            TestRun run = TestRun.Create(hp: 10f);
            run.Player.HP = 0f;
            run.Controller.Tick(TestRun.Dt);
            Assert.AreEqual(RunPhase.GameOver, run.Controller.Phase);

            Assert.Throws<System.InvalidOperationException>(() => run.Controller.StartRun());
        }

        [Test]
        public void BossTimeIsLastDirectorEnd()
        {
            TestRun run = TestRun.Create();
            Assert.AreEqual(10f, run.Controller.BossTime, 1e-4f);
            Assert.AreEqual(10f, run.Director.BossTime, 1e-4f);
        }

        [Test]
        public void ThreatLevelFollowsTheTimelineNotTheSpawnedEnemyTier()
        {
            TestRun run = TestRun.Create();
            Assert.AreEqual(1, run.State.HighestThreat);

            run.AdvanceSeconds(6f);

            // 第二段 Threat=2，但这一段的怪本身只有 Tier 1/2；HUD 读的必须是时间轴的威胁等级。
            Assert.AreEqual(2, run.State.HighestThreat);
        }

        [Test]
        public void BossStateIsSingleSourcedFromBossFight()
        {
            TestRun run = TestRun.Create();
            run.AdvanceSeconds(10.2f);

            Assert.AreSame(run.Director.BossFight, run.Controller.BossFight, "流程与导演必须读同一个 BOSS 对象");
            Assert.AreEqual(run.Director.BossActive, run.Controller.BossActive);
            Assert.AreEqual(run.Director.BossName, run.Controller.BossName);
            Assert.AreEqual(run.Director.BossHpRatio, run.Controller.BossHpRatio, 1e-6f);
            Assert.IsFalse(run.Controller.BossDefeated);

            Assert.IsTrue(run.Enemies.TryGetSlot(run.Director.BossUnitId, out int slot));
            run.Enemies.Get(slot).HP = 0f;
            run.Step(2);
            run.Bus.Flush();

            Assert.IsTrue(run.Controller.BossDefeated);
            Assert.AreEqual(run.Director.BossDefeated, run.Controller.BossDefeated);
        }
    }
}
