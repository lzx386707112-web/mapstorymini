#!/usr/bin/env python3
"""把 Skill 资源从「一个大文件」拆成「按技能分片 + 视频独立」。

背景：282 的 `Skill/_Canvas/<job>.img` 单文件可以到 262 MB（50000），
运行时 `WzAssets.FileRoot` 会为访问其中一个图标把整棵树解出来，代价随文件
体积线性上升；而视频（`Canvas#Video` 的 MCV0 载荷）本来就与像素无关，
塞在定义 img 里只是让 `Skill/<job>.img` 也跟着涨到几十 MB。

拆分后的落盘约定（本项目自有，对齐 282「定义 / 像素」分离的思路再分一层）：

    Skill/_Canvas/<job>.img                    只剩 info 等非 skill 根分支（可能不存在）
    Skill/_Canvas/<job>/<sid>.img              单技能分片，根 = skill/<sid>
    Skill/_Canvas/<job>/<sid>/<branch>.img     单技能再按顶层分支拆，根 = skill/<sid>/<branch>
    Data/Video/skill/<sid>/<branch...>.mcv     视频载荷，定义侧只留 "mcv:<相对路径>"

定义侧 `Skill/<job>.img` 的 `_outlink` 全部重写成新路径。`_Canvas` 里的
`info` 分支保持原位（59 条跨 job 的 `_outlink` 指向它，例如 114.img 的技能书
图标在 `_Canvas/1112.img/info/icon`）。

`_Canvas` 内部实测 0 条 `_outlink`/`_inlink`（导入时已烘成真像素），所以按
技能整棵搬迁不会剪断任何链接；每片仍用 `RuntimeImgMirror` 逐节点校验结构，
并用 wzpy 独立解码比对像素。分片**边算边写**，内存峰值只有单个分片。

用法：
    python3 tools/split_skill_assets.py --dry-run
    python3 tools/split_skill_assets.py
    python3 tools/split_skill_assets.py --job 50000 --canvas-only
"""
from __future__ import annotations

import argparse
import gc
import hashlib
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]           # MapleDash/
REPO = ROOT.parent
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "ms282"))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty, WzVideoProperty  # noqa: E402
from wzpy.writer import _read_opaque_payload  # noqa: E402

from img_bake import (RuntimeImgMirror, _mismatch, check_canvas_pixels,  # noqa: E402
                      expected_tree_from, serialize)

GMS = WzKey.for_region("GMS")
PROJ_DATA = REPO / "MapleDashResources/Data"
SKILL = PROJ_DATA / "Skill"
CANVAS = SKILL / "_Canvas"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"

MAX_PART = 24 * 1024 * 1024        # 单分片上限（压缩后字节），超过就继续拆
VIDEO_TAG = b"mcv:"                # 外置视频的载荷前缀（原载荷是 MCV0 视频流）

split_index: dict[str, set[str]] = {}          # job -> 已分片的技能 id
deep_skills: set[tuple[str, str]] = set()      # 按分支拆过的 (job, sid)
deep_shapes: set[tuple[str, str, int]] = set() # (job, sid, 深度)，dry-run 预览用


# ———————————————————————————— 通用 ————————————————————————————

def load(path: Path, name: str) -> WzImage:
    img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=name)
    img.parse()
    return img


def empty_image(name: str, host: WzImage) -> WzImage:
    """借宿主 reader 造一个空 img（writer 只用它编码字符串表）。"""
    img = WzImage(name=name, parent=None, offset=0, size=0, wz_file=host.wz_file)
    img._root = WzSubProperty("root")
    img._parsed = True
    return img


def walk(node, path: str = ""):
    for child in node.children():
        here = f"{path}/{child.name}" if path else child.name
        yield child, here
        yield from walk(child, here)


def sha(blob: bytes) -> str:
    return hashlib.sha256(blob).hexdigest()


def payload_bytes(canvas) -> bytes:
    data = getattr(canvas, "_png_data", None)
    if data is not None:
        return data
    return b"\x00" * int(getattr(canvas, "_png_length", 0) or 0)


