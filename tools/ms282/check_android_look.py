#!/usr/bin/env python3
"""机器人纸娃娃全模板静态校验（阶段 4 真机前自查）。

复刻 Assets/Scripts/Wz/AndroidLook.cs 的拼装与回退规则，对
Assets/Resources/Bot/AndroidTemplates.json 里的**每一个**模板算一遍最终 id 数组，
再逐项断言资源真的在 NativeAssetIndex.json 里。真机上「装上去什么都不显示」
基本都出在这里，本机可以先挡掉。

同时校验 WzCharacter.Category 的段位映射：AndroidLook.Exists 用 EquipCatalog.DirOf
找目录，而合图时走 WzCharacter.Category——两张表不一致时 DirOf 说「文件在」、
Category 却抛 NotSupportedException，整只机器人崩掉。

用法：python3 tools/ms282/check_android_look.py [-v]
"""
from __future__ import annotations

import argparse
import collections
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEMPLATES = ROOT / "Assets/Resources/Bot/AndroidTemplates.json"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
ITEMS = ROOT / "Assets/Resources/Bot/Items.json"

# 一期排除的两件 androidAni（8bpp 预渲染帧，装上会崩），见 11 文档 §7.1
EXCLUDED_ROBOTS = {1662303, 1662304}

FALLBACK_SKIN = 2000  # NativeAndroidService.FallbackSkin

# EquipCatalog.DirOf / WzCharacter.Category 的同一张表（两边必须一致）
DIR_OF = {
    0: "", 1: "",
    2: "Face", 5: "Face",
    3: "Hair", 4: "Hair", 6: "Hair",
    100: "Cap", 101: "Accessory", 102: "Accessory", 103: "Accessory",
    104: "Coat", 105: "Longcoat", 106: "Pants", 107: "Shoes",
    108: "Glove", 109: "Shield", 110: "Cape",
}

# WzCharacter.Category 认得（不会抛异常）的段
CATEGORY_OK = set(DIR_OF) | set(range(121, 161)) | {169, 170, 166, 167}


def dir_of(item_id: int) -> str | None:
    t = item_id // 10000
    if t in DIR_OF:
        return DIR_OF[t]
    if 121 <= t <= 160 or t in (169, 170):
        return "Weapon"
    if t == 166:
        return "Android"
    if t == 167:
        return "Android" if item_id // 1000 == 1672 else "Accessory"
    return None


def img_path(item_id: int) -> str | None:
    d = dir_of(item_id)
    if d is None:
        return None
    return "Character/" + (d + "/" if d else "") + f"{item_id:08d}.img"


