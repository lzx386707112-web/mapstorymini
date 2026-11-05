#!/usr/bin/env python3
"""技能子状态表 ↔ 运行时 的一致性守卫。

守的还是那条老规矩：写进 JSON 却没人读，等于白做。

    ① 表结构   parent/time/evidence 齐全，parent 在技能表里
    ② 有出处   每条 evidence 非空（母技能 h 原文或节点字段名）
    ③ 有消费   C# 里每个键 / 每个入口都被真的读到
    ④ 接线     母技能施放 → Activate；timedEffect → NativeBuffs 放行；
               proc 链 → AllIds/Ints/Has；换图 → Clear
    ⑤ 增益闭环 timedEffect 的状态必须在 NativeSkillEffects.alias 里有字段映射
    ⑥ 连击闭环 bodies 必须是带 damage 的技能；triggers 非空

用法
    python3 tools/ms282/check_skill_states.py
    python3 tools/ms282/check_skill_states.py --table
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeSkillStates.json"
EFFECTS = ROOT / "Assets/Resources/NativeSkillEffects.json"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def skill_ids() -> set[str]:
    ids: set[str] = set()
    for name in ("VMatrixSkills", "AdvancedSkills", "JobTree"):
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        if path.is_file():
            ids |= set(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return ids


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--table", action="store_true", help="打印状态预览")
    args = parser.parse_args()

    problems: list[str] = []
    if not TABLE.is_file():
        print(f"缺少 {TABLE.relative_to(ROOT)}；先跑 export_skill_states.py")
        return 1
    data = json.loads(TABLE.read_text(encoding="utf-8"))
    states = data.get("states") or {}
    ids = skill_ids()

    # ① 表结构 + ② 出处
    for sid, entry in states.items():
        label = f"{sid} {entry.get('name')}"
        for key in ("parent", "time", "role", "evidence"):
            if key not in entry:
                problems.append(f"{label} 缺 {key}")
        if str(entry.get("parent")) not in ids:
            problems.append(f"{label} 的 parent={entry.get('parent')} 不在技能表")
        if int(entry.get("time") or 0) <= 0:
            problems.append(f"{label} 的 time 不是正数（状态没有时限）")
        if not entry.get("evidence"):
            problems.append(f"{label} 没有 evidence")
        if entry.get("role") not in ("duration", "procGate", "timedEffect", "deathGuard"):
            problems.append(f"{label} 的 role={entry.get('role')!r} 不认识")

    # ③ 有消费：表里的每个**运行时键**都要在 C# 里出现。
    # name/role/evidence 是给人读的证据与分类，不进运行时；它们由本脚本的 ①② 检查。
    META = {"name", "role", "evidence"}
    runtime = read(SCRIPTS / "NativeSkillStates.cs")
    declared = {key for entry in states.values() for key in entry} - META
    mechanics_source = read(SCRIPTS / "NativeSkillMechanics.cs")
    for key in sorted(declared):
        if (f'"{key}"' not in runtime
                and f'"{key}"' not in read(SCRIPTS / "NativeProcSkills.cs")
                and f'"{key}"' not in mechanics_source):
            problems.append(f'表里声明了 "{key}"，但 C# 没有任何地方读它')

    # ④ 接线
    system = read(SCRIPTS / "SkillSystem.cs")
    if "NativeSkillStates.For(p).Activate(" not in system:
        problems.append("SkillSystem 没有按母技能调用 NativeSkillStates.Activate")
    if "GetComponent<NativeSkillStates>()?.Clear()" not in system:
        problems.append("换图/死亡没有清掉子状态")
    buffs = read(SCRIPTS / "NativeBuffs.cs")
    if "NativeSkillStates.IsTimedEffect" not in buffs:
        problems.append("NativeBuffs 没有对 timedEffect 子状态放行（ApplySkill/SkillStats）")
    proc = read(SCRIPTS / "NativeProcSkills.cs")
    for token in ("NativeSkillStates.AllIds", "NativeSkillStates.Ints", "ActivateState(", "states.Has("):
        if token not in proc:
            problems.append(f"NativeProcSkills 没有接 {token}")
    effects_source = read(SCRIPTS / "NativeSkillEffects.cs")
    if "Aliases" not in effects_source:
        problems.append("NativeSkillEffects 没有读 alias 段")
    player = read(SCRIPTS / "PlayerCtrl.cs")
    if "NativeSkillStates.IsDeathGuard" not in player:
        problems.append("PlayerCtrl 没有消费 deathGuard 状态（天堂之门不会挡致死）")

    # ⑤ timedEffect 必须真的能产出 stat
    aliases = json.loads(EFFECTS.read_text(encoding="utf-8")).get("alias", {}) if EFFECTS.is_file() else {}
    for sid, entry in states.items():
        if not entry.get("timedEffect"):
            continue
        # 元素吸收这类状态没有「字段 → stat」的映射：满层值在导出期就算好写进表里，
        # 由 NativeSkillMechanics.AddTimedEffect 直接注入，不需要 alias。
        if entry.get("effectKey"):
            if int(entry.get("effectPct") or 0) <= 0:
                problems.append(f"{sid} 的 effectPct 不是正数")
            for token in ('"effectKey"', '"effectPct"', "AddTimedEffect"):
                if token not in mechanics_source:
                    problems.append(f"NativeSkillMechanics 没有消费 {token}")
            continue
        if sid not in aliases or not aliases[sid]:
            problems.append(f"{sid} 是 timedEffect，但 NativeSkillEffects.alias 里没有字段映射")
        row = (json.loads((ROOT / "Assets/Resources/UI/AdvancedSkills.json").read_text(encoding="utf-8"))
               ["skills"].get(sid, {}).get("levels", {}).get("1", {}))
        if not any(row.get(field) for field in aliases.get(sid, {})):
            problems.append(f"{sid} 的 alias 字段在 level/1 里取不到数值")

    # ⑥ 连击闭环
    for sid, entry in states.items():
        bodies = entry.get("bodies") or []
        if not bodies:
            continue
        if not entry.get("triggers"):
            problems.append(f"{sid} 有连击体却没有触发名单")
        # 冷却两种单位：cooldownSec（海龙霸拳 2 秒）或 cooldownMs（圣域 500 毫秒）。
        cooldown = int(entry.get("cooldownSec") or 0) * 1000 + int(entry.get("cooldownMs") or 0)
        if cooldown <= 0:
            problems.append(f"{sid} 的冷却没配好")
        if "maxActivations" not in entry:
            problems.append(f"{sid} 没有登记次数上限（0 = 不限次数）")
        skills = json.loads((ROOT / "Assets/Resources/UI/AdvancedSkills.json").read_text(encoding="utf-8"))["skills"]
        for body in bodies:
            if not (skills.get(str(body), {}).get("levels", {}).get("1", {}).get("damage")):
                problems.append(f"{sid} 的连击体 {body} 没有 damage")

    if args.table:
        print(f"\n{'id':>10} {'名称':<14} {'母技能':<10} {'秒':>4} 角色")
        for sid, entry in states.items():
            print(f"{sid:>10} {entry['name']:<14} {entry['parent']:<10} "
                  f"{entry['time']:>4} {entry['role']}")

    for item in problems:
        print("  FAIL", item)
    print(f"子状态 {len(states)} 条，不一致 {len(problems)} 条")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
