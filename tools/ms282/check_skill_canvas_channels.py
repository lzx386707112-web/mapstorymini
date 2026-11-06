#!/usr/bin/env python3
"""守卫：项目 Skill/_Canvas 的 ARGB4444 画布颜色必须与 282 源端一致。

2026-11-06 事故：img_bake.decode_rgba 用 texture2ddecoder 解 BC3/BC7，
库输出 BGRA 却按 RGBA 压进 4444，运行时 ImgFile.cs.Rgba() 读出来整体红蓝互换
（金色圣光变青蓝十字架）。当时旧校验拿同一个错误解码器比两边，色差 1.7/255
看不出问题，所以这里的 ground truth **不用** img_bake.decode_rgba：
  * DXT5(2050)/DXT3(1026)/原生格式 -> wzpy.decode_canvas（独立实现）
  * BC7(4098)                      -> texture2ddecoder 原始输出 + 显式 R/B 交换
项目侧则严格按 ImgFile.cs 的 4444 公式解，正是玩家看到的像素。

用法：
  python3 tools/ms282/check_skill_canvas_channels.py                # 每分片抽样
  python3 tools/ms282/check_skill_canvas_channels.py --all           # 全量
  python3 tools/ms282/check_skill_canvas_channels.py --skill 400011072
"""
from __future__ import annotations

import argparse
import os
import sys
import zlib
from collections import defaultdict
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from img_bake import _read_canvas_bytes  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

REPO = HERE.parents[2]
CANVAS = REPO / "MapleDashResources/Data/Skill/_Canvas"
SRC = Path(os.path.expanduser("~/Documents/mxd/MapleStory/Data/Skill/_Canvas"))
GMS, BMS = WzKey.for_region("GMS"), WzKey.for_region("BMS")
BLOCK_FORMATS = (2050, 4098)
THRESHOLD = 8.0  # 4444 量化误差通常 1~3/255；R/B 互换会到 30+


def kids(n):
    return list(n.children())


def get(n, k):
    if n is None:
        return None
    for c in kids(n):
        if c.name == k:
            return c
    return None


def nav(n, p):
    for x in p.split("/"):
        if not x:
            continue
        n = get(n, x)
        if n is None:
            return None
    return n


def canvases(node, prefix, out):
    for c in kids(node):
        p = prefix + "/" + c.name
        if isinstance(c, WzCanvasProperty):
            out.append((p, c))
        elif isinstance(c, WzSubProperty):
            canvases(c, p, out)
    return out


def source_rgba(node, img):
    fmt = node.format + node.format2
    if fmt in (1, 2, 513, 1026, 2050):
        return np.array(decode_canvas(node).convert("RGBA"))
    if fmt == 4098:
        import texture2ddecoder as t2d
        with img.wz_file.reader_lock:
            payload = _read_canvas_bytes(node)
        raw = zlib.decompressobj(-15).decompress(payload[2:])
        data = t2d.decode_bc7(raw, node.width, node.height)
        arr = np.frombuffer(data, np.uint8).reshape(node.height, node.width, 4)
        return arr[:, :, [2, 1, 0, 3]].copy()   # texture2ddecoder 输出 BGRA
    return None


def project_rgba(node, img):
    """ImgFile.cs Rgba() 的 format=1 口径（玩家实际看到的像素）。"""
    if node.format + node.format2 != 1:
        return None
    with img.wz_file.reader_lock:
        payload = _read_canvas_bytes(node)
    if len(payload) < 3 or payload[0] != 0x78 or (payload[0] << 8 | payload[1]) % 31 != 0:
        return None
    raw = zlib.decompressobj(-15).decompress(payload[2:])
    count = node.width * node.height
    if len(raw) < count * 2:
        return None
    pair = np.frombuffer(raw[: count * 2], np.uint8).reshape(count, 2)
    lo, hi = pair[:, 0], pair[:, 1]
    out = np.stack([(hi & 15) * 17, (lo >> 4) * 17,
                    (lo & 15) * 17, (hi >> 4) * 17], axis=-1).astype(np.uint8)
    return out.reshape(node.height, node.width, 4)


def project_file(shard, sid):
    for p in (CANVAS / shard / (sid + ".img"), CANVAS / (shard + ".img")):
        if p.is_file():
            return p
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--shard", help="只查某个分片，如 40001")
    ap.add_argument("--skill", help="只查这些技能 id，逗号分隔（需配合 --shard）")
    ap.add_argument("--all", action="store_true", help="全量，不抽样")
    ap.add_argument("--per-shard", type=int, default=12, help="每分片抽样张数")
    args = ap.parse_args()

    if args.shard:
        packs = [SRC / (args.shard + ".img")]
    else:
        shards = {e.stem for e in CANVAS.iterdir()}
        packs = sorted(SRC / (s + ".img") for s in shards if (SRC / (s + ".img")).is_file())

    skill_filter = set((args.skill or "").split(",")) - {""}
    pcache = {}
    checked = passed = failed = skipped = 0
    worst = []
    for pi, pack in enumerate(packs, 1):
        if not pack.is_file():
            continue
        src = WzImage.from_file(str(pack), key=BMS)
        root = get(src.root, "skill")
        if root is None:
            continue
        shard = pack.stem
        picked = 0
        for sid_node in kids(root):
            if not isinstance(sid_node, WzSubProperty):
                continue
            sid = sid_node.name
            if skill_filter and sid not in skill_filter:
                continue
            pf = project_file(shard, sid)
            if pf is None:
                continue
            for path, c in canvases(sid_node, "", []):
                if c.format + c.format2 not in BLOCK_FORMATS or c._png_length <= 0:
                    continue
                if not args.all and not args.skill and picked >= args.per_shard:
                    break
                picked += 1
                inner = "skill/" + sid + path
                if pf not in pcache:
                    pcache[pf] = WzImage.from_file(str(pf), key=GMS)
                pimg = pcache[pf]
                pn = nav(pimg.root, inner)
                if not isinstance(pn, WzCanvasProperty):
                    skipped += 1
                    continue
                try:
                    a, b = source_rgba(c, src), project_rgba(pn, pimg)
                    if a is None or b is None or a.shape != b.shape:
                        skipped += 1
                        continue
                    m = (a[:, :, 3] > 40) | (b[:, :, 3] > 40)
                    if m.sum() < 30:
                        skipped += 1
                        continue
                    d = float(np.abs(a[:, :, :3].astype(int) - b[:, :, :3].astype(int))[m].mean())
                    checked += 1
                    if d <= THRESHOLD:
                        passed += 1
                    else:
                        failed += 1
                        worst.append((d, shard + ":" + inner))
                except Exception:  # noqa: BLE001
                    skipped += 1
            if picked >= args.per_shard and not args.all and not args.skill:
                break
        if pi % 25 == 0:
            print("  %d/%d 分片，已查 %d 张，失败 %d" % (pi, len(packs), checked, failed), flush=True)

    worst.sort(reverse=True)
    print("\n通道守卫：抽查 %d 张，通过 %d，失败 %d，跳过 %d（阈值 %.1f/255）" %
          (checked, passed, failed, skipped, THRESHOLD))
    if worst:
        print("最大偏差:")
        for d, p in worst[:12]:
            print("  %6.1f  %s" % (d, p))
    print("结论:", "通过" if failed == 0 and checked > 0 else "失败")
    return 0 if failed == 0 and checked > 0 else 1


if __name__ == "__main__":
    sys.exit(main())
