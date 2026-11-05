#!/usr/bin/env python3
"""Export original IMG item names and info scalars for pure C# bot logic.

Uses the existing source wzpy parser; never decodes or replaces UI canvases.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey

# TMS 282 gives both the robot body (01662xxx) and the mechanical heart (01672xxx)
# the islot `Tm`, which this project maps to -18 (TamingMob/mount). Rule 11 calls
# for re-encoding resources to the beidou layout, so the slot is normalised in the
# data layer instead of special-cased in every consumer:
#   robot  -> An (equipment slot -32, allocated by the android migration plan)
#   heart  -> Ht (already -54 in EquipmentSlotOf, same meaning as 282 slot 54)
ANDROID_SLOTS = ((1662000, "An"), (1672000, "Ht"))


def android_strings(path):
    """Read the actual 282 client strings; the server XML can be a different build."""
    img = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region("BMS"))
    tree = img.parse()
    if img.truncated or img.parse_warnings:
        raise ValueError(f"Incomplete 282 strings: {path}: {img.parse_warnings}")
    node = tree.get("Eqp").get("Android")
    return {str(int(c.name)): {field: c.get(field).value for field in ("name", "desc")
                             if c.get(field) is not None}
            for c in node.children() if c.name.isdigit()}


def symbol_strings(path):
    """282 ARC/AUT/Grand 徽章（1712xxx/1713xxx/1714xxx）的名字与说明。

    GMS083 的 String/Eqp.img 没有这三个分支；282 客户端把它们放在
    Eqp/ArcaneForce、Eqp/AuthenticForce 下（1712007 只在服务端存在，客户端没有）。
    """
    img = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region("BMS"))
    tree = img.parse()
    if img.truncated or img.parse_warnings:
        raise ValueError(f"Incomplete 282 strings: {path}: {img.parse_warnings}")
    eqp = tree.get("Eqp")
    out = {}
    for branch in ("ArcaneForce", "AuthenticForce"):
        node = eqp.get(branch)
        if node is None:
            continue
        for c in node.children():
            if not c.name.isdigit():
                continue
            out[str(int(c.name))] = {field: c.get(field).value for field in ("name", "desc")
                                     if c.get(field) is not None}
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", type=Path, default=ROOT.parent / "MapleDashResources/Data")
    ap.add_argument("--out", type=Path, default=ROOT / "Assets/Resources/Bot/Items.json")
    ap.add_argument("--android-string", type=Path,
                    default=Path.home() / "Documents/mxd/MapleStory/Data/String/Eqp.img")
    ap.add_argument("--name-xml", type=Path,
                    default=Path.home() / "Documents/mxd/TMS282.5/data/wz282/String.wz/Eqp.img.xml",
                    help="282 String export used for items this project's String tree lacks")
    ap.add_argument("--consume-string-282", type=Path,
                    default=Path.home() / "Documents/mxd/MapleStory/Data/String/Consume.img",
                    help="282 client (BMS-keyed) Consume strings: names for the imported heart scrolls")
    args = ap.parse_args()
    key = WzKey.for_region("GMS")
    names, items, sources, descriptions = {}, {}, {}, {}

    def image(path, partial=False):
        raw = path.read_bytes()
        rel = path.relative_to(args.data).as_posix()
        sources[rel] = hashlib.sha256(raw).hexdigest()
        img = WzImage.from_bytes(raw, key=key, name=rel)
        tree = img.parse_partial(only=frozenset({"info"})) if partial else img.parse()
        if img.truncated or img.parse_warnings:
            raise ValueError(f"Incomplete IMG parse: {rel}: {img.parse_warnings}")
        return tree

    def strings(node):
        name = node.get("name")
        if node.name.isdigit() and isinstance(getattr(name, "value", None), str):
            names[str(int(node.name))] = name.value
        else:
            for child in node.children():
                if hasattr(child, "children"):
                    strings(child)

    for stem in ["Eqp", "Consume", "Etc", "Ins", "Cash", "Pet"]:
        strings(image(args.data / "String" / (stem + ".img")))

    def add(item_id, node, source):
        info = node.get("info") if node is not None else None
        if info is None:
            return
        scalars = {}
        for child in info.children():
            value = getattr(child, "value", None)
            if isinstance(value, (int, float, str)):
                scalars[child.name] = value
        for start, slot in ANDROID_SLOTS:
            if start <= item_id < start + 10000 and scalars.get("islot") == "Tm":
                scalars["islot"] = slot
                scalars["vslot"] = slot
        record = {"info": scalars, "source": source}

        # 卷轴的适用范围不在 info 里：`req` 是允许的装备 id 白名单（2616004 未来心脏卷、
        # 2643008 妖精之心强化石都靠它），`reqScope` 是「整段 id」口径 —— 2643009/2643010
        # 卡勒马心脏卷没有 req，只有 reqScope/0 = {scope: 167, div: 10000}，即 167xxxx 全部心脏。
        # 服务端 NativeScrollService 是纯 C#，读不到 IMG，所以这两个节点必须进 ItemInfo。
        req = node.get("req")
        allowed = [getattr(child, "value", None) for child in req.children()] if req is not None else []
        allowed = [int(value) for value in allowed if isinstance(value, int)]
        if allowed:
            record["req"] = allowed
        scope = node.get("reqScope")
        ranges = []
        for entry in (scope.children() if scope is not None else []):
            prefix = getattr(entry.get("scope"), "value", None)
            divisor = getattr(entry.get("div"), "value", None)
            if isinstance(prefix, int) and isinstance(divisor, int):
                ranges.append([prefix, divisor])
        if ranges:
            record["reqScope"] = ranges
        items[str(item_id)] = record

    # 282-only equipment (robots, hearts) never made it into this project's GMS083
    # String tree, so their names come from the 282 String export.
    if args.name_xml and args.name_xml.is_file():
        import re
        text = args.name_xml.read_text(encoding="utf-8")
        for match in re.finditer(
                r'<imgdir name="(1[67]\d{5})">\s*<string name="name" value="([^"]*)"', text):
            item_id = int(match.group(1))
            if any(start <= item_id < start + 10000 for start, _ in ANDROID_SLOTS):
                names.setdefault(str(item_id), match.group(2))
        print(f"282 name fallback: {sum(1 for k in names if 1662000 <= int(k) < 1680000)} android names", flush=True)

    if args.android_string.is_file():
        sources["TMS282/String/Eqp.img"] = hashlib.sha256(args.android_string.read_bytes()).hexdigest()
        for item_id, fields in android_strings(args.android_string).items():
            if "name" in fields:
                names[item_id] = fields["name"]
            if fields.get("desc"):
                descriptions[item_id] = fields["desc"]

    paths = sorted((args.data / "Character").rglob("*.img"))
    for index, path in enumerate(paths):
        relative = path.relative_to(args.data / "Character")
        # `_Canvas/` holds the 282-era split pixel files (same 8-digit stems as
        # their host), so they must never be mistaken for items. Hair/Face are
        # character parts, Afterimage is an effect overlay — none are items.
        if (relative.parts[0] in {"Hair", "Face", "Afterimage"} or "_Canvas" in relative.parts
                or not path.stem.isdigit() or int(path.stem) < 1000000):
            continue
        tree = image(path, partial=True)
        add(int(path.stem), tree, path.relative_to(args.data).as_posix())
        if index % 3000 == 0:
            print(f"Character IMG scanned: {index}/{len(paths)}", flush=True)
    for path in sorted((args.data / "Item").rglob("*.img")):
        relative = path.relative_to(args.data / "Item")
        # `_Canvas/` 是 282 的分离像素树（节点名与宿主 img 相同），里面只有 canvas、
        # 没有 info 标量。扫进来会用空 info 覆盖宿主的正确记录（02643009 就这么被抹过）。
        if "_Canvas" in relative.parts:
            continue
        tree = image(path)
        rel = path.relative_to(args.data).as_posix()
        if path.stem.isdigit() and int(path.stem) >= 1000000:
            add(int(path.stem), tree, rel)
        else:
            for child in tree.children():
                if child.name.isdigit() and int(child.name) >= 1000000:
                    add(int(child.name), child, rel)
    # 心脏卷轴（2616004~2616010 未來心臟專用、2643008 妖精之心強化石、2643009/2643010
    # 卡勒馬特殊心臟卷）随 282 的 Item/Consume/0261.img、0264.img 一起进来，
    # GMS083 的 String/Consume.img 没有它们的名字，所以只给「本项目确实有这件道具、
    # 但缺名字」的 id 补 282 的名字与说明，不把 282 的整张 Consume 表带进来。
    if args.consume_string_282.is_file():
        raw = args.consume_string_282.read_bytes()
        sources["TMS282/String/Consume.img"] = hashlib.sha256(raw).hexdigest()
        img = WzImage.from_bytes(raw, key=WzKey.for_region("BMS"), name="Consume.img")
        tree = img.parse()
        if img.truncated or img.parse_warnings:
            raise ValueError(f"Incomplete 282 Consume strings: {img.parse_warnings}")
        filled = 0
        for node in tree.children():
            if not node.name.isdigit():
                continue
            item_id = str(int(node.name))
            if item_id not in items:
                continue
            name = getattr(node.get("name"), "value", None)
            desc = getattr(node.get("desc"), "value", None)
            if name and item_id not in names:
                names[item_id] = name
                filled += 1
            if desc and item_id not in descriptions:
                descriptions[item_id] = desc
        print(f"282 Consume string fallback: {filled} names", flush=True)

    # 徽章名字同样来自 282 客户端的 String/Eqp.img（放在扫描之后，items 才齐全）。
    if args.android_string.is_file():
        symbols = symbol_strings(args.android_string)
        filled = 0
        for item_id, fields in symbols.items():
            if item_id not in items:      # 只给本项目确实导入的徽章补名字
                continue
            if "name" in fields:
                names.setdefault(item_id, fields["name"])
                filled += 1
            if fields.get("desc"):
                descriptions.setdefault(item_id, fields["desc"])
        print(f"282 symbol string fallback: {filled} names", flush=True)

    # 心脏卷轴的属性只存在于宿主 Item img（_Canvas 树只有像素），这里兜一道，
    # 免得 _Canvas 又一次把 info 覆盖成空表。
    for scroll_id in ("2616004", "2643009"):
        record = items.get(scroll_id)
        if not record or not record.get("info") or "_Canvas" in record.get("source", ""):
            raise ValueError(f"heart scroll {scroll_id} lost its info (source {record and record.get('source')})")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps({"version": 1, "names": names, "descriptions": descriptions, "items": items, "sources": sources},
                                  ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Exported {len(names)} item names and {len(items)} item info records from {len(sources)} IMG files")


if __name__ == "__main__":
    main()
