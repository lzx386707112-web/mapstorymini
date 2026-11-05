#!/usr/bin/env python3
"""Import the TMS 282 soul items (2590xxx 开孔器 / 2591xxx 灵魂石) into this project.

Neither `Item/Consume/0259.img` nor its pixel tree exists in the GMS083 base this
project was built from, so 灵魂武器 had no obtainable items at all — the server
logic (NativeSoulService) was already in place but nothing could be used.

What this does:
  1. transcodes `Item/Consume/0259.img` + `Item/Consume/_Canvas/0259.img` (BMS key →
     GMS key, per AGENTS rule 11) and registers them in NativeAssetIndex.json,
  2. merges the item `info` nodes into `Assets/Resources/Bot/Items.json`,
  3. fills names from the 282 Chinese string table, falling back to the 265 English
     one for ids the 282 table doesn't cover.

Usage:
  python3 tools/ms282/import_soul_items.py            # dry run, report only
  python3 tools/ms282/import_soul_items.py --write     # write files + catalog
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]                 # MapleDash/
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec                                    # noqa: E402
from import_androids import DEFAULT_DEST, DEFAULT_SRC, register, verify_imported  # noqa: E402

SERVER = ROOT.parent.parent / "TMS282.5"
NAMES_282 = SERVER / "data/wz282/String.wz/Consume.img.xml"
NAMES_265 = SERVER / "data/wz265/String.wz/Consume.img.xml"
CATALOG = ROOT / "Assets/Resources/Bot/Items.json"
REPORT = ROOT.parent / "docs/tms282-skill/data/soul-item-import.json"

HOST = "Item/Consume/0259.img"
CANVAS = "Item/Consume/_Canvas/0259.img"

# 2590 = 灵魂开孔器（含 2590000 通用），2591 = 灵魂石。
SOUL_ID = re.compile(r"^0?259[01]\d{3}$")


def load_names(path: Path) -> dict[int, str]:
    """<imgdir name="2591010"> ... <string name="name" value="..."/>"""
    if not path.is_file():
        return {}
    text = path.read_text(encoding="utf-8", errors="ignore")
    names: dict[int, str] = {}
    for block in re.finditer(r'<imgdir name="(259[01]\d{3})">(.*?)</imgdir>', text, re.S):
        found = re.search(r'<string name="name" value="([^"]*)"', block.group(2))
        if found:
            names[int(block.group(1))] = found.group(1)
    return names


def resolve_icon(payloads: dict, rel: str, path: str, parsed: dict, depth: int = 0):
    """复刻 WzAssets.cs 的 Canvas 解析：UOL → _outlink/_inlink → 真 Canvas。"""
    from wzpy import WzImage, WzKey
    from wzpy.properties import WzUolProperty

    if depth > 8 or rel not in payloads:
        return None
    if rel not in parsed:
        image = WzImage.from_bytes(payloads[rel], key=WzKey.for_region("GMS"), name=Path(rel).name)
        image.parse()
        parsed[rel] = image
    node = parsed[rel].get(path)
    if node is None:
        return None
    if isinstance(node, WzUolProperty):
        stack = path.split("/")[:-1]
        for segment in str(node.value).split("/"):
            if segment == "..":
                stack.pop()
            elif segment != ".":
                stack.append(segment)
        return resolve_icon(payloads, rel, "/".join(stack), parsed, depth + 1)
    for key in ("_outlink", "_inlink"):
        link = node.get(key)
        if link is None:
            continue
        value = str(link.value)
        if key == "_outlink":
            target, inner = value.split(".img/", 1)
            return resolve_icon(payloads, target + ".img", inner, parsed, depth + 1)
        return resolve_icon(payloads, rel, value, parsed, depth + 1)
    return node if getattr(node, "width", 0) else None


def external_targets(payloads: dict, rel: str) -> set[str]:
    """0259 里每个 icon 都 outlink 出去：把目标 .img 全找出来，缺哪个补哪个。"""
    from wzpy import WzImage, WzKey
    from wzpy.properties import WzUolProperty

    image = WzImage.from_bytes(payloads[rel], key=WzKey.for_region("GMS"), name=Path(rel).name)
    image.parse()
    targets: set[str] = set()
    for node in image.root.children():
        if not SOUL_ID.match(node.name):
            continue
        icon = node.get("info/icon")
        if icon is None or isinstance(icon, WzUolProperty):
            continue
        link = icon.get("_outlink")
        if link is not None:
            targets.add(str(link.value).split(".img/")[0] + ".img")
    return targets


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()
    src, dest = args.src.resolve(), args.dest.resolve()

    from wzpy import WzImage, WzKey

    payloads: dict[str, bytes] = {}
    formats, unsupported_all = Counter(), []
    for rel in (HOST, CANVAS):
        path = src / rel
        if not path.is_file():
            raise SystemExit(f"{rel} missing in the 282 tree")
        raw = path.read_bytes()
        data, _ = codec.transcode(raw)
        file_formats, unsupported = verify_imported(raw, data, rel, strict=False)
        formats.update(file_formats)
        if unsupported:
            unsupported_all.extend(unsupported)
        payloads[rel] = data
        print(f"{rel}: {len(raw)} → {len(data)} bytes, formats {dict(file_formats)}")

    # 有些 icon outlink 到别的 _Canvas 文件（例如 0243.img 的周年图标），
    # 那些图本工程可能还没有；缺就一起导入，否则背包里是空白格。
    extra = []
    for rel in list(payloads):
        for target in sorted(external_targets(payloads, rel)):
            if target in payloads:
                continue
            if (dest / target).is_file():
                continue
            if not (src / target).is_file():
                raise SystemExit(f"{rel}: outlink target {target} missing everywhere")
            extra.append(target)
    for rel in extra:
        raw = (src / rel).read_bytes()
        data, _ = codec.transcode(raw)
        payloads[rel] = data
        print(f"{rel}: 连带导入（icon 目标）{len(data)} bytes")

    names_282 = load_names(NAMES_282)
    names_265 = load_names(NAMES_265)

    image = WzImage.from_bytes(payloads[HOST], key=WzKey.for_region("GMS"), name="0259.img")
    image.parse()
    entries, items = [], {}
    for node in image.root.children():
        if not SOUL_ID.match(node.name):
            continue
        item_id = int(node.name)
        info = node.get("info")
        fields = {child.name: child.value for child in info.children()
                  if isinstance(child.value, (int, float, str))} if info is not None else {}
        entries.append(item_id)
        items[str(item_id)] = {"source": HOST, "info": fields}
    if not entries:
        raise SystemExit("0259.img 里没有 2590/2591 条目")

    unresolved = [item_id for item_id in entries if item_id not in names_282 and item_id not in names_265]
    print(f"2590/2591 条目 {len(entries)}（2590 {sum(1 for i in entries if i // 1000 == 2590)} / "
          f"2591 {sum(1 for i in entries if i // 1000 == 2591)}）；282 中文名 {len(names_282)}，"
          f"265 英文名 {len(names_265)}，两边都查不到 {len(unresolved)}")
    if unresolved[:5]:
        print("  样例:", unresolved[:5])
    if len(unresolved) > len(entries) // 10:
        raise SystemExit("名称覆盖率不足 90%，先核对字符串表")

    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    conflict = [item_id for item_id in items if str(item_id) in catalog["items"]]
    print(f"Items.json 里已有同名条目 {len(conflict)} 条（会覆盖为 0259 的 info）")

    if not args.write:
        print("dry run; pass --write to install into", dest)
        return

    for rel, data in payloads.items():
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    added, updated = register(list(payloads), dest)
    print(f"NativeAssetIndex.json: {added} added, {updated} refreshed")

    for item_id, entry in items.items():
        catalog["items"][item_id] = entry
        name = names_282.get(int(item_id)) or names_265.get(int(item_id))
        if name:
            catalog["names"][item_id] = name
    for rel in payloads:
        catalog.setdefault("sources", {})[rel] = hashlib.sha256(payloads[rel]).hexdigest()
    CATALOG.write_text(json.dumps(catalog, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Bot/Items.json: 写入 {len(items)} 条，总条目 {len(catalog['items'])}")

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps({
        "files": {rel: hashlib.sha256(data).hexdigest() for rel, data in payloads.items()},
        "items": len(items),
        "names": {"zh": len(names_282), "en": len(names_265), "unresolved": len(unresolved)},
        "pixelFormats": dict(formats),
        "unsupportedCanvases": unsupported_all[:20],
        "encoding": "GMS",
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("report:", REPORT)


if __name__ == "__main__":
    main()
