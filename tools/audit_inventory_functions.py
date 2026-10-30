#!/usr/bin/env python3
"""Audit original IMG item routes against NativeInventoryActions (no saves modified)."""
import argparse
import collections
import json
import sys
from pathlib import Path


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", type=Path, default=Path(__file__).resolve().parents[2] / "MapleDashResources/Data")
    ap.add_argument("--wzpy", type=Path, default=Path.home() / "Documents/mxd/Beidou-Server/tool/wz-python")
    ap.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "docs/inventory-function-audit.json")
    args = ap.parse_args()
    sys.path.insert(0, str(args.wzpy))
    from wzpy import WzImage, WzKey
    def read(path):
        img = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region("GMS"), name=path.name)
        img.parse()
        if img.truncated or img.parse_warnings:
            raise ValueError(str(path) + ": incomplete IMG")
        return img
    def value(node, key, default=0):
        child = node.get(key) if node is not None else None
        return getattr(child, "value", default)
    names = {}
    for kind in ("Consume", "Ins", "Etc", "Cash"):
        path = args.data / ("String/" + kind + ".img")
        if path.exists():
            for n in read(path).root.children():
                if n.name.isdigit(): names[int(n.name)] = value(n, "name", "")
    fields = {"pad", "pdd", "mad", "mdd", "acc", "eva", "speed", "jump", "expBuff", "barrier"}
    restoration = {"hp", "mp", "hpR", "mpR"}
    rows = []
    for folder in ("Consume", "Install", "Etc", "Cash"):
        for path in sorted((args.data / "Item" / folder).glob("*.img")):
            for n in read(path).root.children():
                if not n.name.isdigit(): continue
                item = int(n.name)
                spec = n.get("spec")
                keys = {c.name for c in spec.children()} if spec is not None else set()
                script = value(spec, "script", "")
                status, route = "unsupported", "special-consumable"
                if folder == "Consume":
                    if 2050001 <= item <= 2050004: status, route = "implemented", "cure"
                    elif value(spec, "isNpc") == 1: status, route = "npc-route", "npc-menu"
                    elif script:
                        exists = (args.data / "scripts/item" / (script + ".js")).exists()
                        status, route = ("script-route-needs-runtime-check" if exists else "missing-script"), "item-script"
                    elif value(spec, "time") == 0 and keys <= restoration and any(value(spec, k) for k in restoration): status, route = "implemented", "restorative"
                    elif value(spec, "time") > 0 and keys <= fields | restoration | {"time"} and (any(value(spec, k) for k in fields) or item in (2022001, 2022186, 2022040)): status, route = "implemented", "stat-potion"
                    elif item // 10000 == 204: route = "equipment-scroll"
                    elif item // 10000 == 203: route = "return-scroll"
                    elif item // 10000 == 238: route = "monster-card"
                    elif item // 10000 in (206, 207, 233): status, route = "not-double-click", "ammunition"
                    elif value(spec, "isHasItem") == 1: route = "timed-item-grant"
                elif item // 10000 == 301: status, route = "implemented", "chair"
                elif folder == "Cash": route = "cash-item"
                else: status, route = "not-double-click", folder.lower()
                rows.append(dict(id=item, name=names.get(item, ""), category=folder, status=status, route=route, script=script, spec=sorted(keys)))
    counts = collections.Counter((r["status"] + ":" + r["route"]) for r in rows)
    report = dict(boundary="Static route audit only; scripts need real host/runtime validation. Unsupported rows retain the item.", counts=dict(sorted(counts.items())), items=rows)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(report["counts"], ensure_ascii=False, indent=2))

if __name__ == "__main__": main()
