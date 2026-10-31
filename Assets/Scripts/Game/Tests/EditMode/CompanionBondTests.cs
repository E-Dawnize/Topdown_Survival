using System.Collections.Generic;
using Game.Config;
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
    /// 阵营羁绊（I2-7）：2 件触发一级、4 件触发二级、掉到阈值以下逐项回滚、换局清空。
    ///
    /// 断言分两类：
    ///   - 数值对 GDD 7.2 的表（攻速 +10/+25、伤害 +12/+35、移速 +12/+25、生命上限 +15/+35、暴击 +15）；
    ///   - "完全回到基线"是逐项比对的：羁绊碰过的每一个 StatType 都拿招募前的值对一遍，
    ///     而不是只看其中一项。只对一项的话"退了一半"这种残留是发现不了的。
    ///
    /// 旅伴表同样是合成的：Companion.json 每个阵营只有一个旅伴，凑不出同阵营 4 件。
    /// </summary>
    public class CompanionBondTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;
        const ulong Seed = 0xB0B5EED1234ul;

        /// <summary>羁绊会改的属性。回滚断言逐项比的就是这一组。</summary>
        static readonly StatType[] BondStats =
        {
            StatType.AttackSpeed, StatType.ProjectileCount, StatType.Attack,
            StatType.MoveSpeed, StatType.MaxHP, StatType.CritChance
        };

        static readonly CompanionFaction[] Factions =
        {
            CompanionFaction.Machine, CompanionFaction.Ghost, CompanionFaction.Beast, CompanionFaction.Doll
        };

        static WeaponDef BowDef()
        {
            return new WeaponDef
            {
                Id = "Bow", Name = "弓箭", Kind = WeaponKind.Ranged,
                BaseDamage = 8f, Cooldown = 0.85f, Range = 8f, Projectiles = 1, SpreadDeg = 0f,
                BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 18f, Pierce = 1, Params = new float[0]
            };
        }

        static CompanionDef CompanionDefOf(string id, CompanionFaction faction)
        {
            return new CompanionDef
            {
                Id = id, Name = id, Faction = faction, WeaponId = "Bow", FollowRadius = 1.5f, MaxHP = 40f
            };
        }

        /// <summary>同阵营的 count 个旅伴（Id 互不相同：同 Id 是升级，不会多一个人）。</summary>
        static CompanionDef[] SameFaction(CompanionFaction faction, int count)
        {
            var defs = new CompanionDef[count];
            for (int i = 0; i < count; i++) defs[i] = CompanionDefOf(faction + "_" + i, faction);
            return defs;
        }

        static PlayerModel Player()
        {
            return new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
        }

        static CompanionSystem Build(PlayerModel player, out EnemySystem enemies)
        {
            enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new Dictionary<string, WeaponDef> { ["Bow"] = BowDef() };
            return new CompanionSystem(player, enemies, weapons, new DeterministicRandom(Seed));
        }

        static float[] Snapshot(StatBlock stats)
        {
            var values = new float[BondStats.Length];
            for (int i = 0; i < BondStats.Length; i++) values[i] = stats[BondStats[i]];
            return values;
        }

        static void AssertBaseline(string who, StatBlock stats, float[] baseline)
        {
            for (int i = 0; i < BondStats.Length; i++)
            {
                Assert.AreEqual(baseline[i], stats[BondStats[i]], 1e-6f,
                    who + " 的 " + BondStats[i] + " 没有回到基线");
            }
        }

        [Test]
        public void TwoCompanionsTriggerTierOneAndFourTriggerTierTwo()
        {
            foreach (CompanionFaction faction in Factions)
            {
                PlayerModel player = Player();
                CompanionSystem companions = Build(player, out _);
                CompanionDef[] defs = SameFaction(faction, CompanionSystem.MaxCompanions);

                companions.Recruit(defs[0]);
                Assert.AreEqual(0, companions.ActiveBonds.Count, "只有 1 件，不该触发羁绊");

                companions.Recruit(defs[1]);
                Assert.AreEqual(1, companions.ActiveBonds.Count, faction + " 2 件应当触发一级羁绊");
                AssertTier(companions.ActiveBonds[0], faction, count: 2, tier: 1);
                AssertTierOneValues(faction, player, companions.Get(0));

                companions.Recruit(defs[2]);
                Assert.AreEqual(1, companions.ActiveBonds.Count, "3 件还停在一级");
                AssertTier(companions.ActiveBonds[0], faction, count: 3, tier: 1);
                AssertTierOneValues(faction, player, companions.Get(0));

                companions.Recruit(defs[3]);
                Assert.AreEqual(1, companions.ActiveBonds.Count, "4 件应当升到二级，仍然只有一条羁绊");
                AssertTier(companions.ActiveBonds[0], faction, count: 4, tier: 2);
                AssertTierTwoValues(faction, player, companions.Get(0));
            }
        }

        /// <summary>一级数值：GDD 7.2 的 2 件列。</summary>
        static void AssertTierOneValues(CompanionFaction faction, PlayerModel player, CompanionModel companion)
        {
            switch (faction)
            {
                case CompanionFaction.Machine:
                    Assert.AreEqual(0.10f, player.Stats[StatType.AttackSpeed], 1e-6f, "机械 2 件：全队攻速 +10%");
                    Assert.AreEqual(0.10f, companion.Stats[StatType.AttackSpeed], 1e-6f, "全队也包括旅伴自己");
                    break;
                case CompanionFaction.Ghost:
                    Assert.AreEqual(0.12f, player.Stats[StatType.Attack], 1e-6f, "幽灵 2 件：全队伤害 +12%");
                    break;
                case CompanionFaction.Beast:
                    Assert.AreEqual(5.6f, player.Stats[StatType.MoveSpeed], 1e-5f, "野兽 2 件：全队移速 +12%");
                    Assert.AreEqual(0.05f, player.Stats[StatType.CritChance], 1e-6f, "一级不加暴击率");
                    break;
                case CompanionFaction.Doll:
                    Assert.AreEqual(115f, player.Stats[StatType.MaxHP], 1e-5f, "玩偶 2 件：生命上限 +15%");
                    break;
            }
        }

        /// <summary>二级数值：GDD 7.2 的 4 件列。二级是替换而不是在一级上再叠一档。</summary>
        static void AssertTierTwoValues(CompanionFaction faction, PlayerModel player, CompanionModel companion)
        {
            switch (faction)
            {
                case CompanionFaction.Machine:
                    Assert.AreEqual(0.25f, player.Stats[StatType.AttackSpeed], 1e-6f, "机械 4 件：攻速 +25%");
                    Assert.AreEqual(1f, player.Stats[StatType.ProjectileCount], 1e-6f, "机械 4 件：弹道 +1");
                    Assert.AreEqual(1f, companion.Stats[StatType.ProjectileCount], 1e-6f, "全队也包括旅伴");
                    break;
                case CompanionFaction.Ghost:
                    Assert.AreEqual(0.35f, player.Stats[StatType.Attack], 1e-6f, "幽灵 4 件：伤害 +35%");
                    break;
                case CompanionFaction.Beast:
                    Assert.AreEqual(6.25f, player.Stats[StatType.MoveSpeed], 1e-5f, "野兽 4 件：移速 +25%");
                    Assert.AreEqual(0.20f, player.Stats[StatType.CritChance], 1e-6f, "野兽 4 件：暴击率 +15%");
                    Assert.AreEqual(7.5f, companion.Stats[StatType.MoveSpeed], 1e-5f, "旅伴的移速也走同一条");
                    break;
                case CompanionFaction.Doll:
                    Assert.AreEqual(135f, player.Stats[StatType.MaxHP], 1e-5f, "玩偶 4 件：生命上限 +35%");
                    Assert.AreEqual(54f, companion.Stats[StatType.MaxHP], 1e-4f, "旅伴的生命上限也走同一条");
                    break;
            }
        }

        static void AssertTier(ActiveBond bond, CompanionFaction faction, int count, int tier)
        {
            Assert.AreEqual(faction, bond.Faction);
            Assert.AreEqual(count, bond.Count, faction + " 的数量统计不对");
            Assert.AreEqual(tier, bond.Tier, faction + " 的档位不对");
            Assert.IsTrue(bond.IsActive);
        }

        [Test]
        public void DroppingBelowTheThresholdRestoresEveryStatToBaseline()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);
            CompanionDef[] defs = SameFaction(CompanionFaction.Machine, CompanionSystem.MaxCompanions);

            // 先把第一个旅伴买到 3 级：等级修正与羁绊修正共用一份 StatBlock，
            // 回滚羁绊时不能顺手把等级修正也退掉，所以基线里要带着等级修正
            CompanionModel first = companions.Recruit(defs[0]);
            companions.Recruit(defs[0]);
            companions.Recruit(defs[0]);
            Assert.AreEqual(3, first.Level);

            float[] playerBaseline = Snapshot(player.Stats);
            float[] companionBaseline = Snapshot(first.Stats);

            for (int i = 1; i < defs.Length; i++) companions.Recruit(defs[i]);
            Assert.AreEqual(2, companions.ActiveBonds[0].Tier, "4 个机械应当到二级");
            Assert.AreEqual(0.25f, player.Stats[StatType.AttackSpeed], 1e-6f);
            Assert.AreEqual(1f, player.Stats[StatType.ProjectileCount], 1e-6f);

            // 掉到 3 件：4 件档是 4 件专属，少一件就退回一级，加成是替换而不是叠在上面
            Assert.IsTrue(companions.Remove(3));
            Assert.AreEqual(1, companions.ActiveBonds[0].Tier);
            Assert.AreEqual(3, companions.ActiveBonds[0].Count);
            Assert.AreEqual(0.10f, player.Stats[StatType.AttackSpeed], 1e-6f, "二级要换成一级的 +10%");
            Assert.AreEqual(0f, player.Stats[StatType.ProjectileCount], 1e-6f, "一级没有弹道加成");

            // 掉到 2 件：还在一级
            Assert.IsTrue(companions.Remove(2));
            Assert.AreEqual(1, companions.ActiveBonds[0].Tier);
            Assert.AreEqual(2, companions.ActiveBonds[0].Count);
            Assert.AreEqual(0.10f, player.Stats[StatType.AttackSpeed], 1e-6f);

            // 掉到 1 件：羁绊整条消失，逐项回到基线
            Assert.IsTrue(companions.Remove(1));
            Assert.AreEqual(0, companions.ActiveBonds.Count, "只剩 1 件，一条羁绊都不该生效");
            AssertBaseline("玩家", player.Stats, playerBaseline);
            AssertBaseline("旅伴", first.Stats, companionBaseline);

            // 等级修正还在：回滚只按羁绊的 Source 退，不碰模型的等级 Source
            Assert.AreEqual(0.8f, first.Stats[StatType.Attack], 1e-6f, "3 级的 +80% 伤害不该被羁绊回滚带走");
            Assert.AreEqual(3, first.Level);
        }

        [Test]
        public void RestartingTheRunClearsRosterAndBonds()
        {
            var bus = new EventBus();
            PlayerModel player = Player();
            var run = new RunState();
            var enemies = new EnemySystem(player, ArenaHalf);
            var weapons = new Dictionary<string, WeaponDef> { ["Bow"] = BowDef() };
            var companions = new CompanionSystem(player, enemies, weapons, new DeterministicRandom(Seed), bus);

            var waves = new List<WaveDef>
            {
                new WaveDef
                {
                    Id = "W1", Index = 1, Duration = 1f, SpawnInterval = 1f, SpawnCountMin = 0, SpawnCountMax = 0,
                    EnemyPool = new string[0], ConcurrentCap = 0, CoinReward = 0, BossId = ""
                }
            };

            var controller = new RunController(player, run, waves, bus, Seed, "Scavenger", null, companions);

            controller.StartRun();
            bus.Flush();

            CompanionDef[] defs = SameFaction(CompanionFaction.Machine, 2);
            companions.Recruit(defs[0]);
            companions.Recruit(defs[1]);

            Assert.AreEqual(1, companions.ActiveBonds.Count);
            Assert.AreEqual(0.10f, player.Stats[StatType.AttackSpeed], 1e-6f);

            // 整局重开：先把这一局打到结束（玩家死亡 → GameOver），再开一局
            player.HP = 0f;
            controller.Tick(Dt);
            Assert.AreEqual(RunPhase.GameOver, controller.Phase, "玩家死了该进结算态，否则 StartRun 会被忽略");

            controller.StartRun();
            bus.Flush();

            Assert.AreEqual(0, companions.Count, "换局要把队伍清空");
            Assert.AreEqual(0, companions.ActiveBonds.Count, "换局要把羁绊清空");
            Assert.AreEqual(0f, player.Stats[StatType.AttackSpeed], 1e-6f, "上一局的羁绊加成不能带进新的一局");
            Assert.AreEqual(0f, player.Stats[StatType.ProjectileCount], 1e-6f);
        }
    }
}
