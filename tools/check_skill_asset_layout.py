#!/usr/bin/env python3
"""Skill 资源布局守卫：单文件不许再膨胀、引用必须落得到。

背景（2026-11-04）：`Skill/_Canvas/50000.img` 曾到 262 MB，`Skill/<job>.img`
因为内嵌 MCV0 也有几十 MB；运行时 `WzAssets.FileRoot` 会为读一个图标把整棵树
解出来，体积直接等于加载代价。现在拆成：

    Skill/<job>.img                     定义（1x1 桩 + _outlink）
    Skill/_Canvas/<job>/<sid>.img       像素分片（<sid>/<branch>.img 为再拆一层）
    Skill/_Canvas/<job>.img             只剩 info 等非 skill 分支（可能不存在）
    Skill/MobSkill/<id>.img             怪物技能分片
    Data/Video/skill/<sid>/<x>.mcv      视频（定义里只留 "mcv:<路径>"）

本脚本守六条：
  1. 任何 .img 不得 ≥ 40 MB（真的到过 262 MB，回归时先炸在这里）；
  2. 每条 `_outlink` 的目标文件存在，且目标文件里真有那个节点；
  3. 每条 Canvas#Video 载荷要么是 MCV0，要么是 `mcv:` 指针且文件存在；
  4. MobSkill 的每个 id/level 都能按「先平后深」读到；
  5. NativeAssetIndex 的 Skill/Video 条目与磁盘一一对应（无悬空、无漏登）；
  6. 运行时读取点仍在（NativeMobController 走分片、NativeSkillVideo 认 mcv:）。

    python3 tools/check_skill_asset_layout.py
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
REPO = ROOT.parent
sys.path.insert(0, "/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python")

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzVideoProperty  # noqa: E402
from wzpy.writer import _read_opaque_payload  # noqa: E402

GMS = WzKey.for_region("GMS")
DATA = REPO / "MapleDashResources/Data"
SKILL = DATA / "Skill"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
LIMIT = 40 * 1024 * 1024
MCV_MAGIC = b"MCV0"
MCV_PREFIX = b"mcv:"

_roots: dict[str, object] = {}
_problems: list[str] = []


def root_of(rel: str):
    if rel in _roots:
        return _roots[rel]
    path = DATA / rel
    root = None
    if path.is_file():
        img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=path.name)
        img.parse()
        root = img.root
    if len(_roots) > 32:
        _roots.clear()
    _roots[rel] = root
    return root


def resolve_link(value: str):
    """把 `_outlink` 拆成「文件 + 节点路径」，两者都成立才算通。

    定义侧现在指向分片（`Skill/_Canvas/<job>/<sid>.img/skill/...`），
    也可能残留旧口径（`Skill/_Canvas/<job>.img/skill/...`）；
    按最后一个 `.img/` 切一刀对两种形态都成立。
    """
    cut = value.find(".img/")
    if cut < 0:
        return value if (DATA / value).is_file() else None
    file = value[:cut + 4]
    rest = value[cut + 5:]
    root = root_of(file)
    return file if root is not None and root.get(rest) is not None else None


def check_sizes(label: str) -> None:
    big = []
    for path in SKILL.rglob("*.img"):
        size = path.stat().st_size
        if size >= LIMIT:
            big.append(f"{path.relative_to(DATA)} {size / 1e6:.1f}MB")
    if big:
        _problems.append(f"{label}: {len(big)} 个 img ≥ 40MB：" + "、".join(big[:5]))
    total = sum(p.stat().st_size for p in SKILL.rglob("*.img"))
    count = sum(1 for _ in SKILL.rglob("*.img"))
    print(f"S sizes: {count} 个 img / {total/1e6:.1f}MB，最大 "
          f"{max((p.stat().st_size for p in SKILL.rglob('*.img')), default=0)/1e6:.1f}MB "
          f"{'PASS' if not big else 'FAIL'}")


def check_definitions() -> None:
    links = videos = video_bytes = 0
    broken: list[str] = []
    for path in sorted(SKILL.glob("*.img")):
        img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=path.name)
        img.parse()

        def walk(node, here=""):
            nonlocal links, videos, video_bytes
            for child in node.children():
                spot = f"{here}/{child.name}" if here else child.name
                if isinstance(child, WzVideoProperty):
                    videos += 1
                    data = _read_opaque_payload(child)
                    if data.startswith(MCV_PREFIX):
                        target = DATA / data[4:].decode("utf-8")
                        if not target.is_file():
                            broken.append(f"{path.name}{spot} 缺 {data[4:].decode()}")
                        else:
                            head = target.open("rb").read(4)
                            if head != MCV_MAGIC:
                                broken.append(f"{path.name}{spot} 外置文件不是 MCV0")
                            video_bytes += target.stat().st_size
                    elif data.startswith(MCV_MAGIC):
                        video_bytes += len(data)
                    else:
                        broken.append(f"{path.name}{spot} 视频载荷非法（{data[:6]!r}）")
                elif isinstance(child, WzCanvasProperty):
                    link = child.get("_outlink")
                    if link is not None:
                        links += 1
                        if resolve_link(str(link.value)) is None:
                            broken.append(f"{path.name}/{spot} -> {link.value}")
                walk(child, spot)

        walk(img.root)
    if broken:
        _problems.append(f"定义侧 {len(broken)} 条坏引用：" + "、".join(broken[:5]))
    print(f"H outlinks: {links} 条 + 视频 {videos} 条/{video_bytes/1e6:.1f}MB，"
          f"坏引用 {len(broken)} {'PASS' if not broken else 'FAIL'}")


def check_mob_skill() -> None:
    ids = levels = missing = 0
    for path in SKILL.rglob("MobSkill/**/*.img"):
        root = root_of(path.relative_to(DATA).as_posix())
        if root is None:
            _problems.append(f"MobSkill 分片读不出来：{path.name}")
            continue
        for top in root.children():
            node = top.get("level")
            if node is None:
                continue
            ids += 1
            for level in node.children():
                levels += 1
                tail = f"{top.name}/level/{level.name}"
                if not any((root_of(c) is not None and root_of(c).get(tail) is not None)
                           for c in (f"Skill/MobSkill/{top.name}.img",
                                     f"Skill/MobSkill/{top.name}/{level.name}.img")):
                    missing += 1
    if missing:
        _problems.append(f"MobSkill {missing} 个 level 读不到")
    print(f"M mobskill: {ids} 个技能 / {levels} 个等级，读不到 {missing} "
          f"{'PASS' if not missing else 'FAIL'}")


def check_index() -> None:
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    listed = {e["path"] for e in index["files"]}
    disk = {f"Skill/{p.relative_to(SKILL).as_posix()}" for p in SKILL.rglob("*.img")}
    disk |= {f"Video/skill/{p.relative_to(DATA/'Video/skill').as_posix()}"
             for p in (DATA / "Video/skill").rglob("*.mcv")}
    missing = sorted(disk - listed)
    dangling = sorted(p for p in listed
                      if p.startswith(("Skill/", "Video/skill/")) and not (DATA / p).is_file())
    if missing:
        _problems.append(f"索引漏登 {len(missing)} 条：{missing[:4]}")
    if dangling:
        _problems.append(f"索引悬空 {len(dangling)} 条：{dangling[:4]}")
    print(f"I index: 磁盘 {len(disk)} 条，漏登 {len(missing)}，悬空 {len(dangling)} "
          f"{'PASS' if not missing and not dangling else 'FAIL'}")


def check_runtime_readers() -> None:
    mob = (ROOT / "Assets/Scripts/Field/NativeMobController.cs").read_text(encoding="utf-8")
    video = (ROOT / "Assets/Scripts/NativeSkillVideo.cs").read_text(encoding="utf-8")
    issues = []
    if "MobSkillNode(" not in mob or '"Skill/MobSkill/" + id + ".img"' not in mob:
        issues.append("NativeMobController 不再走 MobSkill 分片路径")
    if "Skill/MobSkill.img" not in mob:
        issues.append("NativeMobController 缺少旧包兜底")
    if "mcv:" not in video or "Resolve(data)" not in video:
        issues.append("NativeSkillVideo 不再识别 mcv: 外置载荷")
    if issues:
        _problems.append("；".join(issues))
    print(f"R runtime: 读取点 {'PASS' if not issues else 'FAIL'}")


def main() -> int:
    if not SKILL.is_dir():
        print("缺 Skill 目录")
        return 1
    check_sizes("S")
    check_definitions()
    check_mob_skill()
    check_index()
    check_runtime_readers()
    if _problems:
        print(f"\n✗ {len(_problems)} 个问题：")
        for item in _problems:
            print("   -", item)
        return 1
    print("OK — Skill 资源布局、引用与索引一致")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
