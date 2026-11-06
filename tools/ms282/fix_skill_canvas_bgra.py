#!/usr/bin/env python3
"""回正 Skill/_Canvas 里由 BC3/BC7 烘焙成 ARGB4444 的画布通道顺序。

背景（2026-11-06 光之聖騎士 122 特效回归）：
  img_bake.decode_rgba 走 texture2ddecoder 解 2050(BC3) / 4098(BC7)，
  该库输出是 **BGRA**，但 encode_argb4444 与 ImgFile.cs.Rgba() 都按 RGBA
  落地。于是这批画布在游戏里整体红蓝互换：金色圣光变青蓝十字架。
  wzpy 自己的 decode_canvas 对 DXT5 返回正确 RGBA，导出基准图因此是对的，
  两边互证不出来（旧校验用同一个错误解码器比两边，色差只有 1.7/255）。

修法：对「源端整包里 format ∈ (2050,4098)」的画布，在项目已烘焙成
format=1 的对应节点上做 **无损 nibble 交换**：
    byte0=(G<<4)|B, byte1=(A<<4)|R  ->  byte0=(G<<4)|R, byte1=(A<<4)|B
不重新解码、不二次量化，原本正确的 4444 / 8888 / DXT3(1026) 一律不动。

用法：
  python3 tools/ms282/fix_skill_canvas_bgra.py --shard 40001            # dry-run
  python3 tools/ms282/fix_skill_canvas_bgra.py --shard 40001 --apply
  python3 tools/ms282/fix_skill_canvas_bgra.py --apply                    # 全量
之后需要重跑 pack_resources.sh 生成资源包。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time
import zlib
from collections import defaultdict
from datetime import datetime
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from bake_skill_canvas import (  # noqa: E402
    CANVAS, DATA, GMS, INDEX, mirror_check, parse_img, update_index,
)
from img_bake import _read_canvas_bytes, serialize  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

SRC = Path(os.path.expanduser("~/Documents/mxd/MapleStory/Data/Skill/_Canvas"))
BMS = WzKey.for_region("BMS")
BLOCK_FORMATS = (2050, 4098)
# 幂等：记录每个已回正文件的 sha256。nibble 交换不是对合以外的操作，重复执行
# 会把修好的画布再换回蓝的；文件 sha256 变了（重新烘焙/回滚）就重新判定。
STATE = HERE / ".bgra_fixed.json"


def load_state():
    if STATE.is_file():
        try:
            return json.loads(STATE.read_text(encoding="utf-8"))
        except Exception:  # noqa: BLE001
            pass
    return {}


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def kids(n):
    return list(n.children())


def get(n, k):
    if n is None:
        return None
    for c in kids(n):
        if c.name == k:
            return c
    return None


def nav(n, path):
    for part in path.split("/"):
        if not part:
            continue
        n = get(n, part)
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


def swap_rb(blob: bytes, width: int, height: int) -> bytes:
    """给单张 ARGB4444 载荷做 R/B nibble 交换，保持 zlib 封装不变。"""
    if len(blob) < 3 or blob[0] != 0x78 or ((blob[0] << 8) + blob[1]) % 31 != 0:
        raise ValueError("非标准 zlib 载荷")
    inflater = zlib.decompressobj(-15)
    raw = inflater.decompress(blob[2:]) + inflater.flush()
    count = width * height
    if len(raw) < count * 2:
        raise ValueError("像素长度不足 %d < %d" % (len(raw), count * 2))
    pair = np.frombuffer(raw[: count * 2], np.uint8).reshape(count, 2)
    lo, hi = pair[:, 0], pair[:, 1]
    b, g, r, a = lo & 15, lo >> 4, hi & 15, hi >> 4
    out = np.stack([((g << 4) | r).astype(np.uint8),
                    ((a << 4) | b).astype(np.uint8)], axis=-1)
    return zlib.compress(out.tobytes(), 6)


def project_file(shard: str, sid: str):
    split = CANVAS / shard / (sid + ".img")
    if split.is_file():
        return split
    whole = CANVAS / (shard + ".img")
    if whole.is_file():
        return whole
    return None


def source_targets(pack: Path):
    """源端整包 -> {项目文件: [内部路径...]}，只收 BC3/BC7 来源的画布。"""
    img = WzImage.from_file(str(pack), key=BMS)
    root = get(img.root, "skill")
    if root is None:
        return {}, 0
    shard = pack.stem
    mapping = defaultdict(list)
    total = 0
    for sid_node in kids(root):
        if not isinstance(sid_node, WzSubProperty):
            continue
        sid = sid_node.name
        pf = project_file(shard, sid)
        if pf is None:
            continue
        for path, c in canvases(sid_node, "", []):
            if c.format + c.format2 not in BLOCK_FORMATS or c._png_length <= 0:
                continue
            total += 1
            mapping[pf].append("skill/" + sid + path)
    return mapping, total


def format_check(blob: bytes):
    """只统计可解析的画布数与非 4444 数（不逐画布解码像素，快得多）。"""
    from img_bake import RuntimeImgMirror  # noqa: F401
    mirror = RuntimeImgMirror(blob, GMS)
    total = bad = 0
    stack = [mirror.root]
    while stack:
        node = stack.pop()
        if node.get("kind") == "Canvas":
            total += 1
            if node.get("format") != 1:
                bad += 1
        stack.extend(node.get("children", []))
    return total, bad


def fix_file(path: Path, inners: list[str], apply: bool, backup_root: Path | None):
    img = parse_img(path)
    found = swapped = 0
    errors = []
    for inner in inners:
        node = nav(img.root, inner)
        if not isinstance(node, WzCanvasProperty):
            errors.append("缺节点 " + inner)
            continue
        if node.format + node.format2 != 1 or node._png_length <= 0:
            errors.append("非 4444/无像素 " + inner)
            continue
        found += 1
        try:
            with img.wz_file.reader_lock:
                blob = _read_canvas_bytes(node)
            node._png_data = swap_rb(blob, node.width, node.height)
            swapped += 1
        except Exception as exc:  # noqa: BLE001
            errors.append("%s: %s" % (inner, exc))
    if errors or not swapped:
        return dict(file=path, found=found, swapped=0, ok=False, errors=errors)
    blob = serialize(img)
    total, bad = format_check(blob)
    if bad or total == 0:
        return dict(file=path, found=found, swapped=0, ok=False,
                    errors=["格式校验 画布 %d、非4444 %d" % (total, bad)])
    if apply:
        if backup_root is not None:
            target = backup_root / path.relative_to(DATA)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(path.read_bytes())
        tmp = path.with_name(path.name + ".bgra-tmp")
        tmp.write_bytes(blob)
        os.replace(tmp, path)
    return dict(file=path, found=found, swapped=swapped, ok=True, errors=[])


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apply", action="store_true", help="真正写回（默认只统计）")
    ap.add_argument("--shard", help="只处理某个源端分片，如 40001")
    ap.add_argument("--no-backup", action="store_true")
    args = ap.parse_args()

    if args.shard:
        packs = [SRC / (args.shard + ".img")]
    else:
        # 项目里不存在的分片不用打开源端整包（380 个里通常只有 ~100 个已导入）。
        shards = set()
        for entry in CANVAS.iterdir():
            shards.add(entry.stem)
        packs = sorted(SRC / (s + ".img") for s in shards if (SRC / (s + ".img")).is_file())
    if args.shard and not packs[0].is_file():
        raise SystemExit("源端整包不存在: " + str(packs[0]))
    print("源端整包 %d 个待处理" % len(packs), flush=True)

    backup_root = None
    if args.apply and not args.no_backup:
        backup_root = (DATA.parent / ".backup" /
                       ("canvas-bgra-" + datetime.now().strftime("%Y%m%d-%H%M%S")))
        print("备份目录:", backup_root, flush=True)

    t0 = time.time()
    state = load_state()
    grand = fixed = files_ok = skipped = 0
    changed = []
    new_state = dict(state)
    for i, pack in enumerate(packs, 1):
        try:
            mapping, total = source_targets(pack)
        except Exception as exc:  # noqa: BLE001
            print("  ! %s 源端解析失败 %s" % (pack.name, exc), flush=True)
            continue
        grand += total
        for pf, inners in mapping.items():
            rel = "Skill/_Canvas/" + pf.relative_to(CANVAS).as_posix()
            if state.get(rel) == digest(pf):
                skipped += len(inners)
                continue
            stat = fix_file(pf, inners, args.apply, backup_root)
            if stat["ok"]:
                fixed += stat["swapped"]
                files_ok += 1
                changed.append(rel)
                new_state[rel] = digest(pf)
            elif stat["errors"]:
                print("  ! %s %s" % (pf.name, stat["errors"][:2]), flush=True)
        if i % 20 == 0 or i == len(packs):
            print("  %d/%d 整包  %.0fs  待修 %d 张 / 已修 %d 张 / 跳过已修 %d 张 / 文件 %d" %
                  (i, len(packs), time.time() - t0, grand, fixed, skipped, files_ok), flush=True)

    print("\n%s" % ("已写回" if args.apply else "DRY-RUN（未改动任何文件）"))
    print("  源端 BC3/BC7 画布 %d 张，命中项目节点并回正 %d 张，涉及文件 %d 个，"
          "跳过已回正 %d 张" % (grand, fixed, files_ok, skipped))
    if args.apply and changed:
        update_index(changed)
        STATE.write_text(json.dumps(new_state, ensure_ascii=False, indent=1), encoding="utf-8")
        print("  已同步 %d 条 sha256 到 NativeAssetIndex.json 与幂等状态文件" % len(changed))
        print("  接下来需重跑 pack_resources.sh 生成资源包")
    print("  耗时 %.1fs" % (time.time() - t0))


if __name__ == "__main__":
    main()