def verify_blob(blob: bytes, img: WzImage, label: str, strict: bool = False) -> None:
    """结构 + 每个画布的压缩载荷长度比对。

    像素本身是原样搬过去的（`_png_data` 不参与重编码），所以不必逐张解码——
    只有真解码才需要 `strict=True`（`check_canvas_pixels`，慢一个数量级）。
    """
    expected = expected_tree_from(img)
    mirror = RuntimeImgMirror(blob, GMS)
    problems: list[str] = []
    _mismatch(expected, mirror.root, "<root>", problems)

    stack = [(("<root>"), expected, mirror.root)]
    while stack:
        path, want, got = stack.pop()
        if want.get("kind") == "Canvas":
            length = len(payload_bytes(want["_canvas"]))
            actual = len(got.get("pixels") or b"")
            if length != actual:
                problems.append(f"{path}: payload {length} != {actual}")
        for cw, cg in zip(want.get("children") or [], got.get("children") or []):
            stack.append((f"{path}/{cw.get('name')}", cw, cg))

    if problems:
        raise SystemExit(f"{label}: 拆分后校验失败\n  " + "\n  ".join(problems[:6]))
    if strict:
        check_canvas_pixels(expected, mirror.root, problems)
        if problems:
            raise SystemExit(f"{label}: 像素比对失败\n  " + "\n  ".join(problems[:6]))


# ———————————————————————————— 像素分片 ————————————————————————————

def split_one(job: str, node, prefix: list[str], host: WzImage, out: list[dict],
              dry: bool, depth: int = 0) -> None:
    """把一个技能节点（或其分支）落到一个分片；超限且有子节点就继续往下拆。

    prefix 是 skill 之下的路径，例如 ["500001005"] 或 ["500001005", "summon3"]。
    分片文件的根固定是 `skill/...`，与原来的 `_Canvas/<job>.img` 同一口径，
    这样 outlink 只需换文件名，路径后半段原样保留。
    """
    rel = f"{job}/{'/'.join(prefix)}.img"
    img = empty_image(Path(rel).name, host)
    root = WzSubProperty("skill")
    img._root.add(root)
    parent = root
    for name in prefix[:-1]:
        middle = WzSubProperty(name)
        parent.add(middle)
        parent = middle
    parent.add(node)
    blob = serialize(img)

    children = node.children() if isinstance(node, WzSubProperty) else []
    # 单子节点也要继续拆：400021157/customEffect 只有一个 start，但 start 下面
    # 还有 0/1/2 三棵子树。旧口径 `len(children) > 1` 会把它钉死在 58.7MB。
    if len(blob) > MAX_PART and len(children) > 0 and depth < 6:
        # 超限：把子节点挂回一个同名容器，逐个降级成分片
        container = WzSubProperty(node.name)
        parent._children.pop(node.name, None)
        parent.add(container)
        for kid in list(children):
            container.add(kid)
        del blob, img
        gc.collect()
        for kid in list(children):
            split_one(job, kid, prefix + [kid.name], host, out, dry, depth + 1)
            container._children.pop(kid.name, None)
        return

    if len(blob) > MAX_PART:
        print(f"  ! {rel}: {len(blob)/1e6:.1f}MB 已无可拆子节点")
    verify_blob(blob, img, rel)
    if not dry:
        dest = CANVAS / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(blob)
    out.append({"rel": rel, "sha": sha(blob), "size": len(blob)})
    del blob, img
    gc.collect()


def split_canvas(job: str, dry: bool) -> dict | None:
    path = CANVAS / f"{job}.img"
    if not path.is_file():
        return None
    t0 = time.time()
    before = path.stat().st_size
    src = load(path, f"_Canvas/{job}.img")
    skills = src.root.get("skill")
    if skills is None:
        return None

    fragments: list[dict] = []
    for sid_node in list(skills.children()):
        split_one(job, sid_node, [sid_node.name], src, fragments, dry)

    kept = [c for c in src.root.children() if c.name != "skill"]
    leftovers = None
    if kept:
        tail = empty_image(f"{job}.img", src)
        for child in kept:
            tail._root.add(child)
        leftovers = serialize(tail)
        verify_blob(leftovers, tail, f"_Canvas/{job}.img(info)")
        if not dry:
            path.write_bytes(leftovers)

    total = sum(f["size"] for f in fragments)
    biggest = max((f["size"] for f in fragments), default=0)
    print(f"  {job:>6s} {before/1e6:8.1f}MB -> {len(fragments):4d} 片 "
          f"{total/1e6:8.1f}MB (max {biggest/1e6:5.1f}MB)"
          f"{' [留 info]' if leftovers else ''} {time.time()-t0:.1f}s", flush=True)

    if not dry and leftovers is None:
        path.unlink()
    del src, skills
    gc.collect()
    return {"fragments": fragments, "leftovers": leftovers is not None}


