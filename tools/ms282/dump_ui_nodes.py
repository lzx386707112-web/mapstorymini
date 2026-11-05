#!/usr/bin/env python3
"""Dump a 282 UI IMG branch as a flat node list (path / type / origin / canvas size / text).

282 UI images are split: `UI/<Window>.img` holds the structure and each canvas is a stub
with an `_outlink` into `UI/_Canvas/<Window>.img/<same path>`, where the real pixels,
size and origin live. This dumper follows `_outlink` so the numbers printed are the ones
the client actually draws with.

Usage:
  python3 dump_ui_nodes.py UI/UIWindow4.img/HyperStat
  python3 dump_ui_nodes.py UI/UIArcaneForce.img/ArcEquip --limit 400
"""
import argparse
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402


class Tree:
    def __init__(self, source: Path):
        self.source = source
        self.images = {}

    def image(self, rel: str):
        if rel not in self.images:
            raw = (self.source / rel).read_bytes()
            img = WzImage.from_bytes(raw, key=WzKey.for_region("BMS"), name=rel)
            img.parse()
            if img.truncated or img.parse_warnings:
                raise ValueError(f"Incomplete source {rel}: {img.parse_warnings}")
            self.images[rel] = img
        return self.images[rel]

    def node(self, rel: str, branch: str):
        node = self.image(rel).root
        for part in [p for p in branch.split("/") if p]:
            node = node.get(part)
            if node is None:
                raise KeyError(f"{rel}/{branch}")
        return node

    def resolve(self, rel: str, branch: str):
        """Follow _outlink/_inlink chains; returns (rel, branch, node)."""
        seen = set()
        while True:
            node = self.node(rel, branch)
            link = node.get("_outlink") if hasattr(node, "get") else None
            if link is not None:
                target_rel, _, rest = link.value.partition(".img/")
                target = (target_rel + ".img", rest)
            else:
                inline = node.get("_inlink") if hasattr(node, "get") else None
                if inline is None:
                    return rel, branch, node
                target = (rel, (branch.rsplit("/", 1)[0] + "/" if "/" in branch else "") + inline.value)
            if target in seen:
                return rel, branch, node
            seen.add(target)
            rel, branch = target[0], target[1].strip("/")


def origin_xy(origin):
    value = getattr(origin, "value", None)
    if isinstance(value, (tuple, list)) and len(value) >= 2:
        return value[0], value[1]
    x, y = origin.get("x"), origin.get("y")
    return (x.value if x is not None else 0), (y.value if y is not None else 0)


def value_of(node, name):
    child = node.get(name) if hasattr(node, "get") else None
    return None if child is None else child.value


def describe(tree, rel, branch, node):
    row = {"type": type(node).__name__}
    if isinstance(node, WzCanvasProperty):
        t_rel, t_branch, real = tree.resolve(rel, branch)
        row.update(w=real.width, h=real.height)
        origin = real.get("origin") or node.get("origin")
        if origin is not None:
            ox, oy = origin_xy(origin)
            row["ox"], row["oy"] = ox, oy
        row["z"] = value_of(real, "z") or value_of(node, "z") or 0
        if (t_rel, t_branch) != (rel, branch):
            row["src"] = f"{t_rel}/{t_branch}"
    elif isinstance(node, WzUolProperty):
        row["uol"] = node.value
    elif node.value is not None:
        row["value"] = node.value
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", help="IMG relative path with optional /branch, e.g. UI/UIWindow4.img/HyperStat")
    parser.add_argument("--source", type=Path, default=Path.home() / "Documents/mxd/MapleStory/Data")
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--csv", action="store_true")
    args = parser.parse_args()

    if ".img/" in args.target:
        rel, _, branch = args.target.partition(".img/")
        rel += ".img"
    else:
        rel, branch = args.target, ""
    tree = Tree(args.source)
    root = tree.node(rel, branch)
    rows = []

    def walk(node, path):
        rows.append((path, describe(tree, rel, (branch + "/" + path).strip("/"), node)))
        for child in node.children():
            walk(child, (path + "/" + child.name).lstrip("/"))

    walk(root, "")
    if args.limit:
        rows = rows[: args.limit]
    if args.csv:
        print("path,type,w,h,originX,originY,z,value,uol,src")
    for path, r in rows:
        if args.csv:
            print(path + "," + ",".join(str(r.get(k, "")) for k in ("type", "w", "h", "ox", "oy", "z", "value", "uol", "src")))
        else:
            extra = " ".join(f"{k}={v}" for k, v in r.items() if k != "type")
            print(f"{path:<70} {r['type']:<18} {extra}")
    print(f"# {len(rows)} nodes")


if __name__ == "__main__":
    main()
