#!/usr/bin/env python3
"""紧凑导出 282 UI 分支：只打印定位需要的字段（画布尺寸/原点、vector、string）。

282 的 UI 图像把结构放在 `UI/<Win>.img`，像素放在 `UI/_Canvas/<Win>.img` 并通过
`_outlink` 指向。这里按客户端实际绘制用的数值输出（跟随 outlink 取真实宽高原点），
方便在 C# 里照抄坐标，不用一边翻 XML 一边猜。

用法：
  python3 tools/ms282/dump_window.py UI/UIArcaneForce.img/ArcEquip
  python3 tools/ms282/dump_window.py UI/UIWindow4.img/HyperStat/Window --canvases-only
"""
import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import (  # noqa: E402
    WzCanvasProperty, WzStringProperty, WzUolProperty, WzVectorProperty,
)

DEFAULT_SRC = Path.home() / "Documents/mxd/MapleStory/Data"


class Tree:
    def __init__(self, source: Path):
        self.source = source
        self.cache = {}

    def image(self, rel: str) -> WzImage:
        if rel not in self.cache:
            img = WzImage.from_bytes((self.source / rel).read_bytes(),
                                     key=WzKey.for_region("BMS"), name=rel)
            img.parse()
            self.cache[rel] = img
        return self.cache[rel]

    def resolve(self, node, depth: int = 0):
        """跟随 _outlink 取真实画布（像素在 _Canvas 里）。"""
        if depth > 8 or node is None:
            return node
        link = node.get("_outlink") if hasattr(node, "get") else None
        if link is None:
            return node
        target = str(link.value)
        split = target.find(".img/")
        if split < 0:
            return node
        rel, branch = target[:split + 4], target[split + 5:]
        root = self.image(rel).root
        found = root
        for part in branch.split("/"):
            found = found.get(part) if found is not None else None
        return self.resolve(found, depth + 1) if found is not None else node


def walk(node, prefix, tree, out, canvases_only):
    for child in node.children():
        path = f"{prefix}/{child.name}" if prefix else child.name
        if isinstance(child, WzCanvasProperty):
            real = tree.resolve(child)
            w = getattr(real, "width", 0)
            h = getattr(real, "height", 0)
            origin = child.get("origin")
            ox, oy = (origin.value if isinstance(origin, WzVectorProperty) else (0, 0))
            z = child.get("z")
            out.append(f"C\t{path}\t{w}x{h}\t{ox},{oy}\tz={z.value if z is not None else 0}")
        elif isinstance(child, WzVectorProperty):
            out.append(f"V\t{path}\t{child.value[0]},{child.value[1]}\t\t")
        elif isinstance(child, WzStringProperty):
            out.append(f"S\t{path}\t{child.value}\t\t")
        elif child.children():
            walk(child, path, tree, out, canvases_only)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("branch", help="UI/<Win>.img/<Node>")
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--canvases-only", action="store_true")
    args = ap.parse_args()

    split = args.branch.find(".img/")
    if split < 0:
        raise SystemExit("branch must look like UI/<Win>.img/<Node>")
    rel, branch = args.branch[:split + 4], args.branch[split + 5:]
    tree = Tree(args.src.resolve())
    node = tree.image(rel).root
    for part in branch.split("/"):
        node = node.get(part)
        if node is None:
            raise SystemExit(f"missing node {part} in {rel}/{branch}")
    out = []
    walk(node, "", tree, out, args.canvases_only)
    if args.canvases_only:
        out = [line for line in out if line.startswith("C\t")]
    print(f"# {args.branch}  ({len(out)} entries)")
    for line in out:
        print(line)


if __name__ == "__main__":
    main()
