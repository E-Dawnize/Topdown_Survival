using Game.Config;
using Game.Model;

namespace Game.Tests
{
    /// <summary>
    /// 测试用最小配置。真实数值在 Config/GameConfig.xlsx，这里只放跑流程必须的几行：
    /// 一张 10 级曲线、两段导演（第二段带 BOSS）、一把弓/一把刀、一个近战旅伴、
    /// 若干构筑卡与 buff。测试要验的是规则，不是策划数值，所以不复制整张表。
    /// </summary>
    public static class TestContent
    {
        static ConfigDatabase _config;

        public static ConfigDatabase Config()
        {
            if (_config != null) return _config;

            var source = new MemoryConfigSource()
                .Add("manifest.json", @"{""version"":""test"",""exportedAt"":""2026-01-01 00:00:00"",""tables"":[]}")
                .Add("Character.json", @"[
{""Id"":""Scavenger"",""Name"":""拾荒"",""Description"":""测试角色"",""BaseHP"":100,""BaseMoveSpeed"":5,
 ""BaseAttackInterval"":0.8,""BaseDamage"":6,""BaseRange"":8,""StartingWeaponId"":""Bow"",""TraitType"":""""}
]")
                .Add("Weapon.json", @"[
{""Id"":""Bow"",""Name"":""弓箭"",""Description"":""远程"",""Kind"":""Ranged"",""BaseDamage"":8,""Cooldown"":0.85,
 ""Range"":8,""Projectiles"":1,""SpreadDeg"":0,""BehaviourType"":""Ranged"",""ProjectileSpeed"":18,
 ""Pierce"":1,""Tags"":[""ranged""],""Params"":[0]},
{""Id"":""Sword"",""Name"":""砍刀"",""Description"":""近战"",""Kind"":""Melee"",""BaseDamage"":15,""Cooldown"":0.6,
 ""Range"":2.2,""Projectiles"":1,""SpreadDeg"":45,""BehaviourType"":""Melee"",""ProjectileSpeed"":0,
 ""Pierce"":0,""Params"":[0.08,0.22]}
]")
                .Add("Companion.json", @"[
{""Id"":""IronBox"",""Name"":""铁盒"",""Description"":""机械"",""Faction"":""Machine"",""WeaponId"":""Bow"",
 ""FollowRadius"":1.6,""MaxHP"":40},
{""Id"":""LampGhost"",""Name"":""提灯鬼"",""Description"":""幽灵"",""Faction"":""Ghost"",""WeaponId"":""Bow"",
 ""FollowRadius"":1.8,""MaxHP"":30},
{""Id"":""SquintFox"",""Name"":""眯眼狐"",""Description"":""野兽"",""Faction"":""Beast"",""WeaponId"":""Sword"",
 ""FollowRadius"":1.4,""MaxHP"":45},
{""Id"":""StitchedDoll"",""Name"":""缝合娃娃"",""Description"":""玩偶"",""Faction"":""Doll"",""WeaponId"":""Bow"",
 ""FollowRadius"":1.7,""MaxHP"":38}
]")
                .Add("Enemy.json", @"[
{""Id"":""Crawler"",""Name"":""爬虫"",""PrefabId"":""Crawler"",""Behaviour"":""Chase"",""Tier"":1,
 ""HP"":12,""Damage"":6,""Speed"":3,""AttackRange"":0.6,""AttackInterval"":1,""XpValue"":1,
 ""BodyRadius"":0.35,""Scale"":1},
{""Id"":""SutureGolem"",""Name"":""缝线巨偶"",""PrefabId"":""Charger"",""Behaviour"":""Boss"",""Tier"":9,
 ""HP"":100,""Damage"":10,""Speed"":1.7,""AttackRange"":2.2,""AttackInterval"":1.1,""XpValue"":100,
 ""BodyRadius"":1.1,""Scale"":2.5}
]")
                .Add("Buff.json", @"[
{""Id"":""Frenzy"",""Name"":""疯狂"",""Description"":""击杀叠攻速"",""Effect"":""OnKillAttackSpeed"",
 ""Duration"":0,""MaxStacks"":20,""Interval"":0,""Tags"":""kill"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""Params"":[0.02]},
{""Id"":""ScarArmor"",""Name"":""疤痕护甲"",""Description"":""受击叠甲"",""Effect"":""OnHurtArmor"",
 ""Duration"":0,""MaxStacks"":15,""Interval"":0,""Tags"":""hurt"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""Params"":[2]},
{""Id"":""LastStand"",""Name"":""背水一战"",""Description"":""低血加攻"",""Effect"":""LowHpAttack"",
 ""Duration"":0,""MaxStacks"":1,""Interval"":0,""Tags"":""lowhp"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""Params"":[0.35,0.35]},
{""Id"":""Regen"",""Name"":""再生"",""Description"":""周期回复"",""Effect"":""PeriodicHeal"",
 ""Duration"":0,""MaxStacks"":1,""Interval"":5,""Tags"":""heal"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""Params"":[2]},
{""Id"":""GlassCannon"",""Name"":""玻璃大炮"",""Description"":""攻高血少"",""Effect"":""Stat"",
 ""Duration"":0,""MaxStacks"":1,""Interval"":0,""Tags"":""risk"",""StatA"":""Attack"",""ValueA"":0.25,
 ""OpA"":""Flat"",""StatB"":""MaxHP"",""ValueB"":-0.15,""OpB"":""Percent"",""Params"":[]}
]")
                .Add("Director.json", @"[
{""Id"":""D1"",""StartTime"":0,""EndTime"":5,""Threat"":1,""SpawnInterval"":1,""SpawnCountMin"":1,
 ""SpawnCountMax"":2,""EnemyPool"":[""Crawler""],""EliteChance"":0,""ConcurrentCap"":20,
 ""HpScale"":1,""DamageScale"":1,""SpeedScale"":1,""BossId"":"""",""BossAddInterval"":0,""BossAddCount"":0},
{""Id"":""D2"",""StartTime"":5,""EndTime"":10,""Threat"":2,""SpawnInterval"":0.5,""SpawnCountMin"":2,
 ""SpawnCountMax"":3,""EnemyPool"":[""Crawler""],""EliteChance"":0.1,""ConcurrentCap"":30,
 ""HpScale"":1.5,""DamageScale"":1.2,""SpeedScale"":1.05,""BossId"":""SutureGolem"",
 ""BossAddInterval"":2,""BossAddCount"":1}
]")
                .Add("Level.json", @"[
{""Id"":""L1"",""Level"":1,""XpToNext"":5,""OfferCount"":3,""RarityBonus"":0,""Note"":""1""},
{""Id"":""L2"",""Level"":2,""XpToNext"":6,""OfferCount"":3,""RarityBonus"":0.05,""Note"":""2""},
{""Id"":""L3"",""Level"":3,""XpToNext"":7,""OfferCount"":3,""RarityBonus"":0.1,""Note"":""3""},
{""Id"":""L4"",""Level"":4,""XpToNext"":8,""OfferCount"":3,""RarityBonus"":0.15,""Note"":""4""},
{""Id"":""L5"",""Level"":5,""XpToNext"":9,""OfferCount"":3,""RarityBonus"":0.2,""Note"":""5""},
{""Id"":""L6"",""Level"":6,""XpToNext"":10,""OfferCount"":3,""RarityBonus"":0.25,""Note"":""6""},
{""Id"":""L7"",""Level"":7,""XpToNext"":11,""OfferCount"":3,""RarityBonus"":0.3,""Note"":""7""},
{""Id"":""L8"",""Level"":8,""XpToNext"":12,""OfferCount"":3,""RarityBonus"":0.35,""Note"":""8""},
{""Id"":""L9"",""Level"":9,""XpToNext"":13,""OfferCount"":3,""RarityBonus"":0.4,""Note"":""9""},
{""Id"":""L10"",""Level"":10,""XpToNext"":14,""OfferCount"":3,""RarityBonus"":0.45,""Note"":""10""}
]")
                .Add("Upgrade.json", @"[
{""Id"":""U_Weapon_Bow"",""Name"":""弓箭"",""Description"":""远程"",""Category"":""Weapon"",""Rarity"":""Common"",
 ""Weight"":100,""WeaponId"":""Bow"",""CompanionId"":"""",""BuffId"":"""",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":5,""Tags"":[""ranged""],""Params"":[]},
{""Id"":""U_Weapon_Sword"",""Name"":""砍刀"",""Description"":""近战"",""Category"":""Weapon"",""Rarity"":""Common"",
 ""Weight"":95,""WeaponId"":""Sword"",""CompanionId"":"""",""BuffId"":"""",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":5,""Params"":[]},
{""Id"":""U_Comp_IronBox"",""Name"":""铁盒"",""Description"":""机械"",""Category"":""Companion"",""Rarity"":""Rare"",
 ""Weight"":55,""WeaponId"":"""",""CompanionId"":""IronBox"",""BuffId"":"""",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":3,""Params"":[]},
{""Id"":""U_Comp_LampGhost"",""Name"":""提灯鬼"",""Description"":""幽灵"",""Category"":""Companion"",""Rarity"":""Rare"",
 ""Weight"":55,""WeaponId"":"""",""CompanionId"":""LampGhost"",""BuffId"":"""",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":3,""Params"":[]},
{""Id"":""U_Stat_Attack"",""Name"":""磨刀石"",""Description"":""攻击 +10%"",""Category"":""Stat"",""Rarity"":""Common"",
 ""Weight"":100,""WeaponId"":"""",""CompanionId"":"""",""BuffId"":"""",""StatA"":""Attack"",""ValueA"":0.1,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":10,
 ""Tags"":[""attack""],""Params"":[]},
{""Id"":""U_Stat_MaxHP"",""Name"":""厚皮"",""Description"":""生命 +20"",""Category"":""Stat"",""Rarity"":""Common"",
 ""Weight"":95,""WeaponId"":"""",""CompanionId"":"""",""BuffId"":"""",""StatA"":""MaxHP"",""ValueA"":20,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":10,""Params"":[]},
{""Id"":""U_Buff_Frenzy"",""Name"":""疯狂"",""Description"":""击杀叠攻速"",""Category"":""Buff"",""Rarity"":""Epic"",
 ""Weight"":40,""WeaponId"":"""",""CompanionId"":"""",""BuffId"":""Frenzy"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":1,""Params"":[]},
{""Id"":""U_Buff_Scar"",""Name"":""疤痕护甲"",""Description"":""受击叠甲"",""Category"":""Buff"",""Rarity"":""Epic"",
 ""Weight"":40,""WeaponId"":"""",""CompanionId"":"""",""BuffId"":""ScarArmor"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":1,""Params"":[]},
{""Id"":""U_Buff_LastStand"",""Name"":""背水一战"",""Description"":""低血加攻"",""Category"":""Buff"",""Rarity"":""Legendary"",
 ""Weight"":18,""WeaponId"":"""",""CompanionId"":"""",""BuffId"":""LastStand"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":1,""Params"":[]},
{""Id"":""U_Buff_GlassCannon"",""Name"":""玻璃大炮"",""Description"":""攻高血少"",""Category"":""Buff"",""Rarity"":""Epic"",
 ""Weight"":35,""WeaponId"":"""",""CompanionId"":"""",""BuffId"":""GlassCannon"",""StatA"":""Unknown"",""ValueA"":0,
 ""OpA"":""Flat"",""StatB"":""Unknown"",""ValueB"":0,""OpB"":""Flat"",""MaxStacks"":1,""Params"":[]}
]")
                .Add("Combo.json", @"[
{""Id"":""C_Test"",""Name"":""测试组合"",""Description"":""远程 + 攻击"",""TagA"":""ranged"",""CountA"":1,
 ""TagB"":""attack"",""CountB"":1,""BuffId"":""Frenzy""}
]")
                .Add("Balance.json", @"[
{""Id"":""ArenaHalfExtent"",""Value"":67,""Note"":""""},
{""Id"":""DashSpeedMultiplier"",""Value"":3,""Note"":""""},
{""Id"":""DashDuration"",""Value"":0.15,""Note"":""""},
{""Id"":""DashCooldown"",""Value"":2,""Note"":""""},
{""Id"":""WeaponSlots"",""Value"":4,""Note"":""""},
{""Id"":""WeaponMaxLevel"",""Value"":5,""Note"":""""},
{""Id"":""WeaponDamagePerLevel"",""Value"":0.3,""Note"":""""},
{""Id"":""WeaponCooldownPerLevel"",""Value"":0.92,""Note"":""""},
{""Id"":""WeaponProjectileMilestone"",""Value"":3,""Note"":""""},
{""Id"":""WeaponPierceMilestone"",""Value"":4,""Note"":""""},
{""Id"":""HeartDropChance"",""Value"":0.12,""Note"":""""},
{""Id"":""HeartHeal"",""Value"":8,""Note"":""""},
{""Id"":""EliteHpScale"",""Value"":3.2,""Note"":""""},
{""Id"":""EliteDamageScale"",""Value"":1.5,""Note"":""""},
{""Id"":""EliteSpeedScale"",""Value"":1.05,""Note"":""""},
{""Id"":""EliteXpScale"",""Value"":4,""Note"":""""},
{""Id"":""EliteScale"",""Value"":1.35,""Note"":""""},
{""Id"":""BuildWeightWeapon"",""Value"":30,""Note"":""""},
{""Id"":""BuildWeightCompanion"",""Value"":18,""Note"":""""},
{""Id"":""BuildWeightStat"",""Value"":38,""Note"":""""},
{""Id"":""BuildWeightBuff"",""Value"":14,""Note"":""""},
{""Id"":""RerollsPerLevel"",""Value"":1,""Note"":""""},
{""Id"":""LuckRarityWeight"",""Value"":0.01,""Note"":""""}
]");

            _config = ConfigDatabase.Load(source);
            return _config;
        }

        public static ExperienceCurve Curve() => new ExperienceCurve(Config().Levels);

        public static RunState NewRun() => new RunState(Curve());

        public static WeaponDef Weapon(string id)
        {
            return Config().Weapons[id];
        }

        public static EnemyDef Enemy(string id)
        {
            return Config().Enemies[id];
        }
    }
}
