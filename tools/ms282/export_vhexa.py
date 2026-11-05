#!/usr/bin/env python3
"""导出 V 矩阵 / HEXA 矩阵的核心元数据 → Assets/Resources/UI/{VMatrix,HexaMatrix}.json

数据来源（只读，均为 282 原端）：

    TMS282.5/data/protocol/tms282.3/vmatrix-data.json
        = 282 客户端 Etc/VCore.img 的完整转储（580 个核心 + 51 职业 coreGroup
          + 核心宝石 + V 点费用表 + info）。服务端 Tms282VMatrixData 读的就是它。
    TMS282.5/data/wz282/Etc.wz/HexaCore.img.xml
        = 282 客户端 Etc/HexaCore.img（hexaSkill/coreData 563 核心 + jobCore 51 职业组
          + hexaStat 三档属性核心 + solErda 九档气息）。

为什么不能只抄「种子」：V 矩阵的槽位只是位置，核心等级才决定技能等级
（服务端 effectiveSkills 由 core.level 派生），所以客户端必须拿到全部 580 个核心
的 type/maxLevel/selfOpen/技能映射，否则点开槽位无法判断可强化到几级。

输出里所有中文都过 zhconv 转简体，与项目其它数据文件一致。

用法：
    python3 tools/ms282/export_vhexa.py
    python3 tools/ms282/export_vhexa.py --check
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]       # MapleDash/
OUT = ROOT / "Assets/Resources/UI"
SRC = Path("/Users/lizixian/Documents/mxd/TMS282.5")
VMATRIX_SRC = SRC / "data/protocol/tms282.3/vmatrix-data.json"
HEXA_SRC = SRC / "data/wz282/Etc.wz/HexaCore.img.xml"

# 本项目支持的 17 个职业（四转 ID），与 tools/export_jobs.py 的 ADVANCED 一致。
JOBS = [112, 122, 132, 212, 222, 232, 312, 322, 412, 422, 512, 522,
        1112, 1212, 1312, 1412, 1512]


def simplify(text: str) -> str:
    try:
        from zhconv import convert
        return convert(text, "zh-cn")
    except Exception:
        return text


# ———————————————————————————— V 矩阵 ————————————————————————————

def _values(node, key: str) -> list:
    """CoreData 的列表字段有两形：{"0": 400001000} 或直接是标量。"""
    raw = node.get(key)
    if isinstance(raw, dict):
        return list(raw.values())
    if raw is None:
        return []
    return [raw]


def export_vmatrix() -> dict:
    document = json.loads(VMATRIX_SRC.read_text(encoding="utf-8"))
    data = document["data"]
    vskill = data["vSkill"]
    info = data["info"]
    points = data["vPoint"]

    cores = {}
    for key, node in vskill["CoreData"].items():
        cores[key] = {
            "id": int(key),
            "name": simplify(node.get("name", "")),
            "desc": simplify(node.get("desc", "")),
            "type": int(node.get("type", -1)),
            "maxLevel": int(node.get("maxLevel", 1)),
            "expireDays": int(node.get("expireAfter", 0) or 0),
            # 282: notAbleSelfOpen == 0 表示可以自己开（不需要宝石道具）
            "selfOpen": int(node.get("notAbleSelfOpen", 0) or 0) == 0,
            "skills": [int(v) for v in _values(node, "connectSkill")],
            "subSkills": [int(v) for v in _values(node, "subSkill")],
            "cooldownSkills": [int(v) for v in _values(node, "checkCooltimeSkill")],
            "removalSkills": [int(v) for v in _values(node, "checkRemoveSkill")],
            "equipRemovalSkills": [int(v) for v in _values(node, "NotDuringEquipRemove")],
            "jobs": [v for v in _values(node, "job")],
        }
        if node.get("spCoreOption"):
            cores[key]["special"] = node["spCoreOption"]

    groups = {}
    for job, node in vskill["coreGroup"].items():
        groups[job] = {section: [int(v) for v in (node.get(section) or {}).values()]
                       for section in ("skill", "enforce", "common")}

    gemstones = []
    for node in data["coreGemstone"].values():
        gemstones.append({
            "itemIds": [int(v) for v in (node.get("id") or {}).values()],
            "points": int(node.get("vPoint", 0)),
            "openCore": int(node.get("openCore", 0) or 0),
            "reqQuest": int(node.get("reqQuest", 0) or 0),
            "questAlert": simplify(node.get("questAlert", "") or ""),
            "animation": int(node.get("animationID", 0) or 0),
        })

    return {
        "source": {
            "file": str(VMATRIX_SRC),
            "wz": "Etc/VCore.img",
            "clientsha": document.get("sourceSha256", "")[:16],
            "note": "vSkill.CoreData 580 核心 / coreGroup 51 职业；槽位分区分 4+6+21=31（见 UI/VMatrixUI.img）",
        },
        "info": {
            "resetMeso": int(info["resetMeso"]),
            "registerSpecialCost": int(info["registerSpecialCorevPoint"]),
            "extendSpecialCost": int(info["extendSpecialCorevPoint"]),
            "transformCost": int(info["vPointTransformValue"]),
            "transformItem": int(info["vPointTransformID"]),
            "transformMinutes": int(info["vPointTransformPeriod"]),
        },
        "costs": {
            "skill": {k: int(v) for k, v in (points.get("skill") or {}).items()},
            "enforce": {k: int(v) for k, v in (points.get("enforce") or {}).items()},
        },
        "gemstones": gemstones,
        "groups": groups,
        "cores": dict(sorted(cores.items(), key=lambda kv: int(kv[0]))),
    }


# ———————————————————————————— HEXA 矩阵 ————————————————————————————

def _int(node, name: str, default: int = 0) -> int:
    child = node.find(f'*[@name="{name}"]')
    if child is None or child.get("value") is None:
        return default
    try:
        return int(child.get("value"))
    except ValueError:
        return default


def _str(node, name: str, default: str = "") -> str:
    child = node.find(f'*[@name="{name}"]')
    return child.get("value") if child is not None and child.get("value") is not None else default


def _dict_ints(node) -> list[int]:
    if node is None:
        return []
    return [int(child.get("value")) for child in node if child.get("value")]


def export_hexa() -> dict:
    root = ET.parse(HEXA_SRC).getroot()
    by_name = {node.get("name"): node for node in root}

    sol = by_name["solErda"]
    tiers = []
    for tier in sol:
        if tier.tag != "imgdir":
            continue
        tiers.append({
            "exp": _int(tier, "solErdaExp"),
            "itemIds": _dict_ints(tier.find('*[@name="id"]')),
        })

    skill_group = by_name["hexaSkill"]
    core_data = skill_group.find('*[@name="coreData"]')
    cores = {}
    for node in core_data:
        cores[node.get("name")] = {
            "id": int(node.get("name")),
            "name": simplify(_str(node, "name")),
            "desc": simplify(_str(node, "desc")),
            "type": _int(node, "type"),
            "maxLevel": _int(node, "maxLevel", 1),
            "skills": _dict_ints(node.find('*[@name="connectSkill"]')),
            "subSkills": _dict_ints(node.find('*[@name="subSkill"]')),
            "reqForActivation": _dict_ints(node.find('*[@name="reqForActivation"]')),
            "requiredSkills": {n.get("name"): int(n.get("value")) for n in
                               (list(node.find('*[@name="reqForActivation"]')) if node.find('*[@name="reqForActivation"]') is not None else [])},
        }

    job_group = skill_group.find('*[@name="jobCore"]')
    groups = {}
    for job in job_group:
        groups[job.get("name")] = {
            section: _dict_ints(job.find(f'*[@name="{section}"]'))
            for section in ("mastery", "enforce", "skill", "common")
        }

    stat_group = by_name["hexaStat"]
    stats = {}
    for node in stat_group.find('*[@name="coreData"]'):
        stats[node.get("name")] = {
            "id": int(node.get("name")),
            "name": simplify(_str(node, "name")),
            "desc": simplify(_str(node, "desc")),
            "reqLevel": _int(node, "reqLevel"),
            "maxLevel": _int(node, "maxLevel", 1),
            "reqCore": _int(node, "reqCore"),
        }

    return {
        "source": {
            "file": str(HEXA_SRC),
            "wz": "Etc/HexaCore.img",
            "note": "hexaSkill/coreData 563 核心 / jobCore 51 职业 / hexaStat 3 档属性核心",
        },
        "solErda": {
            "gaugeMax": _int(sol, "gaugeMax"),
            "slotMax": _int(sol, "slotMax"),
            "tiers": tiers,
        },
        "cores": dict(sorted(cores.items(), key=lambda kv: int(kv[0]))),
        "groups": groups,
        "stats": dict(sorted(stats.items(), key=lambda kv: int(kv[0]))),
        "statValues": {section.get("name"): {
            kind.get("name"): [int(next(iter(lv)).get("value")) for lv in
                               kind.find('*[@name="level"]')]
            for kind in section.find('*[@name="type"]')}
            for section in stat_group.find('*[@name="stat"]')},
    }


def report_v(v: dict) -> None:
    cores = v["cores"]
    from collections import Counter
    kinds = Counter(c["type"] for c in cores.values())
    levels = Counter(c["maxLevel"] for c in cores.values())
    print(f"V 矩阵：cores={len(cores)} groups={len(v['groups'])} gemstones={len(v['gemstones'])}")
    print(f"  type={dict(sorted(kinds.items()))} maxLevel={dict(sorted(levels.items()))}")
    for job in JOBS:
        g = v["groups"].get(str(job))
        if g is None:
            print(f"  ! 缺少职业 {job}")
            continue
        total = sum(len(g[k]) for k in g)
        print(f"  job {job}: skill={len(g['skill'])} enforce={len(g['enforce'])} common={len(g['common'])} 合计 {total}")


def report_h(h: dict) -> None:
    cores = h["cores"]
    from collections import Counter
    kinds = Counter(c["type"] for c in cores.values())
    print(f"HEXA：cores={len(cores)} groups={len(h['groups'])} stats={len(h['stats'])} tiers={len(h['solErda']['tiers'])}")
    print(f"  type={dict(sorted(kinds.items()))} gaugeMax={h['solErda']['gaugeMax']} slotMax={h['solErda']['slotMax']}")
    for job in JOBS:
        g = h["groups"].get(str(job))
        if g is None:
            print(f"  ! 缺少职业 {job}")
            continue
        total = sum(len(g[k]) for k in g)
        print(f"  job {job}: mastery={len(g['mastery'])} enforce={len(g['enforce'])} skill={len(g['skill'])} common={len(g['common'])} 合计 {total}")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    v = export_vmatrix()
    h = export_hexa()
    report_v(v)
    report_h(h)

    # 交叉校验：本项目 17 个职业组内引用的每个技能都必须已经落盘
    # （Skill/<id//10000>.img 存在）。其它职业的核心仍在表里（面板数据完整），
    # 但不在迁移范围，故不校验。
    problems = []
    data = ROOT.parent / "MapleDashResources/Data"
    for name, table in (("V", v), ("HEXA", h)):
        inscope = set()
        for job in JOBS:
            group = table["groups"].get(str(job))
            if group is None:
                problems.append(f"{name} 缺少职业组 {job}")
                continue
            for section in group.values():
                inscope.update(section)
        for core_id in sorted(inscope):
            core = table["cores"].get(str(core_id))
            if core is None:
                problems.append(f"{name} 职业组引用了不存在的核心 {core_id}")
                continue
            for sid in core["skills"] + core.get("subSkills", []):
                if not (data / "Skill" / f"{sid // 10000}.img").is_file():
                    problems.append(f"{name} 核心 {core_id} 引用技能 {sid}，缺 Skill/{sid // 10000}.img")
    if problems:
        print(f"引用校验失败 {len(problems)} 条：")
        for p in problems[:10]:
            print("  " + p)
        raise SystemExit(1)
    print("引用校验：17 职业组内全部核心技能都有对应 Skill img")

    if args.check:
        return
    OUT.mkdir(parents=True, exist_ok=True)
    for name, payload in (("VMatrix.json", v), ("HexaMatrix.json", h)):
        path = OUT / name
        path.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        print(f"写入 {path.relative_to(ROOT)}  {path.stat().st_size / 1024:.0f} KiB")


if __name__ == "__main__":
    main()
