#!/usr/bin/env python3
"""Export the 5th/6th job advancement quest nodes exactly as the 282 client stores them.

Scope: 1460..1465, 1478, 1481, 102407/102408 (jobLevel evidence) and 38601..38625.
For every quest the raw `QuestInfo` / `Check` / `Act` / `Say` subtrees are kept verbatim
(no interpretation), plus the related consumables (mysterious stones / Sol Erda) and the
maps the chain anchors on. Missing images are recorded as `missingImage`, never dropped.

Usage:
  python3 tools/ms282/export_advancement_quest_detail.py \
      --out ../docs/tms282-skill/data/advancement-quest-detail.json
"""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402

DEFAULT_DATA = Path.home() / "Documents/mxd/MapleStory/Data"

QUESTS = (list(range(1460, 1466)) + [1478, 1481, 102407, 102408]
          + list(range(38601, 38626)))
STONE_5TH = list(range(2435734, 2435737))
STONE_6TH = list(range(2636387, 2636394))
MAPS = [270010111, 100000201, 400000001, 105300000,
        450000000, 450000010, 450000020, 450000100, 450000110, 450000120,
        993217480, 993217481, 993217482, 993217483, 993217484, 993217485,
        993217491, 993217495, 993217701, 993217703, 993217706]
NPCS = [2140000, 2140001, 1540941, 1540942, 1540943, 1540944, 3004300,
        3006370, 3006371, 3006373, 3006374, 3006375, 3006376, 3006377,
        3006378, 3006384, 3006388, 9010000]


def tree(node):
    if node is None:
        return None
    children = node.children()
    if not children:
        value = getattr(node, "value", None)
        if isinstance(value, tuple):
            return [int(value[0]), int(value[1])]
        return value
    return {c.name: tree(c) for c in children}


def load(data: Path, rel: str, only=None):
    path = data / rel
    if not path.exists():
        return None
    img = WzImage.from_file(str(path), key=WzKey.for_region("BMS"))
    node = img.parse_partial(only=frozenset(only)) if only else img.parse()
    if img.truncated or img.parse_warnings:
        raise ValueError(f"incomplete {rel}: {img.parse_warnings}")
    return node


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source", type=Path, default=DEFAULT_DATA)
    ap.add_argument("--out", default="")
    args = ap.parse_args()
    data = args.source

    quests = {}
    for qid in QUESTS:
        rel = f"Quest/QuestData/{qid}.img"
        root = load(data, rel, ["QuestInfo", "Check", "Act", "Say"])
        if root is None:
            quests[str(qid)] = {"missingImage": True}
            continue
        rec = {k: tree(root.get(k)) for k in ("QuestInfo", "Check", "Act", "Say")}
        info = root.get("QuestInfo") or {}
        for phase in ("0", "1"):
            check = (root.get("Check") or {}).get(phase)
            if check is not None:
                for key in ("startscript", "endscript"):
                    n = check.get(key)
                    if n is not None and n.value:
                        rec.setdefault("scripts", []).append(n.value)
        quests[str(qid)] = rec

    items = {}
    for base, ids in (("Item/Consume/0243.img", STONE_5TH), ("Item/Consume/0263.img", STONE_6TH)):
        root = load(data, base, [f"{i:08}" for i in ids])
        for i in ids:
            n = root.get(f"{i:08}") if root is not None else None
            items[str(i)] = tree(n) if n is not None else {"missing": True}

    maps = {}
    for mid in MAPS:
        rel = f"Map/Map/Map{mid // 100000000}/{mid}.img"
        root = load(data, rel, ["info", "life", "portal"])
        if root is None:
            maps[str(mid)] = {"missingImage": True}
            continue
        maps[str(mid)] = {"info": tree(root.get("info")),
                          "life": tree(root.get("life")),
                          "portal": tree(root.get("portal"))}

    graph = load(data, "Map/Map/Graph.img")
    graph_out = {}
    for group in graph.children():
        for n in group.children():
            if n.name in {str(m) for m in MAPS}:
                graph_out[n.name] = tree(n)

    npcs = {}
    for nid in NPCS:
        root = load(data, f"Npc/{nid:07}.img", ["info"])
        npcs[str(nid)] = tree(root.get("info")) if root is not None else {"missingImage": True}

    report = {"source": str(data), "quests": quests, "items": items, "maps": maps,
              "graph": graph_out, "npcInfo": npcs,
              "boundary": "Raw client nodes only; dialog, teleport and scene order live in "
                          "quest/npc/field scripts that this distribution does not ship."}
    text = json.dumps(report, ensure_ascii=False, indent=1)
    if args.out:
        Path(args.out).write_text(text + "\n", encoding="utf-8")
        missing = [k for k, v in quests.items() if v.get("missingImage")]
        print(f"quests={len(quests)} missing={missing} maps={len(maps)} items={len(items)}")
        print(f"{len(text.splitlines())} lines -> {args.out}")
    else:
        print(text)


if __name__ == "__main__":
    main()
