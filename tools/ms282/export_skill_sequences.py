#!/usr/bin/env python3
"""导出 282 的「形态技能序列」表 → Assets/Resources/NativeSkillSequences.json

发现（2026-11 本轮）
    上一轮把 400031055 連射十字弓砲彈 / 400041025 黑影切斷 / 400011055 極樂之境
    记成「需要 buff 期间解锁子技能的新机制，未实现」。查 282 原始节点才知道机制
    本来就在数据里，只是本项目没接线：

      · 三条母技能的节点上都有 `psdSkill/<子技能id>`，且 `isSequenceOn=1`；
      · 伤害根本不在母技能 common 里，而在子技能的 common 里 ——
        母技能只是「开一个形态 + 给次数」，子技能才是真正的攻击体：
            400031055 (type=15) → 400031056 極速射擊
            400011055 (type=33) → 400011056 黃泉十字架
            400041025 (type=33) → 400041026 黑影切斷（成功版）
      · 子技能一律 `invisible=1`（不在技能窗出现、不能单独加点）。

    h 文案与字段逐项自证（下面每条 SEQUENCES 的 evidence 就是原文）：

      400031055 h「消耗MP #mpCon，#time秒內可使用#x個彈匣
                   #c極速射擊#：消耗MP #y，消耗1個彈匣，
                   發射#v支以#w%的傷害攻擊#u次的箭矢」
        → 子技能 400031056：damage = 500+20x（== 母 w）、attackCount = 9（== 母 u）、
          mpCon = 150（== 母 y）、multiAttackInfo 4 条（== 母 v「發射 4 支」）。
        → 触发是「再次使用技能」（desc「再次使用技能即可使用…全速射擊」）。

      400011055 h「消耗MP #mpCon，在#time秒內可使用#w次交叉冥河
                   #c交叉冥河#：發生#u2次斬擊，每次以#x%的傷害攻擊最多#v名敵人#v2次」
        desc「在極樂之境中，可以以#c一般攻擊#使用交叉冥河」
        → 子技能 400011056：damage = 330+14x（== 母 x）、attackCount = 5（== 母 v2）、
          mobCount = 15（== 母 v）、multiAttackInfo 5 条（== 母 u2）、mpCon = 0。
        → 触发是「形态期间普通攻击被替换」。

      400041025 h「如果有傷口減益效果到達最大值的敵人，可在致命暗殺的最終攻擊
                   和音速狂襲中連接使用，且此時會移動到該名敵人的背後，
                   發動以#w%的傷害攻擊#z次的斬擊#q2次，…攻擊後#t秒內無敵」
        → 子技能 400041026：damage = 825+33x（== 母 w）、attackCount = 7（== 母 z）、
          mobCount = 1、cooltime = 20（== 母 u2）、casterMove=1、
          info2/ignoreImmune=1 + ignoreCounter=1（h「即使對無視攻擊和攻擊反射狀態的
          敵人發動攻擊也可造成傷害」）、multiAttackInfo 5 条（== 母 q2）。
        → 触发是「伤口满层的连携」，并且 4241000 的 `addAttack/skill=400041025`
          + `pattern=151` 是连携窗口的原端形状（see WOUNDS）。

    `magic` 一律继承母技能，**不看子技能的 info/magicDamage**：400031056 是弩手的
    箭矢（母 400031055 物理），节点上却挂着 magicDamage=1，按它结算会拿魔法力
    去打物理箭。

落盘结构
    sequences.<parentId> = {
      parent / child / childName
      trigger      recast | basicAttack | chain
      durationSec  形态时长（母技能 common/time）
      charges      形态内可用次数（母技能字段，见 chargesField）
      childMp      每次子技能消耗的 MP（0 = 不额外扣）
      repeat       一次施放结算几轮（multiAttackInfo 条数，与母技能字段交叉自证）
      childSkill   子技能的 info 元信息（kind/magic/skillType/areaAttack/vMatrix/element）
      childLevels  子技能 common 公式按 1..maxLevel 展开的逐级行
      evidence     母技能 h 原文
    }
    wounds.<parentId> = { maxStacks, durationSec, sources[], evidence }

用法
    python3 tools/ms282/export_skill_sequences.py            # 生成 + 自校验
    python3 tools/ms282/export_skill_sequences.py --check     # 只打印，不写文件
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
    expand, level_rows, skill_string, simplify, text_of,
)
from wzpy import WzImage, WzKey  # noqa: E402

PACKS = Path("/tmp/packs")
OUT = ROOT / "Assets/Resources/NativeSkillSequences.json"
BMS = WzKey.for_region("BMS")
_cache: dict[str, WzImage] = {}


def skill_node(skill_id: int):
    """按 282 的 img 分片规则取 skill/<id>。Skill_<id//10000>.img 的根下是 skill。"""
    src = skill_id // 10000
    name = f"Skill_{src}.img"
    path = PACKS / name
    if not path.is_file():
        raise SystemExit(f"缺少 {path}（先解包 282 Data/Packs/Skill_*.ms 到 /tmp/packs）")
    if name not in _cache:
        _cache[name] = WzImage.from_bytes(path.read_bytes(), key=BMS, name=name)
    root = _cache[name].get("skill")
    if root is None:
        raise SystemExit(f"{name} 没有 skill 根节点")
    node = root.get(str(skill_id))
    if node is None:
        raise SystemExit(f"{name} 里没有技能 {skill_id}")
    return node


