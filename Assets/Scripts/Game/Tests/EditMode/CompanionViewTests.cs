using System.Collections.Generic;
using Game.Config;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;
using Game.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 旅伴视图与旅伴弹道视图里"抽得出来的纯逻辑"：颜色、尺寸、以及"要画哪几个投射物来源"。
    ///
    /// 真正的渲染（胶囊 / 方块怎么建、插值怎么写进 Transform）只能在 Unity 里看，
    /// 这里只验能在命令行里确定的那部分：
    ///   - 旅伴的身体直径与 CompanionSystem.BodyRadius 是同一个数（视图与两两松解不能各说各话）；
    ///   - 四个阵营四种颜色且互不相同（同一个阵营同色是有意的，见 CompanionViewStyle）；
    ///   - 旅伴进队 / 离队 / 同一槽位重新招募时，投射物视图的来源表怎么变。
    /// </summary>
    public class CompanionViewTests
    {
        const float ArenaHalf = 67f;
        const ulong Seed = 0xC0FFEE12345678ul;

        static Dictionary<string, WeaponDef> WeaponTable()
        {
            var bow = new WeaponDef
            {
                Id = "Bow", Name = "弓箭", Kind = WeaponKind.Ranged,
                BaseDamage = 8f, Cooldown = 0.85f, Range = 8f,
                Projectiles = 1, SpreadDeg = 0f,
                BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 18f, Pierce = 1, Params = new float[0]
            };

            return new Dictionary<string, WeaponDef> { [bow.Id] = bow };
        }

        static CompanionDef CompanionDefOf(string id, CompanionFaction faction)
        {
            return new CompanionDef
            {
                Id = id, Name = id, Faction = faction, WeaponId = "Bow",
                FollowRadius = 1.5f, MaxHP = 40f
            };
        }

        static PlayerModel Player()
        {
            return new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
        }

        static CompanionSystem Build(out PlayerModel player, out EnemySystem enemies)
        {
            player = Player();
            enemies = new EnemySystem(player, ArenaHalf);
            return new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));
        }

        // ---- 身体尺寸 ----

        [Test]
        public void CompanionBodyIsAsWideAsTheSimulationBody()
        {
            Assert.AreEqual(CompanionSystem.BodyRadius * 2f, CompanionViewStyle.BodyScale.x, 1e-5f,
                "旅伴的可见直径必须等于两两松解用的身体直径，否则看起来能重叠或者隔得很开");
            Assert.AreEqual(CompanionViewStyle.BodyScale.x, CompanionViewStyle.BodyScale.z, 1e-5f,
                "胶囊的横截面是个圆：x 与 z 缩放必须相同");
        }

        [Test]
        public void CompanionBodyStandsOnTheGround()
        {
            // 胶囊网格在 scale 1 时是 2 米高，所以本地 y 缩放 × 2 才是身高，中心放在身高的一半处才脚贴地（地面 y = 0）
            const float ExpectedHeight = 0.6f;

            Assert.AreEqual(ExpectedHeight, CompanionViewStyle.BodyScale.y * 2f, 1e-5f,
                "旅伴的身高与预期不一致，下面的中心高度断言就是按它算的");
            Assert.AreEqual(ExpectedHeight * 0.5f, CompanionViewStyle.BodyCenterHeight, 1e-5f,
                "身体中心高度必须等于身高的一半，否则旅伴会浮空或者陷进地里");
        }

        [Test]
        public void CompanionBodyIsClearlySmallerThanThePlayer()
        {
            // 玩家预制体是 scale 1 的胶囊：直径 1 米、原点在几何中心（地上看得见的那半截是 1 米高）。
            // 这三条是"一眼分开"里最硬的一条，写成断言是为了以后调尺寸时不会被悄悄破坏。
            Assert.Less(CompanionViewStyle.BodyScale.x, 1f, "旅伴必须比玩家窄");
            Assert.Less(CompanionViewStyle.BodyScale.y * 2f, 1f, "旅伴必须比玩家露在地面上的那半截壳矮");
        }

        // ---- 阵营颜色 ----

        [Test]
        public void FourFactionsHaveFourDistinctColors()
        {
            var seen = new List<Color>();
            CompanionFaction[] factions =
            {
                CompanionFaction.Machine, CompanionFaction.Ghost, CompanionFaction.Beast, CompanionFaction.Doll
            };

            foreach (CompanionFaction faction in factions)
            {
                Color tint = CompanionViewStyle.TintOf(faction);

                // 饱和度必须够：玩家的身体是接近白的浅灰，太淡的颜色放上去就看不出来了
                Color.RGBToHSV(tint, out _, out float saturation, out _);
                Assert.Greater(saturation, 0.5f, faction + " 的颜色太淡，会和玩家撞色");

                foreach (Color other in seen)
                {
                    Assert.AreNotEqual(other, tint, faction + " 的颜色和另一个阵营撞了：阵营读不出来");
                }

                seen.Add(tint);
            }
        }

        [Test]
        public void NoFactionStillGetsAVisibleColor()
        {
            // CompanionFaction.None 不成羁绊，但表里真填了的话旅伴照样要看得见
            Color tint = CompanionViewStyle.TintOf(CompanionFaction.None);
            Assert.Greater(tint.grayscale, 0.5f, "无阵营的旅伴不能是一块黑");
        }

        // ---- 投射物来源 ----

        [Test]
        public void OnlyThePlayerIsASourceBeforeAnyRecruit()
        {
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, companions);

            Assert.AreEqual(1, sources.Sources.Count);
            Assert.AreEqual(0, sources.Sources[0].OwnerKey, "玩家的持有者编号固定是 0");
            Assert.AreSame(playerWeapons.Projectiles, sources.Sources[0].Projectiles);
            Assert.AreEqual(1f, sources.Sources[0].Scale, 1e-6f, "玩家箭是基准大小");
        }

        [Test]
        public void RecruitAddsOneSourceKeyedBySlotAndReusesTheCompanionProjectilePool()
        {
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, companions);
            sources.Refresh();

            CompanionModel machine = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine));
            CompanionModel ghost = companions.Recruit(CompanionDefOf("Whisper", CompanionFaction.Ghost));

            Assert.IsTrue(sources.Refresh(), "招募之后来源表必须报告变化，视图层才会建表");
            Assert.AreEqual(3, sources.Sources.Count);

            // 槽位 0、1 各占一个来源，持有者编号 = 槽位号 + 1（玩家是 0）
            Assert.AreEqual(machine.Slot + 1, sources.Sources[1].OwnerKey);
            Assert.AreEqual(ghost.Slot + 1, sources.Sources[2].OwnerKey);

            // 每个来源画的必须是这个旅伴自己那把武器里的投射物池：装错池就会画出别人的箭
            Assert.AreSame(companions.WeaponsOf(machine.Slot).Projectiles, sources.Sources[1].Projectiles);
            Assert.AreSame(companions.WeaponsOf(ghost.Slot).Projectiles, sources.Sources[2].Projectiles);

            // 旅伴的箭统一是"冷色 + 压小一档"，与玩家的暖黄箭分得开
            Assert.AreNotEqual(ProjectileViewSources.PlayerTint, sources.Sources[1].Tint);
            Assert.Less(sources.Sources[1].Scale, sources.Sources[0].Scale);
        }

        [Test]
        public void RefreshIsQuietWhenNothingChanged()
        {
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));
            companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine));

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, companions);
            int version = sources.Version;

            // 稳态：连续几帧都不该重建来源表，否则视图池每帧都在收表建表
            for (int frame = 0; frame < 5; frame++) Assert.IsFalse(sources.Refresh());

            Assert.AreEqual(version, sources.Version);
        }

        [Test]
        public void LevelingUpDoesNotChangeTheSourceTable()
        {
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));

            CompanionDef def = CompanionDefOf("IronBox", CompanionFaction.Machine);
            companions.Recruit(def);

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, companions);
            int version = sources.Version;

            // 重复购买是升级，旅伴还拿着同一把武器：视图不该重建（重建会把在飞的箭全部收掉）
            companions.Recruit(def);

            Assert.IsFalse(sources.Refresh(), "升级不换武器，来源表不该变");
            Assert.AreEqual(version, sources.Version);
            Assert.AreEqual(2, sources.Sources.Count);
        }

        [Test]
        public void RemoveDropsThatSlotSource()
        {
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));

            CompanionModel machine = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine));
            companions.Recruit(CompanionDefOf("Whisper", CompanionFaction.Ghost));

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, companions);
            Assert.AreEqual(3, sources.Sources.Count);

            companions.Remove(machine.Slot);

            Assert.IsTrue(sources.Refresh());
            Assert.AreEqual(2, sources.Sources.Count, "离队的旅伴不能再占一个来源");
            Assert.AreEqual(0, sources.Sources[0].OwnerKey);
            Assert.AreNotEqual(machine.Slot + 1, sources.Sources[1].OwnerKey, "空出来的槽位不该留在来源表里");
        }

        [Test]
        public void ReRecruitingAFreedSlotReportsAChangeWithANewProjectilePool()
        {
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed));

            CompanionModel first = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine));
            ProjectileSystem firstProjectiles = companions.WeaponsOf(first.Slot).Projectiles;

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, companions);
            int version = sources.Version;

            // 同一槽位换人：新旅伴拿到的是一套新的 WeaponSystem，旧池已经随旧旅伴丢掉了。
            // 视图层必须收到"变了"，否则它会继续从那张已经没人推进的旧池里读箭。
            companions.Remove(first.Slot);
            companions.Recruit(CompanionDefOf("Puppet", CompanionFaction.Doll));

            Assert.IsTrue(sources.Refresh());
            Assert.Greater(sources.Version, version);
            Assert.AreNotSame(firstProjectiles, sources.Sources[1].Projectiles);
        }

        [Test]
        public void MissingCompanionSystemStillRendersThePlayerProjectiles()
        {
            // 只装玩家战斗的场景（AppRoot 的容错口径）：没有旅伴系统时来源表只有玩家一路，且不抛异常
            PlayerModel player = Player();
            var enemies = new EnemySystem(player, ArenaHalf);
            var playerWeapons = new WeaponSystem(enemies, new DeterministicRandom(Seed), new UnitId(1), 64);

            var sources = new ProjectileViewSources(playerWeapons.Projectiles, null);

            Assert.AreEqual(1, sources.Sources.Count);
            Assert.IsFalse(sources.Refresh());
        }
    }
}
