#!/usr/bin/env python3
"""Dump a baked project UI IMG branch (project copies are GMS-keyed, single-file).

用法:
  python3 tools/ms282/dump_baked_ui.py UI/UIWindow4.img HyperStat
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT.parent / "MapleDashResources/Data"
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402

REL = sys.argv[1] if len(sys.argv) > 1 else "UI/UIWindow4.img"
BRANCH = sys.argv[2] if len(sys.argv) > 2 else "HyperStat"

img = WzImage.from_bytes((DATA / REL).read_bytes(), key=WzKey.for_region("GMS"), name=REL)
img.parse()

Cache: dict[str, WzImage] = {REL: img}


def image(rel: str) -> WzImage:
    if rel not in Cache:
        loaded = WzImage.from_bytes((DATA / rel).read_bytes(), key=WzKey.for_region("GMS"), name=rel)
        loaded.parse()
        Cache[rel] = loaded
    return Cache[rel]


def at(rel: str, branch: str):
    found = image(rel).root
    for part in [p for p in branch.split("/") if p]:
        found = found.get(part) if found is not None else None
    return found


def resolve(rel: str, branch: str):
    """Follow _outlink / _inlink to the canvas that actually carries pixels."""
    seen = set()
    while True:
        node = at(rel, branch)
        if node is None:
            return rel, branch, None
        link = node.get("_outlink")
        if link is not None:
            target_rel, _, rest = str(link.value).partition(".img/")
            target = (target_rel + ".img", rest.strip("/"))
        else:
            inline = node.get("_inlink")
            if inline is None:
                return rel, branch, node
            parent = branch.rsplit("/", 1)[0] + "/" if "/" in branch else ""
            target = (rel, (parent + str(inline.value)).strip("/"))
        if target in seen:
            return rel, branch, node
        seen.add(target)
        rel, branch = target


def num(node, name, default=0):
    child = node.get(name) if hasattr(node, "get") else None
    return default if child is None else getattr(child, "value", default)


def walk(rel: str, branch: str, node, depth: int = 0) -> None:
    pad = "  " * depth
    if isinstance(node, WzCanvasProperty):
        t_rel, t_branch, real = resolve(rel, branch)
        real = real or node
        origin = real.get("origin") or node.get("origin")
        ox = num(origin, "x") if origin is not None else 0
        oy = num(origin, "y") if origin is not None else 0
        src = "" if (t_rel, t_branch) == (rel, branch) else f"  << {t_rel}/{t_branch}"
        print(f"{pad}{node.name}  canvas {real.width}x{real.height} origin=({ox},{oy}) z={num(real, 'z')}{src}")
        return
    if isinstance(node, WzUolProperty):
        print(f"{pad}{node.name}  uol -> {node.value}")
        return
    value = getattr(node, "value", None)
    if value is not None and not hasattr(node, "children"):
        print(f"{pad}{node.name} = {value}")
        return
    if value is not None:
        print(f"{pad}{node.name} = {value}")
        return
    print(f"{pad}{node.name}/")
    for child in node.children():
        walk(rel, (branch + "/" + child.name).strip("/"), child, depth + 1)


root = at(REL, BRANCH)
if root is None:
    raise SystemExit("branch not found: " + BRANCH)
print(f"{REL}/{BRANCH}")
for child in root.children():
    walk(REL, (BRANCH + "/" + child.name).strip("/"), child, 1)
