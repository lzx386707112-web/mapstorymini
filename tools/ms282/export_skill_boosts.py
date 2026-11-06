#!/usr/bin/env python3
"""导出 282「技能间伤害强化」→ Assets/Resources/NativeSkillBoods.json

五六转技能里有一批 h 的**方括号被动段**写的是「学了我，另一个技能的伤害增加 #X%p」：

    1141002 愤怒爆发VI   h「[被动效果：狂暴攻击VI的伤害增加#damPlus%p]」
    5141015 勾拳爆破VI   h「[被动效果：闪‧连杀VI伤害增加#u%p、
                              超级闪‧连杀伤害增加#damPlus%p、
                              超级闪‧连杀冲击波伤害增加#v%p]」

这类字段（damPlus / u / v）既不是属性 stat（不能进 setItemBonus），也不产生新的
伤害体（不是 proc），过去完全没人读 —— 12 条五六转技能的被动强化等于没做。

本表只登记「谁强化谁、用哪个字段」，数值仍从技能表逐级读，C# 不写死。

目标 id 的定法（顺序很重要，名字有重名）：
    ① h 里点名的技能名必须出现在目标的 name 里（自证，下面 assert）；
    ② 名字重名时（5141002/5141003 都叫「超级闪‧连杀」、5241009/5241019 都叫
       「轰炸风暴VI」），取**实际结算伤害的那一个**：
         · 4141008 h「刺客刻印VI伤害增加」→ 4141004（4141003 只是 proc 触发体，
           伤害在 body 4141004 上）
         · 5241015 h「轰炸风暴VI」→ 5241009（proc 的 body 自己）
         · 5141015「超级闪‧连杀冲击波」→ 5141003（5141002 的追加冲击波体）

用法
    python3 tools/ms282/export_skill_boosts.py
    python3 tools/ms282/export_skill_boosts.py --check
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import (  # noqa: E402
    PACKS, eval_formula, level_rows, simplify, skill_string, text_of,
)
from wzpy import WzImage, WzKey  # noqa: E402

UI = ROOT / "Assets/Resources/UI"
OUT = ROOT / "Assets/Resources/NativeSkillBoosts.json"
BMS = WzKey.for_region("BMS")

# 来源技能 → [(目标技能, 字段, h 里点名的字符串)]
# 字段一律是来源技能 level 行里的百分比点（#X%p），放大口径 = 目标伤害 ×(100+ΣX)/100。
BOOSTS: dict[int, list[tuple[int, str, str]]] = {
    1141002: [(1141000, "damPlus", "狂暴攻击VI")],      # 愤怒爆发VI → 狂暴攻击VI
    1341003: [(1341000, "damPlus", "冈格尼尔之击VI")],  # 黑暗统合VI → 冈格尼尔之击VI
    15140008: [(15141000, "damPlus", "消灭VI")],        # 海波VI → 消灭VI
    15141003: [(15141002, "damPlus", "落雷")],          # 霹雳VI → 落雷的巨大雷霆
    2241007: [(2240006, "damPlus", "急冻冲击")],        # 闪电球VI → 急冻冲击（技能名写作「极冻冲击」）
    3141002: [(3120008, "damPlus", "进阶终极攻击")],    # 骤雨狂矢VI → 进阶终极攻击
    4141007: [(4141000, "damPlus", "四飞闪VI")],        # 绝杀领域VI → 四飞闪VI
    4141008: [(4141004, "damPlus", "刺客刻印VI")],      # 秽土转生VI → 刺客刻印VI（结算体）
    5141011: [(5141006, "damPlus", "海龙之怒VI")],      # 海龙石VI → 海龙之怒VI
    # 勾拳爆破VI 一条被动强化三个目标：u → 闪‧连杀VI、damPlus → 超级闪‧连杀、v → 冲击波。
    5141015: [
        (5141000, "u", "闪‧连杀VI"),
        (5141002, "damPlus", "超级闪‧连杀"),
        (5141003, "v", "超级闪‧连杀冲击波"),
    ],
    5241000: [(5220020, "damPlus", "枪神奥义")],        # 瞬‧迅雷VI → 枪神奥义
    5241015: [(5241009, "damPlus", "轰炸风暴VI")],      # 攻城轰炸机VI → 轰炸风暴VI
}

_cache: dict[int, WzImage] = {}


def node_of(skill_id: int):
    img_id = skill_id // 10000
    if img_id not in _cache:
        path = PACKS / f"Skill_{img_id}.img"
        if not path.is_file():
            return None
        _cache[img_id] = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
    return _cache[img_id].get(f"skill/{skill_id}")


def name_of(skill_id: int) -> str:
    return simplify(text_of(skill_string(skill_id), "name")).strip()


def h_of(skill_id: int) -> str:
    return simplify(text_of(skill_string(skill_id), "h")).replace("\\n", " ").strip()


def table_rows() -> dict[str, dict]:
    """三张技能表合并；VMatrixSkills 最后 update ⇒ 它的 levels 覆盖另两份。

    轰炸风暴VI(5241009) 同时出现在 JobTree/AdvancedSkills/VMatrixSkills 里，只有
    VMatrixSkills 那份带 COMMON_ALIAS 摊平出来的 damage/attackCount（见
    export_advanced_skills.COMMON_ALIAS）。用 setdefault 会留下没有 damage 的那份。
    """
    rows: dict[str, dict] = {}
    for name in ("JobTree", "AdvancedSkills", "VMatrixSkills"):
        rows.update(json.loads((UI / f"{name}.json").read_text(encoding="utf-8"))["skills"])
    return rows


def series(skill_id: int, field: str, rows: dict[str, dict]) -> list[int]:
    """某技能某字段的逐级数值；表里没有就回退到 IMG 的 common 公式。"""
    row = rows.get(str(skill_id))
    if row is not None:
        levels = row.get("levels") or {}
        if field in (levels.get("1") or {}):
            return [int((levels.get(lv) or {}).get(field, 0)) for lv in sorted(levels, key=int)]
    node = node_of(skill_id)
    common = node.get("common") if node is not None else None
    if common is None:
        return []
    max_level, fields = level_rows(common)
    if field not in fields:
        return []
    raw = fields[field]
    if isinstance(raw, str):
        return [eval_formula(raw, lv) for lv in range(1, max_level + 1)]
    return [int(raw)] * max_level


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只打印摘要，不写文件")
    args = ap.parse_args()

    rows = table_rows()
    problems: list[str] = []
    boosts: dict[str, list[dict]] = {}

    for sid, entries in sorted(BOOSTS.items()):
        row = rows.get(str(sid))
        h = h_of(sid)
        if row is None and not h:
            problems.append(f"{sid} 既不在技能表也没有 h 原文")
            continue
        out: list[dict] = []
        for target, field, said in entries:
            values = series(sid, field, rows)
            if len(values) < 2:
                problems.append(f"{sid} 的字段 {field} 读不到逐级数值")
                continue
            if len(set(values)) < 2:
                problems.append(f"{sid} 的字段 {field} 全级同一个值 {values[0]}，不像逐级成长")
                continue
            target_name = name_of(target)
            if not target_name:
                problems.append(f"{sid} 的目标 {target} 没有中文名，无法自证")
                continue
            # 自证两重：
            #   ① h 原文里必须出现 BOOSTS 登记的那个片段；
            #   ② 目标要么写在来源技能的 psdSkill 里（282 的数据层引用），
            #      要么名字确实出现在 h 里。名字有简繁/近义写法差异
            #      （2241007 的 h 写「急冻冲击」而 2240006 叫「极冻冲击」），
            #      所以不能只靠名字，psdSkill 才是权威的那一路。
            if said not in h:
                problems.append(f"{sid} 的 h 里没有「{said}」，登记与原文不符")
                continue
            psd = {c.name for c in (node_of(sid).get("psdSkill").children()
                                    if node_of(sid) is not None and node_of(sid).get("psdSkill") is not None else [])}
            if str(target) not in psd and target_name not in h:
                problems.append(f"{sid} 的目标 {target}({target_name}) 既不在 psdSkill 也不在 h 里")
                continue
            damage = series(target, "damage", rows)
            if not damage:
                problems.append(f"{sid} 的目标 {target}({target_name}) 没有任何 damage 行")
                continue
            out.append({
                "target": target,
                "targetName": target_name,
                "field": field,
                "said": said,
                "pct": values,
                "targetDamage": damage,
            })
        if out:
            boosts[str(sid)] = out

    payload = {
        "source": "tools/ms282/export_skill_boosts.py",
        "note": "h「[被动效果：X技能伤害增加#field%p]」：学了来源技能后，目标技能伤害 ×(100+Σfield)/100",
        "boosts": boosts,
    }
    if not args.check:
        OUT.write_text(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
                       encoding="utf-8")

    total = sum(len(v) for v in boosts.values())
    print(f"技能间伤害强化 {len(boosts)} 条来源 / {total} 条目标；不一致 {len(problems)} 条")
    for sid, entries in sorted(boosts.items()):
        for e in entries:
            print(f"  {sid} {rows[str(sid)]['name']} → {e['target']} {e['targetName']} "
                  f"{e['field']} lv1={e['pct'][0]} lv{len(e['pct'])}={e['pct'][-1]} "
                  f"(目标伤害 {e['targetDamage'][0]}..{e['targetDamage'][-1]})")
    for p in problems:
        print("  !", p)
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
