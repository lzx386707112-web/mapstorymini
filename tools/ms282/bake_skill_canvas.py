#!/usr/bin/env python3
"""把 Skill/_Canvas 里所有非 ARGB4444 的画布统一烘焙成 format 1。

为什么要做
----------
ImgFile.cs 的 Rgba() 只认 format 1(ARGB4444) / 2(ARGB8888) / 513(RGB565)；
282 客户端的 2050=BC3(DXT5)、4098=BC7、1026=BC2(DXT3) 在 Android 上会抛
NotSupportedException。项目既定口径（import_advanced_skills.py /
img_bake.py）本来就是导入时统一转 4444，但现存 2003 个 _Canvas 分片是
原样落盘的，漏了这一步。

本脚本把 _Canvas 内**所有**非 1 的格式（含 122 张 ARGB8888、159 张 DXT3）
一律转成 4444，跑完后目录内不应再出现任何 BC/DXT 编码。解码统一走
img_bake.decode_rgba：BC3/BC7 用 texture2ddecoder，DXT3 用 wzpy 自带的
_decode_dxt3（texture2ddecoder 没有 decode_bc2）。转换一举两得：运行时
能解 + 载荷变小（抽样省约 31~39%），4444 的 4bit 通道色带与 v83 原生
素材一致（MAE 2.5~3.6 / 255）。

安全性
------
* 默认 dry-run，只读统计，不写任何文件。
* 任一画布解码失败 → 整个文件不写回，磁盘上不会残留半转换的 BC/DXT。
* --apply 每个文件都会先用 img_bake.RuntimeImgMirror（ImgFile.cs 的逐字节
  镜像）把所有画布解一遍 RGBA，校验失败就跳过该文件。
* MapleDashResources/ 不在 git 仓库里（仓库是 MapleDash/.git），所以
  --apply 默认强制备份原文件，只有显式 --no-backup 才跳过。
* 写回后同步 NativeAssetIndex.json 的 sha256 —— build_resource_pack.py
  打包时会逐文件校验哈希，不同步会直接打包失败。

用法
----
    python3 tools/ms282/bake_skill_canvas.py                     # 只统计
    python3 tools/ms282/bake_skill_canvas.py --sample 8          # 统计 + 实测抽样
    python3 tools/ms282/bake_skill_canvas.py --apply --job 40004  # 先试点一个职业组
    python3 tools/ms282/bake_skill_canvas.py --apply --limit 50   # 分批
    python3 tools/ms282/bake_skill_canvas.py --apply              # 全量
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]                       # MapleDash/
REPO = ROOT.parent                           # mxd-android/
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from img_bake import (                        # noqa: E402
    RuntimeImgMirror, decode_rgba, encode_argb4444, serialize,
)
from wzpy import WzImage, WzKey               # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

DATA = REPO / "MapleDashResources/Data"
CANVAS = DATA / "Skill/_Canvas"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
GMS = WzKey.for_region("GMS")


def parse_img(path: Path) -> WzImage:
    img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=path.name)
    img.parse()
    return img


def target_canvases(img: WzImage) -> list[WzCanvasProperty]:
    """所有非 4444 的、有像素载荷、且不靠 _outlink 取像素的画布。"""
    out, stack = [], [img.root]
    while stack:
        node = stack.pop()
        if isinstance(node, WzCanvasProperty) and node._png_length > 0:
            if node.format + node.format2 != 1 and node.get("_outlink") is None:
                out.append(node)
        stack.extend(node.children())
    return out


def mirror_check(blob: bytes) -> tuple[int, int, int]:
    """用 ImgFile.cs 的镜像逐画布解码。

    返回 (画布数, 成功解出 RGBA 数, 非 4444 格式数)；非 4444 数必须为 0。
    """
    mirror = RuntimeImgMirror(blob, GMS)
    total = ok = bad_fmt = 0
    stack = [mirror.root]
    while stack:
        node = stack.pop()
        if node.get("kind") == "Canvas":
            total += 1
            if node.get("format") != 1:
                bad_fmt += 1
            try:
                rgba = mirror.rgba(node)
                if len(rgba) == node["width"] * node["height"] * 4:
                    ok += 1
            except Exception:
                pass
        stack.extend(node.get("children", []))
    return total, ok, bad_fmt


def bake(path: Path, apply: bool, backup_root: Path | None,
         measure_only: bool = False) -> dict:
    before = path.stat().st_size
    img = parse_img(path)
    nodes = target_canvases(img)
    stat = dict(path=path.relative_to(CANVAS).as_posix(), before=before,
                after=before, canvases=len(nodes), converted=0, skipped=0, ok=True)
    # 全量 dry-run 只数画布，不做解码/序列化；否则 2000 个文件要跑几个小时。
    if measure_only or not nodes:
        return stat

    payload_before = sum(n._png_length for n in nodes)
    payload_after = 0
    for node in nodes:
        try:
            rgba = decode_rgba(node, img)
            blob = encode_argb4444(rgba)
        except Exception as exc:
            print(f"    ! {path.name} {node.name}: {str(exc)[:80]}", flush=True)
            stat["skipped"] += 1
            continue
        node.format, node.format2, node._png_data = 1, 0, blob
        payload_after += len(blob)
        stat["converted"] += 1

    # 严格模式：有一张解不出就不写整个文件，磁盘上绝不残留半转换的 BC/DXT。
    if stat["skipped"]:
        print(f"    ! {path.relative_to(CANVAS)}: {stat['skipped']} 张解码失败，整文件跳过", flush=True)
        stat["ok"] = False
        return stat
    if not stat["converted"]:
        return stat

    # 空像素的占位节点只需把 format 标记改成 1；仍带像素的非 1 节点一律视为失败。
    leftovers, stack = [], [img.root]
    while stack:
        node = stack.pop()
        if isinstance(node, WzCanvasProperty) and node.format + node.format2 != 1:
            if node._png_length == 0:
                node.format, node.format2 = 1, 0
            else:
                leftovers.append(node)
        stack.extend(node.children())
    if leftovers:
        print(f"    ! {path.relative_to(CANVAS)}: 仍有 {len(leftovers)} 张非 4444 画布，整文件跳过", flush=True)
        stat["ok"] = False
        return stat

    blob = serialize(img)
    total, ok, bad_fmt = mirror_check(blob)
    if ok != total or bad_fmt:
        print(f"    ! {path.relative_to(CANVAS)}: 镜像校验 {ok}/{total}、非4444 {bad_fmt} 张，跳过", flush=True)
        stat["ok"] = False
        return stat
    stat["after"] = len(blob)
    stat["mirror"] = f"{ok}/{total}"
    stat["payload"] = (payload_before, payload_after)
    stat["ok"] = True

    if apply:
        if backup_root is not None:
            target = backup_root / path.relative_to(DATA)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(path.read_bytes())
        tmp = path.with_name(path.name + ".bake-tmp")
        tmp.write_bytes(blob)
        os.replace(tmp, path)
    return stat


def update_index(changed: list[str]) -> None:
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    table = {e["path"]: e for e in index["files"]}
    for rel in changed:
        entry = table.get(rel)
        if entry is None:
            print(f"    ! 索引里没有 {rel}", flush=True)
            continue
        entry["sha256"] = hashlib.sha256((DATA / rel).read_bytes()).hexdigest()
    INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")),
                     encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apply", action="store_true", help="真正写回（默认只统计）")
    ap.add_argument("--job", help="只处理某个前缀，如 40004 / 50000")
    ap.add_argument("--limit", type=int, help="最多处理多少个文件")
    ap.add_argument("--sample", type=int, default=0,
                    help="dry-run 时对前 N 个大文件做真实转换，测实际压缩率")
    ap.add_argument("--no-backup", action="store_true", help="跳过备份（不推荐）")
    ap.add_argument("--sync-index", action="store_true",
                    help="不转换，只按磁盘重算 Skill/** 的 sha256（修复中断导致的索引不一致）")
    args = ap.parse_args()

    if args.sync_index:
        index = json.loads(INDEX.read_text(encoding="utf-8"))
        table = {e["path"]: e for e in index["files"]}
        fixed = missing = 0
        for rel, entry in table.items():
            if not rel.startswith("Skill/"):
                continue
            path = DATA / rel
            if not path.is_file():
                missing += 1
                continue
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            if entry["sha256"] != digest:
                entry["sha256"] = digest
                fixed += 1
        INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")),
                         encoding="utf-8")
        print(f"索引同步完成：Skill/** 共 {sum(1 for r in table if r.startswith('Skill/'))} 条，"
              f"修正 {fixed} 条 sha256，磁盘缺失 {missing} 条")
        return

    files = sorted(CANVAS.rglob("*.img"))
    if args.job:
        files = [p for p in files if p.relative_to(CANVAS).parts[0] == args.job]
    # 先按体积排序可选样本；全量处理时保持路径序，便于分批续跑
    by_size = sorted(files, key=lambda p: -p.stat().st_size)
    if args.sample and not args.apply:
        files = by_size[:args.sample]
    if args.limit:
        files = files[:args.limit]

    backup_root = None
    if args.apply and not args.no_backup:
        backup_root = (REPO / "MapleDashResources/.backup"
                       / ("canvas4444-" + datetime.now().strftime("%Y%m%d-%H%M%S")))
        print(f"备份目录: {backup_root}", flush=True)

    t0 = time.time()
    measure_only = not args.apply and not args.sample
    total = converted = skipped = 0
    before = after = 0
    affected = affected_bytes = 0
    changed: list[str] = []
    failures = 0
    for i, path in enumerate(files, 1):
        stat = bake(path, args.apply, backup_root, measure_only)
        if stat["canvases"]:
            affected += 1
            affected_bytes += stat["before"]
        total += stat["canvases"]
        converted += stat["converted"]
        skipped += stat["skipped"]
        if stat["ok"] and stat["converted"]:
            before += stat["before"]
            after += stat["after"]
            changed.append("Skill/_Canvas/" + stat["path"])
        if not stat["ok"]:
            failures += 1
        if i % 25 == 0 or i == len(files):
            print(f"  {i}/{len(files)}  {time.time()-t0:.0f}s  已转换 {converted} 张  "
                  f"{before/2**20:.0f} → {after/2**20:.0f} MiB", flush=True)

    print(f"\n{'已写回' if args.apply else 'DRY-RUN（未改动任何文件）'}")
    print(f"  含非 4444 画布的文件 {affected} 个 / {affected_bytes/2**20:.1f} MiB")
    print(f"  文件 {len(files)} 个，非 4444 画布 {total} 张，转换 {converted} 张，"
          f"解码失败 {skipped} 张，文件校验失败 {failures} 个")
    if converted:
        print(f"  受影响文件 {before/2**20:.1f} MiB → {after/2**20:.1f} MiB "
              f"(省 {(1-after/before)*100:.1f}%, {(before-after)/2**20:.1f} MiB)")
    print(f"  耗时 {time.time()-t0:.1f}s")
    if args.apply and changed:
        update_index(changed)
        print(f"  已同步 {len(changed)} 条 sha256 到 {INDEX.name}；"
              f"接下来需重新跑 pack_resources.sh 生成资源包")
    if not args.apply:
        print("  全量执行：python3 tools/ms282/bake_skill_canvas.py --apply")


if __name__ == "__main__":
    main()