def split_mob_skill(dry: bool) -> dict:
    """`Skill/MobSkill.img`（51 个技能共 48 MB，其中 176 一个占 41 MB）按 id 分片。

        Skill/MobSkill/<id>.img          根 = <id>
        Skill/MobSkill/<id>/<level>.img  根 = <id>/level/<level>（单 id 超限时）

    运行时读取点只有 NativeMobController 一处，按「先平后深」探测。
    """
    path = SKILL / "MobSkill.img"
    if not path.is_file():
        return {"fragments": [], "skipped": True}
    src = load(path, "MobSkill.img")
    fragments: list[dict] = []
    deep: set[str] = set()
    t0 = time.time()

    for id_node in list(src.root.children()):
        name = id_node.name
        img = empty_image(f"{name}.img", src)
        img._root.add(id_node)
        blob = serialize(img)
        level = id_node.get("level")
        if len(blob) > MAX_PART and level is not None and len(level.children()) > 1:
            kids = list(level.children())
            for kid in kids:
                sub = empty_image(f"{kid.name}.img", src)
                outer = WzSubProperty(name)
                mid = WzSubProperty("level")
                sub._root.add(outer)
                outer.add(mid)
                mid.add(kid)
                part = serialize(sub)
                verify_blob(part, sub, f"MobSkill/{name}/{kid.name}.img")
                if not dry:
                    dest = CANVAS.parent / "MobSkill" / name / f"{kid.name}.img"
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(part)
                fragments.append({"rel": f"MobSkill/{name}/{kid.name}.img", "sha": sha(part),
                                  "size": len(part)})
                del part, sub
                gc.collect()
            deep.add(name)
            del blob, img
            gc.collect()
            continue

        verify_blob(blob, img, f"MobSkill/{name}.img")
        if not dry:
            dest = CANVAS.parent / "MobSkill" / f"{name}.img"
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(blob)
        fragments.append({"rel": f"MobSkill/{name}.img", "sha": sha(blob), "size": len(blob)})
        del blob, img
        gc.collect()

    total = sum(f["size"] for f in fragments)
    biggest = max((f["size"] for f in fragments), default=0)
    print(f"  MobSkill {path.stat().st_size/1e6:6.1f}MB -> {len(fragments):3d} 片 "
          f"{total/1e6:6.1f}MB (max {biggest/1e6:5.1f}MB) 深拆 {len(deep)} 个 "
          f"{time.time()-t0:.1f}s", flush=True)
    if not dry:
        path.unlink()
    del src
    gc.collect()
    return {"fragments": fragments, "deep": deep, "skipped": False}


# ———————————————————————————— 定义侧：视频外置 + 重写 outlink ————————————————————————————

def video_rel(sid: str, parts: list[str]) -> str:
    """skill/<sid>/<branch...>/video -> Video/skill/<sid>/<branch...>.mcv"""
    tail = parts[2:-1] if len(parts) > 2 and parts[-1] == "video" else parts[2:]
    return f"Video/skill/{sid}/{'_'.join(tail) or 'video'}.mcv"


_probe: dict[str, bool] = {}
_frag_cache: dict[str, object] = {}


def _frag_root(rel: str):
    """按需解析一个分片并缓存它的根节点（只留最近若干个，避免吃内存）。"""
    if rel in _frag_cache:
        return _frag_cache[rel]
    path = PROJ_DATA / rel
    root = None
    if path.is_file():
        img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=Path(rel).name)
        img.parse()
        root = img.root
    if len(_frag_cache) > 24:
        _frag_cache.clear()
    _frag_cache[rel] = root
    return root


