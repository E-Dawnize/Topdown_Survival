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
    /// 旅伴（I2-6）：跟随、瞬移回位、武器绑定、重复购买升级、稳态不分配。
    ///
    /// 旅伴表用合成的 CompanionDef：Companion.json 目前只有 3 个旅伴、且阵营各一个，
    /// 验不了"4 个旅伴同时跟随"与"同阵营 4 件"，所以这里按表的形状造数据（Id / Faction / WeaponId /
    /// FollowRadius / MaxHP 五列），不读 StreamingAssets。
    /// 武器表用与 Weapon.json 同形状的 Bow / Sword：旅伴表里 WeaponId 指向的就是这两个 Id。
    ///
    /// 每个用例都照 RunSimulation 的顺序走：先旅伴移动（TickMovement），再敌人网格 Rebuild，
    /// 最后旅伴开火（TickWeapons）。步长 1/60，与主循环的固定步长一致，秒数才对得上。
    /// </summary>
    public class CompanionSystemTests
    {
        const float Dt = 1f / 60f;
        const float ArenaHalf = 67f;
        const float BowDamage = 8f;
        const float BowCooldown = 0.85f;
        const float BowRange = 8f;
        const float SwordDamage = 15f;
        const ulong Seed = 0xC0FFEE12345678ul;

        /// <summary>身体直径：验收口径"4 个旅伴的最小间距大于身体直径"就按它比。</summary>
        const float BodyDiameter = CompanionSystem.BodyRadius * 2f;

        static WeaponDef BowDef()
        {
            return new WeaponDef
            {
                Id = "Bow", Name = "弓箭", Kind = WeaponKind.Ranged,
                BaseDamage = BowDamage, Cooldown = BowCooldown, Range = BowRange,
                Projectiles = 1, SpreadDeg = 0f,
                BehaviourType = WeaponSystem.RangedBehaviourType,
                ProjectileSpeed = 18f, Pierce = 1, Params = new float[0]
            };
        }

        static WeaponDef SwordDef()
        {
            return new WeaponDef
            {
                Id = "Sword", Name = "砍刀", Kind = WeaponKind.Melee,
                BaseDamage = SwordDamage, Cooldown = 0.6f, Range = 2.2f,
                Projectiles = 1, SpreadDeg = 45f,
                BehaviourType = WeaponSystem.MeleeBehaviourType,
                ProjectileSpeed = 0f, Pierce = 0, Params = new[] { 0.08f, 0.22f }
            };
        }

        static Dictionary<string, WeaponDef> WeaponTable()
        {
            return new Dictionary<string, WeaponDef>
            {
                [BowDef().Id] = BowDef(),
                [SwordDef().Id] = SwordDef()
            };
        }

        static CompanionDef CompanionDefOf(string id, CompanionFaction faction, float followRadius,
            string weaponId = "Bow", float maxHp = 40f)
        {
            return new CompanionDef
            {
                Id = id, Name = id, Faction = faction, WeaponId = weaponId,
                FollowRadius = followRadius, MaxHP = maxHp
            };
        }

        /// <summary>同阵营的 count 个旅伴。Id 必须不同：同一个 Id 买第二次是升级，不会多一个。</summary>
        static CompanionDef[] SameFaction(CompanionFaction faction, int count)
        {
            var defs = new CompanionDef[count];
            for (int i = 0; i < count; i++)
            {
                defs[i] = CompanionDefOf(faction + "_" + i, faction, 1.4f + 0.1f * i);
            }
            return defs;
        }

        static EnemyDef DummyDef(float hp, float bodyRadius = 0.3f, float speed = 0f)
        {
            return new EnemyDef
            {
                Id = "dummy", Name = "测试靶", Behaviour = EnemyBehaviourType.Chase,
                HP = hp, Damage = 0f, Speed = speed, AttackRange = 1f, AttackInterval = 1f,
                XpValue = 0, BodyRadius = bodyRadius, Scale = 1f
            };
        }

        /// <summary>固定值随机桩：暴击/闪避都要"永远这个结果"。0.99 落在 5% 暴击与 0 闪避之外。</summary>
        sealed class FixedRandom : IRandom01
        {
            readonly float _value;

            public FixedRandom(float value)
            {
                _value = value;
            }

            public float Next01() => _value;
        }

        static PlayerModel Player()
        {
            return new PlayerModel(100f, 5f, 15f, 0.15f, 2f, ArenaHalf);
        }

        static CompanionSystem Build(PlayerModel player, out EnemySystem enemies, IRandom01 rng = null)
        {
            var bus = new EventBus();
            enemies = new EnemySystem(player, ArenaHalf);
            return new CompanionSystem(player, enemies, WeaponTable(), rng ?? new DeterministicRandom(Seed), bus);
        }

        // ---- 跟随 ----

        [Test]
        public void FourCompanionsKeepTheirDistanceAndNeverFallBehind()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);

            // 4 个不同阵营、不同 FollowRadius：阵营只是让数据更像真表，跟随与阵营无关
            CompanionFaction[] factions =
                { CompanionFaction.Machine, CompanionFaction.Ghost, CompanionFaction.Beast, CompanionFaction.Doll };
            for (int i = 0; i < factions.Length; i++)
            {
                Assert.IsNotNull(companions.Recruit(CompanionDefOf("C" + i, factions[i], 1.4f + 0.1f * i)));
            }

            Assert.AreEqual(4, companions.Count);

            float minPairDistance = float.MaxValue;
            float maxDistanceToPlayer = 0f;

            // 玩家沿 +x 全速跑 10 秒。旅伴移速 6 > 玩家 5，所以整段都不该触发瞬移
            for (int step = 0; step < 600; step++)
            {
                player.Position += new Vector2(player.MoveSpeed * Dt, 0f);
                companions.TickMovement(player.Position, Dt);

                for (int i = 0; i < companions.SlotCapacity; i++)
                {
                    CompanionModel a = companions.Get(i);
                    if (a == null) continue;

                    maxDistanceToPlayer = Mathf.Max(maxDistanceToPlayer,
                        (a.Position - player.Position).magnitude);

                    for (int j = i + 1; j < companions.SlotCapacity; j++)
                    {
                        CompanionModel b = companions.Get(j);
                        if (b == null) continue;

                        minPairDistance = Mathf.Min(minPairDistance, (a.Position - b.Position).magnitude);
                    }
                }
            }

            Assert.Greater(minPairDistance, BodyDiameter,
                $"4 个旅伴全程的最小间距 {minPairDistance:F3} 米必须大于身体直径 {BodyDiameter:F3} 米");
            Assert.Less(maxDistanceToPlayer, CompanionSystem.TeleportDistance,
                "旅伴移速比玩家快，全速跑时不该触发 4 米瞬移（掉队）");

            // 阻尼跟随不会在玩家停下的同一个 tick 就归位：再跑 2 秒让它回到环上
            for (int step = 0; step < 120; step++)
            {
                companions.TickMovement(player.Position, Dt);
            }

            // 停下之后各自归位：离玩家等于自己的 FollowRadius
            for (int i = 0; i < companions.SlotCapacity; i++)
            {
                CompanionModel companion = companions.Get(i);
                if (companion == null) continue;

                float distance = (companion.Position - player.Position).magnitude;
                Assert.AreEqual(companion.Def.FollowRadius, distance, 0.05f,
                    "旅伴 " + companion.Def.Id + " 没有停在环绕位上");
            }
        }

        [Test]
        public void AggroCompanionMovesTowardNearestEnemy()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out EnemySystem enemies);
            CompanionDef def = CompanionDefOf("Aggro", CompanionFaction.Beast, 1.4f, "Sword");
            def.AI = CompanionAI.Aggro;
            def.AggroRadius = 8f;
            def.PreferredRange = 1.5f;
            def.LeashDistance = 8f;
            CompanionModel companion = companions.Recruit(def);

            EnemyModel enemy = SpawnDummy(enemies, new Vector2(5f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);

            float before = (companion.Position - enemy.Position).magnitude;
            companions.TickMovement(Vector2.zero, Dt);
            float after = (companion.Position - enemy.Position).magnitude;

            Assert.Less(after, before, "Aggro 旅伴应当主动靠近敌人，而不是停在环绕位");
        }

        [Test]
        public void KiterCompanionBacksAwayFromCloseEnemy()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out EnemySystem enemies);
            CompanionDef def = CompanionDefOf("Kiter", CompanionFaction.Machine, 1.6f, "Bow");
            def.AI = CompanionAI.Kiter;
            def.AggroRadius = 8f;
            def.PreferredRange = 5f;
            def.LeashDistance = 8f;
            CompanionModel companion = companions.Recruit(def);

            EnemyModel enemy = SpawnDummy(enemies, new Vector2(2f, 0f), 100f);
            enemies.Grid.Rebuild(Vector2.zero, enemies);

            float before = (companion.Position - enemy.Position).magnitude;
            companions.TickMovement(Vector2.zero, Dt);
            float after = (companion.Position - enemy.Position).magnitude;

            Assert.Greater(after, before, "Kiter 敌人贴脸时应当后退，而不是迎上去");
        }

        [Test]
        public void AiCompanionReturnsToPlayerWhenBeyondLeash()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out EnemySystem enemies);
            CompanionDef def = CompanionDefOf("Aggro", CompanionFaction.Beast, 1.4f, "Sword");
            def.AI = CompanionAI.Aggro;
            def.AggroRadius = 20f;
            def.PreferredRange = 1.5f;
            def.LeashDistance = 4f;
            CompanionModel companion = companions.Recruit(def);

            // 把旅伴放到离玩家 9 米的地方：超过 leach(4)+2，下一步必须瞬移回环绕位
            companion.Position = new Vector2(9f, 0f);
            companion.PrevPosition = companion.Position;

            companions.TickMovement(Vector2.zero, Dt);

            Assert.LessOrEqual((companion.Position - Vector2.zero).magnitude, 2f,
                "超过活动半径要回位，否则会追怪追出屏幕");
        }

        static EnemyModel SpawnDummy(EnemySystem enemies, Vector2 position, float hp)
        {
            int slot = enemies.Spawn(DummyDef(hp), position);
            return enemies.Get(slot);
        }

        [Test]
        public void CompanionTeleportsBackWhenPlayerGetsMoreThanFourMetersAway()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);
            CompanionModel companion = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f));

            // 先让它在环绕位上站好
            companions.TickMovement(player.Position, Dt);
            Vector2 before = companion.Position;

            // 玩家一次瞬移 20 米：靠移速一步只能走 0.1 米，必须靠回位才追得上
            player.Position = new Vector2(20f, 0f);
            companions.TickMovement(player.Position, Dt);

            Assert.Greater((player.Position - before).magnitude, CompanionSystem.TeleportDistance);
            Assert.AreEqual(companion.Def.FollowRadius, (companion.Position - player.Position).magnitude, 1e-3f,
                "超过 4 米要直接瞬移回环绕位，而不是慢慢跑");
            Assert.AreEqual(before.y, companion.Position.y, 1e-3f, "回位点是环绕位，y 与之前一致");
        }

        [Test]
        public void CompanionOnlyWalksWhenPlayerIsWithinTeleportDistance()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);
            CompanionModel companion = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f));

            companions.TickMovement(player.Position, Dt);
            Vector2 start = companion.Position;

            // 玩家走到 3.9 米：还没到瞬移阈值，这一步只能按移速上限追，而且有阻尼
            player.Position = new Vector2(3.9f, 0f);
            Assert.Less((player.Position - start).magnitude, CompanionSystem.TeleportDistance);
            companions.TickMovement(player.Position, Dt);

            float moved = (companion.Position - start).magnitude;
            Assert.Greater(moved, 0f, "没超过 4 米要正常走，不能原地不动");
            Assert.Less(moved, companion.MoveSpeed * Dt + 1e-3f,
                "Follow 从静止起步应当有阻尼，不能一步就顶到 6 米/秒");
            Assert.Less(moved, CompanionSystem.TeleportDistance,
                "没超过 4 米不能瞬移");
        }

        [Test]
        public void FollowCompanionTrailsPlayerVelocity()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);
            CompanionModel companion = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f));

            // 先把环绕位走稳，记下静止时的 x
            for (int step = 0; step < 120; step++) companions.TickMovement(player.Position, Dt);
            float restingX = companion.Position.x;

            // 玩家朝 +x 跑，但位置暂时不动：Follow 的目标点应当按速度往 -x 偏，整队拖在身后
            player.Velocity = new Vector2(player.MoveSpeed, 0f);
            for (int step = 0; step < 120; step++) companions.TickMovement(player.Position, Dt);

            Assert.Less(companion.Position.x, restingX - 0.2f,
                "Follow 旅伴应当按玩家速度拖在身后，而不是和玩家保持同一个相对位置");
        }

        // ---- 武器绑定与击杀归属 ----

        [Test]
        public void CompanionShotsKillAndTheKillBelongsToTheCompanion()
        {
            PlayerModel player = Player();
            var bus = new EventBus();
            var enemies = new EnemySystem(player, ArenaHalf);
            // 0.99：不暴击、不闪避，单发伤害正好等于表里的 8
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new FixedRandom(0.99f), bus);

            CompanionModel companion = companions.Recruit(
                CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f));

            // 敌人放在射手正前方 2.4 米：在弓的 8 米射程内
            int slot = enemies.Spawn(DummyDef(5f), new Vector2(4f, 0f));
            EnemyModel enemy = enemies.Get(slot);

            var damaged = new List<UnitDamaged>();
            bus.Subscribe<UnitDamaged>(damaged.Add);

            int steps = 0;
            while (!enemy.IsDead && steps++ < 300)
            {
                enemies.Grid.Rebuild(player.Position, enemies);
                companions.TickWeapons(player.Position, Dt);
                bus.Flush();
            }

            Assert.IsTrue(enemy.IsDead, "旅伴在射程内应当自动开火并打死 5 血的敌人");
            Assert.Greater(companions.WeaponsOf(companion.Slot).Weapons[0].ShotsFired, 0, "应当真的开过火");

            Assert.AreEqual(companion.UnitId, enemy.LastDamageSource,
                "击杀归属要记在旅伴头上：敌人死亡时清理阶段读的就是 LastDamageSource");
            Assert.AreEqual(UnitKind.Companion, companion.UnitId.Kind, "旅伴 ID 要带种类，不能靠数值区间猜");
            Assert.AreNotEqual(1, companion.UnitId.Value, "旅伴的 UnitId 不能等于玩家（AppRoot.PlayerUnit = 1）");

            Assert.AreEqual(1, damaged.Count, "两发箭里第一发就够杀死 5 血的敌人，只有一次命中");
            Assert.AreEqual(companion.UnitId, damaged[0].Source, "受击事件的来源也必须是旅伴，不能算成玩家");
            Assert.AreEqual(companion.UnitId, companions.UnitIdOf(companion.Slot));
            Assert.AreEqual(companion.Slot, companions.SlotOf(companion.UnitId));
        }

        // ---- 重复购买升级 ----

        [Test]
        public void RepeatPurchasesRaiseTheLevelAndApplyTheGddCurve()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);

            CompanionDef def = CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f);
            CompanionModel companion = companions.Recruit(def);

            Assert.AreEqual(1, companion.Level, "第一次买是 1 级");
            Assert.AreEqual(0f, companion.Stats[StatType.Attack], 1e-6f, "1 级没有伤害加成");
            Assert.AreEqual(0f, companion.Stats[StatType.AttackSpeed], 1e-6f, "1 级没有攻速加成");

            // 第二次买：同一个旅伴升 1 级，不新增一个单位
            Assert.AreSame(companion, companions.Recruit(def));
            Assert.AreEqual(1, companions.Count, "重复购买升的是等级，不是多一个旅伴");
            Assert.AreEqual(2, companion.Level);
            Assert.AreEqual(0.4f, companion.Stats[StatType.Attack], 1e-6f, "GDD 7.3：每级伤害 +40%");

            // 间隔 −8%：间隔 = 基础 / (1 + 攻速)，所以攻速 = 1/0.92 − 1
            Assert.AreEqual(BowCooldown * 0.92f, BowCooldown / (1f + companion.Stats[StatType.AttackSpeed]), 1e-4f,
                "2 级的开火间隔应当是 1 级的 92%");

            // 第三次买：3 级（上限），伤害与间隔继续按同一条曲线走
            Assert.AreSame(companion, companions.Recruit(def));
            Assert.AreEqual(3, companion.Level);
            Assert.AreEqual(0.8f, companion.Stats[StatType.Attack], 1e-6f, "3 级伤害 +80%");
            Assert.AreEqual(BowCooldown * 0.92f * 0.92f,
                BowCooldown / (1f + companion.Stats[StatType.AttackSpeed]), 1e-4f,
                "3 级的开火间隔应当是 1 级的 84.64%");

            // 第四次买不了：队伍里没有变化，等级也不越过上限
            Assert.IsFalse(companions.CanRecruit(def), "3 级是 GDD 7.3 的上限，满级后商店不能再卖");
            Assert.IsNull(companions.Recruit(def), "买不了时不改任何状态");
            Assert.AreEqual(3, companion.Level);
            Assert.AreEqual(1, companions.Count);
        }

        [Test]
        public void HigherLevelShootsHarder()
        {
            PlayerModel player = Player();
            var bus = new EventBus();
            var enemies = new EnemySystem(player, ArenaHalf);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new FixedRandom(0.99f), bus);

            CompanionDef def = CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f);
            CompanionModel companion = companions.Recruit(def);

            int slot = enemies.Spawn(DummyDef(1000f), new Vector2(4f, 0f));
            EnemyModel enemy = enemies.Get(slot);

            float levelOneDamage = FirstHitDamage(enemies, companions, enemy, player.Position);
            Assert.AreEqual(BowDamage, levelOneDamage, 1e-3f, "1 级单发伤害应当等于武器表里的 8");

            companions.Recruit(def);
            companions.Recruit(def);   // 3 级
            enemy.HP = 1000f;

            float levelThreeDamage = FirstHitDamage(enemies, companions, enemy, player.Position);
            Assert.AreEqual(BowDamage * 1.8f, levelThreeDamage, 1e-3f, "3 级伤害 +80%，单发应当是 14.4");
        }

        /// <summary>等下一发箭真的打中，返回这一发扣掉的血。</summary>
        static float FirstHitDamage(EnemySystem enemies, CompanionSystem companions, EnemyModel enemy,
            Vector2 playerPos)
        {
            float before = enemy.HP;
            int steps = 0;
            while (enemy.HP >= before && steps++ < 300)
            {
                enemies.Grid.Rebuild(playerPos, enemies);
                companions.TickWeapons(playerPos, Dt);
            }

            Assert.Less(enemy.HP, before, "等不到命中");
            return before - enemy.HP;
        }

        [Test]
        public void RosterStopsAtFourDistinctCompanions()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);

            CompanionDef[] defs = SameFaction(CompanionFaction.Machine, CompanionSystem.MaxCompanions + 1);

            for (int i = 0; i < CompanionSystem.MaxCompanions; i++)
            {
                Assert.IsNotNull(companions.Recruit(defs[i]), "第 " + (i + 1) + " 个旅伴应当招得到");
            }

            Assert.AreEqual(CompanionSystem.MaxCompanions, companions.Count, "GDD 7：一局最多同时拥有 4 个");
            Assert.IsFalse(companions.CanRecruit(defs[CompanionSystem.MaxCompanions]), "第 5 个不同旅伴招不了");
            Assert.IsNull(companions.Recruit(defs[CompanionSystem.MaxCompanions]));
            Assert.AreEqual(CompanionSystem.MaxCompanions, companions.Count);
        }

        // ---- 武器规则注入与槽位复用 ----

        [Test]
        public void CompanionUpgradesNeverGrowTheWeaponAndTheInjectedRulesAreIgnored()
        {
            PlayerModel player = Player();
            var bus = new EventBus();
            var enemies = new EnemySystem(player, ArenaHalf);

            // 故意传一份“激进”的武器规则：旅伴本阶段没有武器成长，这份规则不应该产生任何效果
            var rules = new WeaponUpgradeRules(9, 0.5f, 0.5f, 2, 2);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed), bus,
                null, rules);

            CompanionDef def = CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f);
            CompanionModel companion = companions.Recruit(def);
            WeaponModel weapon = companions.WeaponsOf(companion.Slot).Weapons[0];

            companions.Recruit(def);
            companions.Recruit(def);   // 旅伴升到 3 级

            Assert.AreEqual(3, companion.Level, "旅伴升级照旧走 CompanionModel");
            Assert.AreEqual(1, weapon.Level, "旅伴升级不动武器等级：本阶段没有旅伴武器成长");
            Assert.AreEqual(BowDamage, weapon.BaseDamage, 1e-4f, "武器伤害仍是表里的基础值");
            Assert.AreEqual(BowCooldown, weapon.Cooldown, 1e-4f, "开火间隔仍是 1 级武器的值");
            Assert.AreSame(WeaponUpgradeRules.Default, weapon.Rules,
                "注入的 weaponRules 被显式忽略，不会因为规则不同而改变旅伴武器");
        }

        [Test]
        public void RemovingACompanionUnbindsItsWeaponsAndTheNextRecruitGetsANewSystem()
        {
            PlayerModel player = Player();
            CompanionSystem companions = Build(player, out _);

            CompanionModel first = companions.Recruit(CompanionDefOf("IronBox", CompanionFaction.Machine, 1.6f));
            int slot = first.Slot;
            WeaponSystem firstWeapons = companions.WeaponsOf(slot);
            Assert.IsNotNull(firstWeapons);

            Assert.IsTrue(companions.Remove(slot));
            Assert.IsNull(companions.Get(slot), "离队后槽位为空");
            Assert.IsNull(companions.WeaponsOf(slot), "离队后不能还留着上一套武器系统");
            Assert.IsFalse(companions.Remove(slot), "重复移除是空操作");

            CompanionModel second = companions.Recruit(CompanionDefOf("Whisper", CompanionFaction.Ghost, 1.6f));
            Assert.AreEqual(slot, second.Slot, "空槽位按槽位号从小到大复用");
            Assert.AreNotSame(firstWeapons, companions.WeaponsOf(slot), "复用槽位要换一套新的武器系统");
            Assert.AreEqual(second.UnitId, companions.WeaponsOf(slot).Shooter, "新武器系统的射手是新旅伴");
        }

        // ---- 稳态不分配 ----

        [Test]
        public void TickDoesNotAllocateInSteadyState()
        {
            PlayerModel player = Player();
            var bus = new EventBus();
            var enemies = new EnemySystem(player, ArenaHalf);
            var companions = new CompanionSystem(player, enemies, WeaponTable(), new DeterministicRandom(Seed), bus);

            CompanionDef[] defs = SameFaction(CompanionFaction.Beast, CompanionSystem.MaxCompanions);
            for (int i = 0; i < defs.Length; i++) companions.Recruit(defs[i]);

            // 一个血厚、站在原地的靶子：让开火与命中每步都真的发生，但不触发死亡清理
            enemies.Spawn(DummyDef(1000000f), new Vector2(4f, 0f));

            // 预热：第一发箭、第一次命中、事件队列的初始扩容都在这里面发生
            for (int i = 0; i < 60; i++) Step();

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) Step();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "跟随 + 开火的稳态路径不该分配（实测 " + allocated + " 字节）");

            void Step()
            {
                // 顺序照 RunSimulation：先旅伴移动，再重建敌人网格，最后开火；事件每步分发一次
                companions.TickMovement(player.Position, Dt);
                enemies.Grid.Rebuild(player.Position, enemies);
                companions.TickWeapons(player.Position, Dt);
                bus.Flush();
            }
        }
    }
}
