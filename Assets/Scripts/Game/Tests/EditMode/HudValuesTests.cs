using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// HUD 的"值没变就不刷新"（I2-9）用 <see cref="HudValues.Diff"/> 表达：每帧取一份快照，
    /// 与上一份比，只有比出来的那几项才写 uGUI。所以这条性质可以在这里直接测，不必进 Unity 数脏标记。
    ///
    /// 顺带钉住取数口：快照必须从 Model / Controller 的只读接口取（血量比例、波次剩余时间、
    /// 击杀数都不是 HUD 自己算的），否则改了规则以后 HUD 会显示一套、模拟跑另一套。
    /// </summary>
    public class HudValuesTests
    {
        const float Dt = 1f / 60f;
        const ulong Seed = 0x11D00000ABCD1234UL;
        const float ArenaHalf = 67f;

        [Test]
        public void IdenticalValuesReportNoChange()
        {
            HudValues values = Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3, secondsLeft: 30,
                dashTenths: 0, alive: 5, kills: 7);

            Assert.AreEqual(HudField.None, values.Diff(values), "同一个快照跟自己比不该有任何一项算变化");
            Assert.IsTrue(values.Equals(Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 5, kills: 7)));
        }

        [Test]
        public void EachFieldReportsOnlyItsOwnFlag()
        {
            HudValues baseline = Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3, secondsLeft: 30,
                dashTenths: 0, alive: 5, kills: 7);

            Assert.AreEqual(HudField.Hp, Snapshot(hp: 79, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 5, kills: 7).Diff(baseline), "血量");

            Assert.AreEqual(HudField.Hp, Snapshot(hp: 80, maxHp: 110, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 5, kills: 7).Diff(baseline), "生命上限变了，血条比例也变了");

            Assert.AreEqual(HudField.Coins, Snapshot(hp: 80, maxHp: 100, coins: 11, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 5, kills: 7).Diff(baseline), "金币");

            Assert.AreEqual(HudField.Wave, Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 3, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 5, kills: 7).Diff(baseline), "波次");

            Assert.AreEqual(HudField.WaveTime, Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 29, dashTenths: 0, alive: 5, kills: 7).Diff(baseline), "波次倒计时");

            Assert.AreEqual(HudField.Dash, Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 18, alive: 5, kills: 7).Diff(baseline), "冲刺冷却");

            Assert.AreEqual(HudField.AliveEnemies, Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 6, kills: 7).Diff(baseline), "存活敌人数");

            Assert.AreEqual(HudField.Kills, Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3,
                secondsLeft: 30, dashTenths: 0, alive: 5, kills: 8).Diff(baseline), "击杀数");
        }

        [Test]
        public void SeveralChangesAccumulateIntoOneDirtyMask()
        {
            HudValues baseline = Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3, secondsLeft: 30,
                dashTenths: 0, alive: 5, kills: 7);

            HudValues next = Snapshot(hp: 80, maxHp: 100, coins: 40, wave: 2, wavesTotal: 3, secondsLeft: 30,
                dashTenths: 0, alive: 5, kills: 9);

            Assert.AreEqual(HudField.Coins | HudField.Kills, next.Diff(baseline),
                "一次变两项就要两项都写，不能写成\"变了就全刷\"或\"变了只刷第一项\"");
        }

        /// <summary>
        /// 快照的取数口：血、比例、金币、波次、倒计时、存活敌人数、击杀数全都来自 Model / Controller。
        /// 这里用真实的 PlayerModel / RunState / RunController / EnemySystem 拼一份，避免自己造数自己验。
        /// </summary>
        [Test]
        public void SnapshotReadsTheModelInsteadOfRecomputingIt()
        {
            var bus = new EventBus();
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var state = new RunState();
            var combatRandom = new DeterministicRandom(Seed);
            var enemies = new EnemySystem(player, ArenaHalf, combatRandom);

            var run = new RunController(player, state, Waves(60f), bus, Seed, "Scavenger");
            run.StartRun();
            bus.Flush();

            // 推进 30 秒：波次倒计时应该剩 30 秒
            int steps = Mathf.RoundToInt(30f / Dt);
            for (int i = 0; i < steps; i++) run.Tick(Dt);

            player.HP = 60f;
            state.AddCoins(42);
            enemies.Spawn(CrawlerDef(), new Vector2(1f, 0f));
            enemies.Spawn(CrawlerDef(), new Vector2(2f, 0f));
            enemies.Spawn(CrawlerDef(), new Vector2(3f, 0f));

            bus.Publish(new UnitDied(new UnitId(2), UnitId.None, 0f, 0f));
            bus.Flush();

            HudValues values = HudValues.From(player, state, run, enemies);

            Assert.AreEqual(60, values.Hp);
            Assert.AreEqual(100, values.MaxHp);
            Assert.AreEqual(0.6f, values.HpRatio, 1e-5f);
            Assert.AreEqual(42, values.Coins);
            Assert.AreEqual(1, values.WaveIndex);
            Assert.AreEqual(1, values.WavesTotal, "这一波测试只搭了配置里的一波");
            Assert.AreEqual(30, values.WaveSecondsLeft, "60 秒的波跑了 30 秒，剩 30 秒（向上取整）");
            Assert.AreEqual(3, values.AliveEnemies);
            Assert.AreEqual(1, values.Kills, "击杀数读 RunController 数到的那一个");

            // 什么都没变的下一帧：不产生任何写入
            Assert.AreEqual(HudField.None, HudValues.From(player, state, run, enemies).Diff(values));
        }

        [Test]
        public void DashTenthsClipToZeroWhenReady()
        {
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
            var state = new RunState();
            state.WavesTotal = 3;

            player.DashCooldownTimer = 0f;
            Assert.AreEqual(0, HudValues.From(player, state, null, null).DashTenths, "冷却为 0 显示就绪");

            player.DashCooldownTimer = 0.04f;
            Assert.AreEqual(1, HudValues.From(player, state, null, null).DashTenths,
                "0.04 秒的残余要按 1 档算：舍成 0 就再也不会因为\"变成 0\"刷成\"就绪\"");

            player.DashCooldownTimer = 1.24f;
            Assert.AreEqual(13, HudValues.From(player, state, null, null).DashTenths);
        }

        /// <summary>
        /// 商店与结算里模拟不推进，HUD 只让金币跟着走（买入、刷新会改它），其余战斗数值一概不写。
        /// 这条掩码就是 HudView 里那一步，写成断言免得以后改回"每项都刷"。
        /// </summary>
        [Test]
        public void FrozenPhaseOnlyLetsCoinsThrough()
        {
            HudValues baseline = Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3, secondsLeft: 30,
                dashTenths: 0, alive: 5, kills: 7);

            HudValues next = Snapshot(hp: 70, maxHp: 100, coins: 20, wave: 2, wavesTotal: 3, secondsLeft: 0,
                dashTenths: 0, alive: 0, kills: 7);

            HudField dirty = next.Diff(baseline) & HudField.Coins;

            Assert.AreEqual(HudField.Coins, dirty, "冻结期间只有金币能进刷新");
        }

        /// <summary>
        /// 冻结期间没写的项必须留在"上一次写过"的基准里：
        /// 否则解冻那一帧的差值算不出来，血条与倒计时会一直停在冻结前的数字。
        /// </summary>
        [Test]
        public void MergeKeepsFieldsThatWereNotWritten()
        {
            HudValues shown = Snapshot(hp: 80, maxHp: 100, coins: 10, wave: 2, wavesTotal: 3, secondsLeft: 30,
                dashTenths: 0, alive: 5, kills: 7);
            HudValues next = Snapshot(hp: 70, maxHp: 100, coins: 20, wave: 2, wavesTotal: 3, secondsLeft: 0,
                dashTenths: 0, alive: 0, kills: 7);

            HudValues stored = shown.Merge(next, HudField.Coins);

            Assert.AreEqual(20, stored.Coins, "写过的项跟着新值");
            Assert.AreEqual(80, stored.Hp, "没写的项保留旧值");
            Assert.AreEqual(30, stored.WaveSecondsLeft);
            Assert.AreEqual(5, stored.AliveEnemies);

            // 解冻后：没写的那几项重新算作变化，一次补齐
            HudField dirty = next.Diff(stored);
            Assert.AreEqual(HudField.Hp | HudField.WaveTime | HudField.AliveEnemies, dirty,
                "解冻那一帧要把冻结期间漏下的项都补上：" + dirty);
        }

        // ---- 装配 ----

        static HudValues Snapshot(int hp, int maxHp, int coins, int wave, int wavesTotal, int secondsLeft,
            int dashTenths, int alive, int kills)
        {
            return new HudValues(hp, maxHp, maxHp > 0 ? (float)hp / maxHp : 0f, coins, wave, wavesTotal, secondsLeft,
                dashTenths, alive, kills);
        }

        /// <summary>照抄 Wave.json 的一波：时长可变，间隔 / 数量 / 上限 / 奖励与表里一致。</summary>
        static List<WaveDef> Waves(float duration)
        {
            return new List<WaveDef>
            {
                new WaveDef
                {
                    Id = "W1", Index = 1, Duration = duration, SpawnInterval = 1.2f,
                    SpawnCountMin = 1, SpawnCountMax = 2, EnemyPool = new[] { "Crawler" },
                    ConcurrentCap = 40, CoinReward = 60, BossId = ""
                },
            };
        }

        /// <summary>照抄 Enemy.json 的 Crawler：HUD 只用到存活数，数值给够就行。</summary>
        static EnemyDef CrawlerDef()
        {
            return new EnemyDef
            {
                Id = "Crawler", Name = "爬虫", Behaviour = EnemyBehaviourType.Chase,
                HP = 12f, Damage = 6f, Speed = 3f, AttackRange = 0.6f, AttackInterval = 1f,
                CoinDrop = 1, BodyRadius = 0.35f
            };
        }
    }
}
