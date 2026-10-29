#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 Config/GameConfig.xlsx 模板。

只依赖标准库。xlsx 就是几份 XML 打包成 zip。
已经存在时不覆盖，除非加 --force。
"""

import argparse
import sys
import zipfile
from pathlib import Path

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
            ["Sack", "布袋", "不能移动，全图吸取金币", 130, 0.0, 0.75, 7, 7, "Sword", "SackTrait"],
            ["Biter", "咬咬", "碰撞流，击杀回血", 100, 5.5, 0.9, 5, 2.4, "Sword", "BiterTrait"],
        ],
    ),
    (
        "Weapon",
        ["Id", "Name", "Kind", "BaseDamage", "Cooldown", "Range", "Projectiles",
         "SpreadDeg", "BehaviourType", "ProjectileSpeed", "Pierce", "Params"],
        ["string", "string", "enum:WeaponKind", "float", "float", "float", "int",
         "float", "string", "float", "int", "list:float"],
        ["唯一 Id", "显示名", "远程/近战", "单发伤害", "冷却 秒", "射程 米", "弹道数",
         "散射角 度", "行为类名", "投射物速度", "穿透数", "行为自定义参数"],
        [
            ["Bow", "弓箭", "Ranged", 8, 0.85, 8, 1, 0, "Ranged", 18, 1, [0]],
            ["Sword", "砍刀", "Melee", 15, 0.6, 2.2, 1, 45, "Melee", 0, 0, [0.08, 0.22]],
        ],
    ),
    (
        "Companion",
        ["Id", "Name", "Faction", "WeaponId", "FollowRadius", "MaxHP"],
        ["string", "string", "enum:CompanionFaction", "id:Weapon", "float", "float"],
        ["唯一 Id", "显示名", "阵营", "自带武器", "跟随半径 米", "生命"],
        [
            ["IronBox", "铁盒", "Machine", "Bow", 1.6, 40],
            ["LampGhost", "提灯鬼", "Ghost", "Bow", 1.8, 30],
            ["SquintFox", "眯眼狐", "Beast", "Sword", 1.4, 45],
        ],
    ),
    (
        "Item",
        ["Id", "Name", "Description", "Rarity", "BasePrice", "Category",
         "StatA", "ValueA", "StatB", "ValueB", "MaxStack", "EffectType", "EffectParams"],
        ["string", "string", "string", "enum:ItemRarity", "int", "enum:ItemCategory",
         "enum:StatType", "float", "enum:StatType", "float", "int", "string", "list:float"],
        ["唯一 Id", "显示名", "效果描述", "品质", "基准价", "分类",
         "属性 A", "数值 A", "属性 B", "数值 B", "最大持有", "触发效果类名", "效果参数"],
        [
            ["WoolGlove", "毛线手套", "生命回复 -2，拾取金币 10% 回 1 血", "Common", 25, "Trigger",
             "Regen", -2, "Unknown", 0, 3, "CoinHeal", [0.1, 1]],
            ["IronNeck", "铁皮护颈", "碰撞伤害 +10", "Rare", 45, "Passive",
             "ContactDamage", 10, "Unknown", 0, 2, "", []],
            ["Perfume", "香水", "每次受击攻击力 +3%，本波可叠 30 层", "Epic", 80, "Trigger",
             "Unknown", 0, "Unknown", 0, 3, "OnHurtAttackUp", [0.03, 30]],
            ["InfiniteSign", "无限路牌", "第 5 波结束后进入无尽模式", "Legendary", 140, "Special",
             "Unknown", 0, "Unknown", 0, 1, "Endless", []],
        ],
    ),
    (
        "Enemy",
        ["Id", "Name", "Behaviour", "HP", "Damage", "Speed", "AttackRange",
         "AttackInterval", "CoinDrop", "BodyRadius"],
        ["string", "string", "enum:EnemyBehaviourType", "float", "float", "float", "float",
         "float", "int", "float"],
        ["唯一 Id", "显示名", "行为", "生命", "伤害", "移速", "攻击距离",
         "攻击间隔", "掉落金币", "碰撞半径"],
        [
            ["Crawler", "爬虫", "Chase", 12, 6, 3.0, 0.6, 1.0, 1, 0.35],
            ["Charger", "冲锋兵", "Charge", 18, 10, 2.5, 4.0, 3.0, 2, 0.40],
            ["Thrower", "投手", "Ranged", 15, 8, 2.0, 5.0, 2.5, 2, 0.35],
        ],
    ),
    (
        "Wave",
        ["Id", "Index", "Duration", "SpawnInterval", "SpawnCountMin", "SpawnCountMax",
         "EnemyPool", "ConcurrentCap", "CoinReward", "BossId"],
        ["string", "int", "float", "float", "int", "int",
         "list:id:Enemy", "int", "int", "id:Enemy"],
        ["唯一 Id", "波次序号", "时长 秒", "刷怪间隔", "单次最少", "单次最多",
         "怪种池", "同屏上限", "波末金币", "BOSS Id"],
        [
            ["W1", 1, 180, 1.2, 1, 2, ["Crawler"], 40, 60, ""],
            ["W2", 2, 180, 1.0, 2, 3, ["Crawler", "Thrower"], 70, 80, ""],
            ["W3", 3, 180, 0.9, 2, 3, ["Crawler", "Charger", "Thrower"], 100, 110, ""],
        ],
    ),
    (
        "Buff",
        ["Id", "Name", "Effect", "Duration", "MaxStacks", "Interval", "Tags", "Params"],
        ["string", "string", "enum:BuffEffectType", "float", "int", "float", "string", "list:float"],
        ["唯一 Id", "显示名", "效果类型", "持续 秒", "最大层数", "触发间隔", "标签", "参数"],
        [],
    ),
    (
        "Balance",
        ["Id", "Value", "Note"],
        ["string", "float", "string"],
        ["常量 Id", "数值", "说明"],
        [
            ["CoinRewardBase", 30, "每波末基础金币"],
            ["PriceGrowthPerWave", 0.1, "价格每波上浮比例"],
            ["EnemyHpScalePerWave", 1.9, "敌人血量每波倍率"],
            ["EnemyDamageScalePerWave", 1.35, "敌人伤害每波倍率"],
            ["ArenaHalfExtent", 67, "玩家可达区域的半边长 米"],
            ["DashSpeedMultiplier", 3.0, "冲刺速度是移速的倍数"],
            ["DashDuration", 0.15, "冲刺持续 秒"],
            ["DashCooldown", 2.0, "冲刺冷却 秒"],
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
