#!/usr/bin/env python3
"""Export lossless ItemOption properties; does not infer grades or effect semantics."""
import argparse
import hashlib
import json
from pathlib import Path

from imglib_282_data_dir import Img


def properties(node):
    if node.kind == "UOL":
        raise ValueError(f"Unresolved UOL in ItemOption: {node.name}")
    if node.children:
        result = {}
        for child in node.children:
            if child.name in result:
                raise ValueError(f"Duplicate property: {child.name}")
            result[child.name] = properties(child)
        return result
    if node.kind == "Shape2D#Vector2D":
        return {"x": node.x, "y": node.y}
    if node.kind not in ("", "Property"):
        raise ValueError(f"Unsupported ItemOption node: {node.kind}")
    return {} if node.kind == "Property" else node.value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path.home() / "Documents/mxd/MapleStory/Data/Item/ItemOption.img")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    root = Img(str(args.source)).root
    options = {}
    for node in root.children:
        if not node.name.isdecimal():
            raise ValueError(f"Unexpected ItemOption root: {node.name}")
        entry = properties(node)
        if not isinstance(entry.get("info"), dict) or not isinstance(entry.get("level"), dict):
            raise ValueError(f"Missing info/level: {node.name}")
        options[node.name] = entry
    payload = {
        "schemaVersion": 1,
        "source": "MapleStory/Data/Item/ItemOption.img",
        "sourceSha256": hashlib.sha256(args.source.read_bytes()).hexdigest(),
        "options": options,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(options)} options, {sum(len(x['level']) for x in options.values())} level rows")


if __name__ == "__main__":
    main()
