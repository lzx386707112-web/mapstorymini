#!/usr/bin/env python3
"""Import the TMS 282 mechanical-heart scroll icons into this project's runtime Data.

The heart scrolls (2616004~2616010 未來心臟專用, 2643008 妖精之心強化石,
2643009/2643010 卡勒馬特殊心臟卷) live in `Item/Consume/0261.img` and
`Item/Consume/0264.img`, neither of which exists in the GMS083 tree this project
was built from. Their `info/icon` canvases are `_outlink`s into the sibling 282
pixel tree `Item/Consume/_Canvas/<same stem>.img`, so both halves have to ship.

Per AGENTS rule 11 every IMG is re-encoded from the 282 BMS key to the GMS key
`Assets/Scripts/Wz/ImgFile.cs` decrypts, and every canvas must stay in a format
`ImgFile.Rgba` can decode (ARGB4444 / RGB565 / ARGB1555).

Usage:
  python3 tools/ms282/import_heart_scrolls.py           # dry run, report only
  python3 tools/ms282/import_heart_scrolls.py --write    # copy + register
"""
from __future__ import annotations

import argparse
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec  # noqa: E402
from import_androids import DEFAULT_DEST, DEFAULT_SRC, register, verify_imported  # noqa: E402

# The two host images and the pixel trees their icons outlink into.
FILES = [
    "Item/Consume/0261.img",
    "Item/Consume/0264.img",
    "Item/Consume/_Canvas/0261.img",
    "Item/Consume/_Canvas/0264.img",
]

# Node names the runtime actually reaches, asserted so a future 282 build that
# repacks the icons elsewhere fails here instead of silently rendering blanks.
REQUIRED = {
    "Item/Consume/0261.img": ["02616004", "02616009", "02616010"],
    "Item/Consume/0264.img": ["02643008", "02643009", "02643010"],
    "Item/Consume/_Canvas/0261.img": ["02616004"],
    "Item/Consume/_Canvas/0264.img": ["02643008", "02643009", "02643010"],
}

# 每张心脏卷的 icon 都要能一路走到真 Canvas（UOL 相对路径 → _outlink → _Canvas 像素），
# 否则背包/商店里就是空白格。02616005~02616010 是 UOL 复用 02616004 的同一张图。
ICON_OWNERS = {
    "Item/Consume/0261.img": ["02616004", "02616005", "02616006", "02616007", "02616008", "02616009", "02616010"],
    "Item/Consume/0264.img": ["02643008", "02643009", "02643010"],
}


def resolve_icon(payloads: dict, rel: str, path: str, parsed: dict, depth: int = 0):
    """复刻 Assets/Scripts/Wz/WzAssets.cs 的 Canvas 解析：UOL → _outlink/_inlink → 真 Canvas。"""
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


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    ap.add_argument("--write", action="store_true", help="copy files and register them in NativeAssetIndex.json")
    args = ap.parse_args()
    src, dest = args.src.resolve(), args.dest.resolve()

    from wzpy import WzImage, WzKey

    rows, formats, failures = [], Counter(), []
    payloads: dict[str, bytes] = {}
    for rel in FILES:
        path = src / rel
        if not path.is_file():
            failures.append((rel, "missing in the 282 tree"))
            continue
        if not rel.startswith("Item/Consume/"):
            failures.append((rel, "unexpected path"))
            continue
        raw = path.read_bytes()
        try:
            data, walker = codec.transcode(raw)
            file_formats, unsupported = verify_imported(raw, data, rel, strict=True)
        except Exception as error:                      # noqa: BLE001 - report and continue
            failures.append((rel, f"{type(error).__name__}: {error}"))
            continue
        formats.update(file_formats)
        payloads[rel] = data
        rows.append({"path": rel, "bytes": len(data), "strings": len(walker.sites),
                     "status": "overwrite" if (dest / rel).is_file() else "new"})
        print(f"{rel}: {len(data)} bytes, {len(walker.sites)} strings, formats {dict(file_formats)}", flush=True)

    print(f"transcoded {len(rows)} files, {sum(r['bytes'] for r in rows)} bytes, "
          f"pixel formats {dict(formats)}")
    if failures:
        print(f"FAILED {len(failures)}:")
        for rel, message in failures:
            print("  ", rel, message)
        raise SystemExit(1)

    # The runtime resolves `info/icon` through `_outlink`, so the named nodes have
    # to survive the re-key byte-for-byte. Read them back with the GMS key.
    for rel, nodes in REQUIRED.items():
        image = WzImage.from_bytes(payloads[rel], key=WzKey.for_region("GMS"), name=Path(rel).name)
        image.parse()
        root = image.root
        for node in nodes:
            found = root.get(node)
            if found is None:
                raise SystemExit(f"{rel}: node {node} missing after transcode")
            if rel.endswith("_Canvas/0261.img") or rel.endswith("_Canvas/0264.img"):
                canvas = found.get("info/icon")
            else:
                canvas = found.get("info/icon")
            if canvas is None:
                raise SystemExit(f"{rel}/{node}: info/icon missing")
        print(f"{rel}: {len(nodes)} nodes verified with the GMS key")

    # 图标必须真的走得到像素：UOL 复用的（02616005~02616010）和跨文件 _outlink 的都要过。
    parsed_icons: dict = {}
    for rel, owners in ICON_OWNERS.items():
        for owner in owners:
            canvas = resolve_icon(payloads, rel, f"{owner}/info/icon", parsed_icons)
            if canvas is None:
                raise SystemExit(f"{rel}/{owner}: info/icon 解析不到 Canvas 像素")
        print(f"{rel}: {len(owners)} 张卷轴图标都能解析到像素")

    if not args.write:
        print("dry run; pass --write to copy into", dest)
        return

    for rel, data in payloads.items():
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)

    added, updated = register(FILES, dest)
    print(f"NativeAssetIndex.json: {added} added, {updated} refreshed, {len(FILES)} total")


if __name__ == "__main__":
    main()