def string_entry(skill_id: int) -> tuple[str, str, str]:
    """String/Skill.img/<id> → (name, desc, h)，并转简体。"""
    node = skill_string(skill_id)
    return (
        simplify(text_of(node, "name")),
        simplify(text_of(node, "desc")),
        simplify(text_of(node, "h")),
    )


def info_int(node, key: str, fallback: int = 0) -> int:
    holder = node.get("info")
    if holder is None:
        return fallback
    child = holder.get(key)
    if child is None or not isinstance(getattr(child, "value", None), (int, float)):
        return fallback
    return int(child.value)


def child_ids(node) -> list[int]:
    psd = node.get("psdSkill")
    if psd is None:
        return []
    result = []
    for child in psd.children():
        if child.name.isdigit():
            result.append(int(child.name))
    return result


# ——— 登记表 ———
#
# 三条都是「母技能开形态 / 子技能出伤害」的 psdSkill 结构，但触发入口不同，
# 这是 282 用 h 文案说明、而不是用字段编码的部分，所以按原文登记。
#
# self_evidence: (子技能字段, 母技能字段) —— 逐级必须相等，否则导出直接失败。
SEQUENCES: dict[int, dict] = {
    400031055: {
        "child": 400031056,
        "trigger": "recast",
        "charges_field": "x",
        "duration_field": "time",
        "child_mp": True,
        "magic": False,
        # 母技能 common 没有 damage（w 是子技能的伤害），按下去只开形态。
        "parent_attack": False,
        "self_evidence": [("damage", "w"), ("attackCount", "u"), ("mpCon", "y")],
        "repeat_field": "v",
        "evidence": "h「消耗MP #mpCon，#time秒內可使用#x個彈匣／#c極速射擊#：消耗MP #y，"
                    "消耗1個彈匣，發射#v支以#w%的傷害攻擊#u次的箭矢」；"
                    "desc「再次使用技能即可使用能利用箭筒的全速射擊」",
    },
    400011055: {
        "child": 400011056,
        "trigger": "basicAttack",
        "charges_field": "w",
        "duration_field": "time",
        "child_mp": True,
        "magic": False,
        # 母技能 common 的 damage/attackCount/mobCount 属于**裂缝爆炸**，不是按下去那一下；
        # 主攻击是子技能 400011056。裂缝见下面 ZONES。
        "parent_attack": False,
        "self_evidence": [("damage", "x"), ("attackCount", "v2"), ("mobCount", "v")],
        "repeat_field": "u2",
        "evidence": "h「消耗MP #mpCon，在#time秒內可使用#w次交叉冥河／#c交叉冥河#：發生#u2次斬擊，"
                    "每次以#x%的傷害攻擊最多#v名敵人#v2次」；"
                    "desc「在極樂之境中，可以以#c一般攻擊#使用交叉冥河」",
    },
    400041025: {
        "child": 400041026,
        "trigger": "chain",
        "charges_field": "",           # 次数由伤口层数消耗，不另计
        "duration_field": "",
        "child_mp": False,             # 母技能按下已扣 mpCon，连携段不再扣
        "magic": False,
        # h「#主動效果#:消耗MP#mpCon，召喚出藏身的同伴，最多向#v名敵人以#y%的傷害攻擊#q次」
        # ⇒ 母技能按下去自己就要打一段（≠ 400011055 那种 buff + 区域）。
        "parent_attack": True,
        "self_evidence": [("damage", "w"), ("attackCount", "z")],
        "repeat_field": "q2",
        "evidence": "h「如果有傷口減益效果到達最大值的敵人，可在致命暗殺的最終攻擊和音速狂襲中"
                    "連接使用，且此時會移動到該名敵人的背後，發動以#w%的傷害攻擊#z次的斬擊#q2次，"
                    "…攻擊後#t秒內無敵」",
    },
}