def resolve_outlink(value: str) -> str | None:
    """Skill/_Canvas/<job>.img/<rest> -> 分片里的真实路径；节点/文件不存在则 None。

    分片文件名本身就编码了拆分方式（`<sid>.img` 还是 `<sid>/<branch>.img`），
    所以不需要内存状态，重复跑、单跑定义侧都不会错。
    返回 None 表示这条引用在原资源里就是悬空的（282 的 `_Canvas` 只给有像素的
    节点建条，其它画布只是 1x1 桩），调用方应删掉 `_outlink` 退回内联像素，
    否则运行时会走进 `Node()` 返回 null 的抛异常分支。
    """
    head = "Skill/_Canvas/"
    if not value.startswith(head):
        return value
    rel = value[len(head):]
    cut = rel.find(".img/")
    if cut < 0:
        return value
    stem, rest = rel[:cut], rel[cut + 5:]
    parts = rest.split("/")
    if not parts or not parts[0]:
        return value

    def hit(cand: str) -> bool:
        root = _frag_root(f"{head}{cand}.img")
        return root is not None and root.get(rest) is not None

    if parts[0] != "skill":
        # info 等非 skill 分支留在 <job>.img；已经指向分片的写法原样沿用。
        for cand in (stem, stem.split("/")[0]):
            if hit(cand):
                return f"{head}{cand}.img/{rest}"
        return None

    # rest = skill/<sid>/<分支…>。`stem` 有三种历史写法：
    #   <job>（原始） / <job>/<sid>（单技能分片） / <job>/<sid>/<分支>（深拆分片）。
    # 统一收敛成同一组候选，重复跑、单跑定义侧才幂等 —— 否则第二次重写会因为
    # 读不懂自己上次写的二级路径而把引用判成悬空删掉。
    job = stem.split("/")[0]
    if len(parts) < 3:
        return None
    sid, branch = parts[1], parts[2:]
    for depth in range(len(branch), 0, -1):
        cand = f"{job}/{sid}/" + "/".join(branch[:depth])
        if hit(cand):
            return f"{head}{cand}.img/{rest}"
    if hit(f"{job}/{sid}"):
        return f"{head}{job}/{sid}.img/{rest}"
    # dry-run：分片还没落盘，退回本进程刚算出的分片表
    if sid in split_index.get(job, ()):
        for depth in range(len(branch), 0, -1):
            if (job, sid, depth + 1) in deep_shapes:
                return f"{head}{job}/{sid}/{('/'.join(branch[:depth]))}.img/{rest}"
        return f"{head}{job}/{sid}.img/{rest}"
    return None


def rewrite_defs(job: str, dry: bool) -> dict:
    path = SKILL / f"{job}.img"
    before = path.stat().st_size
    img = load(path, f"{job}.img")
    videos: list[tuple[str, str, int]] = []
    links = 0
    dropped = 0

    for node, here in walk(img.root):
        if isinstance(node, WzVideoProperty):
            parts = here.split("/")
            sid = parts[1] if len(parts) > 1 else job
            rel = video_rel(sid, parts)
            data = _read_opaque_payload(node)
            # 已外置过（重复跑定义侧）：载荷就是 mcv:<相对路径>，原样留着即可。
            if data.startswith(VIDEO_TAG):
                continue
            if not data.startswith(b"MCV0"):
                raise SystemExit(f"{job}.img {here}: 视频载荷不是 MCV0（{data[:8]!r}）")
            if not dry:
                dest = PROJ_DATA / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
            videos.append((rel, sha(data), len(data)))
            node._data = VIDEO_TAG + rel.encode("utf-8")
            node._data_length = len(node._data)
        elif isinstance(node, WzCanvasProperty):
            link = node.get("_outlink")
            if link is not None:
                new = resolve_outlink(str(link.value))
                if new is None:
                    node._children.pop("_outlink", None)
                    dropped += 1
                elif new != str(link.value):
                    link._value = new
                    links += 1

    blob = serialize(img)
    RuntimeImgMirror(blob, GMS)
    if not dry:
        path.write_bytes(blob)
    video_bytes = sum(v[2] for v in videos)
    after = len(blob)
    del img
    gc.collect()
    return {"job": job, "sha": sha(blob), "after": after, "before": before,
            "videos": videos, "video_bytes": video_bytes, "links": links,
            "dropped": dropped}


# ———————————————————————————— 索引 ————————————————————————————

