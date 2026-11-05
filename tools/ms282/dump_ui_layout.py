#!/usr/bin/env python3
"""Flatten a TMS282 UI IMG branch into layout rows (path / type / size / origin / value).

282 UI IMGs keep structure in `UI/<Window>.img` and pixels in
`UI/_Canvas/<Window>.img/<same path>`; every canvas carries `_outlink`. This tool
follows the link and reports the **drawing** size/origin, so the numbers can be used
as layout parameters. Sub-properties that themselves carry `_outlink` are followed too
(this is the case dump_ui_nodes.py could not describe).

Usage:
  python3 tools/ms282/dump_ui_layout.py UI/VMatrixUI.img/main
  python3 tools/ms282/dump_ui_layout.py UI/HexaMatrixUI.img/hexaSkill --tsv out.tsv
"""
import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402

DEFAULT_DATA = Path.home() / "Documents/mxd/MapleStory/Data"


class Tree:
    def __init__(self, data: Path):
        self.data = data
        self.images = {}

    def image(self, rel):
        if rel not in self.images:
            img = WzImage.from_file(str(self.data / rel), key=WzKey.for_region("BMS"))
            img.parse()
            if img.truncated or img.parse_warnings:
                raise ValueError(f"incomplete {rel}: {img.parse_warnings}")
            self.images[rel] = img
        return self.images[rel]

    def node(self, rel, branch):
        node = self.image(rel).root
        for part in [p for p in branch.split("/") if p]:
            node = node.get(part)
            if node is None:
                raise KeyError(f"{rel}/{branch}")
        return node

    def resolve(self, rel, branch):
        """Follow _outlink/_inlink chains; returns (rel, branch, node)."""
        seen = set()
        while True:
            node = self.node(rel, branch)
            if not hasattr(node, "get"):
                return rel, branch, node
            link = node.get("_outlink")
            if link is not None:
                target_rel, _, rest = link.value.partition(".img/")
                target = (target_rel + ".img", rest.strip("/"))
            else:
                inline = node.get("_inlink")
                if inline is None:
                    return rel, branch, node
                parent = branch.rsplit("/", 1)[0] if "/" in branch else ""
                target = (rel, (parent + "/" if parent else "") + inline.value)
            if target in seen:
                return rel, branch, node
            seen.add(target)
            rel, branch = target


def origin_xy(node):
    origin = node.get("origin") if hasattr(node, "get") else None
    if origin is None:
        return None
    value = getattr(origin, "value", None)
    if isinstance(value, (tuple, list)) and len(value) >= 2:
        return int(value[0]), int(value[1])
    x, y = origin.get("x"), origin.get("y")
    return (x.value if x is not None else 0), (y.value if y is not None else 0)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("target", help="IMG path + optional /branch, e.g. UI/VMatrixUI.img/main")
    ap.add_argument("--source", type=Path, default=DEFAULT_DATA)
    ap.add_argument("--tsv", default="", help="write rows as TSV")
    ap.add_argument("--max-depth", type=int, default=0)
    args = ap.parse_args()

    rel, _, branch = args.target.partition(".img/")
    rel += ".img"
    tree = Tree(args.source)
    root = tree.node(rel, branch)
    rows = []

    def walk(node, path, depth):
        t_rel, t_branch, real = tree.resolve(rel, (branch + "/" + path).strip("/"))
        desc = {"path": path or "/", "type": type(real).__name__}
        if isinstance(real, WzCanvasProperty):
            desc["w"], desc["h"] = real.width, real.height
            o = origin_xy(real) or origin_xy(node)
            if o:
                desc["ox"], desc["oy"] = o
            z = real.get("z") or node.get("z")
            if z is not None:
                desc["z"] = z.value
            if (t_rel, t_branch) != (rel, (branch + "/" + path).strip("/")):
                desc["src"] = f"{t_rel}/{t_branch}"
        elif isinstance(real, WzUolProperty):
            desc["uol"] = real.value
        else:
            value = getattr(real, "value", None)
            if isinstance(value, tuple):
                desc["x"], desc["y"] = int(value[0]), int(value[1])
                desc["type"] = "Vector"
            elif value is not None:
                desc["value"] = value
        rows.append(desc)
        if args.max_depth and depth >= args.max_depth:
            return
        for child in node.children():
            walk(child, (path + "/" + child.name).lstrip("/"), depth + 1)

    walk(root, "", 0)
    cols = ("path", "type", "w", "h", "ox", "oy", "z", "x", "y", "value", "uol", "src")
    if args.tsv:
        Path(args.tsv).write_text(
            "\t".join(cols) + "\n" + "".join(
                "\t".join("" if r.get(c) is None else str(r.get(c)) for c in cols) + "\n" for r in rows),
            encoding="utf-8")
        print(f"{len(rows)} rows -> {args.tsv}")
    for r in rows:
        extra = " ".join(f"{k}={v}" for k, v in r.items() if k not in ("path", "type"))
        print(f"{r['path'][:78]:<78} {r['type']:<18} {extra}")
    print(f"# {len(rows)} nodes")


if __name__ == "__main__":
    main()