# ——— 裂缝（400011055 的第二段机制）———
#
# h「以交叉冥河攻擊時斬裂空間，留下持續#y秒的裂縫
#    #c裂縫#：持續以交叉冥河攻擊裂縫會使裂縫逐漸擴大，最終發動#q次爆炸，
#    以#damage%的傷害攻擊最多#mobCount名敵人#attackCount次
#    裂縫在生成後#z秒可再次生成，最多生成#u3個，裂縫持續時間結束後會消失並立即爆炸，
#    但傷害降低為#u4%」
#
# 所以母技能 common 的 damage/attackCount/mobCount 是**爆炸**的数值，逐级都在 level 行里；
# y/z/q/u3 是常量，u4 是逐级变化的百分比（150+6x）。
ZONES: dict[int, dict] = {
    400011055: {
        "child": 400011056,
        "fields": {"maxZones": "u3", "lifeSec": "y", "regenSec": "z",
                   "stages": "q", "expirePercent": "u4"},
        "damage_fields": {"damage": "damage", "attackCount": "attackCount", "mobCount": "mobCount"},
        "evidence": "h「以交叉冥河攻擊時斬裂空間，留下持續#y秒的裂縫／#c裂縫#：持續以交叉冥河攻擊"
                    "裂縫會使裂縫逐漸擴大，最終發動#q次爆炸，以#damage%的傷害攻擊最多#mobCount名敵人"
                    "#attackCount次／裂縫在生成後#z秒可再次生成，最多生成#u3個，裂縫持續時間結束後"
                    "會消失並立即爆炸，但傷害降低為#u4%」",
    },
}

# ——— 伤口减益 ———
#
# 400041025 的被动段 h：「向被致命暗殺的最後攻擊和音速狂襲命中的敵人留下#time秒間
# 最多重疊3次的傷口減益效果。傷口減益效果只會對1名敵人留下」。
#   · 时长 = 母技能 common/time（10 秒，逐级常量）；
#   · 层数 3 只写在文案里（common 没有对应字段），登记为常量并注明出处；
#   · 来源技能由 h 点名：致命暗殺（4241000 第一击 / 4241001 最終攻擊）+ 音速狂襲 400041039。
WOUNDS: dict[int, dict] = {
    400041025: {
        "maxStacks": 3,
        "duration_field": "time",
        "sources": [4241000, 4241001, 400041039],
        "evidence": "h「向被致命暗殺的最後攻擊和音速狂襲命中的敵人留下#time秒間最多重疊3次的"
                    "傷口減益效果。傷口減益效果只會對1名敵人留下」",
    },
}


def multi_attack_count(node) -> int:
    """multiAttackInfo 的条目数 = 一次施放内的结算轮数（原端分阶段攻击）。"""
    holder = node.get("multiAttackInfo")
    if holder is None:
        return 1
    count = sum(1 for child in holder.children() if child.name.isdigit())
    return max(1, count)


def attack_times(node) -> list[int]:
    """multiAttackInfo 每条的 attackTime（毫秒）—— 轮与轮之间的间隔，原端就是这个数。"""
    holder = node.get("multiAttackInfo")
    if holder is None:
        return [0]
    times: list[int] = []
    for child in holder.children():
        if not child.name.isdigit():
            continue
        value = child.get("attackTime")
        times.append(int(value.value) if value is not None and
                     isinstance(getattr(value, "value", None), (int, float)) else 0)
    return times or [0]


