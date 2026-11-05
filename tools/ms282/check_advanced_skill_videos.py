#!/usr/bin/env python3
"""六转技能视频层回归：定义 img 里的 Canvas#Video 必须完整、可播。

守的是四条（前三条都真的踩过）：
  1. 节点还在 —— wzpy 写回器不认识 Canvas#Video 时会被当成普通 Property 落地，
     视频载荷静默消失（曾 92 条 / 268.6 MB 全丢）。
  2. 载荷形态合法 —— 要么是完整 MCV0（头 4 字节 "MCV0"、长度字段与实际一致），
     要么是外置指针 `mcv:<相对 Data 的路径>`（tools/split_skill_assets.py 的产物）。
  3. 外置指向的文件真实存在、本身是 MCV0 且不小于 1 KiB。
  4. 路径能被运行时定位 —— NativeSkillVfx.Key 拼的是
     Skill/<id/10000>.img/skill/<id>/<branch>，这里按同一规则反查。

    python3 tools/ms282/check_advanced_skill_videos.py
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]      # MapleDash/
REPO = ROOT.parent
sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzVideoProperty  # noqa: E402

GMS = WzKey.for_region("GMS")
SKILL_DIR = REPO / "MapleDashResources/Data/Skill"
DATA = REPO / "MapleDashResources/Data"
ADVANCED = (113, 123, 133, 213, 223, 233, 313, 323, 413, 423, 513, 523,
            1113, 1213, 1313, 1413, 1513)
MCV_MAGIC = b"MCV0"
MCV_PREFIX = b"mcv:"


def scan(path: Path) -> tuple[list[tuple[str, int, int, bytes]], list[str]]:
    """返回 [(层路径, videoType, 长度, 头 8 字节)] 与错误列表。"""
    img = WzImage.from_bytes(path.read_bytes(), key=GMS)
    img.parse()
    found: list[tuple[str, int, int, bytes]] = []
    errors: list[str] = []
    reader = img.wz_file.reader

    def walk(node, prefix=""):
        for child in (node.children() or []):
            here = f"{prefix}/{child.name}"
            if isinstance(child, WzVideoProperty):
                keep = reader.position
                reader.seek(child._data_offset)
                head = reader.read(8)
                reader.seek(keep)
                length = child._data_length
                if head[:4] == MCV_PREFIX:
                    # 外置：真正要校验的是被指向的 .mcv 文件。
                    reader.seek(child._data_offset)
                    rel = reader.read(length)[len(MCV_PREFIX):].decode("utf-8")
                    target = DATA / rel
                    if not target.is_file():
                        errors.append(f"{path.name}{here} 外置视频不存在：{rel}")
                    else:
                        blob_head = target.open("rb").read(8)
                        size = target.stat().st_size
                        if blob_head[:4] != MCV_MAGIC:
                            errors.append(f"{path.name}{here} {rel} 不是 MCV0"
                                          f"（头 {blob_head[:4]!r}）")
                        if size < 1024:
                            errors.append(f"{path.name}{here} {rel} 过短（{size}B）")
                        length = size
                else:
                    # 内联：只校验前 4 字节与长度下限，避免把大载荷全读进内存。
                    if head[:4] != MCV_MAGIC:
                        errors.append(f"{path.name}{here} 不是 MCV0（头 {head.hex()}）")
                    if length < 1024:
                        errors.append(f"{path.name}{here} 载荷过短（{length}B）")
                found.append((here, child.video_type, length, head))
            walk(child, here)

    walk(img.root)
    return found, errors


def main() -> int:
    total = 0
    total_bytes = 0
    errors: list[str] = []
    for job in ADVANCED:
        img_id = job + 1          # 六转定义 img = 六转编码 + 1
        path = SKILL_DIR / f"{img_id}.img"
        if not path.is_file():
            errors.append(f"缺定义文件 {path.name}（{job}）")
            continue
        found, bad = scan(path)
        errors.extend(bad)
        total += len(found)
        total_bytes += sum(item[2] for item in found)
        # 运行时用 Skill/<id/10000>.img 反查，所以 id//10000 必须等于本文件名。
        for layer, _, _, _ in found:
            skill_id = int(layer.split("/")[2])
            if skill_id // 10000 != img_id:
                errors.append(f"{path.name} 层 {layer} 的 id//10000={skill_id // 10000} ≠ {img_id}")
        print(f"  {job:>5} -> {path.name:<10} 视频 {len(found):2} 条")
    print(f"合计 {total} 条 / {total_bytes / 1e6:.1f} MB")
    if errors:
        print(f"\n✗ {len(errors)} 个问题：")
        for item in errors[:20]:
            print("   -", item)
        return 1
    print("✓ 视频层完整")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
