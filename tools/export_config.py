#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Excel 配置表 -> JSON 导出器。

只依赖 Python 标准库：xlsx 就是一个 zip 包，里面是几份 XML。
用法:
    python tools/export_config.py
    python tools/export_config.py --xlsx Config/GameConfig.xlsx --out Assets/StreamingAssets/Config
退出码 0 表示成功，1 表示校验或解析失败。
"""

import argparse
import datetime
import hashlib
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {
    "m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
    "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
}

HEADER_ROW = 1
TYPE_ROW = 2
DESC_ROW = 3
FIRST_DATA_ROW = 4

# 项目根目录：脚本在 tools/ 下，避免依赖运行时的当前工作目录
ROOT = Path(__file__).resolve().parent.parent

# 需要在 C# 端校验的枚举，从源码扫描出来
ENUM_SOURCE = ROOT / "Assets/Scripts/Game/Runtime/Config/Defs/Enums.cs"

TYPE_RE = re.compile(r"^(int|float|bool|string|vec2|color|enum:([A-Za-z_]\w*)|id:([A-Za-z_]\w*)|list:(.+))$")


class ExportError(Exception):
    pass


def col_index(ref):
    """'AB12' -> 28 (1-based)"""
    n = 0
    for ch in ref:
        if ch.isalpha():
            n = n * 26 + (ord(ch.upper()) - 64)
        else:
            break
    return n


def col_name(idx):
    """1 -> 'A'"""
    s = ""
    while idx > 0:
        idx, rem = divmod(idx - 1, 26)
        s = chr(65 + rem) + s
    return s


def strip_ns(tag):
    return tag.split("}", 1)[-1]


def read_shared_strings(z):
    if "xl/sharedStrings.xml" not in z.namelist():
        return []
    root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    out = []
    for si in root:
        if strip_ns(si.tag) != "si":
            continue
        parts = [t.text or "" for t in si.iter() if strip_ns(t.tag) == "t"]
        out.append("".join(parts))
    return out


def read_workbook(z):
    """返回 [(sheet 名, zip 内路径)]"""
    root = ET.fromstring(z.read("xl/workbook.xml"))
    rels_root = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
    rel_map = {rel.get("Id"): rel.get("Target") for rel in rels_root}
    sheets = []
    for node in root.iter():
        if strip_ns(node.tag) != "sheet":
            continue
        rid = node.get("{%s}id" % NS["r"])
        target = rel_map.get(rid, "")
        if target.startswith("/"):
            path = target.lstrip("/")
        else:
            path = "xl/" + target.lstrip("./")
        sheets.append((node.get("name"), path))
    return sheets


def read_sheet(z, path, shared):
    """返回 {行号: {列号: 文本}}"""
    root = ET.fromstring(z.read(path))
    rows = {}
    sheet_data = None
    for node in root:
        if strip_ns(node.tag) == "sheetData":
            sheet_data = node
            break
    if sheet_data is None:
        return rows
    for row in sheet_data:
        if strip_ns(row.tag) != "row":
            continue
        r = int(row.get("r", "0"))
        cells = {}
        for c in row:
            if strip_ns(c.tag) != "c":
                continue
            ref = c.get("r", "")
            if not ref:
                continue
            idx = col_index(ref)
            t = c.get("t")
            v = None
            inline = None
            for child in c:
                tag = strip_ns(child.tag)
                if tag == "v":
                    v = child.text
                elif tag == "is":
                    inline = "".join(x.text or "" for x in child.iter() if strip_ns(x.tag) == "t")
            if t == "inlineStr":
                val = inline or ""
            elif t == "s":
                try:
                    val = shared[int(v)] if v is not None else ""
                except (ValueError, IndexError):
                    val = ""
            elif t == "b":
                val = "true" if (v or "").strip() == "1" else "false"
            elif t in ("str", "e"):
                val = v or ""
            else:
                val = v if v is not None else ""
            cells[idx] = val.strip()
        rows[r] = cells
    return rows


def load_enums():
    """从 C# 源码里扫出枚举成员，用于校验 enum: 类型。"""
    enums = {}
    if not ENUM_SOURCE.exists():
        return enums
    text = ENUM_SOURCE.read_text(encoding="utf-8")
    for m in re.finditer(r"public\s+enum\s+(\w+)\s*\{([^}]*)\}", text):
        name, body = m.group(1), m.group(2)
        members = set()
        for line in body.split(","):
            tok = line.strip().split("=")[0].strip()
            if tok and re.match(r"^[A-Za-z_]\w*$", tok):
                members.add(tok)
        enums[name] = members
    return enums


