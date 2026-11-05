#!/usr/bin/env python3
"""只读：打印本项目 Data/Skill/<id>.img（GMS 密钥）的节点树，用于核对 282 导入结构。

    python3 tools/ms282/dump_project_skill_img.py 114 --depth 5
    python3 tools/ms282/dump_project_skill_img.py 114 --grep video
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parent
sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

GMS = WzKey.for_region("GMS")
DATA = REPO / "MapleDashResources/Data/Skill"


def kids(node):
    try:
        return node.children()
    except TypeError:
        return []


def walk(node, depth, maxd, grep, prefix=""):
    if depth > maxd:
        return
    items = kids(node)
    if not items:
        return
    for v in items:
        k = v.name
        path = f"{prefix}/{k}"
        extra = ""
        if isinstance(v, WzCanvasProperty):
            extra = f"  {v.width}x{v.height} fmt={v.format} origin={getattr(v, 'origin', None)}"
        hit = grep is None or grep in path.lower()
        if hit:
            print("  " * depth + f"{k} <{type(v).__name__}>{extra}")
        walk(v, depth + 1, maxd, grep, path)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("skill", help="技能 img 号，如 114（= 四转 112 + 2）")
    ap.add_argument("--depth", type=int, default=4)
    ap.add_argument("--grep", default=None, help="只打印路径含该子串的节点")
    ap.add_argument("--canvas-dir", action="store_true", help="改看 _Canvas/<id>.img")
    args = ap.parse_args()

    path = DATA / ("_Canvas" if args.canvas_dir else "") / f"{args.skill}.img"
    if not path.exists():
        print(f"missing: {path}")
        return 1
    img = WzImage.from_bytes(path.read_bytes(), key=GMS)
    img.parse()
    print(f"{path}  size={path.stat().st_size}  warnings={getattr(img, 'parse_warnings', None)}")
    walk(img.root, 0, args.depth, args.grep.lower() if args.grep else None)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