def update_index(rows: list[tuple[str, str]], removed: set[str], dry: bool) -> None:
    """索引以磁盘为准：Skill/ 与 Video/skill/ 的条目全部重扫，避免漏登分片。"""
    index = json.loads(INDEX.read_text(encoding="utf-8"))
    files = {e["path"]: e for e in index["files"]
             if not e["path"].startswith(("Skill/", "Video/skill/"))}

    for base, root in (("Skill/", CANVAS.parent), ("Video/skill/", PROJ_DATA / "Video/skill")):
        if not root.is_dir():
            continue
        for path in sorted(root.rglob("*")):
            if not path.is_file() or path.suffix not in (".img", ".mcv"):
                continue
            rel = base + path.relative_to(root).as_posix()
            files[rel] = {"path": rel, "sha256": sha(path.read_bytes())}

    index["files"] = sorted(files.values(), key=lambda e: e["path"])
    if not dry:
        INDEX.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")),
                         encoding="utf-8")


def split_after_import(jobs=None) -> None:
    """导入脚本写完 `<job>.img` / `_Canvas/<job>.img` 后立刻按分片约定拆开。

    幂等：已经拆过的 job 不会再拆，但顶层 outlink 与索引会按磁盘现状重算，
    所以重复调用安全。不传 jobs 时全量重写顶层（约 1 分钟，导入是低频操作）。
    """
    if jobs:
        for job in jobs:
            split_canvas(str(job), False)
    _frag_cache.clear()
    _probe.clear()
    targets = [str(j) for j in jobs] if jobs else sorted(p.stem for p in SKILL.glob("*.img"))
    for job in targets:
        rewrite_defs(job, False)
    update_index([], set(), False)


# ———————————————————————————— 入口 ————————————————————————————

def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--job", action="append", help="只拆这些像素文件（仅配合 --dry-run）")
    ap.add_argument("--canvas-only", action="store_true", help="只做像素分片，不碰定义侧")
    ap.add_argument("--defs-only", action="store_true", help="只做定义侧（视频/outlink）")
    ap.add_argument("--reindex-only", action="store_true",
                    help="只按磁盘重扫 Skill/Video 索引（拆分后补登用）")
    args = ap.parse_args()
    dry = args.dry_run
    if args.job and not dry:
        raise SystemExit("--job 只用于 --dry-run 摸底；真跑必须全量"
                         "（顶层 outlink 要靠完整的分片表才能重写）")

    if args.reindex_only:
        update_index([], set(), dry)
        print("索引已按磁盘重建（Skill/ + Video/skill/）")
        return

    if not args.defs_only:
        jobs = args.job or sorted(p.stem for p in CANVAS.glob("*.img"))
        print(f"像素分片：{len(jobs)} 个 _Canvas 文件，单分片上限 {MAX_PART/1e6:.0f}MB")
        for job in jobs:
            result = split_canvas(job, dry)
            if not result:
                continue
            split_index[job] = {f["rel"][len(job) + 1:].split("/")[0].split(".")[0]
                                for f in result["fragments"]}
            for f in result["fragments"]:
                inner = f["rel"][len(job) + 1:]
                if "/" in inner:
                    deep_skills.add((job, inner.split("/")[0]))
                seg = inner.replace(".img", "").split("/")
                deep_shapes.add((job, seg[0], len(seg)))
        print(f"分片表覆盖 {len(split_index)} 个 job；"
              f"跳过 {sorted(set(jobs) - set(split_index))[:8]}")
        if not args.job:
            print("MobSkill 分片")
            split_mob_skill(dry)

    if not args.canvas_only:
        print("定义侧：视频外置 + outlink 重写")
        video_total = 0
        for path in sorted(SKILL.glob("*.img")):
            row = rewrite_defs(path.stem, dry)
            video_total += row["video_bytes"]
            print(f"  {row['job']:>6s} {row['before']/1e6:8.1f}MB -> {row['after']/1e6:6.1f}MB"
                  f"  视频 {len(row['videos']):2d} ({row['video_bytes']/1e6:5.1f}MB)"
                  f"  outlink {row['links']} 悬空删 {row['dropped']}", flush=True)
        print(f"  视频外置合计 {video_total/1e6:.1f}MB")

    update_index([], set(), dry)
    print("索引已按磁盘重建（Skill/ + Video/skill/）")
    if dry:
        print("（--dry-run：未写盘）")


if __name__ == "__main__":
    main()
