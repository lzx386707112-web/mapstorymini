#!/usr/bin/env python3
"""Dump a TMS282 client IMG branch as a full JSON tree (read-only evidence tool).

Unlike dump_ui_nodes.py this keeps every node type (sub-property, canvas, vector,
string, uol) and does not resolve links unless asked, so quest/item/UI evidence can
be quoted verbatim. `_outlink` / `_inlink` are always printed as their raw target;
`--resolve` additionally follows them for canvas size/origin.

Usage:
  python3 tools/ms282/dump_img_tree.py Quest/QuestData/1462.img --branch Check
  python3 tools/ms282/dump_img_tree.py String/Npc.img --branch 1540942
  python3 tools/ms282/dump_img_tree.py UI/VMatrixUI.img --branch VMatrix --resolve --limit 2
"""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty, WzUolProperty  # noqa: E402

DEFAULT_DATA = Path.home() / "Documents/mxd/MapleStory/Data"


def value_of(node):
    value = getattr(node, "value", None)
    if isinstance(value, tuple):
        return [int(value[0]), int(value[1])]
    if isinstance(value, (bytes, bytearray)):
        return f"<bytes {len(value)}>"
    return value


def tree(node, depth=None, resolve=None):
    if resolve is not None:
        node, src = resolve(node)
    children = node.children()
    if not children:
        return value_of(node)
    out = {"__type": type(node).__name__} if not isinstance(node, WzSubProperty) else {}
    if resolve is not None and src:
        out["__src"] = src
    if isinstance(node, WzCanvasProperty):
        out["__canvas"] = [node.width, node.height]
    if depth is not None and depth <= 0:
        out["__children"] = [c.name for c in children]
        return out
    for c in children:
        out[c.name] = tree(c, None if depth is None else depth - 1, resolve)
    return out


def make_resolver(data: Path):
    cache = {}

    def load(rel):
        if rel not in cache:
            img = WzImage.from_file(str(data / rel), key=WzKey.for_region("BMS"))
            img.parse()
            if img.truncated or img.parse_warnings:
                raise ValueError(f"incomplete {rel}: {img.parse_warnings}")
            cache[rel] = img
        return cache[rel]

    def path_of(node):
        parts, cur = [], node
        while cur is not None and getattr(cur, "parent", None) is not None:
            parts.append(cur.name)
            cur = cur.parent
        return "/".join(reversed(parts))

    def find(rel, branch):
        node = load(rel).root
        for part in [p for p in branch.split("/") if p]:
            node = node.get(part)
            if node is None:
                raise KeyError(f"{rel}/{branch}")
        return node

    def resolve(node):
        """Follow _outlink/_inlink once per hop; report the final source path."""
        src = None
        seen = set()
        for _ in range(8):
            if not hasattr(node, "get"):
                return node, src
            link = node.get("_outlink")
            if link is not None:
                rel, _, branch = link.value.partition(".img/")
                rel += ".img"
            else:
                inline = node.get("_inlink")
                if inline is None:
                    return node, src
                rel, branch = None, None
                parent = getattr(node, "parent", None)
                owner_rel = getattr(parent, "_source_rel", None) if parent is not None else None
                if owner_rel is None:
                    return node, src
                rel = owner_rel
                prefix = path_of(parent)
                branch = (prefix + "/" if prefix else "") + inline.value
            key = (rel, branch)
            if key in seen:
                return node, src
            seen.add(key)
            node = find(rel, branch)
            src = f"{rel}/{branch}"
        return node, src

    return resolve, load


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("target", help="IMG path relative to Data/, e.g. Quest/QuestData/1462.img")
    ap.add_argument("--branch", default="", help="slash separated child path inside the IMG")
    ap.add_argument("--source", type=Path, default=DEFAULT_DATA)
    ap.add_argument("--depth", type=int, default=None)
    ap.add_argument("--resolve", action="store_true", help="follow _outlink/_inlink for canvas nodes")
    ap.add_argument("--out", default="", help="write JSON here instead of stdout")
    args = ap.parse_args()

    resolve, load = make_resolver(args.source)
    node = load(args.target).root
    for part in [p for p in args.branch.split("/") if p]:
        node = node.get(part)
        if node is None:
            raise SystemExit(f"missing branch {args.branch} in {args.target}")
    data = tree(node, args.depth, resolve if args.resolve else None)
    text = json.dumps(data, ensure_ascii=False, indent=1)
    if args.out:
        Path(args.out).write_text(text + "\n", encoding="utf-8")
        print(f"{len(text.splitlines())} lines -> {args.out}")
    else:
        print(text)


if __name__ == "__main__":
    main()
