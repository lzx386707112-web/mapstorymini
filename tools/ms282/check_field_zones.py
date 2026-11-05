#!/usr/bin/env python3
"""周期领域表 ↔ 282 数据 ↔ 运行时 的一致性守卫。

    ① 表结构   每条都挂在 NativeSkillStates.json 的真实子状态上，parent 一致
    ② 有出处   evidence 非空；intervalMs / durationSec / mobCount / projectiles /
               maxActivations 全部能回算到 282 的字段（checks 逐级复验）
    ③ 有消费   表里的每个运行时键都在 NativeFieldZones.cs 里被读到
    ④ 反向孤儿 带 subTime 的 duration 状态要么在本表，要么在「已知未实现」清单
    ⑤ 接线     施放 → StartFieldZones；命中 → TriggerFieldZonesOnHit；
               换图 → ClearFieldZones；协程真的调 FireProc 且能中止

用法
    python3 tools/ms282/check_field_zones.py
    python3 tools/ms282/check_field_zones.py --table
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeFieldZones.json"
STATES = ROOT / "Assets/Resources/NativeSkillStates.json"
EXPORT = ROOT / "tools/ms282/export_field_zones.py"

RUNTIME_KEYS = {
    "state", "parent", "bodies", "durationSec", "intervalMs", "mobCount",
    "projectiles", "maxActivations", "trigger", "targetMode",
}
META_KEYS = {"name", "checks", "intervalField", "durationField", "evidence"}
TRIGGERS = {"onCast", "onHit", "onSkillHitWhileState"}
MODES = {"area", "boss"}

# 带 subTime 但本轮明确不落地的状态：写清楚原因，避免将来悄悄多出一条没人做的。
KNOWN_PENDING = {
    "2141502": "元素吸收上限扩至 10 堆叠 —— 本工程没有元素吸收系统",
    "4241005": "隐身术回避计数 —— 属 darksight 系统，不是领域攻击",
    "4240014": "暗影雾杀区域 —— 已由暗影之刃 proc 的 AltState 消费",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def skills() -> dict[str, dict]:
    merged: dict[str, dict] = {}
    for name in ("VMatrixSkills", "AdvancedSkills"):
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        merged.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return merged


def lv1(row: dict | None) -> dict:
    return ((row or {}).get("levels") or {}).get("1") or {}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--table", action="store_true", help="打印周期领域预览")
    args = ap.parse_args()

    problems: list[str] = []
    if not TABLE.is_file():
        print(f"缺少 {TABLE.relative_to(ROOT)}；先跑 export_field_zones.py")
        return 1
    zones = json.loads(TABLE.read_text(encoding="utf-8")).get("zones") or {}
    states = json.loads(STATES.read_text(encoding="utf-8")).get("states") or {}
    rows = skills()
    runtime = read(SCRIPTS / "NativeFieldZones.cs")

    if len(zones) != 4:
        problems.append(f"周期领域应登记 4 条，实际 {len(zones)} 条")

    # ① 表结构 + ② 数值回算
    for sid, z in sorted(zones.items()):
        label = f"{sid} {z.get('name')}"
        for key in RUNTIME_KEYS | META_KEYS:
            if key not in z:
                problems.append(f"{label} 缺 {key}")
        if not z.get("evidence"):
            problems.append(f"{label} 没有 evidence（必须留 282 h 原文）")
        state = states.get(sid)
        if state is None:
            problems.append(f"{label} 不在 NativeSkillStates.json（领域必须由子状态驱动）")
        elif int(state.get("parent") or 0) != z.get("parent"):
            problems.append(f"{label} parent={z.get('parent')} 与子状态登记的 {state.get('parent')} 不一致")
        if z.get("trigger") not in TRIGGERS or z.get("targetMode") not in MODES:
            problems.append(f"{label} trigger/targetMode 不认识：{z.get('trigger')}/{z.get('targetMode')}")

        parent = rows.get(str(z.get("parent")))
        body = rows.get(str((z.get("bodies") or [0])[0]))
        if parent is None or body is None:
            problems.append(f"{label} 母技能或伤害体不在技能表")
            continue
        p1, b1, s1 = lv1(parent), lv1(body), lv1(rows.get(sid))
        if not p1.get("damage") or not b1.get("damage"):
            problems.append(f"{label} 母技能/伤害体没有 damage")

        # 周期：母技能 s2/s 秒 ×1000，或状态/召唤体的 subTime 毫秒。
        interval_field = z.get("intervalField")
        if interval_field in ("s2", "s"):
            expected = int(p1.get(interval_field, 0)) * 1000
            if expected != z.get("intervalMs"):
                problems.append(f"{label} intervalMs={z.get('intervalMs')} != {interval_field}×1000={expected}")
            if int(s1.get("subTime") or 0) != z.get("intervalMs"):
                problems.append(f"{label} 状态的 subTime={s1.get('subTime')} != intervalMs")
        elif interval_field == "subTime":
            source = b1 if str(z.get("durationField")).startswith("body") else s1
            if int(source.get("subTime") or 0) != z.get("intervalMs"):
                problems.append(f"{label} subTime={source.get('subTime')} != intervalMs")
        else:
            problems.append(f"{label} intervalField={interval_field!r} 不认识")
        if int(z.get("intervalMs") or 0) <= 0:
            problems.append(f"{label} 周期不是正数")

        # 持续时间：母技能 time 或召唤体 time。
        if str(z.get("durationField")) == "parent.time":
            if int(p1.get("time") or 0) != z.get("durationSec"):
                problems.append(f"{label} durationSec != 母技能 time")
        elif str(z.get("durationField")) == "body.time":
            if int(b1.get("time") or 0) != z.get("durationSec"):
                problems.append(f"{label} durationSec != 召唤体 time")
        else:
            problems.append(f"{label} durationField={z.get('durationField')!r} 不认识")

        if int(b1.get("mobCount") or 0) != z.get("mobCount"):
            problems.append(f"{label} mobCount != 伤害体 mobCount")
        if z.get("projectiles", 1) != 1:
            if int(b1.get("bulletCount") or 0) != z.get("projectiles"):
                problems.append(f"{label} projectiles != 伤害体 bulletCount")
            if int(p1.get("z") or 0) != z.get("projectiles"):
                problems.append(f"{label} projectiles != 母技能 z")
        if z.get("maxActivations", 0) and int(p1.get("q2") or 0) != z["maxActivations"]:
            problems.append(f"{label} maxActivations != 母技能 q2")

        # checks：伤害体字段逐级等于母技能字段。
        for body_id, mapping in (z.get("checks") or {}).items():
            brow = rows.get(str(body_id))
            if brow is None:
                problems.append(f"{label} checks 里的 {body_id} 不在技能表")
                continue
            for body_field, parent_field in mapping.items():
                compared = 0
                for lv, line in ((brow.get("levels") or {}).items()):
                    expected = ((parent.get("levels") or {}).get(lv) or {}).get(parent_field)
                    if expected is None:
                        continue
                    compared += 1
                    if line.get(body_field) != expected:
                        problems.append(f"{label} lv{lv}: {body_id}.{body_field} != "
                                        f"parent.{parent_field}")
                if compared < 2:
                    problems.append(f"{label} {body_id}.{body_field} 只比对了 {compared} 级")

    # ③ 有消费：运行时键必须在 C# 源码里以字符串字面量出现。
    for key in sorted(RUNTIME_KEYS):
        if f'"{key}"' not in runtime:
            problems.append(f'表里声明了 "{key}"，但 NativeFieldZones.cs 没有读它')
    # 元数据键必须被本守卫消费（写进表却没人验证也算孤儿）。
    guard = read(Path(__file__))
    for key in sorted(META_KEYS):
        if f'"{key}"' not in guard:
            problems.append(f'表里声明了 "{key}"，但守卫没有验证它')

    # ④ 反向孤儿：带 subTime 的 duration 状态不能悄悄漏掉。
    for sid, entry in states.items():
        s1 = lv1(rows.get(sid))
        if not s1.get("subTime"):
            continue
        if sid in zones or sid in KNOWN_PENDING or entry.get("role") != "duration":
            continue
        problems.append(f"{sid} {entry.get('name')} 带 subTime 但既没进周期领域表，也不在待做清单")

    # ⑤ 接线
    cast = read(SCRIPTS / "SkillSystem.cs")
    followups = read(SCRIPTS / "NativeSkillFollowups.cs")
    for marker, source, where in (
        ("StartFieldZones(s,", cast, "SkillSystem 施放路径"),
        ("ClearFieldZones()", cast, "SkillSystem 换图清理"),
        ("TriggerFieldZonesOnHit(cast,origin,facing)", followups, "TriggerReactiveSkills"),
        ("FireProc(host,", runtime, "周期领域协程"),
        ("SeekingTargets(host,", runtime, "boss 优先追踪"),
        ("ReplayTargets(parent,", runtime, "领域范围取目标"),
        ("ReplayAlive(field, map)", runtime, "换图/死亡中止"),
        ("NativeSkillStates.For(p)", runtime, "状态查询"),
    ):
        if marker not in source:
            problems.append(f"{where} 缺少 {marker}")
    if "NativeSkillStates.For(p).Activate" in cast and "StartFieldZones(s," in cast:
        if cast.index("NativeSkillStates.For(p).Activate") > cast.index("StartFieldZones(s,"):
            problems.append("StartFieldZones 必须在 Activate 之后（先挂状态再启动领域）")
    # 导出脚本与 JSON 必须同源，防止手改表绕过自证。
    if "export_field_zones.py" not in json.loads(TABLE.read_text(encoding="utf-8")).get("source", ""):
        problems.append("JSON 的 source 不是 export_field_zones.py")
    if not EXPORT.is_file():
        problems.append("缺 export_field_zones.py")

    print(f"周期领域 {len(zones)} 条；不一致 {len(problems)} 条")
    for p in problems:
        print("  !", p)
    if args.table:
        print("\n  state     parent   body     dur  interval  shots  cap  trigger                  mode")
        for sid, z in sorted(zones.items()):
            print(f"  {sid:<10} {z.get('parent'):<8} {(z.get('bodies') or [0])[0]:<8} "
                  f"{z.get('durationSec'):<4} {z.get('intervalMs'):<9} "
                  f"{z.get('projectiles'):<6} {z.get('maxActivations'):<4} "
                  f"{z.get('trigger'):<24} {z.get('targetMode')}")
        for sid, why in sorted(KNOWN_PENDING.items()):
            print(f"  [待做] {sid}: {why}")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
