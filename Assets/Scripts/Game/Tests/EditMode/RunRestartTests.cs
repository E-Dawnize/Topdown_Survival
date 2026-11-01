using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Model;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 重开一局的状态干净性。正式重开是场景重载；这里每轮重新装配一套对象，
    /// 另外直接验 BuildController/CompanionSystem/BuffSystem 的 ResetForNewRun 回滚。
    /// </summary>
    public class RunRestartTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void ThreeFreshRunsStartClean()
        {
            for (int restart = 0; restart <= 3; restart++)
            {
                TestRun run = TestRun.Create(hp: 100f);

                Assert.AreEqual(1, run.State.Level, "第 " + restart + " 次重开：等级从 1 开始");
                Assert.AreEqual(0, run.State.Xp, "第 " + restart + " 次重开：经验从 0 开始");
                Assert.AreEqual(0, run.State.Kills, "第 " + restart + " 次重开：击杀数不跨局");
                Assert.AreEqual(1, run.Build.WeaponCount, "第 " + restart + " 次重开：只带起始武器");
                Assert.AreEqual(0, run.Build.CompanionCount, "第 " + restart + " 次重开：队伍为空");
                Assert.AreEqual(0, run.Build.BuffCardCount, "第 " + restart + " 次重开：没有 buff");
                Assert.AreEqual(100f, run.Player.MaxHP, 1e-4f, "第 " + restart + " 次重开：属性回到基线");

                // 打一阵、升一级、选一张卡，再死亡收尾。
                run.AdvanceSeconds(2f);
                run.State.AddExperience(5);
                run.Controller.Tick(Dt);
                if (run.Controller.Phase == RunPhase.LevelUp) run.Controller.ChooseUpgrade(0);
                run.Player.HP = 0f;
                run.Controller.Tick(Dt);

                Assert.AreEqual(RunPhase.GameOver, run.Controller.Phase, "第 " + restart + " 局没有正常收尾");
            }
        }

        [Test]
        public void ResetForNewRunRollsBackStatUpgrades()
        {
            TestRun run = TestRun.Create();
            float baseAttack = run.Player.Stats[StatType.Attack];
            float baseMaxHp = run.Player.MaxHP;

            bool applied = false;
            for (int attempt = 0; attempt < 100 && !applied; attempt++)
            {
                run.Build.OpenDraft(1);
                for (int i = 0; i < run.Build.Options.Count; i++)
                {
                    if (run.Build.Options[i].Def.Category != UpgradeCategory.Stat) continue;
                    applied = run.Build.TryChoose(i);
                    break;
                }
            }

            Assert.IsTrue(applied, "100 轮抽卡里至少要出现一张数值卡");
            Assert.IsTrue(run.Player.Stats[StatType.Attack] > baseAttack ||
                          run.Player.MaxHP > baseMaxHp, "数值卡应该立刻生效");

            run.Build.ResetForNewRun();

            Assert.AreEqual(baseAttack, run.Player.Stats[StatType.Attack], 1e-6f, "换局后攻击力回到基线");
            Assert.AreEqual(baseMaxHp, run.Player.MaxHP, 1e-6f, "换局后生命上限回到基线");
            Assert.AreEqual(1, run.Build.WeaponCount, "换局后只保留起始武器");
            Assert.AreEqual(0, run.Build.StatUpgradeCount, "数值卡层数清零");
        }

        [Test]
        public void ResetForNewRunRollsBackBuffStats()
        {
            TestRun run = TestRun.Create(hp: 100f);
            var config = TestContent.Config();
            BuffDef glassCannon = config.Buffs["GlassCannon"];
            run.Buffs.Acquire(glassCannon);

            float boostedAttack = run.Player.Stats[StatType.Attack];
            float reducedMaxHp = run.Player.MaxHP;
            Assert.Greater(boostedAttack, 0f);
            Assert.Less(reducedMaxHp, 100f);

            run.Build.ResetForNewRun();

            Assert.AreEqual(0f, run.Player.Stats[StatType.Attack], 1e-6f);
            Assert.AreEqual(100f, run.Player.MaxHP, 1e-6f);
            Assert.AreEqual(0, run.Build.BuffCardCount);
        }

        [Test]
        public void ResetForNewRunClearsCompanionsAndBonds()
        {
            TestRun run = TestRun.Create();
            var config = TestContent.Config();
            run.Companions.Recruit(config.Companions["IronBox"]);
            run.Companions.Recruit(new CompanionDef
            {
                Id = "Gearling", Name = "齿轮崽", Faction = CompanionFaction.Machine, WeaponId = "Bow",
                FollowRadius = 1.5f, MaxHP = 40f
            });
            Assert.Greater(run.Companions.ActiveBonds.Count, 0, "两个机械旅伴应该触发一级羁绊");

            run.Build.ResetForNewRun();

            Assert.AreEqual(0, run.Companions.Count);
            Assert.AreEqual(0, run.Companions.ActiveBonds.Count);
            Assert.AreEqual(1, run.Build.WeaponCount);
        }
    }
}
