#!/usr/bin/env python3
"""把 282 的特殊核心图标导进运行时 Data 树（V 矩阵「特殊核心」子窗用）。

为什么必须单独导这一份：18 个特殊核心（type==2）在 VMatrix.json 里 `skills` 是空数组，
它们不对应任何 Skill，`skillicon/<id>` 永远取不到图——这就是「特殊核心面板没图标」的原因。
真正的图标是 `Etc/VCore.img/vSkill/CoreData/<id>/icon|iconDisabled` 的 `_outlink` 目标
`Etc/_Canvas/VCore.img/vSkill/CoreData/<id>/icon|iconDisabled`（32x32），
见 docs/tms282-skill/07-job-advancement.md §7.3。

两件事都得做，缺一条在真机上就是空白：
  1. AGENTS 规则 11：BMS 密钥 → GMS 密钥（bms_to_gms.transcode，等长 XOR，不重新打包）；
  2. 登记进 NativeAssetIndex.json —— 手机端 WzAssets.HasFile 读的是清单里的 files，
     漏登记的话编辑器能显示（读磁盘），真机 HasFile 恒 false → 静默没有图标。

画布本身是 format 1（ARGB4444）/ 2（ARGB8888），ImgFile.Rgba() 直接可解码，
不需要 img_bake 的块压缩烘焙。

用法：
  python3 tools/ms282/import_special_core_icons.py            # 只校验（默认）
  python3 tools/ms282/import_special_core_icons.py --write     # 写入 + 登记
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec  # noqa: E402
from import_growth_ui import DEFAULT_DEST, DEFAULT_SRC, INDEX_PATH  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

REL = "Etc/_Canvas/VCore.img"
VMATRIX = ROOT / "Assets/Resources/UI/VMatrix.json"
# ImgFile.Rgba() 只认这三种；块压缩格式必须先在导入期压成 ARGB4444。
SUPPORTED = {1, 2, 513}


def special_ids() -> list[int]:
    data = json.loads(VMATRIX.read_text(encoding="utf-8"))
    ids = [core["id"] for core in data["cores"].values() if core.get("type") == 2]
    return sorted(ids)


def decode_icon(img: WzImage, core_id: int, name: str) -> tuple[int, int, int]:
    node = img.root.get(f"vSkill/CoreData/{core_id}/{name}")
    if not isinstance(node, WzCanvasProperty):
        raise SystemExit(f"{REL}: 缺少 vSkill/CoreData/{core_id}/{name}")
    if node.format not in SUPPORTED:
        raise SystemExit(f"{REL}: {core_id}/{name} 是 format {node.format}，ImgFile 解不了（需要 1/2/513）")
    rgba = decode_canvas(node)
    if hasattr(rgba, "convert"):
        rgba = rgba.convert("RGBA")
    raw = bytes(rgba) if isinstance(rgba, (bytes, bytearray)) else rgba.tobytes()
    opaque = sum(1 for i in range(3, len(raw), 4) if raw[i] > 0)
    if opaque == 0:
        raise SystemExit(f"{REL}: {core_id}/{name} 全透明，图标是空的")
    return node.width, node.height, opaque


def register(rel: str, digest: str) -> str:
    """登记一条 files 记录。

    import_growth_ui.register 会整篇 json.dumps(indent=1) 重写，而这份清单是
    package_native_assets.py 产出的**单行压缩**文件（6.6MB）——重排一次就是 23 万行
    的伪 diff，review 里再也看不出「只加了一个文件」。所以这里按字节插到末尾的
    files 数组里，然后重新解析一次做自检；只有原文件本来就是多行格式时才回退到重排。
    """
    text = INDEX_PATH.read_text(encoding="utf-8")
    index = json.loads(text)
    files = index["files"]
    for entry in files:
        if entry["path"] == rel:
            if entry["sha256"] == digest:
                return "already"
            entry["sha256"] = digest
            INDEX_PATH.write_text(json.dumps(index, ensure_ascii=False,
                                             indent=1 if "\n" in text else None,
                                             separators=None if "\n" in text else (",", ":")),
                                  encoding="utf-8")
            return "updated"
    entry = '{"path":"%s","sha256":"%s"}' % (rel, digest)
    if "\n" in text and not text.rstrip().endswith("]}"):
        # 多行且 files 不是最后一个键：缩进插入比猜位置安全。
        files.append({"path": rel, "sha256": digest})
        INDEX_PATH.write_text(json.dumps(index, ensure_ascii=False, indent=1), encoding="utf-8")
        return "added"
    if not text.rstrip().endswith("]}"):
        raise SystemExit("NativeAssetIndex.json 结构变了：files 不是最后一个键，请人工处理")
    head = text.rstrip()[:-2]
    merged = head + ("," if files else "") + entry + "]}"
    if json.loads(merged)["files"][-1]["path"] != rel:
        raise SystemExit("插入后自检失败")
    INDEX_PATH.write_text(merged + ("\n" if text.endswith("\n") else ""), encoding="utf-8")
    return "added"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--write", action="store_true", help="写入 Data/ 并登记 NativeAssetIndex.json")
    args = ap.parse_args()

    src = DEFAULT_SRC / REL
    if not src.is_file():
        raise SystemExit(f"缺少源端 {src}")
    raw = src.read_bytes()
    data, _ = codec.transcode(raw)
    codec.verify(raw, data, Path(REL).name)

    img = WzImage.from_bytes(data, key=WzKey.for_region("GMS"), name=Path(REL).name)
    img.parse()
    if img.truncated or img.parse_warnings:
        raise SystemExit(f"{REL}: 转码后不完整 {img.parse_warnings}")

    ids = special_ids()
    sizes = set()
    for core_id in ids:
        for name in ("icon", "iconDisabled"):
            sizes.add(decode_icon(img, core_id, name))
    print(f"{REL}: {len(ids)} 个特殊核心 × icon/iconDisabled 全部可解码 {sorted(sizes)}")

    dest = DEFAULT_DEST / REL
    if args.write:
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        state = register(REL, hashlib.sha256(data).hexdigest())
        print(f"写入 {dest.relative_to(DEFAULT_DEST.parent)}；清单 {state}")
    else:
        print(f"校验通过（未写入）。目标：{dest}；加 --write 落盘")
    return 0


if __name__ == "__main__":
    sys.exit(main())
