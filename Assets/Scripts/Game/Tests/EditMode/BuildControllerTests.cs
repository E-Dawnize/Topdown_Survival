using System;
using System.Collections.Generic;
using Game.Config;
using Game.Controllers;
using Game.Model;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 升级构筑：抽卡去重、武器槽/等级上限、数值叠层、旅伴招募、buff 获取、重抽。
    /// 用 TestRun 的真实系统装配，不 mock 卡池，所以验的是"从抽到生效"的整条链。
    /// </summary>
    public class BuildControllerTests
    {
        [Test]
        public void DraftHasNoDuplicatesAndUsesConfiguredOfferCount()
        {
            TestRun run = TestRun.Create();
            run.Build.OpenDraft(1);

            Assert.AreEqual(3, run.Build.Options.Count);
            var ids = new HashSet<string>();
            foreach (BuildController.DraftOption option in run.Build.Options)
            {
                Assert.IsTrue(ids.Add(option.Def.Id), "同一轮不能出现重复卡：" + option.Def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(option.Title));
            }
        }

        [Test]
        public void RerollIsFreeOncePerLevel()
        {
            TestRun run = TestRun.Create();
            run.Build.OpenDraft(1);

            Assert.AreEqual(1, run.Build.RerollsLeft);
            Assert.IsTrue(run.Build.TryReroll());
            Assert.AreEqual(0, run.Build.RerollsLeft);
            Assert.IsFalse(run.Build.TryReroll());
        }

        [Test]
        public void WeaponUpgradeRaisesLevelAndScalesDamageAndCooldown()
        {
            TestRun run = TestRun.Create();

            bool picked = TryPick(run.Build, o => o.Def.Category == UpgradeCategory.Weapon &&
                                                   o.Def.WeaponId == "Bow");
            Assert.IsTrue(picked, "起始武器应当能被升级");

            var bow = run.Weapons.Find("Bow");
            Assert.AreEqual(2, bow.Level);
            Assert.Greater(bow.BaseDamage, 8f);
            Assert.Less(bow.Cooldown, 0.85f);

            // 从 2 级再升 3 次到 5 级；再往后不再出现这张卡
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(TryPick(run.Build, o => o.Def.WeaponId == "Bow"), "第 " + (i + 3) + " 级升级卡没出现");
            }

            Assert.AreEqual(5, bow.Level);
            Assert.IsTrue(bow.Projectiles > 1, "Lv3 里程碑应加 1 弹道");
            Assert.IsTrue(bow.Pierce > 1, "Lv4 里程碑应加 1 穿透");

            for (int i = 0; i < 20; i++)
            {
                run.Build.OpenDraft(1);
                foreach (BuildController.DraftOption option in run.Build.Options)
                {
                    Assert.AreNotEqual("Bow", option.Def.WeaponId, "满级武器不应再出现在卡池里");
                }
            }
        }

        [Test]
        public void WeaponSlotsCapNewWeapons()
        {
            TestRun run = TestRun.Create();

            // 测试配置只有两把武器（弓已持有、刀未持有）。把刀拿到手后，
            // 武器卡只能升级已有武器，不能再塞第三把不存在的武器。
            Assert.IsTrue(TryPick(run.Build, o => o.Def.WeaponId == "Sword"));
            Assert.AreEqual(2, run.Build.WeaponCount);

            for (int i = 0; i < 10; i++)
            {
                run.Build.OpenDraft(1);
                foreach (BuildController.DraftOption option in run.Build.Options)
                {
                    if (option.Def.Category != UpgradeCategory.Weapon) continue;
                    Assert.IsTrue(option.Def.WeaponId == "Bow" || option.Def.WeaponId == "Sword");
                }
            }
        }

        [Test]
        public void StatUpgradeStacksUntilMaxThenDisappears()
        {
            TestRun run = TestRun.Create();
            float baseline = run.Player.Stats[StatType.Attack];

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(TryPick(run.Build, o => o.Def.Id == "U_Stat_Attack"), "第 " + (i + 1) + " 层没出现");
            }

            Assert.AreEqual(10, run.Build.StatStacksOf("U_Stat_Attack"));
            Assert.AreEqual(baseline + 1f, run.Player.Stats[StatType.Attack], 1e-5f, "10 层 ×10% = +100%");

            for (int i = 0; i < 20; i++)
            {
                run.Build.OpenDraft(1);
                foreach (BuildController.DraftOption option in run.Build.Options)
                {
                    Assert.AreNotEqual("U_Stat_Attack", option.Def.Id, "满层卡不应再出现");
                }
            }
        }

        [Test]
        public void CompanionCanBeRecruitedThenLeveled()
        {
            TestRun run = TestRun.Create();

            Assert.IsTrue(TryPick(run.Build, o => o.Def.Category == UpgradeCategory.Companion &&
                                                   o.Def.CompanionId == "IronBox"));
            Assert.AreEqual(1, run.Companions.LevelOf("IronBox"));

            Assert.IsTrue(TryPick(run.Build, o => o.Def.CompanionId == "IronBox"));
            Assert.AreEqual(2, run.Companions.LevelOf("IronBox"));
            Assert.AreEqual(1, run.Companions.Count, "重复拿同一个旅伴是升级，不是多一个人");
        }

        [Test]
        public void BuffIsAppliedAndCanBeFoundInSystem()
        {
            TestRun run = TestRun.Create();

            Assert.IsTrue(TryPick(run.Build, o => o.Def.BuffId == "Frenzy"));
            Assert.AreEqual(1, run.Buffs.CountOf("Frenzy"));
            Assert.AreEqual(1, run.Build.BuffCardsOf("U_Buff_Frenzy"));

            // 测试配置里 Frenzy 的 UpgradeDef.MaxStacks = 1
            for (int i = 0; i < 20; i++)
            {
                run.Build.OpenDraft(1);
                foreach (BuildController.DraftOption option in run.Build.Options)
                {
                    Assert.AreNotEqual("U_Buff_Frenzy", option.Def.Id);
                }
            }
        }

        [Test]
        public void TagsTriggerComboAndGrantComboBuffOnce()
        {
            TestRun run = TestRun.Create();
            Assert.AreEqual(0, run.Build.ActiveComboCount);

            Assert.IsTrue(TryPick(run.Build, o => o.Def.Id == "U_Weapon_Bow"));
            Assert.AreEqual(0, run.Build.ActiveComboCount, "只有 ranged 标签，组合还不成立");

            Assert.IsTrue(TryPick(run.Build, o => o.Def.Id == "U_Stat_Attack"));
            run.Bus.Flush();

            Assert.AreEqual(1, run.Build.ActiveComboCount, "ranged + attack 应当触发测试组合");
            Assert.AreEqual(1, run.Buffs.CountOf("Frenzy"), "组合应当授予指定 buff");
            Assert.AreEqual("C_Test", run.Build.ActiveCombos[0].Id);

            // 再拿同类标签不会重复触发同一个组合
            Assert.IsTrue(TryPick(run.Build, o => o.Def.Id == "U_Stat_Attack"));
            Assert.AreEqual(1, run.Build.ActiveComboCount);
            Assert.AreEqual(1, run.Buffs.CountOf("Frenzy"));
        }

        [Test]
        public void ChoosingClearsOptionsSoOnePickPerDraft()
        {
            TestRun run = TestRun.Create();
            run.Build.OpenDraft(1);
            Assert.Greater(run.Build.Options.Count, 0);
            Assert.IsTrue(run.Build.TryChoose(0));
            Assert.AreEqual(0, run.Build.Options.Count);
            Assert.IsFalse(run.Build.TryChoose(0));
        }

        [Test]
        public void CompanionDetailPercentComesFromCompanionModelGrowth()
        {
            TestRun run = TestRun.Create();
            CompanionDef companion = run.Config.Companions["IronBox"];
            WeaponDef weapon = run.Config.Weapons[companion.WeaponId];

            // 显示用的每级伤害加成必须等于 CompanionModel 自己写进 StatBlock 的成长值。
            var probe = new CompanionModel(companion, weapon, 0, UnitId.None, 0f);
            probe.SetLevel(2);
            string expected = "伤害 +" + (probe.Stats[StatType.Attack] * 100f).ToString("0") + "%";

            Assert.IsTrue(TryPick(run.Build, o => o.Def.CompanionId == "IronBox"), "先招募该旅伴");

            for (int attempt = 0; attempt < 60; attempt++)
            {
                run.Build.OpenDraft(1);
                foreach (BuildController.DraftOption option in run.Build.Options)
                {
                    if (option.Def.CompanionId != "IronBox" || option.CurrentLevel != 1) continue;
                    StringAssert.Contains(expected, option.Detail);
                    return;
                }
            }

            Assert.Fail("招募后再抽 60 次都没出现该旅伴的升级卡");
        }

        [Test]
        public void DraftRollerSkipsExhaustedCardsAndNeverRepeatsInOneRound()
        {
            TestRun run = TestRun.Create();
            var state = new BuildState(run.Player, run.Config.Combos, run.Config.Buffs, run.Buffs, run.Bus);
            var roller = new DraftRoller(run.Player, run.State, run.Companions, run.Weapons, state, run.Config,
                new Game.Core.DeterministicRandom(TestRun.Seed));

            UpgradeDef stat = run.Config.Upgrades["U_Stat_Attack"];
            for (int i = 0; i < stat.MaxStacks; i++) state.ApplyStat(stat);

            var defs = new List<UpgradeDef>();
            var ids = new HashSet<string>();
            for (int round = 0; round < 20; round++)
            {
                roller.Roll(1, defs);
                Assert.AreEqual(3, defs.Count, "剩下 9 张卡时每轮应给满 3 张");

                ids.Clear();
                foreach (UpgradeDef def in defs)
                {
                    Assert.IsTrue(ids.Add(def.Id), "同一轮不能重复：" + def.Id);
                    Assert.AreNotEqual("U_Stat_Attack", def.Id, "满层数值卡不应再出现");
                }
            }
        }

        /// <summary>反复开卡直到出现满足条件的卡并选中。测试配置总共 10 张卡，20 次内必出现。</summary>
        static bool TryPick(BuildController build, Func<BuildController.DraftOption, bool> predicate)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                build.OpenDraft(1);
                for (int i = 0; i < build.Options.Count; i++)
                {
                    if (!predicate(build.Options[i])) continue;
                    return build.TryChoose(i);
                }
            }
            return false;
        }
    }
}
