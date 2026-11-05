#!/usr/bin/env python3
"""Export soul item id -> soul option code for TMS 282 souls.

The 282 server resolves a soul's option from its (English) item name, not from the id
(`ItemConstants.getSoulOptionFromSoul`): the name prefix picks the stat kind, the boss in
the name picks the tier, and the pair maps to a soulPotList code. 282 client names are
Chinese, so the server reads the *GMS 265* name by id (`data/wz265/String.wz/Consume.img.xml`).

This script replicates that function byte for byte and writes the resulting table, so the
Unity project never has to guess an option code at runtime.

Usage:
  python3 export_soul_options.py            # writes Assets/Resources/Enhancement/SoulOptions.json
  python3 export_soul_options.py --stats     # only print coverage
"""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
SERVER = ROOT.parent.parent / "TMS282.5"            # TMS282.5/

NAMES_XML = SERVER / "data/wz265/String.wz/Consume.img.xml"
OUT = ROOT / "Assets/Resources/Enhancement/SoulOptions.json"

# getSoulOptionFromSoul 的 9 个 Boss 档位关键词（顺序即判定顺序，不可重排）。
TIERS = [
    ["Spirit of Rock", "Prison Guard Ani", "Xerxes", "Black Slime"],
    ["Dragon Rider", "Rex"],
    ["Mu Gong", "Pianus"],
    ["Balrog", "Tutu", "Nene"],
    ["Zakum", "Gold Dragon", "Red Tiger"],
    ["Von Leon", "Hilla"],
    ["Arkarium", "Mad Mage", "Rampant Cyborg", "Vicious Hunter", "Bad Brawler"],
    ["Pink Bean", "Black Knight", "Pierre", "Von Bon"],
    ["Cygnus", "Magnus", "Murgoth", "Crimson Queen", "Vellum", "Lotus", "Damien", "Lucid"],
]

# 四维：档位 -> 四个基础代码（STR/DEX/INT/LUK 依次 +0..+3）
STAT_CODES = [101, 105, 109, 113, 163, 117, 185, 121, 167]


def load_names():
    text = NAMES_XML.read_text(encoding="utf-8", errors="ignore")
    names = {}
    # <imgdir name="2591010"> ... <string name="name" value="Beefy Balrog Soul"/>
    for block in re.finditer(r'<imgdir name="(2591\d{3})">(.*?)</imgdir>', text, re.S):
        item_id, body = block.group(1), block.group(2)
        found = re.search(r'<string name="name" value="([^"]*)"', body)
        if found:
            names[int(item_id)] = found.group(1)
    return names


def boss_tier(name):
    for index, keywords in enumerate(TIERS, start=1):
        for keyword in keywords:
            if keyword in name:
                return index
    return 0


def option_for(name):
    """ItemConstants.getSoulOptionFromSoul, 1:1."""
    tier = boss_tier(name)
    if name.startswith(("Beefy", "Swift", "Clever", "Fortuitous")):
        if not tier:
            return 0
        base = STAT_CODES[tier - 1]
        return base + {"Beefy": 0, "Swift": 1, "Clever": 2, "Fortuitous": 3}[name.split()[0]]
    if name.startswith(("Hearty", "Ample")):
        # HP 表并不完全等差：tier4/5/6/7/8/9 只给 HP（MP 那几档为 0）
        hearty = {1: 131, 2: 133, 3: 135, 4: 137, 5: 175, 6: 138, 7: 189, 8: 139, 9: 177}
        ample = {1: 132, 2: 134, 3: 136}
        return (hearty if name.startswith("Hearty") else ample).get(tier, 0)
    if name.startswith("Flashy"):
        return {1: 201, 2: 202, 3: 203, 4: 204, 5: 210, 6: 205, 8: 40081, 9: 70057}.get(tier, 0)
    if name.startswith(("Potent", "Radiant")):
        table = {5: (2011, 2012), 6: (153, 154), 7: (153, 154), 8: (155, 156), 9: (171, 172)}
        pair = table.get(tier)
        if not pair:
            return 0
        return pair[0] if name.startswith("Potent") else pair[1]
    if name.startswith("Sharp"):
        return 42292
    if name.startswith("Destructive"):
        return 60009
    return 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--stats", action="store_true")
    args = parser.parse_args()

    names = load_names()
    table, unresolved = {}, []
    for item_id, name in sorted(names.items()):
        code = option_for(name)
        if code:
            table[str(item_id)] = code
        else:
            unresolved.append((item_id, name))

    print(f"265 名字表 {len(names)} 条；解析出属性代码 {len(table)} 条；未解析 {len(unresolved)} 条")
    for item_id, name in unresolved[:10]:
        print("  未解析:", item_id, name)
    if args.stats:
        return

    OUT.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "_source": "TMS282.5 ItemConstants.getSoulOptionFromSoul over wz265/String.wz/Consume.img.xml",
        "_note": "soulItemId -> soul option code; ids absent here have no derivable option and must be rejected by the service",
        "options": table,
    }
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print("已写入", OUT, OUT.stat().st_size, "字节")


if __name__ == "__main__":
    main()