def level_value(rows: dict, key: str, level: int) -> float:
    row = rows.get(str(level)) or {}
    value = row.get(key, 0)
    return value if isinstance(value, (int, float)) else 0


def constant_across_levels(rows: dict, key: str) -> int | None:
    """全部等级同一个值就返回它；逐级变化或缺失返回 None。"""
    seen = set()
    for row in rows.values():
        if not isinstance(row, dict) or key not in row:
            return None
        value = row[key]
        if not isinstance(value, (int, float)):
            return None
        seen.add(int(value))
    return seen.pop() if len(seen) == 1 else None


def build() -> dict:
    sequences: dict[str, dict] = {}
    problems: list[str] = []

    for parent_id, spec in SEQUENCES.items():
        parent = skill_node(parent_id)
        child_id = spec["child"]

        # ① psdSkill 必须真的指名这个子技能 —— 登记表不能凭印象写。
        ids = child_ids(parent)
        if child_id not in ids:
            problems.append(
                f"{parent_id}: psdSkill={ids} 不含登记的 {child_id}")
            continue

        child = skill_node(child_id)
        child_common = child.get("common")
        if child_common is None:
            problems.append(f"{child_id}: 没有 common（不是可结算的攻击体）")
            continue
        child_max, child_fields = level_rows(child_common)
        child_rows = expand(child_fields, child_max)

        parent_common = parent.get("common")
        if parent_common is None:
            problems.append(f"{parent_id}: 没有 common")
            continue
        parent_max, parent_fields = level_rows(parent_common)
        parent_rows = expand(parent_fields, parent_max)

        # ② 自证：子技能数值必须与母技能 h 里被引用的字段逐级相等。
        for child_key, parent_key in spec["self_evidence"]:
            for level in range(1, min(parent_max, child_max) + 1):
                left = level_value(child_rows, child_key, level)
                right = level_value(parent_rows, parent_key, level)
                if round(left, 4) != round(right, 4):
                    problems.append(
                        f"{parent_id}/{child_id}: lv{level} {child_key}={left} != 母 {parent_key}={right}")
                    break

        # ③ 轮数：multiAttackInfo 条数，并与母技能字段交叉自证。
        repeat = multi_attack_count(child)
        repeat_field = spec["repeat_field"]
        for level in range(1, min(parent_max, child_max) + 1):
            declared = level_value(parent_rows, repeat_field, level)
            if int(declared) != repeat:
                problems.append(
                    f"{parent_id}/{child_id}: lv{level} {repeat_field}={declared} != multiAttackInfo {repeat}")
                break

        duration = 0
        if spec["duration_field"]:
            duration = constant_across_levels(parent_rows, spec["duration_field"])
            if duration is None:
                problems.append(f"{parent_id}: {spec['duration_field']} 不是逐级常量")

        charges = 0
        if spec["charges_field"]:
            charges = constant_across_levels(parent_rows, spec["charges_field"])
            if charges is None:
                problems.append(f"{parent_id}: {spec['charges_field']} 不是逐级常量")
                charges = 0

        name, _desc, h = string_entry(child_id)
        if not name:
            problems.append(f"{child_id}: String/Skill.img 没有名字")

        sequences[str(parent_id)] = {
            "parent": parent_id,
            "child": child_id,
            "childName": name,
            "trigger": spec["trigger"],
            "parentAttack": bool(spec["parent_attack"]),
            "durationSec": duration,
            "charges": charges,
            "chargesField": spec["charges_field"] or None,
            "childMp": int(level_value(child_rows, "mpCon", 1)) if spec["child_mp"] else 0,
            "repeat": repeat,
            "repeatField": repeat_field,
            "attackTimes": attack_times(child),
            "childSkill": {
                # magic 继承母技能（登记值），不看子技能的 info/magicDamage：
                # 400031056 是弩手的箭矢，节点上却挂着 magicDamage=1，按它结算
                # 会拿魔法力去打物理箭。
                "kind": "Attack",
                "magic": bool(spec["magic"]),
                "skillType": info_int(child, "skillType"),
                "areaAttack": info_int(child, "areaAttack"),
                "vMatrix": True,
                "element": "",
                "maxLevel": child_max,
            },
            "childLevels": {str(lv): row for lv, row in sorted(
                ((int(k), v) for k, v in child_rows.items()), key=lambda p: p[0])},
            "evidence": spec["evidence"],
            "childH": h,
        }

    wounds: dict[str, dict] = {}
    for parent_id, spec in WOUNDS.items():
        parent_common = skill_node(parent_id).get("common")
        parent_max, parent_fields = level_rows(parent_common)
        rows = expand(parent_fields, parent_max)
        seconds = constant_across_levels(rows, spec["duration_field"])
        if seconds is None:
            problems.append(f"{parent_id}: 伤口 {spec['duration_field']} 不是逐级常量")
            seconds = 0
        wounds[str(parent_id)] = {
            "parent": parent_id,
            "maxStacks": spec["maxStacks"],
            "durationSec": seconds,
            "sources": spec["sources"],
            "evidence": spec["evidence"],
        }

    zones: dict[str, dict] = {}
    for parent_id, spec in ZONES.items():
        parent_common = skill_node(parent_id).get("common")
        parent_max, parent_fields = level_rows(parent_common)
        rows = expand(parent_fields, parent_max)
        # 常量字段（y / z / q / u3）必须逐级一致；u4 是逐级变化的百分比，按行存。
        constants: dict[str, int] = {}
        for key, field in spec["fields"].items():
            if key == "expirePercent":
                continue
            value = constant_across_levels(rows, field)
            if value is None:
                problems.append(f"{parent_id}: 裂缝 {key}({field}) 不是逐级常量")
                value = 0
            constants[key] = value
        levels: dict[str, dict] = {}
        for level in range(1, parent_max + 1):
            row = rows.get(str(level)) or {}
            entry = dict(constants)
            for key, field in spec["damage_fields"].items():
                entry[key] = row.get(field, 0)
            entry["expirePercent"] = row.get(spec["fields"]["expirePercent"], 0)
            levels[str(level)] = entry
        zones[str(parent_id)] = {
            "parent": parent_id,
            "child": spec["child"],
            "levels": levels,
            "evidence": spec["evidence"],
        }

    if problems:
        for line in problems:
            print("  FAIL " + line)
        raise SystemExit(f"自校验失败 {len(problems)} 条，未写入 {OUT}")

    return {
        "source": {
            "packs": str(PACKS),
            "nodes": "Skill/<id//10000>.img/skill/<id> 的 psdSkill 与 common",
            "text": "MapleStory/Data/String/Skill.img（h/desc，繁体转简体）",
        },
        "sequences": sequences,
        "wounds": wounds,
        "zones": zones,
    }


