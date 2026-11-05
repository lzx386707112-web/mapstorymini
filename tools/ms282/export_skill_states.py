#!/usr/bin/env python3
"""导出 282 的「技能子状态」表 → Assets/Resources/NativeSkillStates.json

发现（2026-11，本轮）
    check_skill_effects.py 一直报 19 条「buff 但无 stat」，其中 12 条根本不是玩家
    能按的 buff，而是**母技能施放后挂上的内部状态 id**。判据不靠猜，四条证据全在 282：

      ① invisible=1、type=buff，level/1 只有 time / subTime / 单个字母，没有
        mpCon / damage / attackCount / cooltime —— 不满足「可施放」的形状；
      ② String/Skill.img 里这些 id **没有 h**，只有 desc，而同名的兄弟 id 有完整 h
        （2141502 无 h / 2141500 有 h；5141501 无 h / 5141500 有 h …）；
      ③ 影子 id 的 common 字段是母技能 level/1 的静态投影，逐级相等：
        2141502.time=20,x=10  == 2141500.level/1.time=20,x=10
        5141501.time=30       == 5141500.level/1.time=30
        14141021.time=7       == 14141017.level/1.time=7
        4240017.time=3,x=9    == 4240016.level/1.w=3,v=9（h 写「持續#w秒」「#v秒以上」）
      ④ 母技能的 h 显式写了这个状态的语义：
        11141500「使用時#w2秒內最終傷害增加#q2%」    → 11141501.time=20,y=5
        5141500 「在#time秒內…使用五轉技能時觸發水龍鬥氣」→ 5141501.skillList 13 条
        4240016 「隱身術持續#v秒以上時，強化為暗影之刃」 → 4240017

    把它们继续当「没做完的 buff」统计是错误的：它们本来就不该产出 stat。
    正确的接线是：母技能施放 → 记一条有时限的状态 → 由状态去驱动增益 / 追加攻击。

落盘结构
    states.<id> = {
      parent      母技能 id（施放时激活本状态）
      name/time   time 取自影子自己的 common（与母技能 level 行一致）
      evidence    母技能 h 的原文片段 或 同名 desc（证明是同一族）
      role        duration / procGate / timedEffect
      ...         timedEffect / triggers / bodies / chain*（仅个别状态有）
    }

用法
    python3 tools/ms282/export_skill_states.py           # 生成 + 自校验
    python3 tools/ms282/export_skill_states.py --check   # 只打印，不写文件
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

ADVANCED = ROOT / "Assets/Resources/UI/AdvancedSkills.json"
VMATRIX = ROOT / "Assets/Resources/UI/VMatrixSkills.json"
JOBTREE = ROOT / "Assets/Resources/UI/JobTree.json"
SHIELDS = ROOT / "Assets/Resources/NativeShieldContract.json"
OUT = ROOT / "Assets/Resources/NativeSkillStates.json"

# 三条同族里找不到「可施放兄弟」的，按 282 的 h / 节点结构人工登记（原文即依据）。
PARENT_OVERRIDE: dict[int, tuple[int, str]] = {
    # 4241005 黑暗面 info/type=14（darksight），desc 与 4001003 隱身術逐字相同：
    # 「移動時間內將身體隱藏於影子裡，迴避敵人的攻擊…」，是隐身术的 6 转状态体。
    4241005: (4001003, "String/Skill.img 4001003「隱身術」desc 与 4241005 相同"),
    # 4240016 暗影之隱 h：「隱身術持續#v秒以上時，強化為暗影之刃」「#c暗影之刃#：持續#w秒」。
    4240017: (4240016, "4240016 h「隱身術持續#v秒以上時，強化為暗影之刃」；w=3,v=9 与 4240017.time/x 相等"),
    # 5141001 desc：「海龍怒擊發動時，一定時間內閃．連殺VI變更為#c超級閃．連殺#」。
    5141001: (5141014, "5141001 desc「海龍怒擊發動時，一定時間內閃．連殺VI變更為超級閃．連殺」"),
    # 1241501 聖域展開：Skill/124.img 里这个 id 只有 common/tile/screen/variableNumList，
    # 没有 info / effect / level，是领域状态对象而非可施放技能。common 里有 cooltime=360
    # （母技能冷却的投影），所以下面要显式放行。母技能 1241500 h「連續#u秒內形成聖域」，
    # u=30 == 1241501.time=30。
    1241501: (1241500, "1241500 h「連續#u秒內形成聖域，聖騎士在聖域內進行攻擊時…審判之光」；"
                        "1241501 无 info/effect/level，u=time=30"),
}

# 形状上带 cooltime 但原始节点证明是领域状态、不是可按技能的 id。
EXTRA_STATES = {1241501}

# 5141501 的 skillList 节点（Skill/514.img/skill/5141501/skillList，13 条）。
# 母技能 h：「在#time秒內，除格鬥技能與海龍螺旋外，使用五轉技能時觸發#c水龍鬥氣#。
#            #c水龍鬥氣#：冷卻時間#dummyStr秒，最多可發動#dummyStr2次攻擊。」
CHAINS: dict[int, dict] = {
    5141501: {
        "parent": 5141500,
        "triggers": [5101012, 5111002, 5111009, 5121007, 5121016,
                     400051003, 400051042, 400051070, 400051071,
                     5141000, 5141002, 5141003, 5141015],
        # 五段伤害体：其 damage 分别是 589/897/930/992/1136，与母技能 y/w/v/s4/q 逐级对应。
        "bodies": [5141502, 5141503, 5141504, 5141505, 5141506],
        "cooldownSec": 2,     # 5141500.level/1.dummyStr
        "maxActivations": 8,  # 5141500.level/1.dummyStr2
    },
    # 1241500 聖域展開 h：「連續#u秒內形成聖域，聖騎士在聖域內進行攻擊時，
    #   發動最多對#v名的敵人以#y%的傷害攻擊#z次的審判之光，冷卻時間是#t秒」
    # 触发名单 = 1241502.skillList（15 条），伤害体 1241502：
    #   damage=600+20*x（lv1=620 … lv30=1200）== 母 y；attackCount=13 == z；mobCount=5 == v。
    # 再发动冷却取伤害体 common/cooltimeMS=500（母技能 t=0 是占位，不能用秒）。
    1241501: {
        "parent": 1241500,
        "triggers": [1221009, 1221019, 1221021, 1221011, 1221052, 1211018,
                     1211012, 1201015, 1201013, 400011072, 400011132,
                     1241000, 1241003, 1241004, 1241007],
        "bodies": [1241502],
        "cooldownMs": 500,    # 1241502.common/cooltimeMS
        "maxActivations": 0,  # 0 = 不限次数，由状态 30 秒自己收口
    },
}

# 个别状态自己不写 time（只有 prop/x），时长在母技能 h 引用的字段里。
DURATION_OVERRIDE: dict[int, tuple[int, str]] = {
    # 4241013 h「消耗MP#mpCon，#y秒內每隔一段時間…」；level/1.y=12。
    4240014: (12, "4241013 h「#y秒內每隔一段時間…」，level/1.y=12"),
}

# 2341011 天堂之門VI h：「對周圍的隊員賦予無視HP歸零一次的加持效果，
# 獲得加持的隊員#time秒內無法再次獲得」。状态 2341012.time=600 就是这段时限。
DEATH_GUARDS = {
    2341012: "2341011 h「對周圍的隊員賦予無視HP歸零一次的加持效果，#time秒內無法再次獲得」",
}

# 11141500「使用時#w2秒內最終傷害增加#q2%」：状态体 11141501 的 y 就是 q2（lv1=5 → lv30=10）。
TIMED_EFFECTS = {11141501: "11141500 h「使用時#w2秒內最終傷害增加#q2%」；11141501.y 与 q2 逐级相等"}

# 2141502 劇毒煉獄：母技能 2141500 h「#time秒內元素吸收的上限擴充至#x堆疊數」，
# desc「一定時間內讓元素吸收的上限提升，固定為最大堆疊數」。
# 元素吸收本体 Skill/210.img/skill/2100009/common = {x:3, y:5, range:800}，
# h「按照自己的技能來給的持續性傷害，增加最終傷害#x%。最多適用#y個」
# ⇒ 每层 +3% 最终伤害、默认上限 5 层；状态把上限锁到 10 层 ⇒ FinalDamage +30。
# 数值由下面的导出期计算（状态 x × 每层 3），C# 不写死。
ELEMENT_ABSORB = {"state": 2141502, "parent": 2141500, "perStackPct": 3, "baseCap": 5,
                  "statKey": "FinalDamage",
                  "evidence": "2141500 h「元素吸收的上限扩充至#x堆叠数」；"
                              "2100009.common x=3/y=5，h「增加最终伤害#x%。最多适用#y个」"}


def skill_tables() -> dict[str, dict]:
    """状态检测只看 V/HEXA；JobTree 仅用于校验触发技能存在（它是 GMS083 编号，会撞号）。"""
    merged: dict[str, dict] = {}
    for path in (VMATRIX, ADVANCED):
        merged.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return merged


def all_skill_ids() -> set[str]:
    ids: set[str] = set()
    for path in (VMATRIX, ADVANCED, JOBTREE):
        if path.is_file():
            ids |= set(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return ids


def castable(row: dict) -> bool:
    """母技能形状：可施放（有 MP 消耗 / 伤害 / 冷却）。"""
    lv = (row.get("levels") or {}).get("1") or {}
    return all(f in lv for f in ("mpCon", "damage", "cooltime"))


def find_parent(sid: int, row: dict, skills: dict[str, dict]) -> int | None:
    if sid in PARENT_OVERRIDE:
        return PARENT_OVERRIDE[sid][0]
    name, own_time = row.get("name"), (row["levels"]["1"].get("time"))
    candidates = [int(k) for k, v in skills.items()
                  if k != str(sid) and v.get("name") == name
                  and v.get("type") in ("attack", "summon") and castable(v)]
    if not candidates:
        return None
    # 优先取 time 逐级相同的那个（2141502→2141500、5141501→5141500…）；
    # 没有 time 可比时取同族里 id 最小的可施放技能（4240014→4241013、11141501→11141500）。
    for cid in candidates:
        other = (skills[str(cid)]["levels"].get("1") or {}).get("time")
        if own_time is not None and other == own_time:
            return cid
    return min(candidates)


def evidence_for(sid: int, parent: int) -> str:
    parent_node = skill_string(parent)
    h = simplify(text_of(parent_node, "h"))
    if h:
        # 取含 time / 触发语义的那一行，避免整段 h 撑爆 JSON。
        for line in h.replace("\\n", "\n").split("\n"):
            if any(word in line for word in ("#time秒", "最終傷害", "水龍鬥氣", "強化為", "冷卻時間#dummyStr")):
                return line.strip()[:180]
        return h.replace("\\n", " ")[:180]
    own = skill_string(sid)
    return simplify(text_of(own, "desc")).replace("\\n", " ")[:180]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只打印摘要，不写文件")
    args = parser.parse_args()

    skills = skill_tables()
    every_id = all_skill_ids()
    shield_ids = set(json.loads(SHIELDS.read_text(encoding="utf-8"))["shields"]) if SHIELDS.is_file() else set()
    problems: list[str] = []
    states: dict[str, dict] = {}

    for sid_str, row in skills.items():
        sid = int(sid_str)
        lv1 = (row.get("levels") or {}).get("1") or {}
        # 影子状态形状：invisible 的 buff、没有可施放字段、只有 time/subTime/单字母。
        if (row.get("type") != "buff" or row.get("invisible") != 1 or sid_str in shield_ids):
            continue
        if sid not in EXTRA_STATES and any(f in lv1 for f in ("damage", "attackCount", "mpCon", "cooltime", "pad", "mad", "indieDamR")):
            continue
        if not lv1 or "time" not in lv1 and not (set(lv1) & {"x", "prop", "w"}):
            continue
        parent = find_parent(sid, row, skills)
        if parent is None:
            problems.append(f"{sid} {row.get('name')} 找不到母技能（需人工登记 PARENT_OVERRIDE）")
            continue
        if skill_string(sid) is not None and text_of(skill_string(sid), "h"):
            problems.append(f"{sid} {row.get('name')} 自己有 h，不该当成影子状态")

        entry: dict = {
            "name": row.get("name"),
            "parent": parent,
            "time": lv1.get("time", DURATION_OVERRIDE.get(sid, (0, ""))[0]),
            "role": "duration",
            "evidence": evidence_for(sid, parent),
        }
        if sid in DURATION_OVERRIDE:
            entry["evidence"] = DURATION_OVERRIDE[sid][1]
        if sid in TIMED_EFFECTS:
            entry["role"] = "timedEffect"
            entry["timedEffect"] = True
            entry["evidence"] = TIMED_EFFECTS[sid]
        if sid == ELEMENT_ABSORB["state"]:
            # 元素吸收：状态期间锁满 x=10 层，每层 3% ⇒ FinalDamage +30。
            # 默认上限 5 层（2100009.common.y）只作对照，运行时由状态表给出满层值。
            entry["role"] = "timedEffect"
            entry["timedEffect"] = True
            entry["effectKey"] = ELEMENT_ABSORB["statKey"]
            entry["effectPct"] = int(lv1.get("x", 0)) * ELEMENT_ABSORB["perStackPct"]
            entry["elementAbsorbCap"] = int(lv1.get("x", 0))
            entry["evidence"] = ELEMENT_ABSORB["evidence"]
        if sid in DEATH_GUARDS:
            entry["role"] = "deathGuard"
            entry["deathGuard"] = True
            entry["evidence"] = DEATH_GUARDS[sid]
        if sid in CHAINS:
            chain = CHAINS[sid]
            entry["role"] = "procGate"
            entry["triggers"] = chain["triggers"]
            entry["bodies"] = chain["bodies"]
            if "cooldownMs" in chain:
                entry["cooldownMs"] = chain["cooldownMs"]
            else:
                entry["cooldownSec"] = chain["cooldownSec"]
            entry["maxActivations"] = chain["maxActivations"]
            entry["evidence"] = simplify(text_of(skill_string(chain["parent"]), "h")).replace("\\n", " ")[:220]
        states[sid_str] = entry

    # 元素吸收：状态 x 必须与母技能 2141500.level/1.x 相等，满层值 = x × 每层 3%。
    if str(ELEMENT_ABSORB["state"]) in states:
        cap = int((skills[str(ELEMENT_ABSORB["state"])]["levels"]["1"]).get("x", 0))
        parent_cap = int((skills[str(ELEMENT_ABSORB["parent"])]["levels"]["1"]).get("x", 0))
        expected = cap * ELEMENT_ABSORB["perStackPct"]
        if cap != parent_cap:
            problems.append(f"元素吸收状态 x={cap} 与母技能 x={parent_cap} 不相等")
        if states[str(ELEMENT_ABSORB["state"])]["effectPct"] != expected:
            problems.append("元素吸收满层 FinalDamage 计算与 2100009.common.x 不一致")

    # 表自洽：母技能存在、触发/连击体都在技能表里且带伤害。
    for sid, entry in states.items():
        if str(entry["parent"]) not in every_id:
            problems.append(f"{sid} 的母技能 {entry['parent']} 不在技能表")
        # 触发名单来自 282 的 skillList，里面有本工程未实现的老技能（5101012 等），
        # 运行时查不到就等于不会触发，不是错误；但连击伤害体必须真实存在且带 damage。
        for body in entry.get("bodies", []):
            if str(body) not in every_id:
                problems.append(f"{sid} 的连击体 {body} 不在技能表")
            elif not (skills.get(str(body), {}).get("levels", {}).get("1", {}).get("damage")):
                problems.append(f"{sid} 的连击体 {body} 没有 damage")

    # 聖域展開：审判之光伤害体的字段必须逐级等于母技能 h 引用的 y/z/v，
    # 冷却毫秒必须等于伤害体自己的 common/cooltimeMS（母技能 t=0 只是占位）。
    if "1241501" in states:
        parent_lv = skills.get("1241500", {}).get("levels", {})
        body_lv = skills.get("1241502", {}).get("levels", {})
        for body_field, parent_field in (("damage", "y"), ("attackCount", "z"), ("mobCount", "v")):
            compared = 0
            for lv, line in body_lv.items():
                expected = parent_lv.get(lv, {}).get(parent_field)
                if expected is None:
                    continue
                compared += 1
                if line.get(body_field) != expected:
                    problems.append(f"1241501 lv{lv}: 1241502.{body_field}="
                                    f"{line.get(body_field)} != 1241500.{parent_field}={expected}")
            if compared < 2:
                problems.append(f"1241501 1241502.{body_field} 只比对了 {compared} 级")
        if int(body_lv.get("1", {}).get("cooltimeMS", 0)) != CHAINS[1241501]["cooldownMs"]:
            problems.append("1241501 的 cooldownMs 不等于 1241502.common/cooltimeMS")

    payload = {
        "source": "tools/ms282/export_skill_states.py",
        "note": "282 的技能子状态：母技能施放后挂上的 invisible buff id，不是玩家可按的技能",
        "states": dict(sorted(states.items())),
    }
    print(f"== 技能子状态 ==")
    for sid, entry in payload["states"].items():
        print(f"  {sid:>9} {entry['name']:<12} parent={entry['parent']:<10} "
              f"time={entry['time']:<4} role={entry['role']}")
    print(f"状态 {len(states)} 条，不一致 {len(problems)} 条")
    for item in problems:
        print("  FAIL", item)
    if not args.check and not problems:
        OUT.write_text(json.dumps(payload, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print("已写入", OUT.relative_to(ROOT))
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
