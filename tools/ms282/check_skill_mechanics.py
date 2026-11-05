#!/usr/bin/env python3
"""七条「机制实体」表 ↔ 运行时 的一致性守卫。

    ① 表结构   7 条齐全，kind/name/evidence 都有
    ② 数值回算 伤害体与母技能字段逐级相等、公式与 h 字段对得上
    ③ 有消费   C# 真读的键都在表里、表里的运行时键都在 C# 里出现
    ④ 反向孤儿 C# 里的 kind / MechanicId 都在表里；伤害体都真实存在
    ⑤ 接线     施放门 / 替换 / 施放成功 / 命中 / 受击 / 清图 / 增益注册 / 等级加成

用法
    python3 tools/ms282/check_skill_mechanics.py
    python3 tools/ms282/check_skill_mechanics.py --table
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeSkillMechanics.json"
STATES = ROOT / "Assets/Resources/NativeSkillStates.json"
RUNTIME = SCRIPTS / "NativeSkillMechanics.cs"

KINDS = {"resourceBurst", "spread", "charm", "replace", "elementAbsorb", "darkSight", "levelBoost"}
EXPECTED = {"5141011", "400041001", "400041061", "5141001", "2141502", "4241005", "400001004"}
# C# 运行时真正读取的键（Str/Int/Ints/InList/MechField/chosen[...]）。
RUNTIME_KEYS = {
    "kind", "capField", "costField", "durationField", "maxBurstsField", "bonusField",
    "gainSkills", "bodies", "bonusTargets", "throwSkills", "triggerSkills",
    "variants", "triggers", "directions", "field", "mobField",
    "minPerCastField", "maxPerCastField", "passiveIntervalField",
    "damageField", "hitsField", "mobsField", "body",
    "source", "state", "parent", "maxEvadesField", "cooldownPerEvadeSec", "maxStacks",
}
# 这三个键在 NativeSkillStates.json 的 timedEffect 条目里（元素吸收），由
# NativeSkillMechanics.AddTimedEffect 读，不在 mechanics 表内。
STATE_KEYS = {"effectKey", "effectPct", "elementAbsorbCap"}
# 只给导出期自证 / 人读的键，不要求 C# 消费。
EXPORT_ONLY = {
    "name", "evidence", "hLine", "id", "bulletCount", "directionTotal", "perStackPct",
    "baseCap", "statKey", "stateField", "shockFields", "bodyFields", "secondBodyFields",
    "passiveSecondsField", "cooldownField", "bodyFromParentField", "resistanceField",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def skills() -> dict[str, dict]:
    merged: dict[str, dict] = {}
    for name in ("VMatrixSkills", "AdvancedSkills", "JobTree"):
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        if path.is_file():
            merged.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return merged


def series(row: dict, field: str) -> list[int]:
    levels = row.get("levels") or {}
    return [int((levels.get(str(lv)) or {}).get(field, 0))
            for lv in range(1, int(row.get("maxLevel", 30)) + 1)]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--table", action="store_true", help="打印机制预览")
    args = ap.parse_args()

    problems: list[str] = []
    if not TABLE.is_file():
        print(f"缺少 {TABLE.relative_to(ROOT)}；先跑 export_skill_mechanics.py")
        return 1
    table = json.loads(TABLE.read_text(encoding="utf-8"))
    mech = table.get("mechanics") or {}
    all_skills = skills()
    source = read(RUNTIME)

    # ① 表结构
    if set(mech) != EXPECTED:
        problems.append(f"机制条目不是预期的 {len(EXPECTED)} 条：{sorted(mech)}")
    found_kinds = set()
    for sid, entry in mech.items():
        label = f"{sid} {entry.get('name')}"
        for key in ("kind", "name", "evidence"):
            if not entry.get(key):
                problems.append(f"{label} 缺 {key}")
        kind = entry.get("kind")
        found_kinds.add(kind)
        if kind not in KINDS:
            problems.append(f"{label} 的 kind={kind!r} 不认识")
        if not entry.get("evidence"):
            problems.append(f"{label} 没有 h 原文证据")
    if found_kinds != KINDS:
        problems.append(f"kind 集合不齐：缺 {sorted(KINDS - found_kinds)}")

    # ② 数值回算（与导出脚本独立实现，防止两边一起写错）
    dragon = all_skills.get("5141011", {})
    for body, fields in (("5141013", mech.get("5141011", {}).get("bodyFields", {})),
                         ("5141014", mech.get("5141011", {}).get("secondBodyFields", {}))):
        brow = all_skills.get(body)
        if not brow:
            problems.append(f"{body} 伤害体不在技能表")
            continue
        for body_field, parent_field in fields.items():
            if series(brow, body_field) != series(dragon, parent_field):
                problems.append(f"{body}.{body_field} 与 5141011.{parent_field} 不是逐级相等")
    if not mech.get("5141011", {}).get("bodies") or not mech.get("5141011", {}).get("bonusTargets"):
        problems.append("海龙石VI 缺伤害体或被动加伤目标")

    charm = all_skills.get("400041061", {})
    z = series(charm, "z")
    if any(v != 475 + 19 * lv for lv, v in enumerate(z, start=1)):
        problems.append("400041061.z 与爆炸体 common.damage=475+19*x 不符")
    row1 = (charm.get("levels") or {}).get("1", {})
    if int(row1.get("subTime", 0)) != int(row1.get("u", 0)) * 1000:
        problems.append("飞闪起爆符的 subTime 与 u 秒不相等")
    if int(row1.get("s", 0)) >= int(row1.get("w", 0)) or int(row1.get("x", 0)) <= 0:
        problems.append("飞闪起爆符的符咒上下限/容量不对")

    spread = all_skills.get("400041001", {})
    s1 = (spread.get("levels") or {}).get("1", {})
    if int(s1.get("u", 0)) != 290 or int(s1.get("damage", 0)) != 379:
        problems.append("散式投掷的追加倍率与 400041017/18.common 不符")
    total = mech.get("400041001", {}).get("directionTotal", 0)
    for var in mech.get("400041001", {}).get("variants", []):
        if int(var.get("directions", 0)) + int(var.get("bulletCount", 0)) != total:
            problems.append(f"散式投掷变体 {var.get('id')} 的方向数不互补")
        if not var.get("triggers"):
            problems.append(f"散式投掷变体 {var.get('id')} 没有触发名单")

    src = all_skills.get("5141000", {})
    shock = all_skills.get("5141003", {})
    for body_field, src_field in mech.get("5141001", {}).get("shockFields", {}).items():
        if series(shock, body_field) != series(src, src_field):
            problems.append(f"5141003.{body_field} 与 5141000.{src_field} 不是逐级相等")
    if int(((all_skills.get("5141001", {}).get("levels") or {}).get("1") or {}).get("time", 0)) != \
       int(((src.get("levels") or {}).get("1") or {}).get("x", 0)):
        problems.append("超级闪‧连杀状态时限与 5141000.x 不符")

    states = json.loads(STATES.read_text(encoding="utf-8")).get("states", {}) if STATES.is_file() else {}
    poison_state = states.get("2141502", {})
    if poison_state.get("effectKey") != "FinalDamage" or int(poison_state.get("effectPct") or 0) != 30:
        problems.append("剧毒炼狱的 timedEffect 没有给出满层 FinalDamage +30")
    if int(poison_state.get("elementAbsorbCap") or 0) != \
       int((((all_skills.get("2141500", {}).get("levels") or {}).get("1")) or {}).get("x", 0)):
        problems.append("元素吸收层数与母技能 2141500.x 不符")
    if int((((all_skills.get("4241005", {}).get("levels") or {}).get("1")) or {}).get("w2", 0)) != 20:
        problems.append("黑暗面的回避上限与 4001003.common.w2 不符")

    # 实用的战斗命令：x 恒为 1、time=180+3x、asrR=u(x/5)（与导出脚本各自独立回算）
    order = all_skills.get("400001004", {})
    order_levels = order.get("levels") or {}
    if not order_levels:
        problems.append("400001004 不在技能表")
    else:
        total = int(order.get("maxLevel", 0))
        xs = series(order, "x")
        times = series(order, "time")
        asrs = series(order, "asrR")
        if len(xs) != total or any(v != 1 for v in xs):
            problems.append("400001004.x 不是恒为 1（h「所有技能等级+1」）")
        if any(v != 180 + 3 * lv for lv, v in enumerate(times, start=1)):
            problems.append("400001004.time 与 common「180+3*x」不符")
        if any(v != -(-lv // 5) for lv, v in enumerate(asrs, start=1)):
            problems.append("400001004.asrR 与 common「u(x/5)」不符")
        # 被动 asrR 必须真的进了 NativeSkillEffects 的被动表，否则「抗性」又是一句空话
        effects_path = ROOT / "Assets/Resources/NativeSkillEffects.json"
        effects = json.loads(effects_path.read_text(encoding="utf-8")) if effects_path.is_file() else {}
        if (effects.get("passive") or {}).get("400001004", {}).get("ASR") != asrs:
            problems.append("400001004 的 asrR 没有被导出成 NativeSkillEffects 的 ASR 被动")

    # ③ 有消费：运行时键必须在 C# 里出现，且 C# 里引用的键必须在表里
    declared = {key for entry in mech.values() for key in entry} - EXPORT_ONLY
    for var in mech.get("400041001", {}).get("variants", []):
        declared |= set(var) - EXPORT_ONLY
    for key in sorted(RUNTIME_KEYS | STATE_KEYS):
        if f'"{key}"' not in source:
            problems.append(f'C# 没有读运行时键 "{key}"')
    for key in sorted(declared - RUNTIME_KEYS - STATE_KEYS):
        problems.append(f'表里声明了 "{key}"，既不是导出期键也没有被 C# 读')
    for key in sorted(RUNTIME_KEYS - declared):
        problems.append(f'C# 读了 "{key}"，但表里没有任何条目声明它')
    for key in sorted(STATE_KEYS):
        if key not in states.get("2141502", {}):
            problems.append(f'状态表 2141502 缺 "{key}"（元素吸收满层值）')
    for kind in KINDS:
        if kind == "elementAbsorb":
            # 它的运行时在 NativeBuffs 注册增益，不进 OnMechanicHit 的 kind 分支。
            if "public static void AddTimedEffect" not in source:
                problems.append("元素吸收没有 AddTimedEffect 运行时入口")
        elif kind == "levelBoost":
            # 它的运行时是「等级读取」而不是「命中结算」：标记在 NativeBuffs.SkillStats，
            # 换算在 NativeCombatOrders（SkillProgress.LevelOf 调用）。
            if 'Str(id, "kind") == "levelBoost"' not in read(SCRIPTS / "NativeBuffs.cs"):
                problems.append("NativeBuffs 没有为 levelBoost 放 SKILL_ORDER 注册标记")
        elif f'case "{kind}"' not in source and f'MechanicId("{kind}")' not in source:
            problems.append(f'C# 没有处理 kind={kind}')
    # C# 里 MechField / Str 引用的键必须都在表里
    for field_key in set(re.findall(r'MechField\([^,]+,\s*"(\w+)"\)', source)):
        if field_key not in declared:
            problems.append(f'C# 的 MechField 读了表里不存在的 "{field_key}"')

    # ④ 反向孤儿
    for kind in set(re.findall(r'MechanicId\("(\w+)"\)', source)) | set(re.findall(r'case "(\w+)"', source)):
        if kind not in KINDS:
            problems.append(f'C# 里出现表里没有的 kind={kind}')
    for sid, entry in mech.items():
        for body in entry.get("bodies", []):
            if str(body) not in all_skills:
                problems.append(f"{sid} 的伤害体 {body} 不在技能表")
        body = entry.get("body")
        if body and str(body) not in all_skills and not entry.get("bodyFromParentField"):
            problems.append(f"{sid} 的 {body} 不在技能表，也没有登记母技能回退字段")
        required = {
            "resourceBurst": ("gainSkills", "bodies", "bonusTargets"),
            "spread": ("throwSkills", "variants"),
            "charm": ("throwSkills", "body", "damageField"),
            "replace": ("triggerSkills", "bodies"),
        }.get(entry.get("kind"), ())
        for key in required:
            if not entry.get(key):
                problems.append(f"{sid} 缺 {key} 名单/字段")

    # ⑤ 接线
    wiring = [
        (SCRIPTS / "SkillSystem.cs", "s = ResolveMechanicReplacement(s)", "施放前技能替换"),
        (SCRIPTS / "SkillSystem.cs", "MechanicCastGate(s.native.Id", "资源/冷却施放门"),
        (SCRIPTS / "SkillSystem.cs", "OnMechanicCast(s, p.FeetPos", "施放成功后开窗口"),
        (SCRIPTS / "SkillSystem.cs", "ClearMechanics();", "换图/死亡清状态"),
        (SCRIPTS / "NativeSkillFollowups.cs", "OnMechanicHit(cast,captured,origin,facing)", "命中结算"),
        (SCRIPTS / "PlayerCtrl.cs", "TryEvadeHpRatioDamage()", "隐身回避"),
        (SCRIPTS / "NativeBuffs.cs", "NativeSkillMechanics.AddTimedEffect(result, id)", "元素吸收增益注册"),
        (SCRIPTS / "NativeSkillStates.cs", "IsDeathGuard", "（状态组件仍完整）"),
        (SCRIPTS / "SkillBook.cs", "NativeCombatOrders.Bonus(id)", "等级加成读取"),
        (SCRIPTS / "NativeCombatOrders.cs", "public static bool Applies(int id)", "等级加成的可用范围"),
        (SCRIPTS / "Field/NativeMobDebuffs.cs", 'TryGetValue("ASR"', "状态异常抗性"),
    ]
    for path, token, label in wiring:
        if token not in read(path):
            problems.append(f"{path.name} 没有接 {label}（{token}）")
    if "AddTimedEffect" not in read(SCRIPTS / "NativeBuffs.cs"):
        problems.append("NativeBuffs 没有调用 AddTimedEffect（元素吸收增益不会注册）")

    if args.table:
        print(f"\n{'id':>10} {'名称':<14} {'机制':<16} 证据")
        for sid, entry in mech.items():
            print(f"{sid:>10} {entry['name']:<14} {entry['kind']:<16} {(entry.get('evidence') or '')[:48]}")

    for item in problems:
        print("  FAIL", item)
    print(f"机制 {len(mech)} 条，不一致 {len(problems)} 条")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
