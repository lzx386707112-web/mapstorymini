#!/usr/bin/env python3
"""强化核心覆盖表 ↔ 282 数据 ↔ 运行时 的一致性守卫。

    ① 表结构   name / 两个 target / 两个字段 / 三段 h 原文齐全，target 都在技能表
    ② 有出处   强化值必须逐级**高于**被强化技能的同级值（替换而不是相加）
    ③ 有消费   表里的每个运行时键都在 NativeSkillOverrides.cs 里被读到
    ④ 接线     倍率 → NativeProcSkills；时长 → NativeBuffs.SkillDuration；
               summon 注册 → ApplySkill / SkillStats / shared
    ⑤ 口径     check_skill_effects.py 必须把覆盖表的 id 排除出「无 stat buff」

用法
    python3 tools/ms282/check_skill_overrides.py
    python3 tools/ms282/check_skill_overrides.py --table
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeSkillOverrides.json"
EXPORT = ROOT / "tools/ms282/export_skill_overrides.py"
VMATRIX = ROOT / "Assets/Resources/UI/VMatrixSkills.json"

RUNTIME_KEYS = {"scaleTarget", "scaleField", "durationTarget", "durationField"}
META_KEYS = {"name", "evidence", "evidenceScale", "evidenceDuration"}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--table", action="store_true", help="打印覆盖预览")
    args = ap.parse_args()

    problems: list[str] = []
    if not TABLE.is_file():
        print(f"缺少 {TABLE.relative_to(ROOT)}；先跑 export_skill_overrides.py")
        return 1
    data = json.loads(TABLE.read_text(encoding="utf-8"))
    overrides = data.get("overrides") or {}
    skills = json.loads(VMATRIX.read_text(encoding="utf-8"))["skills"]
    runtime = read(SCRIPTS / "NativeSkillOverrides.cs")

    if not overrides:
        problems.append("覆盖表为空")
    if "export_skill_overrides.py" not in data.get("source", ""):
        problems.append("JSON 的 source 不是 export_skill_overrides.py")
    if not EXPORT.is_file():
        problems.append("缺 export_skill_overrides.py")

    for sid, entry in sorted(overrides.items()):
        label = f"{sid} {entry.get('name')}"
        for key in RUNTIME_KEYS | META_KEYS:
            if key not in entry:
                problems.append(f"{label} 缺 {key}")
        if not entry.get("evidence") or not entry.get("evidenceScale") or not entry.get("evidenceDuration"):
            problems.append(f"{label} 三段 h 原文不全")
        row = skills.get(sid)
        if row is None:
            problems.append(f"{label} 不在 VMatrixSkills.json")
            continue
        for kind, target_key, field_key in (("倍率", "scaleTarget", "scaleField"),
                                            ("时长", "durationTarget", "durationField")):
            target = skills.get(str(entry.get(target_key)))
            if target is None:
                problems.append(f"{label} 的{kind}目标 {entry.get(target_key)} 不在技能表")
                continue
            field = entry.get(field_key)
            compared = 0
            for lv, srow in (row.get("levels") or {}).items():
                drow = (target.get("levels") or {}).get(lv)
                if drow is None:
                    continue
                stronger, weaker = srow.get(field), drow.get(field)
                if stronger is None or weaker is None:
                    problems.append(f"{label} lv{lv} 缺 {kind}字段 {field}")
                    continue
                compared += 1
                if stronger <= weaker:
                    problems.append(f"{label} lv{lv}: {kind} {stronger} 没有高于 {weaker}")
            if compared < 2:
                problems.append(f"{label} 的{kind}只比对了 {compared} 级")

    # ③ 逐键消费 + 元数据由本守卫验证
    for key in sorted(RUNTIME_KEYS):
        if f'"{key}"' not in runtime:
            problems.append(f'表里声明了 "{key}"，但 NativeSkillOverrides.cs 没有读它')
    guard = read(Path(__file__))
    for key in sorted(META_KEYS):
        if f'"{key}"' not in guard:
            problems.append(f'表里声明了 "{key}"，但守卫没有验证它')

    # ④ 接线
    buffs = read(SCRIPTS / "NativeBuffs.cs")
    proc = read(SCRIPTS / "NativeProcSkills.cs")
    for marker, source, where in (
        ("NativeSkillOverrides.DurationSeconds(", buffs, "NativeBuffs.SkillDuration"),
        ("NativeSkillOverrides.IsDurationTarget(", buffs, "NativeBuffs.ApplySkill / SkillStats"),
        ('"SUMMON_DURATION"', buffs, "NativeBuffs.shared / SkillStats"),
        ("NativeSkillOverrides.Scale(", proc, "NativeProcSkills 倍率换算"),
        ("public static int Scale(", runtime, "NativeSkillOverrides.Scale"),
        ("public static int DurationSeconds(", runtime, "NativeSkillOverrides.DurationSeconds"),
        ("public static bool IsDurationTarget(", runtime, "NativeSkillOverrides.IsDurationTarget"),
    ):
        if marker not in source:
            problems.append(f"{where} 缺少 {marker}")
    if buffs.count("NativeSkillOverrides.IsDurationTarget(") < 2:
        problems.append("NativeBuffs 里 IsDurationTarget 调用不足 2 处"
                        "（ApplySkill 放行 + SkillStats 注册标记）")

    # ⑤ 效果守卫的 special 名单
    effects_guard = read(ROOT / "tools/ms282/check_skill_effects.py")
    if "NativeSkillOverrides.json" not in effects_guard:
        problems.append("check_skill_effects.py 没有把强化核心排除出「无 stat buff」")

    print(f"强化核心覆盖 {len(overrides)} 条；不一致 {len(problems)} 条")
    for p in problems:
        print("  !", p)
    if args.table:
        print("\n  核心       名称                     倍率目标   字段   时长目标   字段")
        for sid, e in sorted(overrides.items()):
            print(f"  {sid:<10} {e.get('name',''):<24} {e.get('scaleTarget'):<10} "
                  f"{e.get('scaleField'):<6} {e.get('durationTarget'):<10} {e.get('durationField')}")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
