#!/usr/bin/env python3
"""把后台前端（AdminUi/dist-embed）打包成 Unity Resources 里的单个 .bytes。

为什么要打包：Android 上 StreamingAssets 在 APK 的 jar 内，File.ReadAllBytes 读不到；
本工程已有「静态资源 → Resources/*.bytes」的成熟链路（NativeAssetIndex、npc_scripts.json），
沿用同一条路，运行期用 Resources.Load<TextAsset>("AdminUi/adminui") 取到整包。

包格式（小端，与 Assets/Scripts/Admin/AdminStaticBundle.cs 一一对应）：
    "MDUIB1"           6 字节魔数
    u32                文件个数
    每个文件：
        u16            路径长度（UTF-8 字节数）
        bytes          路径，相对于 dist 根，用 / 分隔
        u32            内容长度
        bytes          内容

用法：
    python3 tools/pack_admin_ui.py
    python3 tools/pack_admin_ui.py --dist AdminUi/dist-embed --out Assets/Resources/AdminUi/adminui.bytes
"""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

MAGIC = b"MDUIB1"
SKIP_SUFFIX = {".map", ".gz", ".br"}


def collect(dist: Path) -> list[Path]:
    files = []
    for p in sorted(dist.rglob("*")):
        if not p.is_file():
            continue
        if p.suffix.lower() in SKIP_SUFFIX:
            continue
        files.append(p)
    return files


def pack(dist: Path, out: Path) -> tuple[int, int]:
    files = collect(dist)
    if not files:
        raise SystemExit(f"dist 目录里没有文件：{dist}")
    if not (dist / "index.html").is_file():
        raise SystemExit(f"dist 目录缺少 index.html：{dist}")

    chunks: list[bytes] = [MAGIC, struct.pack("<I", len(files))]
    total = 0
    for f in files:
        rel = f.relative_to(dist).as_posix().encode("utf-8")
        data = f.read_bytes()
        if len(rel) > 0xFFFF:
            raise SystemExit(f"路径过长：{rel!r}")
        chunks.append(struct.pack("<H", len(rel)))
        chunks.append(rel)
        chunks.append(struct.pack("<I", len(data)))
        chunks.append(data)
        total += len(data)

    blob = b"".join(chunks)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(blob)
    return len(files), len(blob)


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser()
    ap.add_argument("--dist", default=str(root / "AdminUi" / "dist-embed"))
    ap.add_argument("--out", default=str(root / "Assets" / "Resources" / "AdminUi" / "adminui.bytes"))
    args = ap.parse_args()

    dist = Path(args.dist)
    out = Path(args.out)
    count, size = pack(dist, out)
    print(f"打包完成：{count} 个文件，{size / 1024 / 1024:.2f} MB -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
