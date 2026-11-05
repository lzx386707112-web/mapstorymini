"""把 Skill 定义层残留的 BC/DXT 画布烘焙成 ARGB4444（format 1）。

为什么单独一个工具
------------------
bake_skill_canvas.py 只管 Skill/_Canvas 的特效分片。定义层 Skill/*.img
（135 个）里还留着 31 张 282 客户端的块压缩画布：29 张 4x4 占位、
40000.img 的 136x420 BC7、424.img 的 772x404 BC3。这些同样会被
ImgFile.cs 的 Rgba() 拒绝（只认 1 / 2 / 513）。

口径边界
--------
* 只处理 BC/DXT 类格式（2050 / 4098 / 1026 等）。
* 保留 v83 原生的 ARGB8888（format 2）——它不是 BC/DXT，运行时支持，
  强转 4444 只会白白损失图标画质。
* _Canvas 已由 bake_skill_canvas.py 全量转成 format 1，本工具不碰。

安全性
------
* 默认 dry-run，只读统计，不写任何文件。
* --apply 之前逐文件做三重校验：任意一张解码失败就整文件跳过；
  序列化后重新解析，节点类型/名称/画布几何必须与转换前完全一致；
  RuntimeImgMirror（ImgFile.cs 逐字节镜像）必须能解出全部画布 RGBA，
  且序列化结果里不得残留任何 BC/DXT 格式。
* 强制备份原文件，写回用临时文件 + os.replace 原子替换。
* 写回后同步 NativeAssetIndex.json 的 sha256（打包脚本会逐文件校验哈希）。

用法（cd MapleDash）
--------------------
    python3 tools/ms282/bake_skill_defs.py                 # dry-run 盘点
    python3 tools/ms282/bake_skill_defs.py --apply        # 写回
"""

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
SKILL = DATA / "Skill"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
GMS = WzKey.for_region("GMS")

# 2050=BC3/DXT5, 4098=BC7, 1026=BC2/DXT3，以及其它可能的块压缩变体
BLOCK_FORMATS = (2050, 4098, 1026, 257, 513, 517, 3)


def parse_img(path: Path) -> WzImage:
    img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=path.name)
    img.parse()
    return img


def canvases(img: WzImage) -> list[WzCanvasProperty]:
    out, stack = [], [img.root]
    while stack:
        node = stack.pop()
        if isinstance(node, WzCanvasProperty):
            out.append(node)
        stack.extend(node.children())
    return out


def geometry(img: WzImage) -> tuple:
    """序列化往返对照：节点类型、名称多重集、画布几何（不含 format/载荷）。"""
    kinds, names, shapes, links = {}, {}, [], 0
    stack = [img.root]
    while stack:
        node = stack.pop()
        k = type(node).__name__
        kinds[k] = kinds.get(k, 0) + 1
        nm = node.name
        names[nm] = names.get(nm, 0) + 1
        if isinstance(node, WzCanvasProperty):
            shapes.append((nm, node.width, node.height))
        if node.get("_outlink") is not None:
            links += 1
        stack.extend(node.children())
    return (kinds, names, shapes, links)


def mirror_check(blob: bytes) -> tuple[int, int, int]:
    """返回 (画布数, 能解出 RGBA 数, 残留 BC/DXT 数)；后两者必须为 0 / 全部。"""
    mirror = RuntimeImgMirror(blob, GMS)
    total = ok = block = 0
    stack = [mirror.root]
    while stack:
        node = stack.pop()
        if node.get("kind") == "Canvas":
            total += 1
            fmt = node.get("format")
            if fmt in BLOCK_FORMATS or (isinstance(fmt, int) and fmt > 2 and fmt != 513):
                block += 1
            try:
                rgba = mirror.rgba(node)
                if len(rgba) == node["width"] * node["height"] * 4:
                    ok += 1
            except Exception:
                pass
        stack.extend(node.get("children", []))
    return total, ok, block


