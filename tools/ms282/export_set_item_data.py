#!/usr/bin/env python3
"""Export TMS282 client set-item data into the project's runtime JSON.

Source of truth is the 282 **client** IMG tree (AGENTS rule 8: the 282 server XML
is a different build and must not be treated as client behaviour).

Outputs one file: Assets/Resources/Admin/SetItem282.json

Key facts this exporter encodes (see inventory-and-tooltip.md, "EXE 绘制链实证"
and "Effect 的 Option / activeSkill 链"):

* `Etc/SetItemInfo.img` has 877 sets and **two generations of ItemID**:
  - legacy: `ItemID/<n>` is an imgdir with candidate ids plus `typeName` /
    `representName` (the two columns of the 282 set panel).
  - modern: `ItemID/<n>` is a flat int list; there is no explicit slot grouping,
    so this exporter groups by equipment category derived from the id prefix.
    INFERRED: the EXE routine that groups modern ItemID was not located.
* `Effect/<tier>` carries `inc*` flat stats, `incAllStat`, `nbdR`, `Option`,
  `activeSkill` and a handful of rare fields. `Option/{option,level}` resolves
  through `Item/ItemOption.img/<6-digit id>/level/<level>` - it reuses the
  equipment-potential system, it is not a set-private mechanic.
* `activeSkill/{id,level}` grants 8000xxxx skills; labels come from
  `String/Skill.img/<id>`.

All display strings are converted to Simplified Chinese (user decision
2026-11-06); the original text is kept alongside as `*Tw` for audit.
The stat-key mapping below is the single place where 282 fields become the
project's `AdminSetItem.SupportedStats` keys, so it can be audited and rerun.
"""
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from dump_img_tree import tree, DEFAULT_DATA  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402

try:
    from zhconv import convert as _zh
except ImportError:
    raise SystemExit("zhconv is required: pip3 install zhconv")

ROOT = HERE.parents[1]
OUT = ROOT / "Assets/Resources/Admin/SetItem282.json"
DATA = DEFAULT_DATA


def cn(value):
    """Traditional -> Simplified, preserving non-string values."""
    return _zh(value, "zh-cn") if isinstance(value, str) else value


def load(rel):
    img = WzImage.from_file(str(DATA / rel), key=WzKey.for_region("BMS"))
    img.parse()
    if img.truncated or img.parse_warnings:
        raise ValueError(f"incomplete {rel}: {img.parse_warnings}")
    return img


def branch(root, path):
    node = root
    for part in [p for p in path.split("/") if p]:
        node = node.get(part) if node is not None else None
        if node is None:
            raise KeyError(path)
    return tree(node)


# 282 inc* -> project stat keys. Keys absent from SupportedStats are kept in
# `unmapped` so no effect is silently dropped.
# 口径对齐：工程里暴击几率的规范键是 CriticalChance（PotentialStats / NativeBuffs
# 都用这个），不是 CriticalRate；nbdR/ignoreTargetDEF 统一到 IgnoreDefense。
STAT_MAP = {
    "incSTR": "STR", "incDEX": "DEX", "incINT": "INT", "incLUK": "LUK",
    "incMHP": "HP", "incMMP": "MP", "incMHPr": "HPPct", "incMMPr": "MPPct",
    "incPAD": "PAD", "incMAD": "MAD", "incPDD": "PDD", "incMDD": "MDD",
    "incACC": "ACC", "incEVA": "EVA", "incSpeed": "SPD", "incJump": "JMP",
    "incPADr": "PADPct", "incMADr": "MADPct", "incSTRr": "STRPct",
    "incDEXr": "DEXPct", "incINTr": "INTPct", "incLUKr": "LUKPct",
    "incPDDr": "PDDPct", "incMDDr": "MDDPct",
    "nbdR": "IgnoreDefense", "incCr": "CriticalChance", "incCDr": "CriticalDamage",
    "incDAMr": "Damage", "incPVPDamage": "PvpDamage",
}
ALL_STAT_KEYS = ("STR", "DEX", "INT", "LUK")
# ItemOption's generic keys -> project keys. `incDAMr` is ambiguous: with a
# `boss:1` level flag it is BossDamage ("攻擊Boss怪物時傷害"), otherwise it is
# plain Damage ("傷害"). Verified against Item/ItemOption.img level payloads
# (60024 has boss:1; 10070/40070/32070 do not).
OPTION_STAT_MAP = {
    "ignoreTargetDEF": "IgnoreDefense",
    "incPAD": "PAD", "incMAD": "MAD", "incMHP": "HP", "incMMP": "MP",
    "incSTR": "STR", "incDEX": "DEX", "incINT": "INT", "incLUK": "LUK",
    "incACC": "ACC", "incEVA": "EVA", "incSpeed": "SPD", "incJump": "JMP",
    "incSTRr": "STRPct", "incDEXr": "DEXPct", "incINTr": "INTPct", "incLUKr": "LUKPct",
    "incPDDr": "PDDPct", "incMDDr": "MDDPct",
    "incCr": "CriticalChance", "incCDr": "CriticalDamage",
    "incCriticaldamage": "CriticalDamage", "incAsrR": "StatusRes",
}
# Level-node keys that are NOT stats (text placeholders / grade markers / flags).
OPTION_NON_STAT_KEYS = {
    "boss", "fixedGrade", "attackType", "prop", "level", "time",
    "incAllskill", "incAllStat", "optionGrade", "reqLevel",
}