def summarize(data: dict) -> None:
    for key, row in data["sequences"].items():
        levels = row["childLevels"]
        first, last = levels.get("1", {}), levels.get(str(len(levels)), {})
        print(f"  {key} {row['childName']}({row['child']}) trigger={row['trigger']} "
              f"dur={row['durationSec']}s charges={row['charges']} "
              f"repeat={row['repeat']} mp={row['childMp']} "
              f"damage {first.get('damage')}..{last.get('damage')} "
              f"seg={first.get('attackCount')} mob={first.get('mobCount')} "
              f"lv={len(levels)}")
    for key, row in data["wounds"].items():
        print(f"  wound {key} stacks={row['maxStacks']} {row['durationSec']}s "
              f"sources={row['sources']}")
    for key, row in data["zones"].items():
        first = row["levels"]["1"]
        print(f"  zone {key} maxZones={first['maxZones']} life={first['lifeSec']}s "
              f"regen={first['regenSec']}s stages={first['stages']} "
              f"explode={first['damage']}%x{first['attackCount']}seg x{first['mobCount']} "
              f"expire={first['expirePercent']}%")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只打印，不写文件")
    args = parser.parse_args()

    data = build()
    summarize(data)
    if args.check:
        return
    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"-> {OUT.relative_to(ROOT)}  {len(data['sequences'])} 条序列 / {len(data['wounds'])} 条伤口")


if __name__ == "__main__":
    main()
