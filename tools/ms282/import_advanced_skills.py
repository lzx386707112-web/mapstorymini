#!/usr/bin/env python3
"""按 282 的目录结构导入六转技能：定义 img 与像素 img 分开存，不合并成单文件。

    定义  MapleDashResources/Data/Skill/<四转ID+2>.img          ← 282 Skill_<四转ID+2>.img（只换密钥）
    像素  MapleDashResources/Data/Skill/_Canvas/<四转ID+2>.img  ← 282 Skill/_Canvas/<...>.img（画布重编码）

为什么分开（对齐 282 的原生形态）：
  * 282 里定义文件只有 1x1 占位画布 + `_outlink`，真像素在同级 `_Canvas/` 文件；
    运行时 WzAssets.Canvas 本来就沿 `_outlink` 找文件，所以「结构 + 像素」两文件是原生形态。
  * 全部 inline 会把单个 img 顶到 100MB（法师系 96~102MB），加载与热更都不划算。

像素重编码（唯一必须做的一步）：
  * 282 像素画布是块压缩：2050 = BC3(DXT5)、4098 = BC7，本项目 ImgFile.cs 只认 1/2/513，
    所以解出 RGBA 后统一压成 **ARGB4444（format 1）**（v83 原生素材用的就是这个）。
  * 282 载荷普遍缺 zlib adler32 尾 → 解压走「跳 2 字节 + 裸 deflate」。

用法：
    python3 tools/ms282/import_advanced_skills.py --job 112 --dry-run
    python3 tools/ms282/import_advanced_skills.py
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]           # MapleDash/
REPO = ROOT.parent
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

import numpy as np  # noqa: E402
import texture2ddecoder as t2d  # noqa: E402

import bms_to_gms  # noqa: E402
from img_bake import PixelSource, RuntimeImgMirror, follow_to_pixels, serialize  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import _read_canvas_bytes, decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

GMS = WzKey.for_region("GMS")
PACKS = Path("/tmp/packs")
DATA282 = Path("/Users/lizixian/Documents/mxd/MapleStory/Data")
PROJ_DATA = REPO / "MapleDashResources/Data"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"

ADVANCED = {
    112: 113, 122: 123, 132: 133,
    212: 213, 222: 223, 232: 233,
    312: 313, 322: 323,
    412: 413, 422: 423,
    512: 513, 522: 523,
    1112: 1113, 1212: 1213, 1312: 1313, 1412: 1413, 1512: 1513,
}

BLOCK_DECODERS = {2050: t2d.decode_bc3, 4098: t2d.decode_bc7}
NATIVE_FORMATS = (1, 2, 513)


def decode_rgba(canvas: WzCanvasProperty, img: WzImage) -> np.ndarray:
    with img.wz_file.reader_lock:
        payload = _read_canvas_bytes(canvas)
    if canvas.format in BLOCK_DECODERS:
        inflater = zlib.decompressobj(-15)
        data = inflater.decompress(payload[2:]) + inflater.flush()
        raw = BLOCK_DECODERS[canvas.format](data, canvas.width, canvas.height)
        return np.frombuffer(raw, dtype=np.uint8).reshape((canvas.height, canvas.width, 4))
    return np.array(decode_canvas(canvas).convert("RGBA"))


def encode_argb4444(rgba: np.ndarray) -> bytes:
    """ImgFile.cs format 1 布局：byte0=(G<<4)|B，byte1=(A<<4)|R。"""
    r = (rgba[:, :, 0] >> 4).astype(np.uint8)
    g = (rgba[:, :, 1] >> 4).astype(np.uint8)
    b = (rgba[:, :, 2] >> 4).astype(np.uint8)
    a = (rgba[:, :, 3] >> 4).astype(np.uint8)
    packed = np.stack([((g << 4) | b).astype(np.uint8), ((a << 4) | r).astype(np.uint8)], axis=-1)
    return zlib.compress(packed.tobytes(), 6)


def convert_tree(node: WzSubProperty, source: PixelSource, home: WzImage, stats: dict) -> None:
    """像素文件内：块压缩画布 → ARGB4444；残留的跨文件链接就地解成像素。"""
    for child in list(node.children()):
        if isinstance(child, WzCanvasProperty):
            if child.format in NATIVE_FORMATS and child.get("_outlink") is None:
                stats["kept"] += 1
                continue
            if child.get("_outlink") is not None:
                owner, owner_img, owner_path = source.resolve_outlink(str(child.get("_outlink").value))
                owner, owner_img, owner_path = follow_to_pixels(owner, owner_img, owner_path, source)
                rgba = decode_rgba(owner, owner_img)
                keep = {c.name: c for c in child.children() if c.name not in ("_outlink", "_inlink")}
                merged = [c for c in owner.children()
                          if c.name not in ("_outlink", "_inlink") and c.name not in keep]
                merged.extend(keep.values())
                child._children.clear()
                for item in merged:
                    child._children[item.name] = item
            else:
                rgba = decode_rgba(child, home)
            child.format, child.format2 = 1, 0
            child._png_data = encode_argb4444(rgba)
            stats["converted"] += 1
            stats["pixels"] += child.width * child.height
        elif isinstance(child, WzSubProperty):
            convert_tree(child, source, home, stats)


def load_gms(path: Path, name: str) -> WzImage:
    data, _ = bms_to_gms.transcode(path.read_bytes())
    img = WzImage.from_bytes(data, key=GMS, name=name)
    img.parse()
    return img


def video_inventory(img: WzImage) -> dict[str, tuple[int, int, str]]:
    """路径 → (videoType, 长度, sha256)。用于导入后逐条比对，防止视频被静默丢弃。"""
    from wzpy.properties import WzVideoProperty

    out: dict[str, tuple[int, int, str]] = {}
    reader = img.wz_file.reader

    def walk(node, path=""):
        for child in (node.children() or []):
            here = f"{path}/{child.name}"
            if isinstance(child, WzVideoProperty):
                keep = reader.position
                reader.seek(child._data_offset)
                blob = reader.read(child._data_length)
                reader.seek(keep)
                out[here] = (child.video_type, len(blob), hashlib.sha256(blob).hexdigest())
            walk(child, here)

    walk(img.root)
    return out


def import_job(base: int, dry: bool) -> dict:
    src = base + 2
    pack = PACKS / f"Skill_{src}.img"
    px_pack = DATA282 / "Skill/_Canvas" / f"{src}.img"
    if not pack.is_file():
        raise SystemExit(f"缺少 {pack}（先用 MsUnpack 把 282 Packs/Skill_*.ms 解到 /tmp/packs）")
    if not px_pack.is_file():
        raise SystemExit(f"缺少像素包 {px_pack}")
    started = time.time()

    # 1) 定义文件：只换密钥，_outlink 原样保留（运行时沿链接去 _Canvas 找像素）
    defs = load_gms(pack, f"Skill_{src}.img")
    wanted = video_inventory(defs)
    defs_bytes = serialize(defs)
    RuntimeImgMirror(defs_bytes, GMS)

    # 2) 像素文件：块压缩 → ARGB4444
    pixels = load_gms(px_pack, f"_Canvas/{src}.img")
    source = PixelSource(PROJ_DATA, GMS, fallback_root=DATA282)
    stats = {"converted": 0, "kept": 0, "pixels": 0}
    convert_tree(pixels.root, source, pixels, stats)
    px_bytes = serialize(pixels)
    RuntimeImgMirror(px_bytes, GMS)

    # 3) 重读写出的字节，逐条核对 Canvas#Video：写回器不认识该类型时会被当成
    #    普通 Property 落地，视频载荷静默消失（282 -> 本项目曾有 92 条全丢）。
    written = WzImage.from_bytes(defs_bytes, key=GMS, name=f"Skill_{src}.img")
    written.parse()
    got = video_inventory(written)
    if got != wanted:
        missing = sorted(set(wanted) - set(got))
        drift = [p for p in wanted if p in got and got[p] != wanted[p]]
        raise SystemExit(f"{src}.img 视频校验失败：缺 {missing[:4]} 变 {drift[:4]}")

    if not dry:
        (PROJ_DATA / "Skill").mkdir(parents=True, exist_ok=True)
        (PROJ_DATA / "Skill/_Canvas").mkdir(parents=True, exist_ok=True)
        (PROJ_DATA / "Skill" / f"{src}.img").write_bytes(defs_bytes)
        (PROJ_DATA / "Skill/_Canvas" / f"{src}.img").write_bytes(px_bytes)
    return {
        "job": ADVANCED[base], "img": src,
        "defs": len(defs_bytes), "pixels": len(px_bytes),
        "converted": stats["converted"], "kept": stats["kept"],
        "videos": len(got), "video_bytes": sum(v[1] for v in got.values()),
        "mpixels": round(stats["pixels"] / 1e6, 1), "seconds": round(time.time() - started, 1),
    }


def update_index(rows: list[dict]) -> None:
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    files = {entry["path"]: entry for entry in index["files"]}
    for row in rows:
        for rel in (f"Skill/{row['img']}.img", f"Skill/_Canvas/{row['img']}.img"):
            blob = (PROJ_DATA / rel).read_bytes()
            entry = files.get(rel) or {"path": rel}
            entry["sha256"] = hashlib.sha256(blob).hexdigest()
            files[rel] = entry
    index["files"] = sorted(files.values(), key=lambda e: e["path"])
    INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--job", type=int, help="只导一个（四转 ID，如 112 / 1112）")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    bases = [args.job] if args.job else list(ADVANCED)
    rows = []
    for base in bases:
        row = import_job(base, args.dry_run)
        rows.append(row)
        print(f"  {row['job']:>5} <- Skill_{row['img']}.img  "
              f"定义 {row['defs']/1024:9.1f} KiB  像素 {row['pixels']/1e6:6.2f} MB  "
              f"转换 {row['converted']:5} / 原生 {row['kept']:3}  "
              f"视频 {row['videos']:2} 条/{row['video_bytes']/1e6:5.1f} MB  "
              f"{row['mpixels']:6.1f} Mpx  {row['seconds']}s")
    total_defs = sum(r["defs"] for r in rows) / 1e6
    total_px = sum(r["pixels"] for r in rows) / 1e6
    total_video = sum(r["video_bytes"] for r in rows) / 1e6
    print(f"合计 {len(rows)} 个职业：定义 {total_defs:.1f} MB（含视频 {total_video:.1f} MB）"
          f" + 像素 {total_px:.1f} MB")
    if args.dry_run:
        return
    # 与闭包导入同一落盘约定：定义 / 像素分片 + 视频外置，不写回单个大 img。
    sys.path.insert(0, str(ROOT / "tools"))
    import split_skill_assets as split  # noqa: E402
    split.split_after_import([r["img"] for r in rows])
    print(f"已按分片落盘并更新 {INDEX.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
