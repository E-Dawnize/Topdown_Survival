#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""为 Assets 下缺失的 .meta 生成稳定 guid。

Unity 自己生成 meta 会用随机 guid；如果协作者各自生成，同一份资产在不同机器上
guid 不同，场景和 Prefab 的引用就会断。所以凡是进 git 的 Assets 文件都必须有 meta。

guid 由相对路径的 md5 得到：同一条路径在任何机器上都得到同一个 guid。
只补齐缺失的，已存在的一律不动。
"""

import hashlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "Assets"

# 不需要 meta 的路径片段
SKIP_DIRS = {"Library", "Temp", "Logs", "obj", "Build", "Builds"}


def guid_for(rel_path: str) -> str:
    return hashlib.md5(rel_path.replace("\\", "/").encode("utf-8")).hexdigest()


def meta_body(kind: str, guid: str) -> str:
    if kind == "folder":
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    if kind == "script":
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "MonoImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  defaultReferences: []\n"
            "  executionOrder: 0\n"
            "  icon: {instanceID: 0}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    if kind == "asmdef":
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "AssemblyDefinitionImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    if kind == "text":
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "TextScriptImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def classify(path: Path) -> str:
    if path.is_dir():
        return "folder"
    suffix = path.suffix.lower()
    if suffix == ".cs":
        return "script"
    if suffix == ".asmdef":
        return "asmdef"
    if suffix in (".json", ".txt", ".csv", ".bytes", ".xml"):
        return "text"
    return "default"


def main():
    created = []
    for path in sorted(ASSETS.rglob("*")):
        if any(part in SKIP_DIRS for part in path.relative_to(ASSETS).parts):
            continue
        if path.name.endswith(".meta"):
            continue
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            continue
        rel = path.relative_to(ROOT).as_posix()
        meta.write_text(meta_body(classify(path), guid_for(rel)), encoding="utf-8")
        created.append(rel)

    print(f"补齐 {len(created)} 个 meta")
    for rel in created:
        print("  " + rel)
    return 0


if __name__ == "__main__":
    sys.exit(main())
