#!/usr/bin/env python3
"""校验运行时 proc 登记表（Assets/Scripts/NativeProcSkills.cs）与三张技能表是否自洽。

proc 是「主攻击命中 → 掷骰/计数 → 用伤害体的 damage%/attackCount 结算」这条链。
表在 C# 里（逻辑与数据同处，模式是枚举），所以这里用轻量解析把它读出来核对：

    Trigger   必须能被 SkillBook 读到（JobTree.json 或 VMatrixSkills.json）
    Body      必须能被 SkillBook 读到，且至少一级 damage > 0
    PropField 必须是 Trigger 等级行里真实存在的字段（prop / x）
    同一 Trigger 不能登记两次
    Basic     只允许終極之弓 3100001 / 終極之弩 3200001（武器的守卫写死在 ProcWeaponOk）

用法：python3 tools/ms282/check_proc_skills.py
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
UI = ROOT / "Assets/Resources/UI"
SOURCE = ROOT / "Assets/Scripts/NativeProcSkills.cs"

BASIC_ALLOWED = {3100001, 3200001}
VALUE_FIELDS = ("damage", "attackCount", "mobCount")


def load(name: str) -> dict:
    return json.loads((UI / name).read_text(encoding="utf-8"))["skills"]


def parse_specs(text: str):
    body = text.split("static readonly ProcSpec[] procSpecs", 1)[1]
    body = body.split("};", 1)[0]
    specs = []
    for raw in re.finditer(r"new ProcSpec\(([^)]*)\)", body):
        args = raw.group(1)
        positional = []
        named = {}
        for piece in args.split(","):
            piece = piece.strip()
            if not piece:
                continue
            if ":" in piece:
                key, value = piece.split(":", 1)
                named[key.strip()] = value.strip()
            else:
                positional.append(piece.strip())
        trigger = int(positional[0])
        mode = positional[1].split(".")[-1]
        spec = {
            "trigger": trigger,
            "mode": mode,
            "body": int(named.get("body", 0)) or trigger,
            "chargeField": named.get("chargeField", '"x"').strip('"'),
            "capField": named.get("capField", '"y"').strip('"'),
            "propField": named.get("propField", '"prop"').strip('"'),
            "brandBody": int(named.get("brandBody", 0)),
            "basic": named.get("basic", "false") == "true",
            # 倍率口径覆盖：非空时表示「按主攻击最终伤害的该字段%」，
            # 这种技能自身没有 damage 行，不能按 has_damage 判。
            "scaleField": named.get("scaleField", '""').strip('"'),
            # 状态升级（暗影之刃）：状态生效期间换伤害体与概率字段。
            "altState": int(named.get("altState", 0)),
            "altBody": int(named.get("altBody", 0)),
            "altPropField": named.get("altPropField", '""').strip('"'),
        }
        specs.append(spec)
    return specs


def has_damage(row: dict) -> bool:
    return any((level or {}).get("damage", 0) > 0 for level in row.get("levels", {}).values())


def main() -> int:
    jobtree = load("JobTree.json")
    vmatrix = load("VMatrixSkills.json")
    advanced = load("AdvancedSkills.json")
    readable = {**advanced, **jobtree, **vmatrix}   # 三张表取并集，和 SkillBook 的加载范围一致

    specs = parse_specs(SOURCE.read_text(encoding="utf-8"))
    print(f"proc 登记: {len(specs)} 条")
    problems = []
    notes = []
    seen = set()
    for spec in specs:
        sid = spec["trigger"]
        if sid in seen:
            problems.append(f"{sid} 重复登记")
        seen.add(sid)
        row = readable.get(str(sid))
        if row is None:
            problems.append(f"{sid} 触发体不在任何技能表里（SkillBook 读不到 ⇒ 永不触发）")
            continue
        body = spec["body"]
        body_row = readable.get(str(body)) if body != sid else None
        if not has_damage(row):
            # 触发体/伤害体分离时（暗影之隱 4240016 → 4241018），数值全在伤害体上；
            # 触发体那一行只有 x/y/u/v/w/q 这类语义字段，本来就不该有 damage。
            if spec["scaleField"]:
                series = [(lv or {}).get(spec["scaleField"], 0)
                          for lv in row.get("levels", {}).values()]
                if not any(series):
                    problems.append(f"{sid} {row.get('name')} 的倍率字段 "
                                    f"{spec['scaleField']} 全级为 0 或不存在")
            elif body_row is not None and has_damage(body_row):
                notes.append(f"{sid} {row.get('name')} 数值取伤害体 {body}")
            else:
                problems.append(f"{sid} {row.get('name')} 没有任何 damage 行")
        first = row.get("levels", {}).get("1", {})
        # Charge 按命中次数触发，不掷骰；其余模式缺 prop 就是「必定发动」（默认 100%），
        # 这是 282 里真实存在的写法（突擊爆發 / 影之槍 / 蝙蝠的 h 都没写概率）。
        if spec["mode"] != "Charge" and spec["propField"] not in first:
            notes.append(f"{sid} {row.get('name')} 无 {spec['propField']} 字段 → 按必定发动（100%）")
        if spec["mode"] in ("Charge", "SummonCharge") and spec["chargeField"] not in first:
            problems.append(f"{sid} 缺计数阈值字段 {spec['chargeField']}")
        body = spec["body"]
        if body != sid:
            if body_row is None:
                problems.append(f"{body} 伤害体不在任何技能表里（{sid} 的 body）")
            elif not has_damage(body_row):
                problems.append(f"{body} {body_row.get('name')} 伤害体没有 damage 行（{sid} 的 body）")
        if spec["altBody"]:
            arow = readable.get(str(spec["altBody"]))
            if not spec["altState"]:
                problems.append(f"{sid} 登记了 altBody 却没有 altState")
            elif arow is None or not has_damage(arow):
                problems.append(f"{spec['altBody']} 升级伤害体缺失或无 damage（{sid} 的 altBody）")
            elif spec["altPropField"] and spec["altPropField"] not in first:
                problems.append(f"{sid} 缺升级概率字段 {spec['altPropField']}")
        if spec["brandBody"]:
            brow = readable.get(str(spec["brandBody"]))
            if brow is None or not has_damage(brow):
                problems.append(f"{spec['brandBody']} 强化体缺失或无 damage（{sid} 的 brandBody）")
        if spec["basic"] and sid not in BASIC_ALLOWED:
            problems.append(f"{sid} 标了 basic，但 ProcWeaponOk 只认 {'/'.join(map(str, sorted(BASIC_ALLOWED)))}")
        # 刺客刻印VI：伤害体的 damage 必须逐级等于触发体的 dummyStr（h 里写的是 #dummyStr%），
        # 且标记/飞镖参数都要从 level 行读，C# 不许写死数值。
        if spec["mode"] == "MarkPerTarget":
            body_levels = (body_row or {}).get("levels", {})
            compared = 0
            for lv, line in row.get("levels", {}).items():
                expected = line.get("dummyStr")
                if expected is None:
                    continue
                compared += 1
                actual = body_levels.get(lv, {}).get("damage")
                if actual != expected:
                    problems.append(f"{sid} lv{lv}: {body}.damage={actual} != dummyStr={expected}")
            if compared < 2:
                problems.append(f"{sid} 的 dummyStr 只逐级比对了 {compared} 级")
            for field in ("bulletCount", "mobCount", "range", "dotTime", "prop"):
                if field not in first:
                    problems.append(f"{sid} 的 level/1 缺 {field} 字段")
            text = SOURCE.read_text(encoding="utf-8")
            for field in ("bulletCount", "mobCount", "range", "dotTime"):
                if f'"{field}"' not in text:
                    problems.append(f'MarkPerTarget 的 {field} 没在 NativeProcSkills.cs 里读')

    for item in problems:
        print("  FAIL", item)
    for item in notes:
        print("  NOTE", item)
    print(f"不一致 {len(problems)} 条")

    if "--table" in sys.argv:
        print_table(specs, readable)
    return 0 if not problems else 1


def print_table(specs, readable) -> None:
    """满级数值预览：给人工验收用（不是校验）。"""
    print("\n满级数值（触发体等级取 maxLevel，伤害取 Body）：")
    header = f"{'触发体':>10} {'名称':<14} {'模式':<13} {'概率':>5} {'伤害':>6} {'段数':>4} {'目标':>4} {'CDms':>6} 阈值/上限"
    print(header)
    for spec in specs:
        sid = spec["trigger"]
        row = readable.get(str(sid)) or {}
        levels = row.get("levels", {})
        top = max((int(k) for k in levels), default=1)
        lv = levels.get(str(top), {})
        brow = readable.get(str(spec["body"])) or row
        blev = (brow.get("levels") or {}).get(str(top), {})
        prop = lv.get(spec["propField"], 100)
        extra = ""
        if spec["mode"] == "Charge":
            prop = "—"          # 按命中次数触发，不掷骰
            extra = f"{spec['chargeField']}={lv.get(spec['chargeField'], '?')}"
        elif spec["mode"] == "SummonCharge":
            extra = f"{spec['chargeField']}={lv.get(spec['chargeField'], '?')}"
            extra += f" 上限{spec['capField']}={lv.get(spec['capField'], '?')}"
        prop_text = f"{prop}%" if isinstance(prop, int) else prop
        # scaleField 型的「伤害」是主攻击的百分比，不是面板 damage%，单独标出来。
        damage_text = (f"主攻{lv.get(spec['scaleField'], 0)}%"
                       if spec["scaleField"] else str(blev.get('damage', 0)) + '%')
        print(f"{sid:>10} {str(row.get('name'))[:12]:<14} {spec['mode']:<13} "
              f"{prop_text:>5} {damage_text:>6} "
              f"{blev.get('attackCount', 1):>4} {blev.get('mobCount', 1):>4} "
              f"{lv.get('cooltimeMS', 0):>6} {extra}")


if __name__ == "__main__":
    sys.exit(main())
