#!/usr/bin/env python3
"""Audit 282 potential-scroll candidates without inventing rules for unmapped items.

This is evidence only, not a runtime allowlist. It preserves missing success fields
and the client text separately so a service default cannot silently override text.
"""
import argparse
import hashlib
import json
from pathlib import Path
from import_enhancement_ui import Importer, ROOT
from wzpy.properties import WzCanvasProperty


def tree(node):
    if isinstance(node, WzCanvasProperty):
        return None
    children = [child for child in node.children() if not isinstance(child, WzCanvasProperty)]
    if children:
        return {child.name: tree(child) for child in children}
    value = node.value
    if value is not None and not isinstance(value, (str, int, float, bool)):
        raise ValueError("Unexpected evidence value: " + node.name)
    return value


def export(client, server):
    importer = Importer(client)
    strings = importer.image("String/Consume.img").root
    table = server / "data/resources/tms282/enhance/enhance-items.tsv"
    mapped = {}
    for line in table.read_text(encoding="utf-8-sig").splitlines():
        parts = line.split("\t")
        if len(parts) == 4 and parts[0].isdigit() and parts[1] == "BONUS_POTENTIAL":
            mapped[int(parts[0])] = dict(pair.split("=", 1) for pair in parts[2].split(";") if pair)
    records = []
    for rel in ("Item/Consume/0204.img", "Item/Consume/0264.img"):
        for item in importer.image(rel).root.children():
            if not item.name.isdigit():
                continue
            item_id = int(item.name)
            if not (2048300 <= item_id < 2048400 or 2049400 <= item_id < 2050000
                    or 2644100 <= item_id < 2644200):
                continue
            name = strings.get(str(item_id))
            records.append({"itemId": item_id, "source": rel,
                "properties": {child.name: tree(child) for child in item.children()},
                "strings": tree(name) if name else None,
                "serverBonusMapping": mapped.get(item_id)})
    hashes = dict(importer.hashes)
    for rel in ("data/resources/tms282/enhance/enhance-items.tsv",
                "source/src/main/java/net/swordie/ms/handlers/item/ItemUpgradeHandler.java",
                "source/src/main/java/net/swordie/ms/client/character/items/Tms282EnhanceItems.java"):
        hashes["server/" + rel] = hashlib.sha256((server / rel).read_bytes()).hexdigest()
    return {"schemaVersion": 1, "evidenceOnly": True, "sourceSha256": hashes,
            "records": sorted(records, key=lambda row: row["itemId"])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client", type=Path, default=ROOT.parent.parent / "MapleStory/Data")
    parser.add_argument("--server", type=Path, default=ROOT.parent.parent / "TMS282.5")
    parser.add_argument("--out", type=Path, default=ROOT.parent / "docs/tms282-skill/data/potential-scroll-audit.json")
    args = parser.parse_args()
    result = export(args.client, args.server)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print("Audited", len(result["records"]), "candidate records; no runtime rules changed")


if __name__ == "__main__":
    main()