def parse_types(raw_types, sheet_name):
    types = {}
    for col, typ in raw_types.items():
        typ = (typ or "").strip()
        if not typ:
            continue
        if not TYPE_RE.match(typ):
            raise ExportError(f"[{sheet_name}] 第 {TYPE_ROW} 行 {col_name(col)} 列的类型 '{typ}' 不认识")
        types[col] = typ
    return types


def convert_value(raw, typ, enums, sheet, row_no, field):
    raw = (raw or "").strip()
    if typ == "int":
        if raw == "":
            return 0
        try:
            return int(float(raw))
        except ValueError:
            raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: '{raw}' 不是整数")
    if typ == "float":
        if raw == "":
            return 0.0
        try:
            return float(raw)
        except ValueError:
            raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: '{raw}' 不是数字")
    if typ == "bool":
        if raw in ("", "false", "0", "FALSE", "F", "否"):
            return False
        if raw in ("true", "1", "TRUE", "T", "是"):
            return True
        raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: '{raw}' 不是布尔值")
    if typ == "string":
        return raw
    if typ == "vec2":
        if raw == "":
            return [0.0, 0.0]
        parts = [p.strip() for p in raw.split(",")]
        if len(parts) != 2:
            raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: vec2 需要 'x,y' 两段")
        try:
            return [float(parts[0]), float(parts[1])]
        except ValueError:
            raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: vec2 含非数字")
    if typ == "color":
        if raw == "":
            return "#FFFFFF"
        if not re.match(r"^#[0-9A-Fa-f]{6}$", raw):
            raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: 颜色要写成 #RRGGBB")
        return raw.upper()
    if typ.startswith("enum:"):
        name = typ.split(":", 1)[1]
        if raw == "":
            raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: 枚举不能为空")
        members = enums.get(name)
        if members is None:
            raise ExportError(f"[{sheet}] {field}: 找不到枚举 {name}（检查 Enums.cs）")
        if raw not in members:
            raise ExportError(
                f"[{sheet}] 第 {row_no} 行 {field}: '{raw}' 不在枚举 {name} 里，可选: {', '.join(sorted(members))}"
            )
        return raw
    if typ.startswith("id:"):
        if raw == "":
            return ""
        return raw
    if typ.startswith("list:"):
        inner = typ[5:]
        if raw == "":
            return []
        items = [p.strip() for p in raw.split("|") if p.strip()]
        return [convert_value(x, inner, enums, sheet, row_no, field) for x in items]
    raise ExportError(f"[{sheet}] 第 {row_no} 行 {field}: 未处理的类型 {typ}")


