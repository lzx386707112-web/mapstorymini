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
    potion_allowed = fields | restoration | {"time", "poison", "seal", "darkness", "weakness", "curse",
        "exp", "inflation", "morphRandom", "consumeOnPickup", "onlyPickup", "prob", "BFSkill", "party",
        "expinc", "mhpR", "mmpR", "mhpRRate", "mmpRRate", "berserk", "booster", "repeatEffect", "morph",
        "thaw", "padRate", "madRate", "pddRate", "mddRate", "accRate", "evaRate", "speedRate"}
    def potion_route(item, spec, keys):
        if not keys <= potion_allowed: return None
        if value(spec, "morph") > 0: return "morph"
        effect = any(value(spec, k) for k in fields | restoration | {"poison", "seal", "darkness", "weakness", "curse"})
        effect |= item in (2022001, 2022186, 2022040)
        effect |= 2022450 <= item <= 2022452 and value(spec, "expinc") != 0
        effect |= (2022585 <= item <= 2022588 or 2022616 <= item <= 2022617) and any(value(spec, k) for k in ("berserk", "booster"))
        effect |= (2022359 <= item <= 2022421 or item in (2022198, 2022337)) and any(value(spec, k) for k in ("mhpR", "mmpR"))
        return "stat-potion" if effect else None
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
                    if n.get("specEx") is not None: status, route = "implemented-needs-runtime-check", "extended-disease"
                    elif 2050001 <= item <= 2050004: status, route = "implemented", "cure"
                    elif value(spec, "isNpc") == 1: status, route = "npc-route", "npc-menu"
                    elif item == 2430680: status,route="implemented-needs-runtime-check","world-quest-draw"
                    elif script:
                        exists = (args.data / "scripts/item" / (script + ".js")).exists()
                        status, route = ("script-route-needs-runtime-check" if exists else "source-missing-script"), "item-script"
                    elif value(spec, "time") == 0 and keys <= restoration and any(value(spec, k) for k in restoration): status, route = "implemented", "restorative"
                    elif value(spec, "time") > 0 and keys <= fields | restoration | {"time"} and (any(value(spec, k) for k in fields) or item in (2022001, 2022186, 2022040)): status, route = "implemented", "stat-potion"
                    elif item == 2003609: status, route = "implemented", "experience-potion"
                    elif potion_route(item, spec, keys):
                        route = potion_route(item, spec, keys)
                        status = "implemented-controls-partial" if route == "morph" else "implemented-needs-runtime-check"
                    elif item // 10000 == 204: status, route = "implemented-service-ui-partial", "equipment-scroll"
                    elif item // 10000 == 210: status, route = "implemented", "summon-bag"
                    elif item // 10000 in (228,229): status, route = "implemented", "skill-book"
                    elif item // 10000 == 212: status, route = "implemented-needs-runtime-check", "pet-food"
                    elif item // 10000 == 226: status, route = "implemented-needs-runtime-check", "mount-fatigue"
                    elif item // 10000 == 227: status, route = "implemented-needs-runtime-check", "monster-capture"
                    elif n.get("reward") is not None: status, route = "implemented-needs-runtime-check", "reward-box"
                    elif value(spec, "morph") > 0: status, route = "implemented-controls-partial", "morph"
                    elif item // 10000 == 237: status, route = "implemented", "banked-experience"
                    elif item // 10000 == 203: status, route = "implemented", "return-scroll"
                    elif item // 10000 == 238: status, route = "implemented", "monster-card"
                    elif item // 10000 in (206, 207, 233): status, route = "not-double-click", "ammunition"
                    elif value(spec, "isHasItem") == 1: status, route = "implemented", "timed-item-grant"
                elif item // 10000 == 301: status, route = "implemented", "chair"
                elif folder == "Cash":
                    route = "cash-item"
                    if item // 10000 in (515,522,525,538,542,546,549): status,route="npc-route-needs-runtime-check","cash-npc-or-quest-ticket"
                    elif item == 5510000: status,route="implemented-ui-validation-pending","death-wheel"
                    elif item == 5180000: status,route="script-route-needs-runtime-check","pet-water-of-life"
                    elif item // 10000 == 519: status,route="source-disabled","cash-client-no-op"
                    elif item // 10000 == 516: status,route="implemented-needs-runtime-check","face-expression"
                    elif item // 10000 == 510: status,route="implemented-needs-runtime-check","jukebox"
                    elif item // 10000 == 561: status,route="implemented-ui-validation-pending","vega-scroll"
                    elif item // 10000 == 524: status,route="implemented-needs-runtime-check","pet-food"
                    elif item // 1000 in (5211,5360): status,route="implemented-passive","scheduled-rate-coupon"
                    elif item // 10000 == 530: status,route="implemented-controls-partial","morph"
                    elif item // 10000 == 520: status,route="implemented","money-bag"
                    elif item // 10000 == 545: status,route="implemented","remote-npc-shop"
                    elif item in (5060000,5060001,5061000,5061001,5061002,5061003) or item // 10000 in (550,552,557): status,route="implemented-needs-runtime-check","cash-equipment"
                    elif item // 10000 == 529 or item // 10000 == 528 and item != 5281000: status,route="source-disabled","cash-source-disabled"
                else: status, route = "not-double-click", folder.lower()
                rows.append(dict(id=item, name=names.get(item, ""), category=folder, status=status, route=route, script=script, spec=sorted(keys)))
    counts = collections.Counter((r["status"] + ":" + r["route"]) for r in rows)
    report = dict(boundary="Static route audit only; scripts need real host/runtime validation. Unsupported rows retain the item.", counts=dict(sorted(counts.items())), items=rows)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(report["counts"], ensure_ascii=False, indent=2))

if __name__ == "__main__": main()
