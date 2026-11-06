#!/usr/bin/env python3
"""技能特效离线合成预览（只读数据，不出包）。

复刻 `NativeSkillVfx.Key/Read` + `SkillVisuals.CastVisuals` 的选层规则，
把一个技能在当前运行时**会看到什么**直接合成成蒙太奇，用来判断「特效对不对」，
而不是靠猜代码。

    python3 tools/ms282/preview_skill_vfx.py 400011003
    python3 tools/ms282/preview_skill_vfx.py 400011003 --roots special,special0
    python3 tools/ms282/preview_skill_vfx.py 400011131 --times 0,0.3,0.6,1.0
"""
from __future__ import annotations

import argparse
import os
import sys

sys.path.insert(0, os.path.expanduser(
    "~/Documents/mxd/BeiDou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402
from PIL import Image  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(os.path.dirname(ROOT), "MapleDashResources", "Data")

SKIP = ("level", "icon", "action", "common", "info", "info2")
CANVAS = "Skill/_Canvas"


def kids(node):
    try:
        return list(node.children())
    except TypeError:
        return []


def get(node, name):
    for child in kids(node):
        if child.name == name:
            return child
    return None


def scalar(node, name, fallback=None):
    prop = get(node, name)
    return getattr(prop, "value", fallback) if prop is not None else fallback


def origin(node):
    prop = get(node, "origin")
    if prop is None or not hasattr(prop, "x"):
        return (0, 0)
    return (prop.x, prop.y)


_cache = {}


def open_img(rel):
    path = os.path.join(DATA, rel)
    if path not in _cache:
        try:
            _cache[path] = WzImage.from_file(path, key=WzKey.for_region("GMS"))
        except Exception as exc:  # noqa: BLE001
            _cache[path] = exc
    return _cache[path]


def node_at(rel, inner):
    img = open_img(rel)
    if isinstance(img, Exception):
        return None
    node = img.root
    for part in inner.split("/"):
        node = get(node, part)
        if node is None:
            return None
    return node


def pixels(frame):
    """占位画布 → outlink 目标画布 → (rgba, w, h)。"""
    link = scalar(frame, "_outlink")
    if link:
        split = link.find(".img/")
        if split > 0:
            target = node_at(link[:split + 4], link[split + 5:])
            if isinstance(target, WzCanvasProperty) and target.width > 1:
                return decode_canvas(target)
    if isinstance(frame, WzCanvasProperty) and frame.width > 1:
        return decode_canvas(frame)
    return None


def frames_of(node, prefix=""):
    """返回 [(名字, 画布, origin, delay_ms)]，数字帧按序号排序。"""
    out = []
    for child in kids(node):
        if isinstance(child, WzCanvasProperty):
            out.append((prefix + child.name, child))
        elif isinstance(child, WzSubProperty) and prefix == "":
            out.extend(frames_of(child, child.name + "/"))
    def key(item):
        tail = item[0].split("/")[-1]
        return int(tail) if tail.isdigit() else 1 << 30
    out.sort(key=key)
    return out


def layer(node, name):
    """一层（可能带 front/back 子组）→ 动画帧序列。"""
    frames = []
    for frame_name, canvas in frames_of(node):
        frames.append({
            "name": frame_name,
            "canvas": canvas,
            "origin": origin(canvas),
            "delay": abs(int(scalar(canvas, "delay", 100) or 100)) / 1000.0,
            "z": int(scalar(canvas, "z", 0) or 0),
        })
    return {"name": name, "frames": frames, "meta": {
        "z": int(scalar(node, "z", 0) or 0),
        "repeat": int(scalar(node, "repeat", 0) or 0),
    }}


def collect(skill_id):
    rel = "Skill/%03d.img" % (skill_id // 10000)
    node = node_at(rel, "skill/%07d" % skill_id)
    if node is None:
        raise SystemExit("missing skill node %d" % skill_id)
    layers = []
    for child in kids(node):
        if not isinstance(child, WzSubProperty):
            continue
        name = child.name
        if name.startswith(SKIP) or name in ("number", "number2"):
            continue
        entry = layer(child, name)
        if entry["frames"] and any(pixels(f["canvas"]) for f in entry["frames"]):
            layers.append(entry)
    return layers


def composite(layers, width, height, anchor, at, center):
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for entry in layers:
        elapsed = 0.0
        for frame in entry["frames"]:
            start = elapsed
            elapsed += max(1e-3, frame["delay"])
            if not (start <= at < elapsed):
                continue
            data = pixels(frame["canvas"])
            if data is None:
                break
            if isinstance(data, Image.Image):
                sprite = data.convert("RGBA")
            else:
                rgba, w, h = data
                sprite = Image.frombytes("RGBA", (w, h), rgba)
            ox, oy = frame["origin"]
            x = int(center[0] + anchor[0] - ox)
            y = int(center[1] + anchor[1] - oy)
            canvas.paste(sprite, (x, y), sprite)
            break
    return canvas


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("skill", type=int)
    ap.add_argument("--roots", default="", help="只合成这些顶层分支（逗号分隔）")
    ap.add_argument("--times", default="0,0.3,0.6,0.9,1.2,1.6,2.0,2.5")
    ap.add_argument("--out", default="")
    args = ap.parse_args(argv)

    layers = collect(args.skill)
    wanted = [r for r in args.roots.split(",") if r]
    if wanted:
        layers = [l for l in layers if l["name"] in wanted]
    if not layers:
        raise SystemExit("没有任何可视层")

    width, height = 900, 700
    center = (width // 2, height - 160)          # 角色脚底
    times = [float(t) for t in args.times.split(",")]
    tiles = []
    for at in times:
        tile = Image.new("RGBA", (width, height), (24, 26, 32, 255))
        tile.alpha_composite(composite(layers, width, height, (0, 0), at, center))
        tiles.append(tile)
    cols = 4
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (width * cols, (height + 26) * rows), (18, 20, 24))
    for index, tile in enumerate(tiles):
        x, y = (index % cols) * width, (index // cols) * (height + 26)
        sheet.paste(tile.convert("RGB"), (x, y + 26))
    out = args.out or "/tmp/fx/skill_%d_%s.png" % (
        args.skill, "-".join(l["name"] for l in layers)[:40].replace("/", "_"))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out)
    print("layers:", ", ".join("%s(%d帧)" % (l["name"], len(l["frames"])) for l in layers))
    print("times :", ", ".join("%.1fs" % t for t in times))
    print("out   :", out)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
