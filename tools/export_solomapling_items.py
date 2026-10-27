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


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", type=Path, default=ROOT.parent / "MapleDashResources/Data")
    ap.add_argument("--out", type=Path, default=ROOT / "Assets/Resources/Bot/Items.json")
    args = ap.parse_args()
    key = WzKey.for_region("GMS")
    names, items, sources = {}, {}, {}

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

    def add(item_id, info, source):
        if info is None:
            return
        scalars = {}
        for child in info.children():
            value = getattr(child, "value", None)
            if isinstance(value, (int, float, str)):
                scalars[child.name] = value
        items[str(item_id)] = {"info": scalars, "source": source}

    paths = sorted((args.data / "Character").rglob("*.img"))
    for index, path in enumerate(paths):
        relative = path.relative_to(args.data / "Character")
        if relative.parts[0] in {"Hair", "Face", "Afterimage"} or not path.stem.isdigit() or int(path.stem) < 1000000:
            continue
        tree = image(path, partial=True)
        add(int(path.stem), tree.get("info"), path.relative_to(args.data).as_posix())
        if index % 3000 == 0:
            print(f"Character IMG scanned: {index}/{len(paths)}", flush=True)
    for path in sorted((args.data / "Item").rglob("*.img")):
        tree = image(path)
        rel = path.relative_to(args.data).as_posix()
        if path.stem.isdigit() and int(path.stem) >= 1000000:
            add(int(path.stem), tree.get("info"), rel)
        else:
            for child in tree.children():
                if child.name.isdigit() and int(child.name) >= 1000000:
                    add(int(child.name), child.get("info"), rel)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps({"version": 1, "names": names, "items": items, "sources": sources},
                                  ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Exported {len(names)} item names and {len(items)} item info records from {len(sources)} IMG files")


if __name__ == "__main__":
    main()
