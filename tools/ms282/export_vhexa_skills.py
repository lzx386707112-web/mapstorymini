#!/usr/bin/env python3
"""导出 V / HEXA 核心连接技能的等级数据 → Assets/Resources/UI/VMatrixSkills.json

V 核心与 HEXA 核心本身没有等级公式，真正带 `common` 公式的是它们连接的技能
（core.connectSkill / subSkill）。服务端的 `effectiveSkills` 把核心等级映射成技能等级，
客户端要算伤害 / 冷却 / MP 就必须拿到这些技能的 levels。

数据来源（只读）：
    /tmp/packs/Skill_<img>.img        —— 282 定义层（BMS 密钥），common 公式展开
    MapleStory/Data/String/Skill.img  —— 技能名/说明（繁体 → 简体）
    Assets/Resources/UI/{VMatrix,HexaMatrix}.json —— 上一步导出的核心表（决定技能集合）

公式语义与 tools/ms282/export_advanced_skills.py 完全一致（复用其求值器）：
    x / y = 等级；d()/u() 取整；logN 先 floor；隐式乘法；未知标识符 0；末尾 (int)。

V/HEXA 技能不走技能窗 SP（等级由核心等级决定），所以每条都打 `"hexa": true`
让 SkillBook 拒绝用 SP 升级；`core` 字段记录它属于哪个核心，便于反查。

用法：
    python3 tools/ms282/export_vhexa_skills.py            # 生成
    python3 tools/ms282/export_vhexa_skills.py --check    # 只看摘要
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
REPO = ROOT.parent
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import (  # noqa: E402
    PACKS, classify, expand, level_rows, simplify, skill_string, text_of,
)
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

BMS = WzKey.for_region("BMS")
UI = ROOT / "Assets/Resources/UI"
OUT = UI / "VMatrixSkills.json"

# 本项目支持的 17 个职业（四转 ID）——只导这些职业组内核心连接的技能。
JOBS = [112, 122, 132, 212, 222, 232, 312, 322, 412, 422, 512, 522,
        1112, 1212, 1312, 1412, 1512]

_cache: dict[int, WzImage] = {}


def definition(skill_id: int):
    img_id = skill_id // 10000
    if img_id not in _cache:
        path = PACKS / f"Skill_{img_id}.img"
        if not path.is_file():
            raise SystemExit(f"缺少 {path}（先解包 282 Data/Packs/Skill_*.ms 到 /tmp/packs）")
        image = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
        _cache[img_id] = image
    image = _cache[img_id]
    node = image.get(f"skill/{skill_id}")
    if node is None:
        raise SystemExit(f"Skill_{img_id}.img 里没有技能 {skill_id}")
    return node


def wanted() -> dict[int, tuple[str, int]]:
    """技能 id → (来源标记 v|hexa, 首个核心 id)。"""
    result: dict[int, tuple[str, int]] = {}
    for name, table in (("v", json.loads((UI / "VMatrix.json").read_text(encoding="utf-8"))),
                        ("hexa", json.loads((UI / "HexaMatrix.json").read_text(encoding="utf-8")))):
        inscope: set[int] = set()
        for job in JOBS:
            group = table["groups"].get(str(job))
            if group is None:
                raise SystemExit(f"{name} 表缺少职业组 {job}")
            for section in group.values():
                inscope.update(section)
        for core_id in sorted(inscope):
            core = table["cores"].get(str(core_id))
            if core is None:
                raise SystemExit(f"{name} 表引用了不存在的核心 {core_id}")
            for sid in core["skills"] + (core.get("subSkills") or []):
                result.setdefault(sid, (name, core_id))
    return result


def export_skill(sid: int, kind: str, core_id: int) -> dict | None:
    node = definition(sid)
    common = node.get("common")
    if common is None:
        return None
    max_level, fields = level_rows(common)
    if max_level <= 0:
        return None
    rows = expand(fields, max_level)
    name_node = skill_string(sid)
    req = []
    req_node = node.get("req")
    if req_node is not None:
        for child in req_node.children():
            try:
                req.append([int(child.name), int(getattr(child, "value", 0) or 0)])
            except (TypeError, ValueError):
                continue
    invisible = 0
    inv = node.get("invisible")
    if inv is not None:
        try:
            invisible = int(getattr(inv, "value", 0) or 0)
        except (TypeError, ValueError):
            invisible = 0
    has_icon = isinstance(node.get("icon"), WzCanvasProperty)
    return {
        "id": sid,
        "name": simplify(text_of(name_node, "name")) or str(sid),
        "desc": simplify(text_of(name_node, "desc")),
        "job": sid // 10000,
        "maxLevel": max_level,
        "masterLevel": 0,
        "type": classify(node, fields),
        "invisible": invisible,
        "auto": False,
        "hexa": True,
        "from": kind,
        "core": core_id,
        "req": req,
        "icon": f"skillicon/{sid}.png" if has_icon else "",
        "levels": rows,
    }


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    plan = wanted()
    skills, skipped = {}, []
    for sid, (kind, core_id) in sorted(plan.items()):
        row = export_skill(sid, kind, core_id)
        if row is None:
            skipped.append(sid)
        else:
            skills[str(sid)] = row

    from collections import Counter
    kinds = Counter(s["from"] for s in skills.values())
    types = Counter(s["type"] for s in skills.values())
    print(f"核心技能 {len(plan)} → 导出 {len(skills)}（V {kinds['v']} / HEXA {kinds['hexa']}）"
          f"  跳过 {len(skipped)}  levels {sum(len(s['levels']) for s in skills.values())}")
    print(f"  type={dict(sorted(types.items()))}")
    if skipped:
        print(f"  跳过（无 common / maxLevel=0）: {skipped[:10]}")
    for sid in (400011001, 1141000):
        row = skills.get(str(sid))
        if row:
            print(f"  {sid} {row['name']} lv1={row['levels']['1']}")

    if args.check:
        return
    payload = {
        "source": {
            "packs": str(PACKS),
            "definitions": "Skill/<img>.img（282 定义层，BMS 密钥）",
            "cores": "Assets/Resources/UI/{VMatrix,HexaMatrix}.json",
            "jobs": "四转 ID（17 个迁移职业）",
            "levels": "common 公式按 1..maxLevel 展开（SkillFormulaEvaluator 语义）",
        },
        "skills": dict(sorted(skills.items(), key=lambda kv: int(kv[0]))),
    }
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"写入 {OUT.relative_to(ROOT)}  {OUT.stat().st_size / 1024:.0f} KiB")


if __name__ == "__main__":
    main()