# Equipment category by item id prefix - mirrors AdminSetItem.SlotNameOf.
def slot_of(item_id):
    p = item_id // 10000
    return {
        100: "帽子", 101: "脸饰", 102: "眼饰", 103: "耳环", 104: "上衣",
        105: "套服", 106: "裤子", 107: "鞋子", 108: "手套", 109: "盾牌",
        110: "披风", 111: "戒指", 112: "吊坠", 113: "腰带", 114: "勋章",
        115: "肩章", 180: "宠物装备", 190: "骑宠", 191: "骑宠", 193: "骑宠",
        # 500xxxx 是宠物本体（如 set 40「独角兽宠物组」三只宠物），不是武器。
        500: "宠物",
    }.get(p, "武器" if item_id // 1000000 == 1 else "装备")


def iter_ids(value):
    """Yield every int item id out of either ItemID generation."""
    if isinstance(value, dict):
        for k, v in value.items():
            if k in ("typeName", "representName", "byGender"):
                continue
            yield from iter_ids(v)
    elif isinstance(value, int):
        yield value


def main():
    print("reading Etc/SetItemInfo.img ...", flush=True)
    sets_raw = branch(load("Etc/SetItemInfo.img").root, "")

    # First pass: collect referenced option ids / skill ids / item ids.
    option_refs, skill_ids, item_ids = set(), set(), set()
    for raw in sets_raw.values():
        if not isinstance(raw, dict):
            continue
        for v in (raw.get("ItemID") or {}).values():
            for i in iter_ids(v):
                if i > 0:
                    item_ids.add(i)
        for tier in (raw.get("Effect") or {}).values():
            if not isinstance(tier, dict):
                continue
            for od in (tier.get("Option") or {}).values():
                if isinstance(od, dict) and od.get("option"):
                    option_refs.add(int(od["option"]))
            for sk in (tier.get("activeSkill") or {}).values():
                if isinstance(sk, dict) and sk.get("id"):
                    skill_ids.add(int(sk["id"]))
    print(f"  sets={len(sets_raw)} options={len(option_refs)} "
          f"skills={len(skill_ids)} items={len(item_ids)}", flush=True)

    # Resolve ItemOption templates/levels referenced by the sets.
    print("reading Item/ItemOption.img ...", flush=True)
    item_option_root = load("Item/ItemOption.img").root
    options = {}
    for oid in sorted(option_refs):
        node = item_option_root.get(f"{oid:06d}")
        if node is None:
            print(f"  WARN missing ItemOption {oid:06d}")
            continue
        d = tree(node)
        info = d.get("info") or {}
        template = info.get("string") if isinstance(info, dict) else None
        options[str(oid)] = {
            "text": cn(template or ""),
            "textTw": template or "",
            "levels": d.get("level") or {},
        }

    # Resolve labels of granted skills.
    print("reading String/Skill.img ...", flush=True)
    skill_root = load("String/Skill.img").root
    skills = {}
    for sid in sorted(skill_ids):
        node = skill_root.get(str(sid))
        if node is None:
            skills[str(sid)] = {"name": f"技能 {sid}"}
            continue
        d = tree(node)
        name = d.get("name") or f"技能 {sid}"
        skills[str(sid)] = {
            "name": cn(name), "nameTw": name,
            "desc": cn(d.get("desc") or ""), "h1": cn(d.get("h1") or ""),
        }

    # Resolve equipment names for 282-only item ids (GMS083 String tree lacks them).
    print("reading String/Eqp.img ...", flush=True)
    eqp_root = load("String/Eqp.img").root
    eqp_names = {}
    eqp_node = eqp_root.get("Eqp")
    if eqp_node is not None:
        for cat in tree(eqp_node).values():
            if not isinstance(cat, dict):
                continue
            for key, entry in cat.items():
                if key.isdigit() and isinstance(entry, dict) and entry.get("name"):
                    eqp_names[key] = cn(entry["name"])

    unmapped = defaultdict(int)
    sets = []
    for sid, raw in sets_raw.items():
        if not isinstance(raw, dict) or not sid.lstrip("-").isdigit():
            continue
        name_tw = raw.get("setItemName") or ""
        # --- ItemID: two generations ---
        slots = []
        item_id_node = raw.get("ItemID") or {}
        legacy = any(isinstance(v, dict) for v in item_id_node.values())
        if legacy:
            for _, grp in sorted(item_id_node.items(), key=lambda kv: int(kv[0]) if kv[0].isdigit() else 0):
                if not isinstance(grp, dict):
                    continue
                ids = [v for k, v in grp.items() if k.isdigit() and isinstance(v, int)]
                part_tw = grp.get("typeName") or ""
                rep_tw = grp.get("representName") or ""
                slots.append({
                    "part": cn(part_tw.strip("（）()") or slot_of(ids[0] if ids else 0)),
                    "partTw": part_tw,
                    "items": ids,
                    "represent": cn(rep_tw), "representTw": rep_tw,
                })
        else:
            grouped = defaultdict(list)
            for _, v in sorted(item_id_node.items(), key=lambda kv: int(kv[0]) if kv[0].isdigit() else 0):
                for i in iter_ids(v):
                    if i > 0:
                        grouped[slot_of(i)].append(i)
            for part, ids in grouped.items():
                slots.append({"part": part, "partTw": "", "items": ids,
                              "represent": cn(eqp_names.get(str(ids[0]), "")),
                              "representTw": ""})

        # --- Effect tiers ---
        tiers = []
        for tname, tier in sorted((raw.get("Effect") or {}).items(),
                                  key=lambda kv: int(kv[0]) if kv[0].isdigit() else 0):
            if not isinstance(tier, dict) or not tname.isdigit():
                continue
            stats, extra = {}, {}
            for key, val in tier.items():
                if key in ("Option", "activeSkill", "OptionToMob", "bonusByTime"):
                    continue
                if not isinstance(val, int):
                    continue
                if key == "incAllStat":
                    for k in ALL_STAT_KEYS:
                        stats[k] = stats.get(k, 0) + val
                elif key in STAT_MAP:
                    k = STAT_MAP[key]
                    stats[k] = stats.get(k, 0) + val
                else:
                    extra[key] = val
                    unmapped[key] += 1
            opt_list = []
            for _, od in sorted((tier.get("Option") or {}).items(),
                                key=lambda kv: int(kv[0]) if kv[0].isdigit() else 0):
                if not isinstance(od, dict) or not od.get("option"):
                    continue
                oid, lvl = int(od["option"]), int(od.get("level", 0))
                definition = options.get(str(oid)) or {}
                level_node = (definition.get("levels") or {}).get(str(lvl)) or {}
                text_tw = definition.get("textTw") or ""
                text = definition.get("text") or ""
                for k, v in level_node.items():
                    if isinstance(v, int):
                        text_tw = text_tw.replace("#" + k, str(v))
                        text = text.replace("#" + k, str(v))
                opt_stats = {}
                boss_only = bool(level_node.get("boss"))
                for k, v in level_node.items():
                    if not isinstance(v, int) or k in OPTION_NON_STAT_KEYS:
                        continue
                    if k == "incDAMr":
                        key = "BossDamage" if boss_only else "Damage"
                    else:
                        key = OPTION_STAT_MAP.get(k)
                    if key:
                        opt_stats[key] = opt_stats.get(key, 0) + v
                opt_list.append({"id": oid, "level": lvl, "text": text,
                                 "textTw": text_tw, "stats": opt_stats})
            skill_list = []
            for _, sk in sorted((tier.get("activeSkill") or {}).items(),
                                key=lambda kv: int(kv[0]) if kv[0].isdigit() else 0):
                if not isinstance(sk, dict) or not sk.get("id"):
                    continue
                skill_list.append({"id": int(sk["id"]), "level": int(sk.get("level", 1))})
            tiers.append({"required": int(tname), "stats": stats, "unmapped": extra,
                          "options": opt_list, "activeSkills": skill_list})

        sets.append({
            "id": int(sid),
            "name": cn(name_tw), "nameTw": name_tw,
            "completeCount": int(raw.get("completeCount") or len(slots)),
            "slots": slots,
            "tiers": tiers,
            "desc": {k: cn(v) for k, v in (raw.get("Desc") or {}).items() if isinstance(v, str)},
            "jokerPossible": int(raw.get("jokerPossible") or 0),
            "zeroWeaponJokerPossible": int(raw.get("zeroWeaponJokerPossible") or 0),
            "legacyItemId": legacy,
        })

    out = {
        "source": "TMS282 client Etc/SetItemInfo.img + Item/ItemOption.img + String/Skill.img + String/Eqp.img",
        "note": "Simplified Chinese display strings; *Tw fields keep the original. "
                "modern ItemID slot grouping is INFERRED (see exporter docstring). "
                "Option incDAMr maps to BossDamage only when the level node has boss:1, else Damage.",
        "setCount": len(sets),
        "unmappedFields": dict(sorted(unmapped.items())),
        "options": {k: {"text": v["text"], "textTw": v["textTw"]} for k, v in options.items()},
        "skills": skills,
        "equipNames": {k: v for k, v in eqp_names.items() if int(k) in item_ids},
        "sets": sets,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, ensure_ascii=False, separators=(",", ":")) + "\n")
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes)")
    print("unmapped fields:", dict(sorted(unmapped.items())))


if __name__ == "__main__":
    main()
