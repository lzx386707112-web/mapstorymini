#!/usr/bin/env python3
"""形态技能序列表 ↔ 282 数据 ↔ 运行时 的一致性守卫。

    NativeSkillSequences.json 回答三件事：
        ① 哪条技能是「母技能开形态、子技能出伤害」（psdSkill + isSequenceOn）；
        ② 形态怎么被消费（再按键 / 普通攻击 / 伤口连携），以及次数、时长、轮数；
        ③ 伤口与裂缝这两种跟着母技能的第二段机制的参数。

    六项检查
        ① 表结构    parent/child/trigger/parentAttack/durationSec/charges/childMp/repeat/
                    attackTimes/childSkill/childLevels 齐全，levels 连续到 maxLevel，
                    且每级都有 damage>0、attackCount>=1、mobCount>=1
        ② 有出处    重跑导出脚本的构建逻辑，必须与本表逐字节等价（数据不是手抄的）
        ③ 有消费    表里的运行时键都被 NativeSkillSequences.cs 读到；
                    反向：C# 读的键都在表里（防拼写不对导致静默为 0）
        ④ 接线      四个入口都被调用：再按键分流 / 普攻替换 / 命中挂伤口 / 换图清理；
                    parentAttack 必须真的被 Cast 的 buff 短路读到
        ⑤ 口径      子技能不在任何技能表（不能出现在技能窗、不能加 SP）；
                    触发方式与 h 原文一致；magic 继承母技能而不是子节点的 magicDamage
        ⑥ 资源      子技能的 IMG 节点在 NativeAssetIndex 里（否则特效/动作全空）

用法
    python3 tools/ms282/check_skill_sequences.py
    python3 tools/ms282/check_skill_sequences.py --table
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SCRIPTS = ROOT / "Assets/Scripts"
TABLE = ROOT / "Assets/Resources/NativeSkillSequences.json"
UI = ROOT / "Assets/Resources/UI"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

# 运行时真的会读的键；其余是给人回查 h 原文的元数据。
RUNTIME_KEYS = {
    "parent", "child", "childName", "trigger", "parentAttack", "durationSec",
    "charges", "childMp", "repeat", "attackTimes", "childSkill", "childLevels",
    "levels", "maxZones", "lifeSec", "regenSec", "stages", "damage",
    "attackCount", "mobCount", "expirePercent", "maxStacks", "sources", "parent",
}
META_KEYS = {"source", "evidence", "childH", "repeatField", "chargesField", "packs", "nodes", "text"}

# string_entry 返回的是 simplify 之后的简体，关键字也写简体。
TRIGGER_EVIDENCE = {
    "recast": ("再次使用技能",),
    "basicAttack": ("一般攻击", "普通攻击"),
    "chain": ("连接使用",),
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8") if path.is_file() else ""


def fail(problems: list[str], message: str) -> None:
    problems.append(message)


def table_skills() -> dict[str, dict]:
    rows: dict[str, dict] = {}
    for name in ("JobTree", "AdvancedSkills", "VMatrixSkills"):
        path = UI / f"{name}.json"
        if not path.is_file():
            continue
        rows.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    return rows


def check_structure(data: dict, problems: list[str]) -> None:
    sequences = data.get("sequences") or {}
    if not sequences:
        fail(problems, "sequences 为空")
    for key, row in sequences.items():
        where = f"sequences.{key}"
        for field in ("parent", "child", "trigger", "parentAttack", "durationSec",
                      "charges", "childMp", "repeat", "childSkill", "childLevels"):
            if field not in row:
                fail(problems, f"{where}: 缺 {field}")
        if str(row.get("parent")) != key:
            fail(problems, f"{where}: parent={row.get('parent')} 与键不一致")
        meta = row.get("childSkill") or {}
        max_level = int(meta.get("maxLevel") or 0)
        levels = row.get("childLevels") or {}
        if max_level <= 0:
            fail(problems, f"{where}: childSkill.maxLevel 缺失")
        if len(levels) != max_level:
            fail(problems, f"{where}: levels {len(levels)} 条 != maxLevel {max_level}")
        for level in range(1, max_level + 1):
            entry = levels.get(str(level))
            if not isinstance(entry, dict):
                fail(problems, f"{where}: 缺 level {level}")
                continue
            if float(entry.get("damage") or 0) <= 0:
                fail(problems, f"{where}: lv{level} damage<=0")
            if int(entry.get("attackCount") or 0) < 1:
                fail(problems, f"{where}: lv{level} attackCount<1")
            if int(entry.get("mobCount") or 0) < 1:
                fail(problems, f"{where}: lv{level} mobCount<1")
        if int(row.get("repeat") or 0) < 1:
            fail(problems, f"{where}: repeat<1")
        times = row.get("attackTimes") or []
        if len(times) != int(row.get("repeat") or 0):
            fail(problems, f"{where}: attackTimes {len(times)} != repeat {row.get('repeat')}")
        if row.get("trigger") not in TRIGGER_EVIDENCE:
            fail(problems, f"{where}: 未知 trigger {row.get('trigger')}")
        # 只有 recast / basicAttack 需要形态时长与次数；chain 靠伤口门。
        if row.get("trigger") in ("recast", "basicAttack"):
            if int(row.get("durationSec") or 0) <= 0:
                fail(problems, f"{where}: {row.get('trigger')} 型缺 durationSec")
            if int(row.get("charges") or 0) <= 0:
                fail(problems, f"{where}: {row.get('trigger')} 型缺 charges")

    for key, row in (data.get("wounds") or {}).items():
        where = f"wounds.{key}"
        if int(row.get("maxStacks") or 0) < 1:
            fail(problems, f"{where}: maxStacks<1")
        if int(row.get("durationSec") or 0) <= 0:
            fail(problems, f"{where}: durationSec<=0")
        if not row.get("sources"):
            fail(problems, f"{where}: sources 为空")

    for key, row in (data.get("zones") or {}).items():
        where = f"zones.{key}"
        levels = row.get("levels") or {}
        if not levels:
            fail(problems, f"{where}: levels 为空")
        for level, entry in levels.items():
            for field in ("maxZones", "lifeSec", "regenSec", "stages", "damage",
                          "attackCount", "mobCount", "expirePercent"):
                if field not in entry:
                    fail(problems, f"{where}.{level}: 缺 {field}")
            if float(entry.get("damage") or 0) <= 0:
                fail(problems, f"{where}.{level}: damage<=0")


def check_origin(data: dict, problems: list[str]) -> None:
    """重跑导出脚本的构建，结果必须与本表一致 —— 防止有人手改 JSON。"""
    try:
        import export_skill_sequences as build
    except Exception as error:            # noqa: BLE001
        fail(problems, f"无法导入导出脚本：{error}")
        return
    try:
        fresh = build.build()
    except SystemExit as error:
        fail(problems, f"导出脚本自校验失败：{error}")
        return
    for section in ("sequences", "wounds", "zones"):
        old, new = data.get(section) or {}, fresh.get(section) or {}
        if set(old) != set(new):
            fail(problems, f"{section}: 键集不一致 {sorted(set(old) ^ set(new))}")
        for key in sorted(set(old) & set(new)):
            if json.dumps(old[key], sort_keys=True, ensure_ascii=False) != \
                    json.dumps(new[key], sort_keys=True, ensure_ascii=False):
                fail(problems, f"{section}.{key}: 与 282 数据重新导出的结果不一致")


def check_consumption(data: dict, problems: list[str]) -> None:
    source = read(SCRIPTS / "NativeSkillSequences.cs")
    if not source:
        fail(problems, "找不到 NativeSkillSequences.cs")
        return
    # 只从 C# 源码里提取「按字符串读的键」，不能把表里的字段混进来，
    # 否则拼错的键也会被当成「存在」而漏报。
    used = set()
    for line in source.splitlines():
        if "SeqInt(" in line or "SeqInts(" in line:
            for part in line.split('"')[1::2]:
                if part and " " not in part and "/" not in part:
                    used.add(part)
    # 反向：C# 里按字符串读的每个键都必须在一处出现过，否则拼错也不会报错。
    known = set()
    for section in ("sequences", "wounds", "zones"):
        for row in (data.get(section) or {}).values():
            if not isinstance(row, dict):
                continue
            known.update(row)
            for value in row.values():
                if isinstance(value, dict):
                    known.update(value)
    for key in sorted(used):
        if key in known or key in META_KEYS:
            continue
        fail(problems, f"NativeSkillSequences.cs 读了表里不存在的键「{key}」")
    # 运行时必须真的读到的键（漏读 = 该功能静默失效）。
    for required in ("trigger", "durationSec", "charges", "childMp", "repeat", "attackTimes",
                     "parentAttack", "childLevels", "childSkill", "maxStacks", "sources",
                     "durationSec", "maxZones", "lifeSec", "regenSec", "stages", "expirePercent"):
        if required not in source:
            fail(problems, f"NativeSkillSequences.cs 没有读「{required}」")


def check_wiring(data: dict, problems: list[str]) -> None:
    wiring = {
        "Assets/Scripts/SkillSystem.cs": [
            ("TrySequenceRecast", "再按键分流没接（400031055 的极速射击永远打不出去）"),
            ("SequenceParentAttacks", "母技能伤害段的表开关没接（黑影切断的主动效果不会结算）"),
            ("FormSequenceOf", "形态没有开启入口"),
            ("RunSequenceChain", "连携段没有入口"),
            ("ClearSequences", "换图/死亡没有清形态与伤口"),
        ],
        "Assets/Scripts/PlayerCtrl.cs": [
            ("TrySequenceBasicAttack", "普通攻击替换没接（极乐之境形态期间打不出黄泉十字架）"),
        ],
        "Assets/Scripts/NativeSkillFollowups.cs": [
            ("NotifySequenceHit", "伤口没有挂接点（黑影切断永远连携不了）"),
        ],
    }
    for relative, entries in wiring.items():
        text = read(ROOT / relative)
        if not text:
            fail(problems, f"找不到 {relative}")
            continue
        for symbol, message in entries:
            if symbol not in text:
                fail(problems, f"{relative}: {message}（缺 {symbol}）")
    # 形态技能必须在冷却检查之前分流，否则 60 秒 buff / 120 秒冷却会把第二发挡死。
    system = read(SCRIPTS / "SkillSystem.cs")
    recast_at = system.find("TrySequenceRecast(")
    cooldown_at = system.find('State(s.native.Id).Remaining(Time.realtimeSinceStartupAsDouble) > 0')
    if recast_at < 0 or cooldown_at < 0 or recast_at > cooldown_at:
        fail(problems, "SkillSystem.BeginCast: 形态分流必须在冷却检查之前")


def check_semantics(data: dict, problems: list[str]) -> None:
    skills = table_skills()
    index = json.loads(INDEX.read_text(encoding="utf-8")) if INDEX.is_file() else {}
    index_text = json.dumps(index, ensure_ascii=False)
    try:
        import export_skill_sequences as build
        string_entry = build.string_entry
        skill_node = build.skill_node
    except Exception as error:            # noqa: BLE001
        fail(problems, f"无法导入导出脚本：{error}")
        return
    for key, row in (data.get("sequences") or {}).items():
        child = int(row["child"])
        parent = int(row["parent"])
        where = f"sequences.{key}"
        # 子技能是 invisible 攻击体：不能进技能表，否则会出现在技能窗里被加点。
        if str(child) in skills:
            fail(problems, f"{where}: 子技能 {child} 出现在技能表（会显示在技能窗）")
        node = skill_node(child)
        invisible = node.get("invisible")
        if invisible is None or int(invisible.value) != 1:
            fail(problems, f"{where}: 子技能 {child} 的 invisible != 1")
        parent_node = skill_node(parent)
        psd = [c.name for c in (parent_node.get("psdSkill").children()
                                if parent_node.get("psdSkill") is not None else [])]
        if str(child) not in psd:
            fail(problems, f"{where}: 母技能 psdSkill={psd} 不含子技能 {child}")
        # 触发方式必须能在 h/desc 原文里找到对应说法。
        name, desc, h = string_entry(parent)
        text = h + " " + desc
        keywords = TRIGGER_EVIDENCE[row["trigger"]]
        if not any(word in text for word in keywords):
            fail(problems, f"{where}: trigger={row['trigger']} 在 h/desc 里找不到依据"
                           f"（期望其一：{keywords}）")
        # magic 必须继承母技能：子节点上的 info/magicDamage 是误导（400031056 有，
        # 但它是弩手的物理箭）。
        if row["childSkill"].get("magic"):
            fail(problems, f"{where}: childSkill.magic 为 true，三段机制都是物理职业")
        # 资源必须真的在包里，否则动作/特效全空。
        if str(child) not in index_text:
            fail(problems, f"{where}: 子技能 {child} 不在 NativeAssetIndex 里")
    for key, row in (data.get("wounds") or {}).items():
        for source in row.get("sources") or []:
            node = skill_node(int(source))
            info = node.get("info")
            kind = info.get("type").value if info is not None and info.get("type") is not None else None
            if kind != 1:
                fail(problems, f"wounds.{key}: 来源 {source} 的 info/type={kind}，不是攻击技能")


def print_table(data: dict) -> None:
    for key, row in (data.get("sequences") or {}).items():
        levels = row["childLevels"]
        first = levels["1"]
        last = levels[str(len(levels))]
        print(f"  {key} → {row['child']} {row['childName']}  trigger={row['trigger']} "
              f"parentAttack={row['parentAttack']} dur={row['durationSec']}s "
              f"charges={row['charges']} repeat={row['repeat']} mp={row['childMp']} "
              f"damage {first['damage']}..{last['damage']} "
              f"{first['attackCount']}seg×{first['mobCount']}mob  lv{len(levels)}")
    for key, row in (data.get("wounds") or {}).items():
        print(f"  wound {key} ×{row['maxStacks']} / {row['durationSec']}s  ← {row['sources']}")
    for key, row in (data.get("zones") or {}).items():
        first = row["levels"]["1"]
        print(f"  rift  {key} max={first['maxZones']} life={first['lifeSec']}s "
              f"regen={first['regenSec']}s stages={first['stages']} "
              f"explode={first['damage']}%×{first['attackCount']}seg×{first['mobCount']} "
              f"expire={first['expirePercent']}%")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--table", action="store_true", help="只打印表内容")
    args = parser.parse_args()
    if not TABLE.is_file():
        raise SystemExit(f"缺少 {TABLE}（先跑 tools/ms282/export_skill_sequences.py）")
    data = json.loads(TABLE.read_text(encoding="utf-8"))
    if args.table:
        print_table(data)
        return
    problems: list[str] = []
    check_structure(data, problems)
    check_origin(data, problems)
    check_consumption(data, problems)
    check_wiring(data, problems)
    check_semantics(data, problems)
    print_table(data)
    if problems:
        for line in problems:
            print("  FAIL " + line)
        raise SystemExit(f"{len(problems)} 项不一致")
    print("All checks passed.")


if __name__ == "__main__":
    main()
