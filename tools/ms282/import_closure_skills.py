#!/usr/bin/env python3
"""按闭包清单导入 V / HEXA 技能资源（方案 B：不碰四转树，只补闭包引用的节点）。

范围来源：`docs/tms282-skill/data/skill-closure.json` 的 `jobs.<四转ID>.closure` 并集。
闭包 = V 矩阵 29 个核心 + HEXA 13 个核心，连同它们 `skillList`/`psdSkill`/`addAttack`
等引用链递归展开的结果，所以「补闭包」等于「补齐 V/HEXA 面板能点到的一切技能」。

落盘形态与六转一致（对齐 282 的原生拆分）：

    定义  MapleDashResources/Data/Skill/<img>.img          ← 结构（图标是 1x1 桩 + `_outlink`）
    像素  MapleDashResources/Data/Skill/_Canvas/<img>.img  ← 真像素（块压缩重编码为 ARGB4444）

与六转导入的区别：这里是**按 id 剪枝合并**，不是整包覆盖。

  * 项目已有的四转定义 img（112/122/…/1512）保留原有全部节点，只追加缺失的闭包技能；
  * 项目没有的共享 img（40000/40001/…/50000）只保留闭包用到的 id —— 40000 有 754 个
    技能但闭包只要 206 个，50000 的 344 个里只要 105 个，剪枝能砍掉大半体积；
  * 像素文件同样按 id 剪枝，块压缩画布转 ARGB4444（Im.File 只认 1/2/513）。

用法：
    python3 tools/ms282/import_closure_skills.py --dry-run
    python3 tools/ms282/import_closure_skills.py --img 40000
    python3 tools/ms282/import_closure_skills.py
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]           # MapleDash/
REPO = ROOT.parent
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms  # noqa: E402
from img_bake import PixelSource, RuntimeImgMirror, serialize  # noqa: E402
from import_advanced_skills import convert_tree, load_gms  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzSubProperty, WzVideoProperty  # noqa: E402
from wzpy.writer import _read_opaque_payload  # noqa: E402

GMS = WzKey.for_region("GMS")
PACKS = Path("/tmp/packs")
DATA282 = Path("/Users/lizixian/Documents/mxd/MapleStory/Data")
PROJ_DATA = REPO / "MapleDashResources/Data"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
CLOSURE = REPO / "docs/tms282-skill/data/skill-closure.json"


def closure_plan() -> dict[int, set[int]]:
    """img 号 → 闭包需要的技能 id 集合。"""
    data = json.loads(CLOSURE.read_text(encoding="utf-8"))
    need: dict[int, set[int]] = defaultdict(set)
    for job in data["jobs"].values():
        for sid in job["closure"]:
            need[int(sid) // 10000].add(int(sid))
    return dict(need)


def video_inventory(img: WzImage) -> dict[str, tuple[int, int, str]]:
    """路径 → (videoType, 长度, sha256)。按节点自己的来源文件读载荷。

    跨 img 搬运的节点仍指向源 img 的 reader，不能用宿主的 reader 去读。
    """
    out: dict[str, tuple[int, int, str]] = {}

    def walk(node, path=""):
        for child in (node.children() or []):
            here = f"{path}/{child.name}"
            if isinstance(child, WzVideoProperty):
                blob = _read_opaque_payload(child)
                out[here] = (child.video_type, len(blob), hashlib.sha256(blob).hexdigest())
            walk(child, here)

    walk(img.root)
    return out


def skill_ids(img: WzImage) -> list[int]:
    node = img.root.get("skill")
    if node is None:
        return []
    return sorted(int(c.name) for c in node.children() if c.name.isdigit())


def prune(img: WzImage, keep: set[int]) -> int:
    """只留 `skill/<keep>`，返回删掉的节点数。"""
    node = img.root.get("skill")
    if node is None:
        raise SystemExit("img 没有 skill 节点")
    dropped = 0
    for child in list(node.children()):
        if child.name.isdigit() and int(child.name) not in keep:
            node._children.pop(child.name, None)
            dropped += 1
    return dropped


def merge_defs(img_id: int, ids: set[int], dry: bool) -> dict:
    """定义侧：已有文件追加缺失节点；新文件剪枝到闭包集合。"""
    src_path = PACKS / f"Skill_{img_id}.img"
    px_path = DATA282 / "Skill/_Canvas" / f"{img_id}.img"
    if not src_path.is_file():
        raise SystemExit(f"缺少 {src_path}（先用 MsUnpack 把 282 Packs/Skill_*.ms 解到 /tmp/packs）")
    if not px_path.is_file():
        raise SystemExit(f"缺少像素包 {px_path}")

    dst_path = PROJ_DATA / "Skill" / f"{img_id}.img"
    src = load_gms(src_path, f"Skill_{img_id}.img")

    if dst_path.is_file():
        dst = WzImage.from_bytes(dst_path.read_bytes(), key=GMS, name=f"Skill_{img_id}.img")
        dst.parse()
        have = set(skill_ids(dst))
        missing = sorted(ids - have)
        if missing:
            host = dst.root.get("skill")
            guest = src.root.get("skill")
            for sid in missing:
                node = guest.get(str(sid))
                if node is None:
                    raise SystemExit(f"{img_id}.img 源端没有技能 {sid}")
                host.add(node)
        added = len(missing)
        dropped = 0
    else:
        dst = src
        dropped = prune(dst, ids)
        added = len(skill_ids(dst))

    wanted = video_inventory(dst)
    defs_bytes = serialize(dst)
    RuntimeImgMirror(defs_bytes, GMS)

    # 视频载荷必须逐条一致：写回器不认识 Canvas#Video 时会被当成普通 Property，
    # 载荷静默清零（282 六转导入曾整批丢掉 268.6 MB）。
    written = WzImage.from_bytes(defs_bytes, key=GMS, name=f"Skill_{img_id}.img")
    written.parse()
    got = video_inventory(written)
    if got != wanted:
        diff = sorted(set(wanted) ^ set(got))[:4]
        raise SystemExit(f"{img_id}.img 视频校验失败：{diff}")

    if not dry:
        dst_path.parent.mkdir(parents=True, exist_ok=True)
        dst_path.write_bytes(defs_bytes)

    return {
        "img": img_id, "new_file": dropped > 0 or not dst_path.is_file(),
        "defs": len(defs_bytes), "added": added, "dropped": dropped,
        "videos": len(got), "video_bytes": sum(v[1] for v in got.values()),
        "bytes": defs_bytes,
    }


def merge_pixels(img_id: int, ids: set[int], dry: bool) -> dict:
    """像素侧：剪枝到闭包集合，块压缩 → ARGB4444；已有文件按 id 合并。"""
    src_path = DATA282 / "Skill/_Canvas" / f"{img_id}.img"
    dst_path = PROJ_DATA / "Skill/_Canvas" / f"{img_id}.img"
    source = load_gms(src_path, f"_Canvas/{img_id}.img")

    # 纯逻辑技能在像素文件里没有节点（282 只给有画布的技能建目录），跳过不算错。
    present = set(skill_ids(source))
    absent = sorted(ids - present)

    if dst_path.is_file():
        dst = WzImage.from_bytes(dst_path.read_bytes(), key=GMS, name=f"_Canvas/{img_id}.img")
        dst.parse()
        missing = sorted((ids & present) - set(skill_ids(dst)))
        stats = {"converted": 0, "kept": 0, "pixels": 0}
        if missing:
            host = dst.root.get("skill")
            guest = source.root.get("skill")
            pixels = PixelSource(PROJ_DATA, GMS, fallback_root=DATA282)
            for sid in missing:
                node = guest.get(str(sid))
                convert_tree(node, pixels, source, stats)
                host.add(node)
        added = len(missing)
        dropped = 0
    else:
        dst = source
        dropped = prune(dst, ids & present)
        stats = {"converted": 0, "kept": 0, "pixels": 0}
        convert_tree(dst.root, PixelSource(PROJ_DATA, GMS, fallback_root=DATA282), dst, stats)
        added = len(skill_ids(dst))

    px_bytes = serialize(dst)
    RuntimeImgMirror(px_bytes, GMS)
    if not dry:
        dst_path.parent.mkdir(parents=True, exist_ok=True)
        dst_path.write_bytes(px_bytes)

    return {
        "img": img_id, "pixels": len(px_bytes), "added": added, "dropped": dropped,
        "converted": stats["converted"], "kept": stats["kept"], "absent": absent,
        "mpixels": round(stats["pixels"] / 1e6, 1), "bytes": px_bytes,
    }


def update_index(rows: list[dict]) -> None:
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    files = {entry["path"]: entry for entry in index["files"]}
    for row in rows:
        for rel, blob in row:
            entry = files.get(rel) or {"path": rel}
            entry["sha256"] = hashlib.sha256(blob).hexdigest()
            files[rel] = entry
    index["files"] = sorted(files.values(), key=lambda e: e["path"])
    INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--img", type=int, action="append", help="只导指定 img（可重复）")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    plan = closure_plan()
    imgs = sorted(args.img or plan)
    unknown = [i for i in imgs if i not in plan]
    if unknown:
        raise SystemExit(f"闭包里没有这些 img：{unknown}")

    rows = []
    for img_id in imgs:
        ids = plan[img_id]
        started = time.time()
        d = merge_defs(img_id, ids, args.dry_run)
        p = merge_pixels(img_id, ids, args.dry_run)
        flag = "新" if d["new_file"] else "合"
        extra = f" 无像素 {len(p['absent'])}" if p["absent"] else ""
        print(f"  {flag} {img_id:>6}  定义 {d['defs']/1024:8.1f} KiB（+{d['added']:>3} 删{d['dropped']:>3}"
              f" 视频 {d['videos']:>2}/{d['video_bytes']/1e6:5.1f} MB）  "
              f"像素 {p['pixels']/1e6:7.2f} MB（+{p['added']:>3} 删{p['dropped']:>3}"
              f" 转换 {p['converted']:>4}/{p['kept']:>3} {p['mpixels']:5.1f} Mpx）{extra} {time.time()-started:.1f}s")
        rows.append({
            "img": img_id,
            "defs": d["defs"], "pixels": p["pixels"],
            "added": d["added"] + p["added"], "dropped": d["dropped"] + p["dropped"],
            "videos": d["videos"], "video_bytes": d["video_bytes"],
            "converted": p["converted"], "mpixels": p["mpixels"],
            "files": [(f"Skill/{img_id}.img", d["bytes"]), (f"Skill/_Canvas/{img_id}.img", p["bytes"])],
        })

    print(f"合计 {len(rows)} 个 img：定义 {sum(r['defs'] for r in rows)/1e6:.1f} MB"
          f"（含视频 {sum(r['video_bytes'] for r in rows)/1e6:.1f} MB）"
          f" + 像素 {sum(r['pixels'] for r in rows)/1e6:.1f} MB"
          f"  新增 {sum(r['added'] for r in rows)} / 剪掉 {sum(r['dropped'] for r in rows)}"
          f"  转换画布 {sum(r['converted'] for r in rows)}")
    if args.dry_run:
        return
    # 落盘形态必须是分片（单文件 262 MB 会在运行时把整棵树解出来）；
    # 顺手把定义侧视频外置、outlink 重指到分片，并重建索引。
    sys.path.insert(0, str(ROOT / "tools"))
    import split_skill_assets as split  # noqa: E402
    split.split_after_import(imgs)
    print(f"已按分片落盘并更新 {INDEX.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
