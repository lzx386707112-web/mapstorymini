#!/usr/bin/env python3
"""导出「全量地图目录」——资源包里每一张 Map/Map/Map*/<id>.img 都登记在案。

    python3 tools/export_field_index.py [--data <Data 目录>]

设计前提（direct-img-runtime）：
    贴图与几何一律由运行时从原始 IMG 直读（WzAssets / NativeMapView），
    这里**不导出任何 PNG、不落 foothold/portal/life 明细**——那只会造出上百 MB 的
    重复数据，还会和 IMG 脱钩。本文件只产出「不解析 IMG 就拿不到全局视野」的元数据：

    id / name / street / town / returnMap / forcedReturn / fieldLimit / mobRate
    bgm / vr / link（重定向）/ 各类条目计数

消费方：
    tools/package_native_assets.py  —— 打包时遍历「全部」地图引用的 Tile/Obj/Back/Npc
    tools/export_sound.py           —— BGM 收集覆盖全部地图
    Assets/Editor/*Checks.cs        —— 断言目录条数 == 资源包里的地图数
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DATA = ROOT.parent / "MapleDashResources" / "Data"
SOURCE_DATA = Path("/Users/lizixian/Documents/mxd/BeiDou-Server/clien/Data")
OUT = ROOT / "Assets/Resources/Field/FieldIndex.json"

WZPY = "/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python"
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402

KEY = WzKey.for_region("GMS")


def wz(rel: str) -> WzImage:
    img = WzImage.from_bytes(open(rel, "rb").read(), key=KEY, name=os.path.basename(rel)[:-4])
    img.parse()
    return img


def num(node, name, default=0):
    if node is None:
        return default
    c = node.get(name)
    if c is None:
        return default
    v = getattr(c, "value", default)
    return v if isinstance(v, (int, float)) else default


def text(node, name, default=""):
    if node is None:
        return default
    c = node.get(name)
    if c is None:
        return default
    v = getattr(c, "value", default)
    return v if isinstance(v, str) and v else default


def raw_link(node):
    """info/link 可能是 int 也可能是字符串，统一按原样取出。"""
    if node is None:
        return None
    c = node.get("link")
    if c is None:
        return None
    v = getattr(c, "value", None)
    return v


def count_children(node) -> int:
    if node is None:
        return 0
    return len(list(node.children()))


def load_labels(data: Path):
    """String/Map.img：<大区> → <地图id> → {mapName, streetName}"""
    labels = {}
    path = data / "String/Map.img"
    if not path.exists():
        return labels
    root = wz(str(path))
    for region in root.children():
        for entry in region.children():
            if not entry.name.isdigit():
                continue
            labels[int(entry.name)] = (text(entry, "mapName"), text(entry, "streetName"))
    return labels


def scan_map_files(data: Path):
    out = []
    base = data / "Map/Map"
    for sub in sorted(os.listdir(base)):
        d = base / sub
        if not d.is_dir() or not sub.startswith("Map"):
            continue
        for name in sorted(os.listdir(d)):
            if not name.endswith(".img") or not name[:-4].isdigit():
                continue
            out.append((int(name[:-4]), str(d / name)))
    out.sort()
    return out


def read_one(item):
    map_id, path = item
    try:
        img = wz(path)
    except Exception as exc:                       # 单个坏图不能拖垮全量导出
        return {"id": map_id, "broken": str(exc)}
    info = img.get("info")
    if info is None:
        return {"id": map_id, "broken": "no info"}
    vr = [num(info, k) for k in ("VRLeft", "VRTop", "VRRight", "VRBottom")]
    has_vr = any(v != 0 for v in vr)
    rec = {
        "id": map_id,
        "town": num(info, "town"),
        "returnMap": num(info, "returnMap"),
        "forcedReturn": num(info, "forcedReturn"),
        "fieldLimit": num(info, "fieldLimit"),
        "mobRate": num(info, "mobRate", 1) or 1,
        "bgm": text(info, "bgm"),
        # 原端 info/link 是字符串（如 100020101 → "100020100"）：源端 CMapLoadable 与
        # 我们的 NativeMapView.MapRoot 都会顺着它跳到被指向的图再取几何。
        "link": int(raw_link(info)) if raw_link(info) not in ("", None) else 0,
        "hasVr": 1 if has_vr else 0,
        "vr": vr if has_vr else None,
        "fh": 0,
        "rope": count_children(img.get("ladderRope")),
        "portal": count_children(img.get("portal")),
        "back": count_children(img.get("back")),
        "life": count_children(img.get("life")),
    }
    fh = img.get("foothold")
    if fh is not None:
        rec["fh"] = sum(len(list(g.children())) for layer in fh.children() for g in layer.children())
    life = img.get("life")
    mobs = npcs = 0
    if life is not None:
        for n in life.children():
            kind = text(n, "type")
            if kind == "m":
                mobs += 1
            elif kind == "n":
                npcs += 1
    rec["mob"] = mobs
    rec["npc"] = npcs
    return rec


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", default=str(DEFAULT_DATA), help="Data 目录（默认 MapleDashResources/Data）")
    ap.add_argument("--jobs", type=int, default=os.cpu_count() or 4)
    args = ap.parse_args()

    data = Path(args.data)
    if not (data / "Map/Map").exists():
        data = SOURCE_DATA
    files = scan_map_files(data)
    labels = load_labels(data)
    print(f"Data = {data}")
    print(f"地图 IMG {len(files)} 张，String/Map.img 标签 {len(labels)} 条")

    maps = []
    with ProcessPoolExecutor(max_workers=args.jobs) as pool:
        for i, rec in enumerate(pool.map(read_one, files, chunksize=64)):
            if i and i % 1000 == 0:
                print(f"  ... {i}/{len(files)}", flush=True)
            name, street = labels.get(rec["id"], ("", ""))
            rec["name"] = name
            rec["street"] = street
            maps.append(rec)

    # link 图：几何走被指向的那张（NativeMapView.MapRoot 同规则），目录里把元数据补齐
    by_id = {m["id"]: m for m in maps}
    linked = 0
    for m in maps:
        if not m.get("link"):
            continue
        target = m["link"]
        seen = set()
        while target in by_id and by_id[target].get("link") and target not in seen:
            seen.add(target)
            target = by_id[target]["link"]
        src = by_id.get(target)
        if not src:
            continue
        if not m["name"]:
            m["name"] = src["name"]
        if not m["street"]:
            m["street"] = src["street"]
        if not m["bgm"]:
            m["bgm"] = src["bgm"]
        if not m["returnMap"]:
            m["returnMap"] = src["returnMap"]
        if not m["hasVr"]:
            m["vr"] = src["vr"]
            m["hasVr"] = src["hasVr"]
        m["fh"] = m["fh"] or src["fh"]
        linked += 1

    broken = [m for m in maps if m.get("broken")]
    towns = sorted(m["id"] for m in maps if m.get("town"))
    bgms = sorted({m["bgm"] for m in maps if m.get("bgm")})
    streets = {}
    for m in maps:
        if m.get("street"):
            streets[m["street"]] = streets.get(m["street"], 0) + 1

    payload = {
        "version": 1,
        "ppu": 40,
        "source": str(data),
        "count": len(maps),
        "townCount": len(towns),
        "linkedCount": linked,
        "streets": dict(sorted(streets.items(), key=lambda kv: -kv[1])),
        "bgms": bgms,
        "towns": towns,
        "maps": maps,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")

    print(f"写出 {OUT}  ({OUT.stat().st_size / 1024:.0f} KiB)")
    print(f"地图 {len(maps)} 张 / 城镇 {len(towns)} 张 / link 图 {linked} 张 / BGM {len(bgms)} 首 / 坏图 {len(broken)} 张")
    if broken:
        for b in broken[:10]:
            print("  坏图:", b)
    return 0 if not broken else 1


if __name__ == "__main__":
    sys.exit(main())