def check_icons(files: set[str]) -> dict[int, str] | None:
    """逐件读 info/icon 的 _outlink，返回 {itemId: 目标 img 路径}；环境里没有 wzpy 时返回 None。

    282 的机器人图标不是「按自身 id 同名画布」：1662004 借 01662000 的、史烏(1662053) 借
    01662002 的。所以必须真读 _outlink，不能按文件名猜。
    """
    try:
        import os

        wzpy = os.environ.get("BEIDOU_WZPY") or str(Path.home() / "Documents/mxd/BeiDou-Server/tool/wz-python")
        if wzpy not in sys.path:
            sys.path.insert(0, wzpy)
        from wzpy import WzImage, WzKey
    except Exception:
        return None

    data = ROOT.parent / "MapleDashResources/Data"
    if not data.exists():
        return None
    key = WzKey.for_region("GMS")
    result: dict[int, str] = {}
    for k in json.loads(ITEMS.read_text(encoding="utf-8"))["items"]:
        iid = int(k)
        if not (1662000 <= iid < 1680000) or iid in EXCLUDED_ROBOTS:
            continue
        path = data / "Character/Android" / f"{iid:08d}.img"
        if not path.exists():
            result[iid] = "<主文件缺失>"
            continue
        try:
            img = WzImage.from_file(str(path), key=key)
            info = img.get("info")
            icon = info.get("icon") if info is not None else None
            link = icon.get("_outlink") if icon is not None else None
            target = getattr(link, "value", None)
            if not target:
                result[iid] = "<icon 无 _outlink>"
                continue
            result[iid] = target[: -len("/info/icon")] if target.endswith("/info/icon") else target
        except Exception as error:  # 坏文件不要中断整轮检查
            result[iid] = f"<解析失败 {error}>"
    return result


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    files = {f["path"] for f in json.loads(INDEX.read_text(encoding="utf-8"))["files"]}
    templates = json.loads(TEMPLATES.read_text(encoding="utf-8"))["templates"]

    def exists(item_id: int) -> bool:
        p = img_path(int(item_id))
        return p is not None and p in files

    def pick(pool: list[int], picked: int) -> int:
        """AndroidLook.Available 的复刻：以 picked 为起点沿池向后找第一个存在的。"""
        pool = [int(x) for x in (pool or [])]
        if not pool:
            return picked if exists(picked) else 0
        start = pool.index(picked) if picked in pool else 0
        for i in range(len(pool)):
            cand = pool[(start + i) % len(pool)]
            if exists(cand):
                return cand
        return picked if exists(picked) else 0

    fails: list[str] = []
    missing = collections.Counter()
    missing_samples: dict[str, list[int]] = {}
    ok_solo = 0
    id_counts = collections.Counter()

    for tid, tpl in templates.items():
        pools = {k: tpl.get(k) or [] for k in ("skin", "hair", "face")}
        basic = tpl.get("basic") or {}

        skin = int(pools["skin"][0]) if pools["skin"] else FALLBACK_SKIN
        if not (exists(skin) and exists(skin + 10000)):
            skin = FALLBACK_SKIN if exists(FALLBACK_SKIN) and exists(FALLBACK_SKIN + 10000) else 0
        if skin == 0:
            fails.append(f"{tid}: 连兜底肤色 {FALLBACK_SKIN} 的 body/head 都缺，整只不显示")
            continue

        ids = [f"{skin:08d}", f"{skin + 10000:08d}"]
        face = pick(pools["face"], int(pools["face"][0]) if pools["face"] else 0)
        hair = pick(pools["hair"], int(pools["hair"][0]) if pools["hair"] else 0)
        if face:
            ids.append(f"{face:08d}")
        if hair:
            ids.append(f"{hair:08d}")
        if not face and not hair:
            fails.append(f"{tid}: 脸和发一件可用都没有（模板池 face={len(pools['face'])} hair={len(pools['hair'])}）")

        # basic 是模板自带穿着，没有回退（282 里每个模板都自带完整一套）
        longcoat = int(basic.get("longcoat") or 0) // 10000 == 105
        for key in ("cap", "faceAccessory", "top", "bottom", "shoes", "gloves", "cape"):
            v = int(basic.get(key) or 0)
            if v <= 0:
                continue
            if key == "bottom" and longcoat:
                continue
            if exists(v):
                ids.append(f"{v:08d}")
            else:
                pool = pools.get(key) or []
                missing[dir_of(v) or "?"] += 1
                missing_samples.setdefault(dir_of(v) or "?", []).append(v)

        # 段位映射：Exists 找得到目录但 Category 会抛异常 → 合图时整只崩
        for raw in ids:
            seg = int(raw) // 10000
            if seg not in CATEGORY_OK:
                fails.append(f"{tid}: id {raw} 的段位 {seg} 不在 WzCharacter.Category 里（会抛异常）")

        id_counts[len(ids)] += 1
        if len(ids) <= 2:
            fails.append(f"{tid}: 只有身体+头（没有任何发型/脸/穿着），真机上是白模")
        else:
            ok_solo += 1

    print(f"模板 {len(templates)} 个，可显示 {ok_solo} 个，失败 {len(fails)} 个")
    print("id 数量分布：" + "  ".join(f"{k}件×{v}" for k, v in sorted(id_counts.items())))

    # 图标可达性：282 把机器人/心臟的 info/icon 做成 1×1 占位 + _outlink 指向
    # Character/Android/_Canvas/<某某>.img/info/icon。注意**不是按自身 id**——1662004 借的是
    # 01662000 的画布，史烏(1662053) 借 01662002。所以必须真读 _outlink，按同名文件判会误报。
    # _Canvas 没导进来时装备格/背包格里是空白（看着像"没装上"），实体却是正常的。
    icon_links = check_icons(files)
    if icon_links is None:
        print("图标画布：跳过（找不到 wzpy，设 BEIDOU_WZPY 指向 BeiDou-Server/tool/wz-python）")
    else:
        bad = sorted(f"{iid}→{target}" for iid, target in icon_links.items() if target not in files)
        print(f"图标画布：{len(icon_links)} 件，缺目标 {len(bad)} 件")
        if bad:
            fails.append(f"{len(bad)} 件机器人/心臟的图标 _outlink 指向不存在的文件：{bad[:6]}")

    if missing:
        print("basic 缺件（已跳过，不影响其他部件）：")
        for cat, n in sorted(missing.items()):
            samples = sorted(set(missing_samples[cat]))[:6]
            print(f"  {cat:12s} {n:4d} 次  例：{samples}")
    if fails:
        print("\n失败：")
        for line in fails[:40]:
            print("  ✗ " + line)
        if len(fails) > 40:
            print(f"  ... 还有 {len(fails) - 40} 条")
        return 1
    if args.verbose:
        print("\n全部模板：")
        for tid in sorted(templates, key=int):
            print(f"  {tid}  skin={templates[tid].get('skin')}  hair={len(templates[tid].get('hair') or [])}  "
                  f"face={len(templates[tid].get('face') or [])}")
    print("OK：每个模板都能拼出可显示的纸娃娃")
    return 0


if __name__ == "__main__":
    sys.exit(main())
