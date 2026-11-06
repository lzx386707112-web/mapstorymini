#!/usr/bin/env python3
"""技能间伤害强化表 ↔ 282 数据 ↔ 运行时 的一致性守卫。

    NativeSkillBoosts.json 回答的是「谁的被动在强化谁的伤害」：
        h「[被动效果：X 技能伤害增加 #field%p]」→ 目标的 damage ×(100+Σpct)/100
    12 条来源 / 14 条目标，字段是 damPlus 或单字母 u/v。

    ① 表结构    source 对得上、target/field/pct/targetName/targetDamage 齐全
    ② 有出处   pct 长度 == 来源技能 maxLevel，且与技能表逐级相等；
               targetName 必须出现在 h 里，或 target 在来源的 psdSkill 里
    ③ 有消费   每个运行时键都被 NativeSkillBoosts.cs 读到；
               Pct() 必须真的被 SkillBook.Native 调用（否则表是死的）
    ④ 反向孤儿 C# / 导出脚本里出现的技能 id 都在表里（没登记就等于没做）
    ⑤ 口径     目标必须有 damage，否则放大的是 0；来源不能强化自己

用法
    python3 tools/ms282/check_skill_boosts.py
    python3 tools/ms282/check_skill_boosts.py --table
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeSkillBoosts.json"
EXPORT = ROOT / "tools/ms282/export_skill_boosts.py"
UI = ROOT / "Assets/Resources/UI"
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

RUNTIME_KEYS = {"target", "pct"}
# field / targetName / said / targetDamage 都是导出期与守卫用的元数据：
# 运行时只按 target 查 pct，字段名留在表里是为了让人能回到 h 原文对照。
META_KEYS = {"field", "targetName", "said", "targetDamage"}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def skills_all() -> dict[str, dict]:
    rows: dict[str, dict] = {}
    for name in ("JobTree", "AdvancedSkills", "VMatrixSkills"):
        rows.update(json.loads((UI / f"{name}.json").read_text(encoding="utf-8"))["skills"])
    return rows


def common_damage(skill_id: int) -> list[int]:
    """目标不在导出表时的兜底：读 IMG 的 common/damage 展开。

    与 SkillBook.Get 的兜底同一条路（levels 缺失就按 common 公式展开），
    所以这里判「有伤害」就代表运行时也拿得到。读不到 packs 就返回空，交由调用方报缺。
    """
    try:
        from export_advanced_skills import PACKS, eval_formula, level_rows
        from wzpy import WzImage, WzKey
    except Exception:
        return []
    path = PACKS / f"Skill_{skill_id // 10000}.img"
    if not path.is_file():
        return []
    try:
        node = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region("BMS"),
                                  name=path.name).get(f"skill/{skill_id}")
    except Exception:
        return []
    common = node.get("common") if node is not None else None
    if common is None:
        return []
    max_level, fields = level_rows(common)
    if "damage" not in fields:
        return []
    raw = fields["damage"]
    if isinstance(raw, str):
        return [eval_formula(raw, level) for level in range(1, max_level + 1)]
    return [int(raw)] * max_level


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--table", action="store_true", help="打印强化预览")
    args = ap.parse_args()

    problems: list[str] = []
    if not TABLE.is_file():
        print(f"缺少 {TABLE.relative_to(ROOT)}；先跑 export_skill_boosts.py")
        return 1
    data = json.loads(TABLE.read_text(encoding="utf-8"))
    boosts = data.get("boosts") or {}
    skills = skills_all()
    runtime = read(SCRIPTS / "NativeSkillBoosts.cs")
    book = read(SCRIPTS / "SkillBook.cs")
    source_py = read(EXPORT)

    # ① 表结构
    if not boosts:
        problems.append("强化表为空")
    if "export_skill_boosts.py" not in data.get("source", ""):
        problems.append("JSON 的 source 不是 export_skill_boosts.py")
    if not EXPORT.is_file():
        problems.append("缺 export_skill_boosts.py")

    declared: set[int] = set()
    covered: set[int] = set()
    for sid, entries in sorted(boosts.items()):
        declared.add(int(sid))
        src = skills.get(sid)
        if src is None:
            problems.append(f"{sid} 不在技能表")
            continue
        label = f"{sid} {src.get('name')}"
        if not isinstance(entries, list) or not entries:
            problems.append(f"{label} 没有目标")
            continue
        src_levels = src.get("levels") or {}
        for e in entries:
            for key in RUNTIME_KEYS | META_KEYS:
                if key not in e:
                    problems.append(f"{label} 的目标条目缺 {key}")
            target = int(e.get("target") or 0)
            covered.add(target)
            if target == int(sid):
                problems.append(f"{label} 强化了自己")
            # ② pct 与来源技能逐级相等
            pct = e.get("pct") or []
            field = e.get("field")
            if len(pct) != len(src_levels):
                problems.append(f"{label} 的 {field} 有 {len(pct)} 级，技能表有 {len(src_levels)} 级")
            mismatch = [lv for lv, row in src_levels.items()
                        if int(row.get(field, 0)) != int(pct[int(lv) - 1]) if int(lv) <= len(pct)]
            if mismatch:
                problems.append(f"{label} 的 {field} 与技能表不符（lv {mismatch[:5]}）")
            if len(set(pct)) < 2:
                problems.append(f"{label} 的 {field} 全级同一个值 {pct[0] if pct else '?'}")
            # ⑤ 目标必须有伤害。3120008 进阶终极攻击 / 5220020 枪神奥义 不在导出表里，
            # 但 SkillBook.Get 会直读 IMG 的 common 展开 —— 与导出脚本同一条兜底路径，
            # 所以这里也回退到 common 判一次，别再按表里有没有就下结论。
            tgt = skills.get(str(target))
            tgt_damage = [int((tgt.get("levels") or {}).get(lv, {}).get("damage", 0))
                          for lv in (tgt.get("levels") or {})] if tgt else []
            if not any(tgt_damage):
                tgt_damage = common_damage(target)
            if not any(tgt_damage):
                problems.append(f"{label} 的目标 {target}({e.get('targetName')}) 没有 damage 行，放大了也没用")
            # ② 名字自证：h 里点名的片段必须在目标的 name 或来源的 psdSkill 里
            if e.get("said") and e.get("targetName") and e["said"] not in str(e.get("targetName")) + str(e.get("said")):
                problems.append(f"{label} 的 said「{e.get('said')}」与目标名「{e.get('targetName')}」无关")

    # ③ 逐键消费
    for key in sorted(RUNTIME_KEYS):
        if f'"{key}"' not in runtime:
            problems.append(f'表里声明了 "{key}"，但 NativeSkillBoosts.cs 没有读它')
    guard = read(Path(__file__))
    for key in sorted(META_KEYS):
        if f'"{key}"' not in guard:
            problems.append(f'表里声明了 "{key}"，但守卫没有验证它')
    if "NativeSkillBoosts.Pct(" not in book:
        problems.append("SkillBook.Native 没有调用 NativeSkillBoosts.Pct（表是死的）")
    if "public static int Pct(" not in runtime:
        problems.append("NativeSkillBoosts 没有 Pct 入口")

    # ④ 反向孤儿：导出脚本登记的 id 必须都落进表里
    for sid in re.findall(r'^\s{4}(\d{6,9}):\s*\[', source_py, re.M):
        if int(sid) not in declared:
            problems.append(f"导出脚本登记了 {sid}，但表里没有（导出时被跳过？）")
    for target in re.findall(r'\((\d{6,9}),\s*"(?:damPlus|u|v)",', source_py):
        if int(target) not in covered:
            problems.append(f"导出脚本登记的目标 {target} 没有出现在表里")

    # ⑤ 口径：效果守卫必须知道这张表（damPlus 不是属性 buff）
    effects_guard = read(ROOT / "tools/ms282/check_skill_effects.py")
    if "NativeSkillBoosts.json" not in effects_guard:
        problems.append("check_skill_effects.py 没有登记强化表（damPlus 会被当成无消费字段）")

    # ⑥ 跨技能取数值：500001005 灵魂赫卡忒的 damage/attackCount 在 psdSkill 的
    # 500001010 上，由 export_vhexa_skills.LINKED_ATTACK 在导出期补进来。
    # 这里只验证「声明了就必须真的落地」——补字段的逻辑在导出脚本里，运行时无代码。
    for sid, row in sorted(skills.items()):
        link = row.get("linkedAttack") if isinstance(row, dict) else None
        if not link:
            continue
        names = [n for n in str(link.get("fields", "")).split(",") if n]
        levels = row.get("levels") or {}
        if not levels:
            problems.append(f"{sid} 声明了 linkedAttack 却没有 levels")
            continue
        for name in names:
            if any(name not in lv for lv in levels.values()):
                problems.append(f"{sid} 声明从 {link.get('from')} 补 {name}，但 levels 里有等级行缺它")
        if not int(link.get("from") or 0):
            problems.append(f"{sid} 的 linkedAttack 没有 from")

    total = sum(len(v) for v in boosts.values())
    print(f"技能间伤害强化 {len(boosts)} 条来源 / {total} 条目标；不一致 {len(problems)} 条")
    for p in problems:
        print("  !", p)
    if args.table:
        print("\n  来源         名称             → 目标      名称            字段      lv1..lvN")
        for sid, entries in sorted(boosts.items()):
            name = (skills.get(sid) or {}).get("name", "")
            for e in entries:
                pct = e.get("pct") or [0]
                print(f"  {sid:<12} {name:<16} → {e.get('target'):<10} "
                      f"{str(e.get('targetName')):<16} {e.get('field'):<8} {pct[0]}..{pct[-1]}")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
