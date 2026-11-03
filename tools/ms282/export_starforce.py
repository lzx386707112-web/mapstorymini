#!/usr/bin/env python3
"""导出 TMS 282 星力（Star Force）完整规则表。

数据来源（全部只读，不改动）：
  * 成功率/破坏率/星级上限/chance-time
      ~/Documents/mxd/TMS282.5/data/resources/tms282/enhance/starforce.tsv
      ~/Documents/mxd/TMS282.5/data/resources/tms282/enhance/starforce-caps.tsv
      ~/Documents/mxd/TMS282.5/data/resources/tms282/enhance/starforce-settings.tsv
  * star 0..24 的 TSV 缺项、star 25..29 全部由内置常量兜底，值抄自
      ~/Documents/mxd/TMS282.5/source/src/main/java/net/swordie/ms/constants/GameConstants.java
      initEnchantRates()（第 618-650 行）/ initEnchantRatesSuperior()（第 709-730 行）
  * 费用公式复刻 GameConstants.getEnchantmentMesoCost()（第 778-819 行）——表里 meso_cost = -1，
    即所有档位都走公式
  * 星级上限 STARFORCE_LEVELS / STARFORCE_LEVELS_SUPERIOR（第 314-329 行）、
    装备特例 ItemConstants.getItemStarLimit()（第 4699-4711 行）

用法：
    python3 tools/ms282/export_starforce.py --out ../docs/tms282-skill/data/starforce-table.tsv
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path

SERVER = Path(os.path.expanduser("~/Documents/mxd/TMS282.5"))
ENHANCE = SERVER / "data/resources/tms282/enhance"

# initEnchantRates() 内置值：star -> (success, destroy)，per 10000
BUILTIN_NORMAL = {
    0: (9500, 0), 1: (9000, 0), 2: (8500, 0), 3: (8500, 0), 4: (8000, 0),
    5: (7500, 0), 6: (7000, 0), 7: (6500, 0), 8: (6000, 0), 9: (5500, 0),
    10: (4500, 0), 11: (3500, 0), 12: (3000, 0), 13: (3000, 0), 14: (3000, 0),
    15: (3000, 210), 16: (3000, 210), 17: (1500, 680), 18: (1500, 680),
    19: (1500, 850), 20: (3000, 1050), 21: (1500, 1275),
    22: (1500, 1700), 23: (1000, 1800), 24: (1000, 1800),
    25: (1000, 1800), 26: (700, 1860), 27: (500, 1900), 28: (300, 1940), 29: (100, 1980),
}

# initEnchantRatesSuperior() 内置值；注意该表保持 per-1000，TSV 是 per-10000（加载时 /10）
BUILTIN_SUPERIOR = {
    0: (500, 0), 1: (500, 0), 2: (450, 0), 3: (400, 0), 4: (400, 0),
    5: (400, 18), 6: (400, 30), 7: (400, 42), 8: (400, 60), 9: (370, 95),
    10: (350, 130), 11: (350, 162), 12: (30, 485), 13: (20, 490), 14: (10, 500),
}

STARFORCE_LEVELS = [(2147483647, -1), (137, 30), (127, 20), (117, 10), (107, 8), (95, 5)]
STARFORCE_LEVELS_SUPERIOR = [(2147483647, 15), (137, 15), (127, 10), (117, 8), (107, 5), (95, 3)]

# ItemConstants.getItemStarLimit()
ITEM_STAR_LIMIT = {
    1072870: 15, 1082556: 15, 1102623: 15, 1132247: 15,  # Sweetwater
    1182060: 22, 1182273: 22,                            # Ghost Ship / Sengoku Badge
}


def read_tsv(path: Path) -> list[list[str]]:
    out = []
    if not path.is_file():
        return out
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        out.append(line.split("\t"))
    return out[1:]  # drop header


def meso_cost(level: int, star: int, superior: bool) -> int:
    """GameConstants.getEnchantmentMesoCost()，row.mesoCost() 恒为 -1 时走公式。"""
    if superior:
        return round((1000 + math.pow(level, 3.56)) / 100) * 100
    if star < 10:
        c = 1000 + math.pow(level, 3) * (star + 1) / 25.0
    elif star == 10:
        c = 1000 + math.pow(level, 3) * math.pow(11, 2.7) / 400.0
    elif star == 11:
        c = 1000 + math.pow(level, 3) * math.pow(12, 2.7) / 220.0
    elif star == 12:
        c = 1000 + math.pow(level, 3) * math.pow(13, 2.7) / 150.0
    elif star == 13:
        c = 1000 + math.pow(level, 3) * math.pow(14, 2.7) / 110.0
    elif star == 14:
        c = 1000 + math.pow(level, 3) * math.pow(15, 2.7) / 75.0
    elif star in (15, 16):
        c = 1000 + math.pow(level, 3) * math.pow(star + 1, 2.7) / 200.0
    elif star == 17:
        c = 1000 + math.pow(level, 3) * math.pow(18, 2.7) / 150.0
    elif star == 18:
        c = 1000 + math.pow(level, 3) * math.pow(19, 2.7) / 70.0
    elif star == 19:
        c = 1000 + math.pow(level, 3) * math.pow(20, 2.7) / 45.0
    elif star == 20:
        c = 1000 + math.pow(level, 3) * math.pow(21, 2.7) / 200.0
    elif star == 21:
        c = 1000 + math.pow(level, 3) * math.pow(22, 2.7) / 125.0
    else:
        c = 1000 + math.pow(level, 3) * math.pow(star + 1, 2.7) / 200.0
    return round(c / 100) * 100


def cap_for(level: int, superior: bool, caps_rows: list[list[str]]):
    """返回 (max_stars, 说明)。caps TSV 优先，缺省回落内置数组。"""
    table = caps_rows if caps_rows else None
    if table:
        best = None
        for c in table:
            kind, maxlv, stars = c[0], int(c[1]), int(c[2])
            if (kind == "SUPERIOR") != superior:
                continue
            if level <= maxlv and (best is None or maxlv < best[1]):
                best = (stars, maxlv)
        if best:
            if best[0] == -1:
                return -1, "starforce-caps.tsv 给 -1 -> ItemConstants 物品上限（默认 30；Sweetwater 15 / 徽章 22）"
            return best[0], "starforce-caps.tsv"
    for maxlv, stars in (STARFORCE_LEVELS_SUPERIOR if superior else STARFORCE_LEVELS):
        if level <= maxlv:
            return stars, "GameConstants 内置数组"
    return -1, "ItemConstants.getItemStarLimit()"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--level", type=int, default=150, help="费用示例用的装备等级")
    ap.add_argument("--runtime-out", type=Path, help="Export per-10000 rates and raw cost overrides for C#")
    args = ap.parse_args()

    rows = read_tsv(ENHANCE / "starforce.tsv")
    caps_rows = read_tsv(ENHANCE / "starforce-caps.tsv")
    settings = read_tsv(ENHANCE / "starforce-settings.tsv")

    tsv: dict[tuple[bool, int], dict] = {}
    for c in rows:
        sup = c[0] == "SUPERIOR"
        star = int(c[1])
        tsv[(sup, star)] = {
            "success": int(c[2]), "destroy": int(c[3]),
            "down": c[4] == "1", "meso": int(c[5]), "src": c[8],
        }

    bonus_time = 0
    for c in settings:
        if c[0] == "bonus_time":
            bonus_time = int(c[1])

    lines = [
        "# TMS 282 星力规则表（export_starforce.py 生成，勿手改）",
        "# success/destroy 单位为万分之一（per 10000）。keep = 10000 - success - destroy。",
        "# down = 失败时掉一颗星（服务端 isStarDownOnFailure 已实现判定，但 ItemHandler 尚未调用）。",
        f"# bonus_time = {bonus_time}；本项目用户决定连续掉星两次后下一次必成，源端实际结算未实现。",
        f"# meso 列按 rLevel + iIncReq = {args.level} 计算；服务端 TSV 的 meso_cost 全为 -1 表示走公式。",
        "kind\tstar\tsuccess_279\tdestroy_279\tkeep_279\tdown\t"
        "success_builtin\tdestroy_builtin\tmeso\tmeso_safeguard\tnote",
    ]

    for sup, label in ((False, "NORMAL"), (True, "SUPERIOR")):
        limit = 30 if not sup else 15
        for star in range(limit):
            row = tsv.get((sup, star))
            builtin = (BUILTIN_SUPERIOR if sup else BUILTIN_NORMAL)[star]
            # 尊贵内置表是 per-1000，统一换算到 per-10000
            bs, bd = (builtin[0] * 10, builtin[1] * 10) if sup else builtin
            if row:
                s, d, down, note = row["success"], row["destroy"], row["down"], row["src"]
            else:
                s, d, down, note = bs, bd, False, "无 TSV 行 -> 内置常量"
            keep = 10000 - s - d
            cost = meso_cost(args.level, star, sup)
            lines.append(
                f"{label}\t{star}\t{s}\t{d}\t{keep}\t{1 if down else 0}\t{bs}\t{bd}\t"
                f"{cost}\t{cost * 3}\t{note}"
            )

    limits = [
        "# 星级上限（starforce-caps.tsv 优先，回落 GameConstants 内置数组）",
        "kind\treq_level\tmax_stars\tsource",
    ]
    for lv in (95, 100, 107, 117, 127, 137, 150, 200):
        for sup, label in ((False, "NORMAL"), (True, "SUPERIOR")):
            stars, src = cap_for(lv, sup, caps_rows)
            shown = stars if stars != -1 else 30
            limits.append(f"{label}\t{lv}\t{shown}\t{src}")

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n\n" + "\n".join(limits) + "\n", encoding="utf-8")
    print(f"wrote {out} ({len(lines) - 1} rows)")
    if args.runtime_out:
        runtime = {"schemaVersion": 1, "scale": 10000, "normal": [], "superior": [],
                   "itemCaps": {str(k): v for k, v in ITEM_STAR_LIMIT.items()},
                   "sourceSha256": {name: hashlib.sha256((ENHANCE / name).read_bytes()).hexdigest()
                                    for name in ("starforce.tsv", "starforce-caps.tsv", "starforce-settings.tsv")}}
        for sup, key in ((False, "normal"), (True, "superior")):
            for star, builtin in (BUILTIN_SUPERIOR if sup else BUILTIN_NORMAL).items():
                row = tsv.get((sup, star))
                scale = 10 if sup else 1
                runtime[key].append({"star": star, "success": row["success"] if row else builtin[0] * scale,
                                     "destroy": row["destroy"] if row else builtin[1] * scale,
                                     "down": row["down"] if row else False,
                                     "meso": row["meso"] if row else -1})
        args.runtime_out.parent.mkdir(parents=True, exist_ok=True)
        args.runtime_out.write_text(json.dumps(runtime, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
