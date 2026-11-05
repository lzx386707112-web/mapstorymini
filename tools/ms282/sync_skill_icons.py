#!/usr/bin/env python3
"""给 Skill img 里的技能补 `skillicon/<id>` 别名（NativeAssetIndex.aliases）。

运行时图标走别名查表：`skillicon/1120003` → `Skill/112.img/skill/1120003/icon`，
节点本身是 1x1 桩 + `_outlink` 到 `Skill/_Canvas/<img>.img`，由 WzAssets.Canvas 跟随。

别名表原来由 tools/package_native_assets.py 在整包时生成（依赖已下线的
`Library/LegacyArtBeforeNativeIMG/skillicon/*.bytes`），本次迁移新增的 V/HEXA 技能
没有对应 .bytes，所以单独补：

  * 来源 = `Assets/Resources/UI/VMatrixSkills.json`（V/HEXA 核心技能）
  * 已存在的别名一律不动（避免覆盖 package 工具写入的 crop 等属性）
  * 只补 icon 节点确实存在的技能

用法：
    python3 tools/ms282/sync_skill_icons.py --dry-run
    python3 tools/ms282/sync_skill_icons.py
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]           # MapleDash/
REPO = ROOT.parent
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

GMS = WzKey.for_region("GMS")
DATA = REPO / "MapleDashResources/Data"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
SKILLS = ROOT / "Assets/Resources/UI/VMatrixSkills.json"


def has_icon(skill_id: int) -> bool:
    path = DATA / "Skill" / f"{skill_id // 10000}.img"
    if not path.is_file():
        return False
    image = WzImage.from_bytes(path.read_bytes(), key=GMS, name=path.name)
    image.parse()
    node = image.get(f"skill/{skill_id}")
    return node is not None and isinstance(node.get("icon"), WzCanvasProperty)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    index = json.loads(INDEX.read_text(encoding="utf-8"))
    aliases = index["aliases"]
    skills = json.loads(SKILLS.read_text(encoding="utf-8"))["skills"]

    added, present, missing = [], 0, []
    for key in sorted(skills, key=int):
        alias = f"skillicon/{key}"
        if alias in aliases:
            present += 1
            continue
        if not has_icon(int(key)):
            missing.append(key)
            continue
        aliases[alias] = {"source": f"Skill/{int(key) // 10000}.img/skill/{key}/icon"}
        added.append(alias)

    print(f"V/HEXA 技能 {len(skills)}：已有别名 {present}，新增 {len(added)}，无 icon {len(missing)}")
    if missing[:5]:
        print(f"  无 icon 样本: {missing[:5]}")
    if args.dry_run or not added:
        return
    index["aliases"] = dict(sorted(aliases.items()))
    INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"已更新 {INDEX.relative_to(ROOT)}（aliases {len(aliases)} 条）")


if __name__ == "__main__":
    main()
