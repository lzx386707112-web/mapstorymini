#!/usr/bin/env python3
"""导出 V / HEXA 矩阵「核心 id → 原生图标」映射，并迁移缺失的原端画布。

## 为什么需要

V 矩阵窗口原来只画 `skillicon/<core.skills[0]>`。但 282 的核心图标并不在 Skill 树里：

* 权威表是 `Etc/VCore.img/vSkill/CoreData/<coreId>/icon` 的 `_outlink`
  —— 260 条（75 条指向 `Etc/_Canvas/VCore.img`、185 条指向 `Etc/_Canvas/HexaCore.img`）；
  本机此前只搬了 `VCore.img` 且只有特殊核心在用。
* `Etc/HexaCore.img/hexaSkill/coreData/<coreId>/icon` 是 HEXA 563 个核心的图标，
  全部外链到 `Etc/_Canvas/HexaCore.img`（本机此前完全没搬）。
* **强化核心（type 1，306 条）在 VCore 里根本没有 icon 节点**：原端用
  `iconMask` 把 `psdSkill` 指向的 1~4 个被强化技能图标切角拼成一张。项目沿用
  单图标口径（与已有的 154 条一致），缺图标时按 400004021 的先例
  （结构侧 icon 外链直接指向 `psdSkill` 里的真实技能图标）取第一个可用技能图标。

`Skill/<img>.img` 里 164 个技能的 `icon` 是 **1×1 全透明桩**（无 `_outlink`），
它们就是玩家看到的空格。这里一律不猜图标、不自绘：像素全部来自 282 原端 IMG。

## 产物

1. `Data/Etc/_Canvas/HexaCore.img` —— BMS→GMS 等长 XOR（不重新打包），ARGB4444/8888 原生格式；
2. `Data/Etc/_Canvas/MatrixCoreIcons.img` —— 新建的 GMS/ARGB4444 画布，
   `v/<coreId>/icon` 与 `h/<coreId>/icon`，只放「原端没有直引路径」的补强图标；
3. `Assets/Resources/UI/MatrixCoreIcons.json` —— `v` / `h` 两张 coreId→路径表；
4. `NativeAssetIndex.json` 的 files 登记（真机 HasFile 读清单）。

## 用法
    python3 tools/ms282/export_matrix_core_icons.py            # 只校验/报告
    python3 tools/ms282/export_matrix_core_icons.py --write    # 落盘 + 登记
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import zlib
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]           # MapleDash/
REPO = ROOT.parent
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec                                   # noqa: E402
import img_bake                                              # noqa: E402
from wzpy import WzImage, WzKey                              # noqa: E402
from wzpy.canvas import decode_canvas                        # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402
from wzpy.writer import encode_image_body                    # noqa: E402

BMS = WzKey.for_region("BMS")
GMS = WzKey.for_region("GMS")
SRC = Path.home() / "Documents/mxd/MapleStory/Data"
DST = REPO / "MapleDashResources/Data"
UI = ROOT / "Assets/Resources/UI"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
OUT_JSON = UI / "MatrixCoreIcons.json"
HEXA_CANVAS = "Etc/_Canvas/HexaCore.img"
EXTRA_CANVAS = "Etc/_Canvas/MatrixCoreIcons.img"
VCORE_CANVAS = "Etc/_Canvas/VCore.img"

SUPPORTED = (1, 2, 513)


class Images:
    """按 (数据源, 相对路径) 缓存已 parse 的 IMG。282 源端用 BMS key。"""

    def __init__(self):
        self.cache = {}

    def get(self, rel: str, source: bool = False):
        key = (rel, source)
        if key not in self.cache:
            path = (SRC if source else DST) / rel
            if not path.is_file():
                self.cache[key] = None
            else:
                img = WzImage.from_file(str(path), key=BMS if source else GMS)
                img.parse()
                self.cache[key] = img
        return self.cache[key]

    def canvas(self, rel: str, path: str, source: bool = False):
        img = self.get(rel, source)
        if img is None:
            return None, None
        node = img.get(path)
        return (node, img) if isinstance(node, WzCanvasProperty) else (None, None)


def canvas_rgba(canvas: WzCanvasProperty, img: WzImage) -> bytes:
    """解成 RGBA；块压缩走 img_bake 的裸 deflate 分支（与 ImgFile.cs 一致）。"""
    rgba = img_bake.decode_rgba(canvas, img)
    return rgba.tobytes() if hasattr(rgba, "tobytes") else bytes(rgba)


def opaque(rgba: bytes) -> int:
    return sum(1 for i in range(3, len(rgba), 4) if rgba[i] > 8)


def argb4444_payload(canvas: WzCanvasProperty, img: WzImage) -> tuple[int, int, int, bytes]:
    """统一压成 ARGB4444（AGENTS 规则 11）：新画布不残留 282 的 8888/BC 格式。"""
    import numpy as np
    rgba = canvas_rgba(canvas, img)
    arr = np.frombuffer(rgba, dtype=np.uint8).reshape((canvas.height, canvas.width, 4))
    return 1, canvas.width, canvas.height, img_bake.encode_argb4444(arr)


def outlink_paths(images: Images, rel: str, branch: str) -> dict[int, str]:
    """读 282 结构 IMG：coreId -> icon 的 _outlink 原样路径。"""
    img = images.get(rel, source=True)
    if img is None:
        raise SystemExit("缺少 282 源端 " + rel)
    node = img.get(branch)
    if node is None:
        raise SystemExit(rel + " 缺少分支 " + branch)
    out = {}
    for child in node.children():
        icon = child.get("icon")
        if not isinstance(icon, WzCanvasProperty):
            continue
        link = icon.get("_outlink")
        if link is not None and str(link.value):
            out[int(child.name)] = str(link.value)
    return out


def split_link(link: str) -> tuple[str, str]:
    if ".img/" not in link:
        raise SystemExit("非法外链 " + link)
    f, rest = link.split(".img/", 1)
    return f + ".img", rest


def psd_skills(images: Images, skill_id: int) -> list[int]:
    img = images.get("Skill/%03d.img" % (skill_id // 10000))
    if img is None:
        return []
    node = img.get("skill/%07d/psdSkill" % skill_id)
    if node is None:
        return []
    return [int(c.name) for c in node.children() if c.name.isdigit()]


def boost_candidates(images: Images, core: dict) -> list[tuple[str, str, str, bool]]:
    """缺图标核心的候选：(相对文件, 节点路径, 说明, 是否源端)。按原端 psdSkill 顺序。"""
    result = []
    for sid in core.get("skills") or []:
        sid = int(sid)
        img_no = sid // 10000
        # 先试本体技能自己在源端大画布里的 icon（职业核心常见）。
        result.append(("Skill/_Canvas/%03d.img" % img_no,
                       "skill/%07d/icon" % sid, "源端本体画布", True))
        # 强化核心：原端 400004021 的 icon 外链直接指向 psdSkill 里的真实技能。
        for real in psd_skills(images, sid):
            real_no = real // 10000
            result.append(("Skill/_Canvas/%03d/%07d.img" % (real_no, real),
                           "skill/%07d/icon" % real, "本机分片", False))
            result.append(("Skill/_Canvas/%03d.img" % real_no,
                           "skill/%07d/icon" % real, "源端画布", True))
        result.append(("Skill/%03d.img" % img_no,
                       "skill/%07d/icon" % sid, "结构桩", False))
    return result


def resolve_canvas(images: Images, rel: str, path: str, source: bool):
    """跟随结构侧 1×1 桩的 _outlink，直到真正带像素的画布。"""
    canvas, img = images.canvas(rel, path, source)
    for _ in range(8):
        if canvas is None:
            return None, None
        link = canvas.get("_outlink")
        if link is None or not str(link.value):
            break
        f, rest = split_link(str(link.value))
        nxt, nxt_img = images.canvas(f, rest, source=False)
        if nxt is None:
            nxt, nxt_img = images.canvas(f, rest, source=True)
        if nxt is None:
            return None, None
        canvas, img = nxt, nxt_img
    return canvas, img


def find_pixels(images: Images, candidates) -> tuple[tuple[bytes, int, int, int], str] | tuple[None, str]:
    for rel, path, tag, source in candidates:
        canvas, img = resolve_canvas(images, rel, path, source)
        if canvas is None or (canvas.format not in SUPPORTED and canvas.format not in (2050, 4098)):
            continue
        try:
            if opaque(canvas_rgba(canvas, img)) <= 32:
                continue
            payload = argb4444_payload(canvas, img)
        except Exception:
            continue
        return payload, "%s/%s (%s)" % (rel, path, tag)
    return None, ""


def register(index: dict, rel: str, digest: str) -> None:
    files = index["files"]
    for entry in files:
        if entry["path"] == rel:
            entry["sha256"] = digest
            return
    files.append({"path": rel, "sha256": digest})


def build_extra(images: Images, entries: dict[str, dict[int, tuple[bytes, int, int, int]]]) -> bytes:
    """把补强图标写进一个全新的 GMS/ARGB4444 IMG：根节点下 v/ 与 h/。"""
    tpl = images.get(VCORE_CANVAS)
    if tpl is None:
        raise SystemExit("缺少模板 " + VCORE_CANVAS)
    root = tpl.root
    root._children.clear()
    for group, table in entries.items():
        group_node = WzSubProperty(group, root)
        root._children[group] = group_node
        for core_id, (fmt, w, h, payload) in sorted(table.items()):
            node = WzSubProperty(str(core_id), group_node)
            group_node._children[str(core_id)] = node
            canvas = WzCanvasProperty("icon", node)
            node._children["icon"] = canvas
            canvas.width, canvas.height = w, h
            canvas.format, canvas.format2 = fmt, 0
            canvas._png_data = payload
    return encode_image_body(tpl, tpl.wz_file.reader)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    images = Images()
    v_links = outlink_paths(images, "Etc/VCore.img", "vSkill/CoreData")
    h_links = outlink_paths(images, "Etc/HexaCore.img", "hexaSkill/coreData")
    vmatrix = json.loads((UI / "VMatrix.json").read_text(encoding="utf-8"))
    hexamatrix = json.loads((UI / "HexaMatrix.json").read_text(encoding="utf-8"))
    vcores = {int(k): v for k, v in vmatrix["cores"].items()}
    hcores = {int(k): v for k, v in hexamatrix["cores"].items()}

    # 1. 原端直引路径：验证目标画布真实存在、格式可解、有像素。
    v_paths, h_paths = {}, {}
    stats = Counter()
    for table, links, owner, dest in (("V", v_links, vcores, v_paths),
                                      ("HEXA", h_links, hcores, h_paths)):
        for core_id, link in links.items():
            if core_id not in owner:
                stats[table + " 外链核心不在核心表"] += 1
                continue
            f, rest = split_link(link)
            canvas, img = images.canvas(f, rest, source=(f == HEXA_CANVAS))
            # VCore 画布本机已转 GMS；HexaCore 画布本次才迁移，源端读 BMS。
            if canvas is None and f == HEXA_CANVAS:
                canvas, img = images.canvas(f, rest, source=True)
            if canvas is None:
                stats[table + " 目标画布缺失"] += 1
                continue
            if canvas.format not in SUPPORTED:
                stats[table + " 格式不受支持"] += 1
                continue
            dest[core_id] = link
            stats[table + " 原端直引"] += 1

    # 2. 没有直引路径的核心：项目已有 skillicon 的保持回退，其余按 psdSkill 补强。
    extra = {"v": {}, "h": {}}
    fallback = Counter()
    fallback_ids = {"v": set(), "h": set()}
    unresolved = []
    for group, cores, paths, links in (("v", vcores, v_paths, v_links),
                                       ("h", hcores, h_paths, h_links)):
        for core_id, core in cores.items():
            if core_id in paths:
                continue
            if core.get("type") == 2 and group == "v":
                fallback[group + " 特殊核心走 SpecialIcon"] += 1
                fallback_ids[group].add(core_id)
                continue
            # 运行时回退 skillicon/<skills[0]>：只有确实画得出像素才算已覆盖。
            skills = core.get("skills") or []
            if skills:
                sid = int(skills[0])
                cands = [("Skill/%03d.img" % (sid // 10000),
                          "skill/%07d/icon" % sid, "结构桩", False)]
                got, _ = find_pixels(images, cands)
                if got is not None:
                    fallback[group + " skillicon 回退"] += 1
                    fallback_ids[group].add(core_id)
                    continue
            got, why = find_pixels(images, boost_candidates(images, core))
            if got is None:
                stats[group + " 仍无图标"] += 1
                unresolved.append((group, core_id, core.get("type"), skills[:2], core.get("name")))
                continue
            extra[group][core_id] = got
            paths[core_id] = "%s/%s/%d/icon" % (EXTRA_CANVAS, group, core_id)
            stats[group + " psdSkill 补强"] += 1

    print("V 核心 %d：直引 %d，补强 %d，skillicon 回退 %d，特殊核心 %d，仍缺 %d" % (
        len(vcores), stats["V 原端直引"], stats["v psdSkill 补强"],
        fallback["v skillicon 回退"], fallback["v 特殊核心走 SpecialIcon"], stats["v 仍无图标"]))
    print("HEXA 核心 %d：直引 %d，补强 %d，skillicon 回退 %d，仍缺 %d" % (
        len(hcores), stats["HEXA 原端直引"], stats["h psdSkill 补强"],
        fallback["h skillicon 回退"], stats["h 仍无图标"]))
    for k, n in sorted(stats.items()):
        if "缺失" in k or "不支持" in k or "仍无" in k:
            print("  ! %s: %d" % (k, n))

    # 3. 逐职业覆盖报告（用户要求「每个职业都检查下」）
    bad_jobs = []
    for table, paths, covered in (("VMatrix.json", v_paths, fallback_ids["v"]),
                                  ("HexaMatrix.json", h_paths, fallback_ids["h"])):
        d = vmatrix if table.startswith("V") else hexamatrix
        for job, sections in d["groups"].items():
            ids = [int(c) for group in sections.values() for c in group]
            miss = [c for c in ids if c not in paths and c not in covered]
            if miss:
                bad_jobs.append((table, job, len(miss), len(ids), miss[:6]))
    print("逐职业：%d 个职业组，%d 组存在无图标核心" % (
        len(vmatrix["groups"]) + len(hexamatrix["groups"]), len(bad_jobs)))
    for item in bad_jobs[:15]:
        print("   ", item)
    if unresolved:
        print("仍无图标核心明细：")
        for u in unresolved[:20]:
            print("   ", u)

    if not args.write:
        print("校验完成（未写入）。加 --write 落盘。")
        return 0

    # 4. 落盘：HexaCore 画布等长 XOR 转 GMS；补强画布写独立 IMG；登记清单。
    raw = (SRC / HEXA_CANVAS).read_bytes()
    data, _ = codec.transcode(raw)
    codec.verify(raw, data, Path(HEXA_CANVAS).name)
    (DST / HEXA_CANVAS).parent.mkdir(parents=True, exist_ok=True)
    (DST / HEXA_CANVAS).write_bytes(data)
    extra_bytes = build_extra(images, extra)
    (DST / EXTRA_CANVAS).write_bytes(extra_bytes)

    table = {
        "source": {
            "client": "/Users/lizixian/Documents/mxd/MapleStory/Data",
            "v": "Etc/VCore.img/vSkill/CoreData/<coreId>/icon._outlink",
            "hexa": "Etc/HexaCore.img/hexaSkill/coreData/<coreId>/icon._outlink",
            "boost": "Skill/<img>.img skill/<id>/psdSkill 的真实技能图标（400004021 先例）",
            "note": "强化核心原端是 1~4 技能切角合成（core/iconMask）；本工程沿用单图标口径，不新绘素材",
        },
        "v": {str(k): v for k, v in sorted(v_paths.items())},
        "h": {str(k): v for k, v in sorted(h_paths.items())},
    }
    OUT_JSON.write_text(json.dumps(table, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")

    index = json.loads(INDEX.read_text(encoding="utf-8"))
    register(index, HEXA_CANVAS, hashlib.sha256(data).hexdigest())
    register(index, EXTRA_CANVAS, hashlib.sha256(extra_bytes).hexdigest())
    INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print("已写入 %s (%d B)、%s (%d B)、%s、NativeAssetIndex.json" % (
        HEXA_CANVAS, len(data), EXTRA_CANVAS, len(extra_bytes), OUT_JSON.name))
    return 0


if __name__ == "__main__":
    sys.exit(main())