def bake(path: Path, apply: bool, backup_root: Path | None) -> dict:
    before = path.stat().st_size
    img = parse_img(path)
    targets = [n for n in canvases(img)
               if n.format + n.format2 in BLOCK_FORMATS]
    stat = dict(path=path.relative_to(DATA).as_posix(), before=before,
                after=before, canvases=len(targets), converted=0,
                empty=0, skipped=0, ok=True)
    if not targets:
        return stat

    geom = geometry(img)
    for node in targets:
        if node._png_length == 0:
            node.format, node.format2 = 1, 0
            stat["empty"] += 1
            continue
        try:
            rgba = decode_rgba(node, img)
            blob = encode_argb4444(rgba)
        except Exception as exc:
            print(f"    ! {path.name} {node.name}: {str(exc)[:90]}", flush=True)
            stat["skipped"] += 1
            continue
        node.format, node.format2, node._png_data = 1, 0, blob
        stat["converted"] += 1

    # 严格模式：任何一张失败、或仍残留块压缩节点，就不写这个文件
    if stat["skipped"]:
        print(f"    ! {path.name}: {stat['skipped']} 张解码失败，整文件跳过", flush=True)
        stat["ok"] = False
        return stat
    leftovers = [n for n in canvases(img)
                 if n.format + n.format2 in BLOCK_FORMATS and n._png_length > 0]
    if leftovers:
        print(f"    ! {path.name}: 仍残留 {len(leftovers)} 张块压缩画布，跳过", flush=True)
        stat["ok"] = False
        return stat

    blob = serialize(img)
    reparsed = WzImage.from_bytes(blob, key=GMS, name=path.name)
    reparsed.parse()
    if geometry(reparsed) != geom:
        print(f"    ! {path.name}: 序列化往返结构不一致，跳过", flush=True)
        stat["ok"] = False
        return stat
    total, ok, block = mirror_check(blob)
    if ok != total or block:
        print(f"    ! {path.name}: 镜像校验 {ok}/{total}、残留块压缩 {block} 张，跳过",
              flush=True)
        stat["ok"] = False
        return stat

    stat["after"] = len(blob)
    stat["mirror"] = f"{ok}/{total}"
    if apply:
        if backup_root is not None:
            target = backup_root / path.relative_to(DATA)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(path.read_bytes())
        tmp = path.with_name(path.name + ".defs-tmp")
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
    ap.add_argument("--no-backup", action="store_true", help="跳过备份（不推荐）")
    args = ap.parse_args()

    files = sorted(SKILL.glob("*.img")) + sorted((SKILL / "MobSkill").rglob("*.img"))
    backup_root = None
    if args.apply and not args.no_backup:
        backup_root = (REPO / "MapleDashResources/.backup"
                       / ("defs4444-" + datetime.now().strftime("%Y%m%d-%H%M%S")))
        print(f"备份目录: {backup_root}", flush=True)

    t0 = time.time()
    targets = converted = empty = skipped = failures = 0
    before = after = 0
    changed: list[str] = []
    for path in files:
        stat = bake(path, args.apply, backup_root)
        targets += stat["canvases"]
        converted += stat["converted"]
        empty += stat["empty"]
        skipped += stat["skipped"]
        if not stat["ok"]:
            failures += 1
            continue
        if stat["converted"] or stat["empty"]:
            before += stat["before"]
            after += stat["after"]
            changed.append(stat["path"])
            print(f"  {stat['path']}: {stat['canvases']} 张 "
                  f"(解码重压 {stat['converted']} / 占位改标记 {stat['empty']}) "
                  f"镜像 {stat['mirror']}  {stat['before']/2**20:.2f} → "
                  f"{stat['after']/2**20:.2f} MiB", flush=True)

    print(f"\n{'已写回' if args.apply else 'DRY-RUN（未改动任何文件）'}")
    print(f"  定义层 {len(files)} 个文件，BC/DXT 画布 {targets} 张："
          f"转换 {converted} 张、占位改标记 {empty} 张、解码失败 {skipped} 张、"
          f"文件校验失败 {failures} 个")
    if changed:
        print(f"  受影响 {len(changed)} 个文件 {before/2**20:.1f} → {after/2**20:.1f} MiB")
    print(f"  耗时 {time.time()-t0:.1f}s")
    if args.apply and changed:
        update_index(changed)
        print(f"  已同步 {len(changed)} 条 sha256 到 {INDEX.name}")
    if not args.apply:
        print("  执行：python3 tools/ms282/bake_skill_defs.py --apply")


if __name__ == "__main__":
    main()
