#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 Config/GameConfig.xlsx 模板。

只依赖标准库。xlsx 就是几份 XML 打包成 zip。
已经存在时不覆盖，除非加 --force。

这一版的表结构对应"单局时间轴 + 经验升级构筑 + 时间到打 BOSS"：
Character / Weapon / Companion / Enemy / Buff / Director / Level / Upgrade / Balance。
旧的 Wave 与 Item（波次商店）已删除。
"""

import argparse
import sys
import zipfile
from pathlib import Path


def level_rows():
    """30 级经验曲线。经验需求按 1.16 复利增长，升级给 3 张卡，10/20 级给 4 张。"""
    rows = []
    for level in range(1, 31):
        xp = round(6 * (1.16 ** (level - 1)))
        offers = 4 if level in (10, 20) else 3
        bonus = round(min(1.0, (level - 1) / 29.0), 3)
        rows.append(["L" + str(level), level, xp, offers, bonus, "等级 " + str(level)])
    return rows


# 构筑标签。组合效果按标签数量判定；没有标签的卡不参与组合。
UPGRADE_TAGS = {
    "U_Weapon_Bow": ["ranged"],
    "U_Weapon_Sword": ["melee"],
    "U_Weapon_Dagger": ["melee", "crit", "attackSpeed"],
    "U_Weapon_Crossbow": ["ranged", "pierce", "crit"],
    "U_Weapon_Orb": ["ranged", "multi"],
    "U_Weapon_Hammer": ["melee", "aoe"],
    "U_Comp_IronBox": ["companion", "ranged"],
    "U_Comp_LampGhost": ["companion", "ranged"],
    "U_Comp_SquintFox": ["companion", "melee"],
    "U_Comp_StitchedDoll": ["companion", "ranged"],
    "U_Comp_Gearling": ["companion", "melee"],
    "U_Comp_Wisp": ["companion", "ranged"],
    "U_Stat_Attack": ["attack"],
    "U_Stat_AttackSpeed": ["attackSpeed"],
    "U_Stat_MaxHP": ["maxhp"],
    "U_Stat_MoveSpeed": ["movespeed"],
    "U_Stat_Crit": ["crit"],
    "U_Stat_CritDamage": ["crit"],
    "U_Stat_Range": ["range"],
    "U_Stat_Pickup": ["pickup"],
    "U_Stat_Armor": ["armor"],
    "U_Stat_Regen": ["heal"],
    "U_Stat_Projectile": ["projectile"],
    "U_Stat_Luck": ["luck"],
    "U_Stat_Contact": ["contact"],
    "U_Stat_Dodge": ["dodge"],
    "U_Buff_Frenzy": ["attackSpeed", "kill"],
    "U_Buff_BloodBag": ["heal", "kill"],
    "U_Buff_ScarArmor": ["armor", "hurt"],
    "U_Buff_LastStand": ["attack", "lowhp"],
    "U_Buff_Regen": ["heal"],
    "U_Buff_GlassCannon": ["attack", "maxhp"],
    "U_Buff_Swift": ["attackSpeed", "movespeed"],
    "U_Buff_Giant": ["maxhp", "movespeed"],
    "U_Buff_Lucky": ["luck"],
    "U_Buff_Berserk": ["attackSpeed", "kill"],
    "U_Buff_Medic": ["heal"],
    "U_Buff_Vampire": ["heal", "kill"],
    "U_Buff_Combustion": ["explosive"],
    "U_Buff_Chain": ["chain"],
    "U_Buff_Orbit": ["orbit"],
    "U_Buff_Vampiric": ["lifesteal"],
    "U_Buff_Thorns": ["thorns"],
}


def upgrade(uid, name, desc, category, rarity, weight, weapon="", companion="", buff="",
            stat_a="Unknown", value_a=0, op_a="Flat", stat_b="Unknown", value_b=0, op_b="Flat",
            max_stacks=1, params=None):
    return [uid, name, desc, category, rarity, weight, weapon, companion, buff,
            stat_a, value_a, op_a, stat_b, value_b, op_b, max_stacks,
            UPGRADE_TAGS.get(uid, []), params or []]


# 每个 sheet: (名称, 表头, 类型行, 说明行, 数据)
SHEETS = [
    (
        "Character",
        ["Id", "Name", "Description", "BaseHP", "BaseMoveSpeed", "BaseAttackInterval",
         "BaseDamage", "BaseRange", "StartingWeaponId", "TraitType"],
        ["string", "string", "string", "float", "float", "float",
         "float", "float", "id:Weapon", "string"],
        ["唯一 Id", "显示名", "一句话描述", "初始生命", "移速 米/秒", "攻击间隔 秒",
         "初始伤害", "初始射程 米", "初始武器", "特性类名"],
        [
            ["Scavenger", "拾荒", "均衡型，拾取范围 +50%", 100, 5.0, 0.8, 6, 8, "Bow", "ScavengerTrait"],
            ["Sack", "布袋", "不能移动，全图吸取经验", 130, 0.0, 0.75, 7, 7, "Sword", "SackTrait"],
            ["Biter", "咬咬", "碰撞流，击杀回血", 100, 5.5, 0.9, 5, 2.4, "Sword", "BiterTrait"],
        ],
    ),
    (
        "Weapon",
        ["Id", "Name", "Description", "Kind", "BaseDamage", "Cooldown", "Range", "Projectiles",
         "SpreadDeg", "BehaviourType", "ProjectileSpeed", "Pierce", "Params"],
        ["string", "string", "string", "enum:WeaponKind", "float", "float", "float", "int",
         "float", "string", "float", "int", "list:float"],
        ["唯一 Id", "显示名", "构筑卡描述", "远程/近战", "单发伤害", "冷却 秒", "射程 米", "弹道数",
         "散射角 度", "行为类名", "投射物速度", "穿透数", "行为自定义参数"],
        [
            ["Bow", "弓箭", "自动索敌，稳定远程", "Ranged", 8, 0.85, 8, 1, 0, "Ranged", 18, 1, [0]],
            ["Sword", "砍刀", "近身扇形，伤害高", "Melee", 15, 0.6, 2.2, 1, 45, "Melee", 0, 0, [0.08, 0.22]],
            ["Dagger", "双匕", "极快近战，适合叠触发", "Melee", 7, 0.32, 1.9, 1, 35, "Melee", 0, 0, [0.04, 0.14]],
            ["Crossbow", "重弩", "慢速高伤，穿透四人", "Ranged", 26, 1.35, 10, 1, 0, "Ranged", 22, 4, [0]],
            ["Orb", "灵球", "三发散射，覆盖面广", "Ranged", 5, 0.9, 6.5, 3, 14, "Ranged", 14, 1, [0]],
            ["Hammer", "战锤", "全向重击，清场用", "Melee", 30, 1.2, 2.8, 1, 180, "Melee", 0, 0, [0.18, 0.36]],
        ],
    ),
    (
        "Companion",
        ["Id", "Name", "Description", "Faction", "WeaponId", "FollowRadius", "MaxHP",
         "AI", "AggroRadius", "PreferredRange", "LeashDistance"],
        ["string", "string", "string", "enum:CompanionFaction", "id:Weapon", "float", "float",
         "enum:CompanionAI", "float", "float", "float"],
        ["唯一 Id", "显示名", "构筑卡描述", "阵营", "自带武器", "跟随半径 米", "生命",
         "AI", "接敌半径 米", "理想距离 米", "活动半径 米"],
        [
            ["IronBox", "铁盒", "机械阵营，稳定远程", "Machine", "Bow", 1.6, 40, "Kiter", 9, 5.5, 8],
            ["LampGhost", "提灯鬼", "幽灵阵营，灵体输出", "Ghost", "Bow", 1.8, 30, "Kiter", 9, 5.5, 8],
            ["SquintFox", "眯眼狐", "野兽阵营，近身撕咬", "Beast", "Sword", 1.4, 45, "Aggro", 7, 1.4, 8],
            ["StitchedDoll", "缝合娃娃", "玩偶阵营，贴身辅助", "Doll", "Bow", 1.7, 38, "Guard", 5, 4.5, 6],
            ["Gearling", "齿轮崽", "机械阵营，近战钻头", "Machine", "Sword", 1.5, 42, "Aggro", 6, 1.3, 7],
            ["Wisp", "幽光", "幽灵阵营，灵球散射", "Ghost", "Orb", 1.9, 28, "Kiter", 9, 6, 8],
        ],
    ),
    (
        "Enemy",
        ["Id", "Name", "PrefabId", "Behaviour", "Tier", "HP", "Damage", "Speed", "AttackRange",
         "AttackInterval", "XpValue", "BodyRadius", "Scale"],
        ["string", "string", "string", "enum:EnemyBehaviourType", "int", "float", "float", "float",
         "float", "float", "int", "float", "float"],
        ["唯一 Id", "显示名", "复用哪个预制体", "行为", "威胁等级", "生命", "伤害", "移速",
         "攻击距离", "攻击间隔", "经验", "碰撞半径", "视觉/判定缩放"],
        [
            ["Crawler", "爬虫", "Crawler", "Chase", 1, 12, 6, 3.0, 0.6, 1.0, 1, 0.35, 1.0],
            ["Runner", "疾行虫", "Crawler", "Chase", 2, 16, 7, 3.8, 0.55, 0.9, 2, 0.32, 0.9],
            ["Thrower", "投手", "Thrower", "Ranged", 2, 18, 8, 2.0, 5.0, 2.5, 2, 0.35, 1.0],
            ["Charger", "冲锋兵", "Charger", "Charge", 3, 30, 11, 2.5, 4.0, 3.0, 3, 0.40, 1.1],
            ["Brute", "重装", "Charger", "Chase", 4, 90, 16, 1.9, 0.9, 1.2, 6, 0.55, 1.5],
            ["Warden", "守卫", "Charger", "Shield", 5, 160, 20, 1.6, 1.0, 1.4, 10, 0.60, 1.6],
            ["Ravager", "掠食者", "Crawler", "Chase", 5, 70, 14, 3.2, 0.7, 0.8, 8, 0.45, 1.2],
            ["SutureGolem", "缝线巨偶", "Charger", "Boss", 9, 9000, 32, 1.7, 2.2, 1.1, 300, 1.10, 2.5],
        ],
    ),
    (
        "Buff",
        ["Id", "Name", "Description", "Effect", "Duration", "MaxStacks", "Interval", "Tags",
         "StatA", "ValueA", "OpA", "StatB", "ValueB", "OpB", "Params"],
        ["string", "string", "string", "enum:BuffEffectType", "float", "int", "float", "string",
         "enum:StatType", "float", "enum:StatOp", "enum:StatType", "float", "enum:StatOp", "list:float"],
        ["唯一 Id", "显示名", "描述", "触发类型", "持续 秒（0 常驻）", "叠层上限", "触发间隔",
         "标签", "属性 A", "数值 A", "方式 A", "属性 B", "数值 B", "方式 B", "效果参数"],
        [
            ["Frenzy", "疯狂", "每击杀 +2% 攻速，最多 20 层", "OnKillAttackSpeed", 0, 20, 0, "kill,offense",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [0.02]],
            ["BloodBag", "血袋", "每击杀回复 1.5 生命", "OnKillHeal", 0, 1, 0, "kill,defense",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [1.5]],
            ["ScarArmor", "疤痕护甲", "每次受击 +2 护甲，最多 15 层", "OnHurtArmor", 0, 15, 0, "hurt,defense",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [2]],
            ["LastStand", "背水一战", "生命低于 35% 时攻击 +35%", "LowHpAttack", 0, 1, 0, "lowhp,offense",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [0.35, 0.35]],
            ["Regen", "再生", "每 5 秒回复 2 生命", "PeriodicHeal", 0, 1, 5, "heal",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [2]],
            ["GlassCannon", "玻璃大炮", "攻击 +25%，生命上限 -15%", "Stat", 0, 1, 0, "offense,risk",
             "Attack", 0.25, "Flat", "MaxHP", -0.15, "Percent", []],
            ["Swift", "疾风", "攻速 +12%，移速 +10%", "Stat", 0, 1, 0, "speed",
             "AttackSpeed", 0.12, "Flat", "MoveSpeed", 0.10, "Percent", []],
            ["Giant", "巨人", "生命上限 +30，移速 -5%", "Stat", 0, 1, 0, "defense",
             "MaxHP", 30, "Flat", "MoveSpeed", -0.05, "Percent", []],
            ["Lucky", "幸运星", "幸运 +10", "Stat", 0, 1, 0, "economy",
             "Luck", 10, "Flat", "Unknown", 0, "Flat", []],
            ["Berserk", "暴怒", "每击杀 +1.5% 攻速，最多 30 层", "OnKillAttackSpeed", 0, 30, 0, "kill,offense",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [0.015]],
            ["Medic", "战地医师", "每 8 秒回复 4 生命", "PeriodicHeal", 0, 1, 8, "heal",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [4]],
            ["Vampire", "吸血", "每击杀回复 2.5 生命", "OnKillHeal", 0, 1, 0, "kill,defense",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [2.5]],
            ["Combustion", "燃烧弹", "击杀时引发范围爆炸", "OnKillExplode", 0, 1, 0, "explosive",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [3.2, 10]],
            ["Chain", "导电", "命中时向附近敌人弹射闪电", "OnHitChain", 0, 1, 0.4, "chain",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [4.5, 6, 3]],
            ["OrbitBlade", "环绕刀", "刀片绕玩家旋转，周期伤害", "OrbitDamage", 0, 1, 0.6, "orbit",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [2.4, 8]],
            ["Vampiric", "吸血獠牙", "造成伤害时按比例回血", "Lifesteal", 0, 1, 0, "lifesteal",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [0.12]],
            ["Thorns", "荆棘甲", "受击时对周围敌人反伤", "OnHurtThorns", 0, 1, 0, "thorns",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [3.0, 8]],
            ["Overload", "过载", "命中时引发小范围爆炸", "OnHitExplode", 0, 1, 1.0, "combo",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [2.8, 10]],
            ["BloodRite", "血契", "暴击时吸血提高", "Lifesteal", 0, 1, 0, "combo",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [0.15]],
            ["Ballista", "弩阵", "弹道 +1", "Stat", 0, 1, 0, "combo",
             "ProjectileCount", 1, "Flat", "Unknown", 0, "Flat", []],
            ["Whirlwind", "旋风", "环绕刀片伤害更高", "OrbitDamage", 0, 1, 0.5, "combo",
             "Unknown", 0, "Flat", "Unknown", 0, "Flat", [2.8, 12]],
        ],
    ),
    (
        "Director",
        ["Id", "StartTime", "EndTime", "Threat", "SpawnInterval", "SpawnCountMin", "SpawnCountMax",
         "EnemyPool", "EliteChance", "ConcurrentCap", "HpScale", "DamageScale", "SpeedScale",
         "BossId", "BossAddInterval", "BossAddCount"],
        ["string", "float", "float", "int", "float", "int", "int", "list:id:Enemy", "float", "int",
         "float", "float", "float", "id:Enemy", "float", "int"],
        ["唯一 Id", "开始 秒", "结束 秒", "威胁等级", "刷怪间隔", "单批最少", "单批最多",
         "怪种池", "精英概率", "同屏上限", "血量倍率", "伤害倍率", "速度倍率",
         "BOSS Id（只写最后一段）", "BOSS 召唤间隔", "BOSS 召唤数量"],
        [
            ["D1", 0, 45, 1, 2.2, 1, 1, ["Crawler"], 0.0, 12, 1.0, 1.0, 1.0, "", 0, 0],
            ["D2", 45, 105, 2, 1.9, 1, 2, ["Crawler", "Runner"], 0.0, 20, 1.10, 1.05, 1.0, "", 0, 0],
            ["D3", 105, 180, 3, 1.6, 2, 2, ["Crawler", "Runner", "Thrower"], 0.01, 30, 1.25, 1.15, 1.02, "", 0, 0],
            ["D4", 180, 270, 4, 1.35, 2, 3, ["Crawler", "Runner", "Thrower", "Charger"], 0.03, 45, 1.50, 1.28, 1.04, "", 0, 0],
            ["D5", 270, 360, 5, 1.15, 3, 3, ["Crawler", "Runner", "Thrower", "Charger", "Brute"], 0.05, 60, 1.80, 1.42, 1.06, "", 0, 0],
            ["D6", 360, 450, 6, 1.0, 3, 4, ["Crawler", "Runner", "Thrower", "Charger", "Brute", "Warden"], 0.07, 80, 2.10, 1.58, 1.08, "", 0, 0],
            ["D7", 450, 540, 7, 0.85, 4, 4, ["Crawler", "Runner", "Thrower", "Charger", "Brute", "Warden", "Ravager"], 0.09, 100, 2.50, 1.75, 1.10, "", 0, 0],
            ["D8", 540, 600, 8, 0.7, 4, 5, ["Crawler", "Runner", "Thrower", "Charger", "Brute", "Warden", "Ravager"], 0.12, 120, 2.90, 1.95, 1.12, "SutureGolem", 12, 4],
        ],
    ),
    (
        "Level",
        ["Id", "Level", "XpToNext", "OfferCount", "RarityBonus", "Note"],
        ["string", "int", "int", "int", "float", "string"],
        ["唯一 Id", "当前等级", "升到下一级所需经验", "升级给几张卡", "稀有度上浮 0..1", "备注"],
        level_rows(),
    ),
    (
        "Upgrade",
        ["Id", "Name", "Description", "Category", "Rarity", "Weight", "WeaponId", "CompanionId",
         "BuffId", "StatA", "ValueA", "OpA", "StatB", "ValueB", "OpB", "MaxStacks", "Tags", "Params"],
        ["string", "string", "string", "enum:UpgradeCategory", "enum:Rarity", "int", "id:Weapon",
         "id:Companion", "id:Buff", "enum:StatType", "float", "enum:StatOp", "enum:StatType",
         "float", "enum:StatOp", "int", "list:string", "list:float"],
        ["唯一 Id", "显示名", "构筑卡描述", "类别", "品质", "权重", "武器 Id", "旅伴 Id", "buff Id",
         "属性 A", "数值 A", "方式 A", "属性 B", "数值 B", "方式 B", "最多拿几次", "构筑标签", "参数"],
        [
            # 武器：拿新武器或升级已有武器，等级上限由 Balance 的 WeaponMaxLevel 管
            upgrade("U_Weapon_Bow", "弓箭", "自动索敌，稳定远程", "Weapon", "Common", 100, weapon="Bow", max_stacks=5),
            upgrade("U_Weapon_Sword", "砍刀", "近身扇形，伤害高", "Weapon", "Common", 95, weapon="Sword", max_stacks=5),
            upgrade("U_Weapon_Dagger", "双匕", "极快近战，适合叠触发", "Weapon", "Common", 100, weapon="Dagger", max_stacks=5),
            upgrade("U_Weapon_Crossbow", "重弩", "慢速高伤，穿透四人", "Weapon", "Rare", 80, weapon="Crossbow", max_stacks=5),
            upgrade("U_Weapon_Orb", "灵球", "三发散射，覆盖面广", "Weapon", "Rare", 85, weapon="Orb", max_stacks=5),
            upgrade("U_Weapon_Hammer", "战锤", "全向重击，清场用", "Weapon", "Epic", 55, weapon="Hammer", max_stacks=5),
            # 旅伴：招募或升级，队伍上限 4
            upgrade("U_Comp_IronBox", "铁盒", "机械阵营，稳定远程", "Companion", "Rare", 55, companion="IronBox", max_stacks=3),
            upgrade("U_Comp_LampGhost", "提灯鬼", "幽灵阵营，灵体输出", "Companion", "Rare", 55, companion="LampGhost", max_stacks=3),
            upgrade("U_Comp_SquintFox", "眯眼狐", "野兽阵营，近身撕咬", "Companion", "Rare", 55, companion="SquintFox", max_stacks=3),
            upgrade("U_Comp_StitchedDoll", "缝合娃娃", "玩偶阵营，贴身辅助", "Companion", "Epic", 45, companion="StitchedDoll", max_stacks=3),
            upgrade("U_Comp_Gearling", "齿轮崽", "机械阵营，近战钻头", "Companion", "Rare", 50, companion="Gearling", max_stacks=3),
            upgrade("U_Comp_Wisp", "幽光", "幽灵阵营，灵球散射", "Companion", "Rare", 50, companion="Wisp", max_stacks=3),
            # 数值：攻击力/攻速/生命/移速/暴击/射程/拾取/护甲/回复/弹道/幸运/碰撞/闪避
            upgrade("U_Stat_Attack", "磨刀石", "攻击力 +10%", "Stat", "Common", 100,
                    stat_a="Attack", value_a=0.10, max_stacks=10),
            upgrade("U_Stat_AttackSpeed", "齿轮", "攻击速度 +8%", "Stat", "Common", 95,
                    stat_a="AttackSpeed", value_a=0.08, max_stacks=10),
            upgrade("U_Stat_MaxHP", "厚皮", "生命上限 +20", "Stat", "Common", 95,
                    stat_a="MaxHP", value_a=20, max_stacks=10),
            upgrade("U_Stat_MoveSpeed", "轻靴", "移动速度 +8%", "Stat", "Common", 80,
                    stat_a="MoveSpeed", value_a=0.08, op_a="Percent", max_stacks=8),
            upgrade("U_Stat_Crit", "锐眼", "暴击率 +5%", "Stat", "Rare", 60,
                    stat_a="CritChance", value_a=0.05, max_stacks=8),
            upgrade("U_Stat_CritDamage", "重击", "暴击伤害 +25%", "Stat", "Rare", 60,
                    stat_a="CritDamage", value_a=0.25, max_stacks=8),
            upgrade("U_Stat_Range", "长弓弦", "射程 +12%", "Stat", "Rare", 55,
                    stat_a="Range", value_a=0.12, max_stacks=6),
            upgrade("U_Stat_Pickup", "磁石", "拾取范围 +0.5 米", "Stat", "Common", 60,
                    stat_a="PickupRadius", value_a=0.5, max_stacks=6),
            upgrade("U_Stat_Armor", "护甲片", "护甲 +5", "Stat", "Rare", 55,
                    stat_a="Armor", value_a=5, max_stacks=8),
            upgrade("U_Stat_Regen", "绷带", "生命回复 +0.3/秒", "Stat", "Rare", 45,
                    stat_a="Regen", value_a=0.3, max_stacks=8),
            upgrade("U_Stat_Projectile", "分裂箭", "弹道数 +1", "Stat", "Epic", 25,
                    stat_a="ProjectileCount", value_a=1, max_stacks=3),
            upgrade("U_Stat_Luck", "四叶草", "幸运 +8", "Stat", "Rare", 50,
                    stat_a="Luck", value_a=8, max_stacks=5),
            upgrade("U_Stat_Contact", "倒刺", "碰撞伤害 +8", "Stat", "Rare", 45,
                    stat_a="ContactDamage", value_a=8, max_stacks=6),
            upgrade("U_Stat_Dodge", "残影", "闪避 +4%", "Stat", "Epic", 30,
                    stat_a="Dodge", value_a=0.04, max_stacks=6),
            # buff 道具
            upgrade("U_Buff_Frenzy", "疯狂", "每击杀 +2% 攻速，最多 20 层", "Buff", "Epic", 40, buff="Frenzy"),
            upgrade("U_Buff_BloodBag", "血袋", "每击杀回复 1.5 生命", "Buff", "Rare", 55, buff="BloodBag"),
            upgrade("U_Buff_ScarArmor", "疤痕护甲", "每次受击 +2 护甲，最多 15 层", "Buff", "Epic", 40, buff="ScarArmor"),
            upgrade("U_Buff_LastStand", "背水一战", "生命低于 35% 时攻击 +35%", "Buff", "Legendary", 18, buff="LastStand"),
            upgrade("U_Buff_Regen", "再生", "每 5 秒回复 2 生命", "Buff", "Rare", 55, buff="Regen"),
            upgrade("U_Buff_GlassCannon", "玻璃大炮", "攻击 +25%，生命上限 -15%", "Buff", "Epic", 35, buff="GlassCannon"),
            upgrade("U_Buff_Swift", "疾风", "攻速 +12%，移速 +10%", "Buff", "Rare", 50, buff="Swift"),
            upgrade("U_Buff_Giant", "巨人", "生命上限 +30，移速 -5%", "Buff", "Rare", 45, buff="Giant"),
            upgrade("U_Buff_Lucky", "幸运星", "幸运 +10", "Buff", "Rare", 45, buff="Lucky"),
            upgrade("U_Buff_Berserk", "暴怒", "每击杀 +1.5% 攻速，最多 30 层", "Buff", "Epic", 35, buff="Berserk"),
            upgrade("U_Buff_Medic", "战地医师", "每 8 秒回复 4 生命", "Buff", "Rare", 45, buff="Medic"),
            upgrade("U_Buff_Vampire", "吸血", "每击杀回复 2.5 生命", "Buff", "Legendary", 18, buff="Vampire"),
            upgrade("U_Buff_Combustion", "燃烧弹", "击杀时引发范围爆炸", "Buff", "Epic", 35, buff="Combustion"),
            upgrade("U_Buff_Chain", "导电", "命中时向附近敌人弹射闪电", "Buff", "Epic", 35, buff="Chain"),
            upgrade("U_Buff_Orbit", "环绕刀", "刀片绕玩家旋转，周期伤害", "Buff", "Rare", 45, buff="OrbitBlade"),
            upgrade("U_Buff_Vampiric", "吸血獠牙", "造成伤害时按比例回血", "Buff", "Rare", 50, buff="Vampiric"),
            upgrade("U_Buff_Thorns", "荆棘甲", "受击时对周围敌人反伤", "Buff", "Rare", 45, buff="Thorns"),
        ],
    ),
    (
        "Combo",
        ["Id", "Name", "Description", "TagA", "CountA", "TagB", "CountB", "BuffId"],
        ["string", "string", "string", "string", "int", "string", "int", "id:Buff"],
        ["唯一 Id", "显示名", "组合描述", "标签 A", "A 数量", "标签 B", "B 数量", "获得的 buff"],
        [
            ["C_Overload", "过载", "爆炸 + 闪电 → 命中引发爆炸", "explosive", 1, "chain", 1, "Overload"],
            ["C_BloodRite", "血契", "吸血 + 暴击 → 吸血比例提高", "lifesteal", 1, "crit", 1, "BloodRite"],
            ["C_Ballista", "弩阵", "2 远程 + 穿透 → 弹道 +1", "ranged", 2, "pierce", 1, "Ballista"],
            ["C_Whirlwind", "旋风", "2 近战 + 攻速 → 环绕刀", "melee", 2, "attackSpeed", 1, "Whirlwind"],
        ],
    ),
    (
        "Balance",
        ["Id", "Value", "Note"],
        ["string", "float", "string"],
        ["常量 Id", "数值", "说明"],
        [
            ["ArenaHalfExtent", 67, "玩家可达区域的半边长 米"],
            ["DashSpeedMultiplier", 3.0, "冲刺速度是移速的倍数"],
            ["DashDuration", 0.15, "冲刺持续 秒"],
            ["DashCooldown", 2.0, "冲刺冷却 秒"],
            ["WeaponSlots", 4, "玩家同时持有的武器上限"],
            ["WeaponMaxLevel", 5, "单把武器等级上限"],
            ["WeaponDamagePerLevel", 0.30, "武器每级伤害 +30%"],
            ["WeaponCooldownPerLevel", 0.92, "武器每级间隔 ×0.92"],
            ["WeaponProjectileMilestone", 3, "武器到这一级额外 +1 弹道"],
            ["WeaponPierceMilestone", 4, "武器到这一级额外 +1 穿透"],
            ["HeartDropChance", 0.12, "击杀掉心概率"],
            ["HeartHeal", 8, "一颗心回复生命"],
            ["EliteHpScale", 3.2, "精英血量倍率"],
            ["EliteDamageScale", 1.5, "精英伤害倍率"],
            ["EliteSpeedScale", 1.05, "精英移速倍率"],
            ["EliteXpScale", 4, "精英经验倍率"],
            ["EliteScale", 1.35, "精英视觉缩放"],
            ["BuildWeightWeapon", 30, "抽卡类别权重：武器"],
            ["BuildWeightCompanion", 18, "抽卡类别权重：旅伴"],
            ["BuildWeightStat", 32, "抽卡类别权重：数值"],
            ["BuildWeightBuff", 22, "抽卡类别权重：buff/道具机制"],
            ["RerollsPerLevel", 1, "每次升级的免费重抽次数"],
            ["LuckRarityWeight", 0.01, "每点幸运增加的稀有度上浮"],
        ],
    ),
]


def col_name(idx):
    s = ""
    while idx > 0:
        idx, rem = divmod(idx - 1, 26)
        s = chr(65 + rem) + s
    return s


class SharedStrings:
    def __init__(self):
        self.items = []
        self.index = {}

    def get(self, value):
        key = str(value)
        if key not in self.index:
            self.index[key] = len(self.items)
            self.items.append(key)
        return self.index[key]

    def xml(self):
        body = "".join(
            '<si><t xml:space="preserve">%s</t></si>' % esc(s) for s in self.items
        )
        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'count="%d" uniqueCount="%d">%s</sst>' % (len(self.items), len(self.items), body)
        )


def esc(s):
    return (
        str(s)
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def cell_xml(ref, value, shared):
    if value is None or value == "" or (isinstance(value, list) and not value):
        if isinstance(value, list):
            return ""
        return ""
    if isinstance(value, bool):
        return '<c r="%s" t="b"><v>%d</v></c>' % (ref, 1 if value else 0)
    if isinstance(value, (int, float)):
        text = repr(value) if isinstance(value, float) else str(value)
        return '<c r="%s"><v>%s</v></c>' % (ref, text)
    if isinstance(value, list):
        text = "|".join(str(v) for v in value)
    else:
        text = str(value)
    return '<c r="%s" t="s"><v>%d</v></c>' % (ref, shared.get(text))


def sheet_xml(header, types, desc, rows, shared):
    lines = []
    all_rows = [header, types, desc] + rows
    for r, row in enumerate(all_rows, start=1):
        cells = []
        for c, value in enumerate(row, start=1):
            xml = cell_xml("%s%d" % (col_name(c), r), value, shared)
            if xml:
                cells.append(xml)
        lines.append('<row r="%d">%s</row>' % (r, "".join(cells)))
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        '<sheetData>%s</sheetData></worksheet>' % "".join(lines)
    )


def build(path, force=False):
    if path.exists() and not force:
        print(f"{path} 已存在，跳过（加 --force 覆盖）")
        return 0

    shared = SharedStrings()
    sheet_parts = [sheet_xml(h, t, d, rows, shared) for _, h, t, d, rows in SHEETS]

    content_types = [
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>',
        '<Default Extension="xml" ContentType="application/xml"/>',
        '<Override PartName="/xl/workbook.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>',
        '<Override PartName="/xl/sharedStrings.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>',
        '<Override PartName="/xl/styles.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>',
    ]
    for i in range(len(SHEETS)):
        content_types.append(
            '<Override PartName="/xl/worksheets/sheet%d.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>' % (i + 1)
        )

    sheet_nodes = "".join(
        '<sheet name="%s" sheetId="%d" r:id="rId%d"/>' % (esc(name), i + 1, i + 1)
        for i, (name, *_rest) in enumerate(SHEETS)
    )
    workbook = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        "<sheets>%s</sheets></workbook>" % sheet_nodes
    )

    wb_rels = ['<Relationship Id="rId0" '
               'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" '
               'Target="styles.xml"/>']
    for i in range(len(SHEETS)):
        wb_rels.append(
            '<Relationship Id="rId%d" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
            'Target="worksheets/sheet%d.xml"/>' % (i + 1, i + 1)
        )
    wb_rels.append(
        '<Relationship Id="rId%d" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" '
        'Target="sharedStrings.xml"/>' % (len(SHEETS) + 1)
    )
    workbook_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">%s</Relationships>'
        % "".join(wb_rels)
    )

    rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="xl/workbook.xml"/></Relationships>'
    )

    styles = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        '<fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>'
        '<fills count="1"><fill><patternFill patternType="none"/></fill></fills>'
        '<borders count="1"><border/></borders>'
        '<cellStyleXfs count="1"><xf/></cellStyleXfs>'
        '<cellXfs count="1"><xf xfId="0"/></cellXfs>'
        "</styleSheet>"
    )

    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml",
                   '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                   '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">%s</Types>'
                   % "".join(content_types))
        z.writestr("_rels/.rels", rels)
        z.writestr("xl/workbook.xml", workbook)
        z.writestr("xl/_rels/workbook.xml.rels", workbook_rels)
        z.writestr("xl/styles.xml", styles)
        z.writestr("xl/sharedStrings.xml", shared.xml())
        for i, part in enumerate(sheet_parts):
            z.writestr("xl/worksheets/sheet%d.xml" % (i + 1), part)

    print(f"已生成 {path}（{len(SHEETS)} 个 sheet）")
    return 0


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="Config/GameConfig.xlsx")
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args(argv)
    return build(Path(args.out), args.force)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
