#!/usr/bin/env python3
"""导出「任务浏览索引」—— 任务挂靠「大地区 → 城镇」所需的两张全局表。

    python3 tools/export_quest_index.py [--data <Data 目录>] [--jobs N]

为什么离线导出（对齐源端 gms-server QuestBrowseService 的懒建索引，但把成本前移）：
    任务归属规则是「任务起始 NPC → 该 NPC 出现的地图 → 该地图在 String/Map.img 里的
    大地区/街道」。「NPC → 地图」必须扫全量 Map/Map/**/*.img 的 life 子树（6000+ 张图），
    源端在首次请求时现扫 XML 要 1~2 秒；Unity 这边后台 HTTP 跑在 ThreadPool 线程上，
    在请求线程里扫 6000+ 张 IMG 会把游戏卡死。所以这里用 wzpy 多进程离线扫一次，
    运行时（AdminQuestBrowse）只读产物 JSON，任务元数据仍现读 Quest/*.img（纯属性树，便宜）。

产物 Assets/Resources/Quest/QuestBrowseIndex.json：
    {
      "npcMaps": { "<npcId>": [mapId, ...] },       # life 里 type=="n" 的 NPC 出现过的地图
      "mapInfo": { "<mapId>": {"region": "...", "town": "..."} }
    }
    - npcId/mapId 均按「去前导零的十进制字符串/数字」归一（与源端 normalizeId 一致）；
    - mapInfo 只收录 String/Map.img 里出现的地图：源端 mapLookup 也只认 String/Map.img，
      不在 lookup 里的地图直接跳过、不参与归属；region 取顶层分组名（china/chinese 等
      嵌套分组仍归 china，与源端 collectMapNodes 一致），town 取该节点的 streetName；
    - mapInfo 只存 region/town 两个短字符串，控制产物体积（不落地图名等冗余字段）。

消费方：Assets/Scripts/Admin/AdminQuestBrowse.cs（后台任务浏览 /quest/v1/*）。
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
OUT = ROOT / "Assets/Resources/Quest/QuestBrowseIndex.json"

WZPY = "/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python"
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402

KEY = WzKey.for_region("GMS")


def wz(rel: str) -> WzImage:
    img = WzImage.from_bytes(open(rel, "rb").read(), key=KEY, name=os.path.basename(rel)[:-4])
    img.parse()
    return img


def text(node, name, default=""):
    if node is None:
        return default
    c = node.get(name)
    if c is None:
        return default
    v = getattr(c, "value", default)
    return v if isinstance(v, str) and v else default


def norm_id(value):
    """去前导零归一 id；非数字返回 None（与源端 QuestBrowseService.normalizeId 一致）。"""
    if value is None:
        return None
    try:
        return str(int(str(value).strip()))
    except (TypeError, ValueError):
        return None


# ── String/Map.img：地图 → 大地区/街道 ──────────────────────────────
def load_map_info(data: Path) -> dict:
    """递归展开 String/Map.img 的（可能嵌套的）地区分组。

    与源端 buildMapLookup/collectMapNodes 对齐：
      - region 取顶层分组名，嵌套分组（如 china/chinese）不改归属；
      - 节点名是数字（去前导零后）就算一张地图，取它的 streetName；
      - 同一地图 id 只记第一次出现（源端 seen 集合）。
    """
    out: dict = {}
    path = data / "String/Map.img"
    if not path.exists():
        return out
    root = wz(str(path))

    def walk(node, region: str):
        key = norm_id(node.name)
        if key is not None and key not in out:
            out[key] = {"region": region, "town": text(node, "streetName")}
        for child in node.children():
            walk(child, region)

    for region_node in root.children():
        walk(region_node, region_node.name)
    return out


# ── Map/Map/**/*.img：life 子树 → NPC 出现过的地图 ──────────────────
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


def read_life(item):
    """单张地图：只解析 life 子树（parse_partial 跳过 Canvas 等大块），收集 type=="n" 的 NPC。"""
    map_id, path = item
    npcs = []
    try:
        img = WzImage.from_file(path, key=KEY)
        root = img.parse_partial(only=frozenset({"life"}))
        life = root.get("life")
        if life is not None:
            for entry in life.children():
                if text(entry, "type") != "n":
                    continue
                npc = norm_id(getattr(entry.get("id"), "value", None))
                if npc is not None:
                    npcs.append(npc)
    except Exception as exc:  # 单张坏图不能拖垮全量导出
        return {"id": map_id, "npcs": [], "broken": str(exc)}
    return {"id": map_id, "npcs": npcs}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", default=str(DEFAULT_DATA), help="Data 目录（默认 MapleDashResources/Data）")
    ap.add_argument("--jobs", type=int, default=os.cpu_count() or 4)
    args = ap.parse_args()

    data = Path(args.data)
    if not (data / "Map/Map").exists():
        data = SOURCE_DATA
    files = scan_map_files(data)
    map_info = load_map_info(data)
    print(f"Data = {data}")
    print(f"地图 IMG {len(files)} 张，String/Map.img 目录 {len(map_info)} 条")

    npc_maps: dict = {}
    broken = []
    with ProcessPoolExecutor(max_workers=args.jobs) as pool:
        for i, rec in enumerate(pool.map(read_life, files, chunksize=32)):
            if i and i % 1000 == 0:
                print(f"  ... {i}/{len(files)}", flush=True)
            if rec.get("broken"):
                broken.append(rec)
            for npc in rec["npcs"]:
                npc_maps.setdefault(npc, set()).add(rec["id"])

    payload = {
        "version": 1,
        "mapFileCount": len(files),
        "mapInfoCount": len(map_info),
        "npcCount": len(npc_maps),
        "npcMaps": {k: sorted(v) for k, v in sorted(npc_maps.items(), key=lambda kv: int(kv[0]))},
        "mapInfo": {k: map_info[k] for k in sorted(map_info, key=int)},
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")

    links = sum(len(v) for v in npc_maps.values())
    print(f"写出 {OUT}  ({OUT.stat().st_size / 1024:.0f} KiB)")
    print(f"NPC {len(npc_maps)} 个 / NPC-地图归属 {links} 条 / 坏图 {len(broken)} 张")
    for b in broken[:10]:
        print("  坏图:", b["id"], b["broken"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