def build_table(sheet_name, rows, enums):
    header = rows.get(HEADER_ROW, {})
    if not header:
        raise ExportError(f"[{sheet_name}] 第 {HEADER_ROW} 行是空的")

    max_col = max(header)
    fields = []
    seen = set()
    for c in range(1, max_col + 1):
        name = (header.get(c) or "").strip()
        if not name:
            continue
        if not re.match(r"^[A-Za-z_]\w*$", name):
            raise ExportError(f"[{sheet_name}] {col_name(c)}1 的字段名 '{name}' 不是合法标识符")
        if name in seen:
            raise ExportError(f"[{sheet_name}] 字段名 '{name}' 重复")
        seen.add(name)
        fields.append((c, name))

    types_raw = rows.get(TYPE_ROW, {})
    types = parse_types({c: types_raw.get(c, "") for c, _ in fields}, sheet_name)
    for c, name in fields:
        if c not in types:
            raise ExportError(f"[{sheet_name}] 字段 '{name}' 缺少类型（第 {TYPE_ROW} 行）")

    data_rows = sorted(r for r in rows if r >= FIRST_DATA_ROW)
    records = []
    id_seen = {}
    for r in data_rows:
        cells = rows[r]
        if not any((cells.get(c) or "").strip() for c, _ in fields):
            continue  # 整行空，跳过
        rec = {}
        for c, name in fields:
            rec[name] = convert_value(cells.get(c, ""), types[c], enums, sheet_name, r, name)
        rid = str(rec.get("Id", "")).strip()
        if not rid:
            raise ExportError(f"[{sheet_name}] 第 {r} 行 Id 为空")
        if rid in id_seen:
            raise ExportError(f"[{sheet_name}] Id '{rid}' 与第 {id_seen[rid]} 行重复")
        id_seen[rid] = r
        records.append(rec)
    return fields, types, records


def validate_refs(tables, types_by_table):
    errors = []
    id_sets = {name: {str(r["Id"]) for r in recs} for name, (_, _, recs) in tables.items()}
    for sheet, (fields, types, recs) in tables.items():
        for rec in recs:
            for c, field in fields:
                typ = types.get(c, "")
                if typ.startswith("id:"):
                    target = typ.split(":", 1)[1]
                    val = rec.get(field, "")
                    if val and val not in id_sets.get(target, set()):
                        errors.append(f"[{sheet}] {rec['Id']}.{field}: 引用的 {target} '{val}' 不存在")
                elif typ.startswith("list:id:"):
                    target = typ[len("list:id:"):]
                    for val in rec.get(field, []):
                        if val and val not in id_sets.get(target, set()):
                            errors.append(f"[{sheet}] {rec['Id']}.{field}: 引用的 {target} '{val}' 不存在")
    return errors


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--xlsx", default=str(ROOT / "Config" / "GameConfig.xlsx"))
    ap.add_argument("--out", default=str(ROOT / "Assets" / "StreamingAssets" / "Config"))
    args = ap.parse_args(argv)

    xlsx = Path(args.xlsx)
    out_dir = Path(args.out)
    if not xlsx.exists():
        print(f"找不到配置表: {xlsx}", file=sys.stderr)
        return 1

    try:
        with zipfile.ZipFile(xlsx) as z:
            bad = z.testzip()
            if bad:
                raise ExportError(f"xlsx 内部损坏: {bad}")
            shared = read_shared_strings(z)
            sheets = read_workbook(z)
            if not sheets:
                raise ExportError("workbook 里没有任何 sheet")
            enums = load_enums()

            tables = {}
            types_by_table = {}
            for name, path in sheets:
                rows = read_sheet(z, path, shared)
                try:
                    fields, types, recs = build_table(name, rows, enums)
                except ExportError as e:
                    raise ExportError(str(e))
                tables[name] = (fields, types, recs)
                types_by_table[name] = types
    except zipfile.BadZipFile as e:
        print(f"不是有效的 xlsx: {e}", file=sys.stderr)
        return 1
    except ExportError as e:
        print(f"导表失败: {e}", file=sys.stderr)
        return 1

    errors = validate_refs(tables, types_by_table)
    if errors:
        print("导表失败，引用校验不通过:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1

    out_dir.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha1()
    written = []
    for name, (_, _, recs) in tables.items():
        text = json.dumps(recs, ensure_ascii=False, indent=2)
        digest.update(text.encode("utf-8"))
        (out_dir / f"{name}.json").write_text(text + "\n", encoding="utf-8")
        written.append(f"{name}.json ({len(recs)} 行)")

    manifest = {
        "version": digest.hexdigest()[:12],
        "exportedAt": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "tables": list(tables.keys()),
    }
    (out_dir / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"导表完成 -> {out_dir}  版本 {manifest['version']}")
    for line in written:
        print("  " + line)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
