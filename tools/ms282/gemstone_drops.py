"""282 gemstone drops: static table plus Mob.initCustomizedDrops (not just JSON).

Two rolls on 8643012 are intentional: static chance 50 and regional chance 5.
Source uses /10000; project uses /999999, rounded to nearest integer.
"""
import json
import re
from pathlib import Path

SERVER = Path.home() / "Documents/mxd/TMS282.5"


def source_rows():
    java = SERVER / "source/src/main/java/net/swordie/ms"
    constants = (java / "constants/GameConstants.java").read_text()
    mob = (java / "life/mob/Mob.java").read_text()
    assert "new DropInfo(2435902, 5, 1, 1)" in mob
    assert re.search(r"MAX_DROP_CHANCE\s*=\s*10000", constants)
    rows = []
    for name in ("isVanishingJourney", "isChuChuIsLand", "isLachelein",
                 "isArcana", "isMorass", "isEsfera"):
        block = re.search(r"boolean " + name + r"\(int mobID\).*?case (.*?) ->", constants, re.S)
        assert block and "GameConstants." + name + "(getTemplateId())" in mob
        rows.extend((int(i), 2435902, 1, 1, 0, 500)
                    for i in re.findall(r"\d+", block[1]))
    root = Path(__file__).resolve().parents[2]
    matrix = json.loads((root / "Assets/Resources/UI/VMatrix.json").read_text())
    ids = {i for g in matrix["gemstones"] for i in g["itemIds"]}
    data = json.loads((SERVER / "data/resources/mob_drops.json").read_text())
    for row in data:
        if row["itemId"] in ids:
            assert not row["reactorDrop"]
            rows.append((row["mobId"], row["itemId"], row["minQuant"],
                         row["maxQuant"], 0, (row["chance"] * 999999 + 5000) // 10000))
    return ids, sorted(rows)


def replace_legacy(rows, globals_):
    ids, source = source_rows()
    removed = sum(r[1] in ids for r in rows)
    print(f"282 gemstones: remove {removed} legacy rows; import {len(source)} source rolls")
    return sorted([r for r in rows if r[1] not in ids] + source), [r for r in globals_ if r[1] not in ids]
