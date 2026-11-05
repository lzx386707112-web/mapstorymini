#!/usr/bin/env python3
"""导出 282 剩余 6 条「buff 无 stat / timer-only」的真实机制 → NativeSkillMechanics.json

背景：check_skill_effects.py 的「无 stat」名单里，400001004 之外还剩 3 条真缺口
（5141011 / 400041001 / 400041061），以及 §9 里 3 条只挂了计时器的子状态
（2141502 / 5141001 / 4241005）。逐条查 282 原始节点后发现它们全都能落地，
且数值不需要猜：

  5141011 海龍石VI   h「消耗#u个海龙石，#time秒內可發動突擊技能」「每次發動海龍技能時
      可獲得1個海龍石」「[被動效果：海龍之怒VI傷害增加#damPlus%p]」
      Skill/514.img/skill/5141013/skillList = 5101012/5111002/5121016/5141015
      Skill/514.img/skill/5141014/skillList = 5121007/5141000/5141002
      5141013.damage 与母技能 damage、5141014.damage 与母技能 z 30 级逐行相等。
  400041001 散式投擲 h「三飞闪：追加4方向，追加方向以#u%的傷害攻擊最多#dummyStr名敵人」
      「四飞闪：追加3方向，追加方向以#damage%的傷害攻擊最多#dummyStr2名敵人」
      Skill/40004.img/skill/400041017.common.damage=290 == 母技能 u
      Skill/40004.img/skill/400041018.common.damage=379 == 母技能 damage
      追加方向 4/3 与这两个伤害体的 bulletCount 3/4 互补（合计 7）。
  400041061 飛閃起爆符 h「投擲手裏劍的攻擊技能命中敵人時…最多#v名敵人…
      #z%的傷害攻擊#q次的爆炸」「每#u秒會準備1個爆炸符咒」「#time秒內可使用#x個」
      「主動效果和被動效果不會同時發動」「最少#s個到最多#w個符咒」
      Skill/40004.img/skill/400041061/skillList = 13 条投掷技能
      爆炸体 400041062.damage = 475+19*x == 母技能 z（494..1045）逐级相等，
      attackCount=q=5、mobCount=v=6。subTime=10000ms == u=10 秒。
  5141001 超級閃‧連殺 desc「海龍怒擊發動時，一定時間內閃．連殺VI變更為超級閃．連殺」
      5141000 h「发动海龙怒击时，#x秒內可使用超級閃‧連殺」x=10 == 状态 time=10
      替换体 5141002（本体 3 段）+ 5141003（冲击波 10 段，damage==5141000.w2、
      attackCount==q2、mobCount==v2）都在技能表里，30 级逐级相等。
  2141502 劇毒煉獄 母技能 2141500 h「#time秒內元素吸收的上限扩充至#x堆叠数」
      Skill/210.img/skill/2100009.common = {x:3, y:5}（每堆叠最终伤害 +3%，默认最多 5 个）
      h「按照自己的技能來給的持續性傷害，增加最終傷害#x%。最多適用#y個」
      desc「一定時間內讓元素吸收的上限提升，固定為最大堆疊數」
      ⇒ 状态期间锁满 10 层 = FinalDamage +3×10=30%，默认上限 5 层 = +15%。
  4241005 黑暗面  desc「迴避敵人的攻擊…依照迴避次數的比例套用冷卻時間」
      母技能 4001003 h「回避依照最大HP的比率造成伤害的攻击时，會等隱身術解除後
      依照迴避的次數套用冷卻時間#cooltime秒，最多可重疊到#y次」
      Skill/400.img/skill/4001003/common = {cooltime:3, y:5, w2:20}
      状态 4241005.common = {time:6, w2:20, subTime:4000}
      ⇒ 隐身期间免疫「按最大HP 比率」的伤害（本工程的 TakeBossDamage 就是这条通路），
        按回避次数 × 3 秒累计隐身术冷却，最多 5 次、单次窗口最多回避 20 次。

落盘只登记「字段名 / id 名单 / 原文证据」，所有伤害数值运行时从技能表逐级读。

用法
    python3 tools/ms282/export_skill_mechanics.py            # 生成 + 自校验
    python3 tools/ms282/export_skill_mechanics.py --check    # 只打印，不写文件
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import skill_string, simplify, text_of  # noqa: E402

TABLES = [ROOT / "Assets/Resources/UI/VMatrixSkills.json",
          ROOT / "Assets/Resources/UI/AdvancedSkills.json",
          ROOT / "Assets/Resources/UI/JobTree.json"]
OUT = ROOT / "Assets/Resources/NativeSkillMechanics.json"

# 原端 skillList / common 里没有展开进 UI 技能表的常量。出处写在 evidence 里，
# 守卫会拿它们与母技能 level 行、h 原文交叉校验；不放进 C# 代码。
THROW_SKILLS = [4121013, 4121017, 4121052, 4001344, 4101008, 4111010, 4111015,
                4141000, 4141001, 4141005, 4141009, 4141010, 4141013]
DRAGON_GAIN = [5101012, 5111002, 5121016, 5141015, 5121007, 5141000, 5141002]
DRAGON_TRIGGER = [5121007, 5141000, 5141002]

MECHANICS: dict[int, dict] = {
    5141011: {
        "kind": "resourceBurst",
        "capField": "u", "costField": "u", "durationField": "time",
        "maxBurstsField": "w3",
        "gainSkills": DRAGON_GAIN,
        "bodies": [5141013, 5141014],
        # 伤害体字段与母技能字段的逐级对应（导出期校验，运行时读伤害体自己的表行）
        "bodyFields": {"damage": "damage", "attackCount": "attackCount", "mobCount": "mobCount"},
        "secondBodyFields": {"damage": "z", "attackCount": "w2", "mobCount": "w"},
        "bonusField": "damPlus", "bonusTargets": [5141006, 5141008],
        "hLine": "消耗#u个海龙石",
    },
    400041001: {
        "kind": "spread",
        "durationField": "time", "cooldownField": "cooltime",
        "throwSkills": THROW_SKILLS + [400041017, 400041018],
        "variants": [
            {"id": 400041017, "field": "u", "directions": 4, "mobField": "dummyStr",
             "bulletCount": 3, "hLine": "三飞闪：追加4方向",
             "triggers": [400041017, 4111010, 4121007]},
            {"id": 400041018, "field": "damage", "directions": 3, "mobField": "dummyStr2",
             "bulletCount": 4, "hLine": "四飞闪：追加3方向",
             "triggers": [400041018, 4121013, 4141000, 4141001, 4141005, 4141009,
                          4141010, 4141013, 4121017, 4121052, 4001344, 4101008, 4111015]},
        ],
        # 追加方向数与伤害体的 bulletCount 互补（4+3 / 3+4），合计一次投掷 7 个方向。
        "directionTotal": 7,
        "hLine": "使用投掷类技能时一次向多个方向投掷",
    },
    400041061: {
        "kind": "charm",
        "capField": "x", "durationField": "time", "cooldownField": "cooltime",
        "minPerCastField": "s", "maxPerCastField": "w",
        "passiveIntervalField": "subTime", "passiveSecondsField": "u",
        "damageField": "z", "hitsField": "q", "mobsField": "v",
        "body": 400041062,
        "throwSkills": THROW_SKILLS,
        "hLine": "爆炸符咒",
    },
    5141001: {
        "kind": "replace",
        "state": 5141001, "parent": 5141014, "source": 5141000,
        "durationField": "time", "stateField": "time",
        "triggerSkills": DRAGON_TRIGGER,
        "bodies": [5141002, 5141003],
        # 冲击波 5141003 的字段与源技能 5141000 的字段逐级相等
        "shockFields": {"damage": "w2", "attackCount": "q2", "mobCount": "v2"},
        "hLine": "发动海龙怒击时",
    },
    2141502: {
        "kind": "elementAbsorb",
        "state": 2141502, "parent": 2141500,
        "durationField": "time", "capField": "x",
        # Skill/210.img/skill/2100009/common：x=3 每层最终伤害%，y=5 默认堆叠上限
        "perStackPct": 3, "baseCap": 5, "statKey": "FinalDamage",
        "hLine": "元素吸收的上限扩充至#x堆叠数",
    },
    4241005: {
        "kind": "darkSight",
        "state": 4241005, "parent": 4001003,
        "durationField": "time", "maxEvadesField": "w2",
        # Skill/400.img/skill/4001003/common：cooltime=3 秒/次、y=5 最多重叠次数
        "cooldownPerEvadeSec": 3, "maxStacks": 5,
        "hLine": "回避依照最大HP的比率造成伤害的攻击时",
    },
}


def skills() -> dict[str, dict]:
    merged: dict[str, dict] = {}
    for path in TABLES:
        merged.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return merged


def levels(row: dict) -> dict[str, dict]:
    return row.get("levels") or {}


def series(row: dict, field: str) -> list[int]:
    return [int((levels(row).get(str(lv)) or {}).get(field, 0))
            for lv in range(1, int(row.get("maxLevel", 30)) + 1)]


def h_of(sid: int) -> str:
    return simplify(text_of(skill_string(sid), "h")).replace("\\n", " ").strip()


def desc_of(sid: int) -> str:
    return simplify(text_of(skill_string(sid), "desc")).replace("\\n", " ").strip()


def source_text(cfg: dict, sid: int) -> str:
    """证据文本源：状态型看母技能/源技能的 h，其余看自己。与下面的校验同源。"""
    if cfg["kind"] in ("elementAbsorb", "darkSight"):
        return h_of(cfg["parent"])
    if cfg["kind"] == "replace":
        return h_of(cfg["source"]) + " " + desc_of(cfg["state"])
    return h_of(sid)


def evidence(cfg: dict, sid: int) -> str:
    h = source_text(cfg, sid)
    line = cfg.get("hLine", "")
    if line and line in h:
        return h[max(0, h.index(line) - 40):h.index(line) + len(line) + 90]
    return (h or desc_of(cfg.get("parent", sid)))[:170]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只打印摘要，不写文件")
    args = ap.parse_args()

    table = skills()
    problems: list[str] = []
    payload: dict[str, dict] = {}

    for sid, cfg in MECHANICS.items():
        row = table.get(str(sid))
        if row is not None:
            lv1 = levels(row).get("1", {})
            for key in ("capField", "costField", "durationField", "maxBurstsField",
                        "minPerCastField", "maxPerCastField", "passiveIntervalField",
                        "passiveSecondsField", "damageField", "hitsField", "mobsField",
                        "bonusField", "cooldownField", "maxEvadesField"):
                field = cfg.get(key)
                if field and field not in lv1:
                    problems.append(f"{sid} 的 {key}={field} 不在 level/1")
        entry = dict(cfg)
        entry["name"] = (row or {}).get("name") or ""
        payload[str(sid)] = entry

    # —— 逐级自证 ——
    dragon = table.get("5141011", {})
    for body, fields in (("5141013", MECHANICS[5141011]["bodyFields"]),
                         ("5141014", MECHANICS[5141011]["secondBodyFields"])):
        brow = table.get(body)
        if not brow:
            problems.append(f"{body} 伤害体不在技能表")
            continue
        for body_field, parent_field in fields.items():
            if series(brow, body_field) != series(dragon, parent_field):
                problems.append(f"{body}.{body_field} 与 5141011.{parent_field} 不是逐级相等")
        # 母技能 damage/z 必须是 30 级递增的实数，不是常量占位
        if len(set(series(dragon, fields["damage"]))) < 10:
            problems.append(f"5141011.{fields['damage']} 不像逐级数值")

    charm = table.get("400041061", {})
    z = series(charm, "z")
    if any(v != 475 + 19 * lv for lv, v in enumerate(z, start=1)):
        problems.append("400041061.z 与爆炸体 400041062.common.damage=475+19*x 不相等")
    passive_ms = int((levels(charm).get("1") or {}).get("subTime", 0))
    passive_s = int((levels(charm).get("1") or {}).get("u", 0))
    if passive_ms != passive_s * 1000:
        problems.append("400041061.subTime 不等于 u 秒（被动准备周期）")
    for key in ("q", "v", "x", "s", "w", "time"):
        if int((levels(charm).get("1") or {}).get(key, 0)) <= 0:
            problems.append(f"400041061.{key} 缺失或非正")

    spread = table.get("400041001", {})
    l1 = levels(spread).get("1", {})
    if l1.get("u") != 290 or l1.get("damage") != 379:
        problems.append("400041001 的三/四飞闪追加倍率与 400041017/18.common 不符")
    for var in MECHANICS[400041001]["variants"]:
        if var["directions"] + var["bulletCount"] != MECHANICS[400041001]["directionTotal"]:
            problems.append(f"{var['id']} 的追加方向与 bulletCount 不互补")
        if int(l1.get(var["mobField"], 0)) <= 0:
            problems.append(f"400041001.{var['mobField']} 缺失")

    src = table.get("5141000", {})
    shock = table.get("5141003", {})
    for body_field, src_field in MECHANICS[5141001]["shockFields"].items():
        if series(shock, body_field) != series(src, src_field):
            problems.append(f"5141003.{body_field} 与 5141000.{src_field} 不是逐级相等")
    state_time = int((levels(table.get("5141001", {})).get("1") or {}).get("time", 0))
    if state_time != int((levels(src).get("1") or {}).get("x", 0)):
        problems.append("5141001.time 与 5141000.x（海龙怒击后可用秒数）不符")

    poison = table.get("2141500", {})
    state_poison = table.get("2141502", {})
    cap = int((levels(state_poison).get("1") or {}).get("x", 0))
    if cap != int((levels(poison).get("1") or {}).get("x", 0)):
        problems.append("2141502.x 与母技能 2141500.x（元素吸收上限）不符")
    if cap != 10 or MECHANICS[2141502]["perStackPct"] * cap != 30:
        problems.append("元素吸收的锁满层数或每层百分比与 2100009.common 不符")

    dark = table.get("4241005", {})
    d1 = levels(dark).get("1", {})
    if int(d1.get("w2", 0)) != 20 or int(d1.get("time", 0)) <= 0:
        problems.append("4241005 的 w2/time 与 4001003.common 不符")

    # —— h / desc 原文证据（判据是文案，不是字段名）——
    for sid, cfg in MECHANICS.items():
        text = source_text(cfg, sid)
        line = cfg.get("hLine", "")
        if line and line not in text:
            problems.append(f"{sid} 的 h/desc 里找不到证据片段「{line}」")
        payload[str(sid)]["evidence"] = evidence(cfg, sid)

    # —— 名单里的 id 必须是正整数，伤害体必须在技能表（不在表的爆炸体/追加体单独标注）——
    for sid, cfg in MECHANICS.items():
        for var in cfg.get("variants", []):
            line = var.get("hLine", "")
            if line and line not in h_of(sid):
                problems.append(f"{sid} 的变体 {var['id']} 缺 h 证据「{line}」")
            for value in var.get("triggers", []):
                if not isinstance(value, int) or value <= 0:
                    problems.append(f"{sid} 变体 {var['id']} 的 triggers 里有非法 id {value}")
        for key in ("gainSkills", "throwSkills", "triggerSkills", "bodies", "bonusTargets"):
            for value in cfg.get(key, []):
                if not isinstance(value, int) or value <= 0:
                    problems.append(f"{sid}.{key} 里有非法 id {value}")
        for body in cfg.get("bodies", []):
            if str(body) not in table:
                problems.append(f"{sid} 的伤害体 {body} 不在技能表")
        if cfg.get("body") and str(cfg["body"]) not in table:
            # 400041062 的伤害值已经用 475+19*x 逐级自证过，运行时读母技能 z。
            payload[str(sid)]["bodyFromParentField"] = "z"

    out = {"source": "tools/ms282/export_skill_mechanics.py", "mechanics": payload}
    if args.check:
        print(json.dumps(out, ensure_ascii=False, indent=1)[:2000])
    else:
        OUT.write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"机制 {len(payload)} 条，不一致 {len(problems)} 条"
          + ("" if args.check else f" → {OUT.relative_to(ROOT)}"))
    for problem in problems:
        print("  !", problem)
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
