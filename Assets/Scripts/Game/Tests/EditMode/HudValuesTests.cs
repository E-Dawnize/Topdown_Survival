using Game.Views;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// HUD 的"值没变就不刷新"用 HudValues.Diff 表达：每帧取快照，只有变了的那一组才写 uGUI。
    /// 这里直接构造快照，验位标记不会漏项、不会把没变的项算脏。
    ///
    /// 快照还负责把展示所需的一切带齐（是否还有玩家、冲刺进度、BOSS 名字/狂暴、是否冻结），
    /// 所以这里也验这些字段会被算进对应组，免得 HudView 又回头去读 Model。
    /// </summary>
    public class HudValuesTests
    {
        static HudValues Snapshot(int hp = 80, int maxHp = 100, bool hasPlayer = true, int level = 3, int xp = 4,
            int xpToNext = 10, int secondsLeft = 30, int threat = 2, int kills = 7, int alive = 5, int dash = 0,
            float dashRatio = 1f, int weapons = 2, int weaponSlots = 4, int companions = 1,
            int companionSlots = 4, int combos = 0, bool boss = false, float bossHp = 0f,
            string bossName = "", bool bossEnraged = false, bool frozen = false, int buildRevision = 0)
        {
            return new HudValues(hp, maxHp, maxHp > 0 ? (float)hp / maxHp : 0f, hasPlayer, level, xp, xpToNext,
                xpToNext > 0 ? (float)xp / xpToNext : 0f, secondsLeft, threat, kills, alive, dash, dashRatio,
                weapons, weaponSlots, companions, companionSlots, combos, boss, bossHp, bossName, bossEnraged,
                frozen, buildRevision);
        }

        [Test]
        public void IdenticalValuesReportNoChange()
        {
            HudValues values = Snapshot();

            Assert.AreEqual(HudField.None, values.Diff(values));
            Assert.AreEqual(HudField.None, Snapshot().Diff(values));
        }

        [Test]
        public void EachFieldReportsOnlyItsOwnFlag()
        {
            HudValues baseline = Snapshot();

            Assert.AreEqual(HudField.Hp, Snapshot(hp: 79).Diff(baseline), "生命");
            Assert.AreEqual(HudField.Hp, Snapshot(hasPlayer: false).Diff(baseline), "玩家缺席要让血条文字换掉");
            Assert.AreEqual(HudField.Progress, Snapshot(xp: 5).Diff(baseline), "经验");
            Assert.AreEqual(HudField.Progress, Snapshot(level: 4).Diff(baseline), "等级");
            Assert.AreEqual(HudField.RunTime, Snapshot(secondsLeft: 29).Diff(baseline), "倒计时");
            Assert.AreEqual(HudField.RunTime, Snapshot(threat: 3).Diff(baseline), "威胁");
            Assert.AreEqual(HudField.Dash, Snapshot(dash: 18, dashRatio: 0.1f).Diff(baseline), "冲刺冷却");
            Assert.AreEqual(HudField.Dash, Snapshot(dashRatio: 0.5f).Diff(baseline), "冲刺进度条");
            Assert.AreEqual(HudField.AliveEnemies, Snapshot(alive: 6).Diff(baseline), "存活敌人");
            Assert.AreEqual(HudField.Kills, Snapshot(kills: 8).Diff(baseline), "击杀");
            Assert.AreEqual(HudField.Build, Snapshot(weapons: 3).Diff(baseline), "武器数");
            Assert.AreEqual(HudField.Build, Snapshot(companions: 2).Diff(baseline), "旅伴数");
            Assert.AreEqual(HudField.Build, Snapshot(buildRevision: 1).Diff(baseline),
                "武器/旅伴升级、组合触发要让装备栏重拼");
            Assert.AreEqual(HudField.Boss, Snapshot(boss: true, bossHp: 1f).Diff(baseline), "BOSS 条");
            Assert.AreEqual(HudField.Boss, Snapshot(bossName: "深渊守望").Diff(baseline), "BOSS 名字");
            Assert.AreEqual(HudField.Boss, Snapshot(boss: true, bossHp: 1f, bossEnraged: true).Diff(
                Snapshot(boss: true, bossHp: 1f)), "狂暴后缀");
        }

        [Test]
        public void SeveralChangesAccumulateIntoOneDirtyMask()
        {
            HudValues baseline = Snapshot();
            HudValues next = Snapshot(xp: 8, kills: 9, weapons: 4);

            Assert.AreEqual(HudField.Progress | HudField.Kills | HudField.Build, next.Diff(baseline));
        }

        [Test]
        public void FrozenIsControlStateAndDoesNotDirtyAnyField()
        {
            // 冻结只影响 HudView 要不要写控件；它本身不是显示项，不该混进差集里。
            HudValues baseline = Snapshot();
            HudValues frozen = Snapshot(frozen: true);

            Assert.AreEqual(HudField.None, frozen.Diff(baseline));
            Assert.IsTrue(frozen.Frozen);
        }

        [Test]
        public void MissingDependenciesProduceASafeSnapshot()
        {
            // 装配半途失败（没有玩家/系统）时也能取一份能直接写进控件的快照，不抛异常、不出现空名字。
            HudValues values = HudValues.From(null, null, null, null, null, null, bossEnraged: false);

            Assert.IsFalse(values.HasPlayer);
            Assert.IsFalse(values.Frozen);
            Assert.AreEqual(0f, values.HpRatio);
            Assert.AreEqual(0f, values.DashRatio);
            Assert.AreEqual(string.Empty, values.BossName);
            Assert.IsFalse(values.BossActive);
            Assert.AreEqual(1, values.Level);
            Assert.AreEqual(1, values.Threat);
        }
    }
}
