#!/usr/bin/env python3
"""校验 NativeSkillEffects.json 与消费方是否自洽 → 只读，不改数据。

守的就是上一轮那个坑：把字段映射到一张没人读的表上，等于没做。
本脚本会去 C# 源码里把「谁读哪些键」解析出来，再逐条比对：

  1. fields 映射出的每个 setItemBonus 键，必须真的有人读
     （EquipLoadout.ApplyStat / AdminSetItem.ApplyDamage / PlayerCtrl 的暴击特例）
  2. passive 里的技能 id 必须存在于技能表，否则永远不会触发
  3. 逐级数值数组的长度必须等于 maxLevel，且不能全是 0
  4. strX/padX 这类「覆盖范围由 h 上下文决定」的字段不许出现在时效增益侧
     —— 那说明括号划分漏了，展开规则没生效
  5. 统计 V/HEXA 里 type=buff 但拿不到任何 stat 的条数（仍会「按下没反应」）

用法
    python3 tools/ms282/check_skill_effects.py
    python3 tools/ms282/check_skill_effects.py --table    # 附带每条技能的映射结果
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeSkillEffects.json"
SKILL_TABLES = ("VMatrixSkills", "AdvancedSkills", "JobTree")
WIDE_FIELDS = {"strX", "padX"}       # 覆盖范围由 h 上下文决定，只该出现在被动侧

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_advanced_skills import PROC_SKILLS  # noqa: E402


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def consumed_keys() -> tuple[set[str], set[str]]:
    """返回 (setItemBonus 键, 由 C# 单独消费的键)。"""
    set_keys: set[str] = set()
    other: set[str] = set()

    # EquipLoadout.Apply：ApplyStat(set, "KEY", …) —— flat 与 KEY+Pct 两种都要认
    for match in re.finditer(r'ApplyStat\(set,\s*"([A-Za-z]+)"', read(SCRIPTS / "EquipLoadout.cs")):
        set_keys.add(match.group(1))
        set_keys.add(match.group(1) + "Pct")

    # AdminSetItem.ApplyDamage / ApplyRate
    admin = read(SCRIPTS / "Admin" / "AdminSetItem.cs")
    for match in re.finditer(r'bonus\.TryGetValue\("([A-Za-z]+)"', admin):
        set_keys.add(match.group(1))
    # ApplyDamage 固定读这四个（含 boss 三元那一路）
    set_keys |= {"Damage", "BossDamage", "NormalDamage", "FinalDamage"}
    # ApplyRate(set, "KEY", …) 的键是变量，正则抓不到字面量，按已知倍率键补上
    if "ApplyRate(" in admin:
        set_keys |= {"ExpRate", "DropRate", "MesoRate"}

    # CombatRules 直接读 setItemBonus 的 IgnoreDefense
    if 'TryGetValue("IgnoreDefense"' in read(SCRIPTS / "CombatRules.cs"):
        set_keys.add("IgnoreDefense")

    # PlayerCtrl 对暴击/速度/跳跃走独立通道，不进 ApplyStat
    other |= {"CriticalChance", "Speed", "Jump"}
    return set_keys, other


def load_skills() -> dict[str, dict]:
    merged: dict[str, dict] = {}
    for name in SKILL_TABLES:
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        if path.is_file():
            merged.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return merged


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--table", action="store_true", help="打印逐条映射结果")
    args = parser.parse_args()

    if not TABLE.is_file():
        print("FAIL 缺少 NativeSkillEffects.json —— 先跑 export_skill_effects.py")
        return 1
    data = json.loads(TABLE.read_text(encoding="utf-8"))
    fields: dict[str, list[str]] = data["fields"]
    passives: dict[str, dict[str, list[int]]] = data["passive"]
    passive_fields: dict[str, list[str]] = data.get("passiveFields", {})
    aliases: dict[str, dict[str, list[str]]] = data.get("alias", {})
    skills = load_skills()
    set_keys, other_keys = consumed_keys()

    problems: list[str] = []

    # ① 每个映射键都要有人读
    mapped: set[str] = set()
    for keys in fields.values():
        mapped |= set(keys)
    for key in sorted(mapped):
        if key not in set_keys and key not in other_keys:
            problems.append(f"字段映射到没人读的键 {key}（加进 EquipLoadout.ApplyStat 或 ApplyDamage）")

    # ①b 逐条登记（alias）同样要有消费方，且技能得真实存在
    for sid, binding in aliases.items():
        if sid not in skills:
            problems.append(f"alias 里的 {sid} 不在任何技能表里")
        for field, keys in binding.items():
            for key in keys:
                if key not in set_keys and key not in other_keys:
                    problems.append(f"alias {sid} 的 {field} 映射到没人读的键 {key}")

    # ②③ 被动 id 与逐级数组必须真实可用
    for sid, binding in passives.items():
        skill = skills.get(sid)
        if skill is None:
            problems.append(f"{sid} 不在任何技能表里（SkillBook 读不到 ⇒ 永不生效）")
            continue
        max_level = int(skill.get("maxLevel") or 0)
        for key, values in binding.items():
            if not isinstance(values, list) or not values:
                problems.append(f"{sid} {skill.get('name')} 的 {key} 没有逐级数值")
                continue
            if max_level and len(values) != max_level:
                problems.append(f"{sid} {skill.get('name')} 的 {key} 长度 {len(values)} ≠ maxLevel {max_level}")
            if not any(values):
                problems.append(f"{sid} {skill.get('name')} 的 {key} 每一级都是 0")

    # ④ WIDE 字段不许被当成时效增益
    for sid, skill in skills.items():
        if skill.get("type") != "buff":
            continue
        row = skill["levels"].get(str(skill.get("maxLevel", 1)))
        if not isinstance(row, dict):
            continue
        declared = set(passive_fields.get(sid, []))
        for field in WIDE_FIELDS:
            if field in row and field not in declared:
                problems.append(f"{sid} {skill.get('name')} 把 {field} 当成了时效增益（覆盖范围会错，需人工确认）")

    # ⑤ 仍拿不到 stat 的 buff。
    # 既有通道（flat 属性 / statups / 召唤 / 椅子 / 疾风脚）本来就能注册，不算无效果。
    flat = {"pad", "pdd", "mad", "mdd", "acc", "eva", "speed", "jump", "expBuff", "barrier", "morph"}
    contract_path = ROOT / "Assets/Resources/NativeBuffContract.json"
    contract = json.loads(contract_path.read_text(encoding="utf-8")) if contract_path.is_file() else {}
    legacy = set(map(str, contract.get("statups", {}).keys()))
    legacy |= {str(v) for v in contract.get("summons", [])}
    legacy |= {str(v) for v in contract.get("mapChairs", [])}
    legacy |= {str(v) for v in contract.get("nimbleFeet", [])}

    hollow: list[str] = []
    # 已由专用通道实现的技能不算「无 stat」。这张名单每一项都是「不是 stat 系统管的东西」：
    #   · 护盾表 NativeShieldContract —— 护盾是会被消耗的池子，走 NativeShields；
    #   · NativeProcSkills 里真实登记的 ProcSpec —— 伤害在 proc 链结算，本来就不该有 stat
    #     （注意不能用 PROC_SKILLS 整表：那里还有 2141007/5141011 这类只做了判型、
    #      没接链路的 buff，它们仍然「按下没反应」，必须继续暴露出来）；
    #   · 3141009 追加魔幻箭筒 —— NativeQuiverBurst 自管持续时间（common 只有 time/cooltime）；
    #   · 400001009 艾尔达斯的意志 —— Hero's Will 通道：异常状态下可施放 + Purge 解除，
    #     h 只有「消耗#hpCon HP，冷却时间#cooltime秒」，和 1121011 等十二条同族一样不写效果。
    special: set[str] = set()
    proc_source = SCRIPTS / "NativeProcSkills.cs"
    if proc_source.is_file():
        special |= set(re.findall(r"new ProcSpec\(\s*(\d+)", proc_source.read_text(encoding="utf-8")))
    shield_path = ROOT / "Assets/Resources/NativeShieldContract.json"
    if shield_path.is_file():
        special |= set(json.loads(shield_path.read_text(encoding="utf-8")).get("shields", {}))
    # 282 技能子状态（invisible buff id，母技能施放时挂上）：不是玩家可按的技能，
    # 本来就不产出 stat。12 条的形状与证据见 export_skill_states.py。
    states_path = ROOT / "Assets/Resources/NativeSkillStates.json"
    if states_path.is_file():
        special |= set(json.loads(states_path.read_text(encoding="utf-8")).get("states", {}))
    # 强化核心（500004073 強化暗影侍從/暗影瞬移）：自己不产出 stat，而是把别的技能的
    # x / time 字段替换掉。表与逐级自证见 export_skill_overrides.py / check_skill_overrides.py。
    override_path = ROOT / "Assets/Resources/NativeSkillOverrides.json"
    if override_path.is_file():
        special |= set(json.loads(override_path.read_text(encoding="utf-8")).get("overrides", {}))
    # 资源/追加方向/符咒/技能替换/元素吸收/隐身回避：这 6 条分别有自己的实体结算，
    # 表与 h 证据见 export_skill_mechanics.py / check_skill_mechanics.py。
    # 5141011 过去在 PROC_SKILLS 里只是「改判型」而没接链路，现在由 resourceBurst 真正实现。
    mechanics_path = ROOT / "Assets/Resources/NativeSkillMechanics.json"
    if mechanics_path.is_file():
        special |= set(json.loads(mechanics_path.read_text(encoding="utf-8")).get("mechanics", {}))
    special |= {"3141009", "400001009"}
    # 秘术超越 400001021 走 NativeBuffs.SkillStats 里的内置分支（x 阶段 × z% 逐段衰减），
    # 不是字段映射表能表达的（两个字段要相乘，不是一对一映射）。
    buffs_source = (SCRIPTS / "NativeBuffs.cs").read_text(encoding="utf-8")
    if "id == 400001021" in buffs_source:
        special.add("400001021")
    # 爆击强化 400031023：只放一个注册标记 CRIT_REINFORCE，真正的换算在
    # PlayerCtrl.RefreshAttributes 里读 x 完成（「暴击率超出部分 × x%」）。
    if "CRIT_REINFORCE" in buffs_source:
        special.add("400031023")
    # 只统计五转/六转（VMatrix + Advanced）：JobTree 的四转 buff 大量走 statups /
    # 召唤名单，本就不该由这套表接；混进来会让这个数字失去意义。
    vhexa: set[str] = set()
    for name in ("VMatrixSkills", "AdvancedSkills"):
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        if path.is_file():
            vhexa |= set(json.loads(path.read_text(encoding="utf-8"))["skills"].keys())
    for sid, skill in skills.items():
        if sid not in vhexa or skill.get("type") != "buff":
            continue
        if sid in special:
            continue
        row = skill["levels"].get(str(skill.get("maxLevel", 1)))
        if not isinstance(row, dict):
            continue
        if sid in legacy or any(f in row for f in flat):
            continue
        if sid in passive_fields:
            continue                      # 有 [被动效果] 的走 AddPassive，不算无效果
        declared = set(passive_fields.get(sid, []))
        got = [f for f in fields if f in row and f not in declared and row.get(f) not in (0, None)]
        # 逐条登记的单字母字段（alias）也算「有 stat」
        got += [f for f, _ in aliases.get(sid, {}).items() if row.get(f) not in (0, None)]
        if not got:
            hollow.append(f"{sid} {skill.get('name')}")

    print("== 技能效果映射自洽 ==")
    print(f"  被动技能 {len(passives)} 条，覆盖字段 {len(fields)} 个，映射键 {len(mapped)} 个")
    print(f"  消费方认得 {len(set_keys)} 个 set 键 + {len(other_keys)} 个独立通道")
    print(f"  buff 仍无 stat: {len(hollow)} 条")
    if args.table:
        for sid, binding in sorted(passives.items()):
            name = skills.get(sid, {}).get("name", "?")
            cells = ", ".join(f"{k}={v[0]}..{v[-1]}" for k, v in binding.items())
            print(f"    {sid} {name}: {cells}")
        for item in hollow:
            print(f"    [无 stat] {item}")
    for item in problems:
        print("  FAIL", item)
    print(f"不一致 {len(problems)} 条")
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
