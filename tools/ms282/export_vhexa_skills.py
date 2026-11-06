#!/usr/bin/env python3
"""导出 V / HEXA 核心连接技能的等级数据 → Assets/Resources/UI/VMatrixSkills.json

V 核心与 HEXA 核心本身没有等级公式，真正带 `common` 公式的是它们连接的技能
（core.connectSkill / subSkill）。服务端的 `effectiveSkills` 把核心等级映射成技能等级，
客户端要算伤害 / 冷却 / MP 就必须拿到这些技能的 levels。

数据来源（只读）：
    /tmp/packs/Skill_<img>.img        —— 282 定义层（BMS 密钥），common 公式展开
    MapleStory/Data/String/Skill.img  —— 技能名/说明（繁体 → 简体）
    Assets/Resources/UI/{VMatrix,HexaMatrix}.json —— 上一步导出的核心表（决定技能集合）

公式语义与 tools/ms282/export_advanced_skills.py 完全一致（复用其求值器）：
    x / y = 等级；d()/u() 取整；logN 先 floor；隐式乘法；未知标识符 0；末尾 (int)。

V/HEXA 技能不走技能窗 SP（等级由核心等级决定），所以每条都打 `"hexa": true`
让 SkillBook 拒绝用 SP 升级；`core` 字段记录它属于哪个核心，便于反查。

用法：
    python3 tools/ms282/export_vhexa_skills.py            # 生成
    python3 tools/ms282/export_vhexa_skills.py --check    # 只看摘要
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
REPO = ROOT.parent
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import (  # noqa: E402
    PACKS, PROC_SKILLS, apply_alias, classify, eval_formula, expand, info_int, level_rows, simplify,
    skill_string, text_of,
)
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

BMS = WzKey.for_region("BMS")
UI = ROOT / "Assets/Resources/UI"
OUT = UI / "VMatrixSkills.json"

# 本项目支持的 17 个职业（四转 ID）——只导这些职业组内核心连接的技能。
JOBS = [112, 122, 132, 212, 222, 232, 312, 322, 412, 422, 512, 522,
        1112, 1212, 1312, 1412, 1512]

# ——— 282 里「伤害不在 common 顶层」的两种写法 ———
#
# (1) common/attackInfo/<四转职业ID>/{damage,attackCount}
#     艾尔达斯的降临(400001036)、蜘蛛之镜(400001039)、烈阳印记(400001059)。
#     String.wz 的 h 模板直接写 #damage% / #attackCount 次，客户端按自己职业取那一行，
#     所以同一条技能的伤害随职业变化（英雄 522+17x、圣骑士 996+34x…），
#     不能摊平成单个数 ⇒ 导出成 attackInfo 表，运行时按 Profile.JobId 选行。
#
# (2) common 用单字母占位，含义只能从 h 模板读出来（不能按字母猜、也不能跨技能套用）：
#     400021030 落雷凝聚 h:「最多對#q名的敵方使用以#v%傷害值攻擊#s次的閃電#w次」
#               → q=mobCount, v=damage, s=attackCount（w=闪电道数，本引擎一次只放一道）
#     400011052 祝福之鎚 h:「在#v秒內鐵鎚自動以#q%的傷害攻擊最多#s名敵人#z次」
#               → q=damage, s=mobCount, z=attackCount, v=time
#     这条规则与六转技能共用一张表：export_advanced_skills.COMMON_ALIAS / apply_alias()。

_cache: dict[int, WzImage] = {}


# ——— 「自身没有 damage，数值在同节点 psdSkill 引用的攻击技能上」———
#
# 500001005 灵魂赫卡忒 h:「消耗HP #hpCon，召喚靈魂赫卡忒／靈魂赫卡忒會自動追蹤敵人，
# 並每隔一定時間生成高密度靈魂艾爾達粒子 #bulletCount個／#c靈魂艾爾達粒子#：
# 以 #damage% 的傷害攻擊 #attackCount 次」
# 但它的 common 只有召唤参数（lt/rb/attackDelay/speed/x=50…），damage 与 attackCount
# 一个都没有 —— 这两项在 psdSkill 的 500001010 上：damage=660+22*x、bulletCount=4、
# attackCount=2，与 h 的三个占位符逐项相等。不补的话 NativeSummon 拿到的
# DamagePercent 恒为 0，召唤出来站着不打。
#
# 值 = (被引用的技能, (要补的字段…))；只在自身没有该字段时写入，不覆盖已有定义。
LINKED_ATTACK = {
    500001005: (500001010, ("damage", "attackCount", "mobCount", "bulletCount")),
}


def apply_linked_attack(sid: int, rows: dict) -> str:
    """把 psdSkill 里真正的结算技能字段补进每一级；返回补上的字段名（逗号分隔）。"""
    cfg = LINKED_ATTACK.get(sid)
    if cfg is None:
        return ""
    linked, names = cfg
    common = definition(linked).get("common")
    if common is None:
        raise SystemExit(f"{sid} 的伤害来源 {linked} 没有 common")
    _, raw = level_rows(common)
    filled: list[str] = []
    for name in names:
        if name not in raw:
            continue
        value = raw[name]
        for level, row in rows.items():
            if name not in row:
                row[name] = eval_formula(value, int(level)) if isinstance(value, str) else value
        filled.append(name)
    if not filled:
        raise SystemExit(f"{sid} 的伤害来源 {linked} 里一个字段都没取到")
    return ",".join(filled)


def definition(skill_id: int):
    img_id = skill_id // 10000
    if img_id not in _cache:
        path = PACKS / f"Skill_{img_id}.img"
        if not path.is_file():
            raise SystemExit(f"缺少 {path}（先解包 282 Data/Packs/Skill_*.ms 到 /tmp/packs）")
        image = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
        _cache[img_id] = image
    image = _cache[img_id]
    node = image.get(f"skill/{skill_id}")
    if node is None:
        raise SystemExit(f"Skill_{img_id}.img 里没有技能 {skill_id}")
    return node


def wanted() -> dict[int, tuple[str, int]]:
    """技能 id → (来源标记 v|hexa, 首个核心 id)。"""
    result: dict[int, tuple[str, int]] = {}
    for name, table in (("v", json.loads((UI / "VMatrix.json").read_text(encoding="utf-8"))),
                        ("hexa", json.loads((UI / "HexaMatrix.json").read_text(encoding="utf-8")))):
        inscope: set[int] = set()
        for job in JOBS:
            group = table["groups"].get(str(job))
            if group is None:
                raise SystemExit(f"{name} 表缺少职业组 {job}")
            for section in group.values():
                inscope.update(section)
        for core_id in sorted(inscope):
            core = table["cores"].get(str(core_id))
            if core is None:
                raise SystemExit(f"{name} 表引用了不存在的核心 {core_id}")
            for sid in core["skills"] + (core.get("subSkills") or []):
                result.setdefault(sid, (name, core_id))
    return result


def every_core_skill() -> dict[int, int]:
    """全部核心引用的技能 id → 首个引用它的核心 id（不限 17 职业、含強化核心的包装技能）。

    `wanted()` 只覆盖 17 个已迁移职业组，用来导「可施放技能」的等级行；
    这里要的是**名字全覆盖**：玩家背包里滚出来的核心是随机职业的，V 矩阵里点开一个
    400011006 却只显示数字 id，等于没法用。名字只有 282 的 String/Skill.img 有，
    项目自带的 String/Skill.img 只到四转（960 条，五转一条都没有）。
    """
    result: dict[int, int] = {}
    for table_path in ("VMatrix.json", "HexaMatrix.json"):
        table = json.loads((UI / table_path).read_text(encoding="utf-8"))
        for core_id, core in table["cores"].items():
            for sid in (core.get("skills") or []) + (core.get("subSkills") or []):
                result.setdefault(int(sid), int(core_id))
    return result


def export_names(exported: dict[str, dict]) -> dict[str, dict]:
    """{id: {name, desc}}：给运行时 Get() 直读 IMG 的那条路径兜底中文名。

    已经在 skills 表里的条目名字本来就有，不重复写。
    """
    names: dict[str, dict] = {}
    for sid, core_id in sorted(every_core_skill().items()):
        if str(sid) in exported:
            continue
        name_node = skill_string(sid)
        name = simplify(text_of(name_node, "name"))
        desc = simplify(text_of(name_node, "desc"))
        if not name and not desc:
            continue
        names[str(sid)] = {"name": name or str(sid), "desc": desc, "core": core_id}
    return names



def job_attack_info(common, max_level: int) -> dict[str, dict[str, dict]]:
    """common/attackInfo → {四转职业ID: {等级: {damage, attackCount, …}}}。

    只收本项目的 17 个职业；形态与 h 模板一致（#damage% / #attackCount 次）。
    未迁移的职业不落盘，免得 JSON 里躺着一堆本项目用不到的 51 组数字。
    """
    table = common.get("attackInfo")
    if table is None:
        return {}
    result: dict[str, dict[str, dict]] = {}
    for entry in table.children():
        try:
            job = int(entry.name)
        except (TypeError, ValueError):
            continue
        if job not in JOBS:
            continue
        fields = {}
        for child in entry.children():
            if child.name.startswith("dummyStr"):      # 占位串，和 damage 同公式，不重复存
                continue
            value = getattr(child, "value", None)
            if isinstance(value, (int, float)):
                fields[child.name] = value
            elif isinstance(value, str):
                fields[child.name] = value
            elif child.name in ("lt", "rb") and value is not None:
                fields[child.name] = list(value)
        if fields:
            result[str(job)] = expand(fields, max_level)
    return result


def child_damage_bounds(sid: int, rows: dict, max_level: int):
    """父技能缺 lt/rb 时，从 id+1 的「伤害子技能」借判定框。

    282 有一批技能把控制参数（冷却 / MP / 道数 / 持续秒）写在父技能，把
    damage / mobCount / attackCount / lt / rb 写在 id+1 的子技能上：

        400021030 落雷凝聚（v=380+16x、w=8 道） → 400021031（damage 380+16x、lt/rb）
        400011052 祝福之鎚（q=525+21x、v=30 秒） → 400011053（damage 525+21x、lt/rb）

    判据只认「同一条伤害视图」：子技能的 damage 在 1..maxLevel 每一级都必须与父技能
    展开后的 damage 完全相等。不等的是另一段伤害（11141003 日月力量 3500+100x
    对 11141004 的 480+20x），借过来就是给技能换了伤害。

    返回 (子技能 common 字段, 证据说明) 或 None。
    """
    parent = []
    for level in range(1, max_level + 1):
        row = rows.get(str(level)) or {}
        if "damage" not in row:
            return None
        parent.append(row["damage"])
    try:
        sub = definition(sid + 1)
    except SystemExit:
        return None
    common = sub.get("common")
    if common is None:
        return None
    _, fields = level_rows(common)
    if not {"damage", "lt", "rb"} <= set(fields):
        return None
    damage = fields["damage"]
    for index in range(max_level):
        value = eval_formula(damage, index + 1) if isinstance(damage, str) else int(damage)
        if value != parent[index]:
            return None
    return fields


def apply_child_bounds(sid: int, rows: dict, max_level: int) -> str:
    """把可证的子技能字段并进父技能等级行；返回子技能 id（字符串）或空串。"""
    inherited = child_damage_bounds(sid, rows, max_level)
    if inherited is None:
        return ""
    for level in range(1, max_level + 1):
        row = rows[str(level)]
        # lt/rb 是判定框，mobCount/attackCount 是结算规模，attackDelay 是「自动重复
        # 攻击」的周期（400011053 的 attackDelay=1800，就是祝福之鎚「30 秒内每 1.8 秒
        # 自动打一次」的唯一书面来源）。time 不继承：父技能若真持续，282 自己会写
        # （400011052 的 time 由 h 模板的 #v 秒给出），继承会把一次性技能变成常驻。
        for key in ("lt", "rb", "mobCount", "attackCount", "attackDelay"):
            if key in row or key not in inherited:
                continue
            value = inherited[key]
            row[key] = eval_formula(value, level) if isinstance(value, str) else value
    return str(sid + 1)


def export_skill(sid: int, kind: str, core_id: int) -> dict | None:
    node = definition(sid)
    common = node.get("common")
    if common is None:
        return None
    max_level, fields = level_rows(common)
    if max_level <= 0:
        return None
    # 单字母占位先改名成引擎认识的字段，classify() 才能看出这是攻击技能。
    apply_alias(sid, fields)
    rows = expand(fields, max_level)
    linked_fields = apply_linked_attack(sid, rows)
    bounds_from = ""
    if not ({"lt", "rb"} <= set(rows.get("1") or {})):
        bounds_from = apply_child_bounds(sid, rows, max_level)
    name_node = skill_string(sid)
    req = []
    req_node = node.get("req")
    if req_node is not None:
        for child in req_node.children():
            try:
                req.append([int(child.name), int(getattr(child, "value", 0) or 0)])
            except (TypeError, ValueError):
                continue
    invisible = 0
    inv = node.get("invisible")
    if inv is not None:
        try:
            invisible = int(getattr(inv, "value", 0) or 0)
        except (TypeError, ValueError):
            invisible = 0
    # 282 用 info/magicDamage=1 标记「这条攻击走魔法管线」。V/HEXA 的 common 里
    # 一条 mad 都没有（526 条全无），所以只看 mad 会把法师的 V 技能判成物理，
    # 让火毒/冰雷/主教用物理攻击力结算。20 条带这个标记，全是法师系。
    info = node.get("info")
    magic = False
    if info is not None:
        flag = info.get("magicDamage")
        try:
            magic = flag is not None and int(getattr(flag, "value", 0) or 0) != 0
        except (TypeError, ValueError):
            magic = False
    has_icon = isinstance(node.get("icon"), WzCanvasProperty)
    attack_info = job_attack_info(common, max_level)
    entry = {
        "id": sid,
        "name": simplify(text_of(name_node, "name")) or str(sid),
        "desc": simplify(text_of(name_node, "desc")),
        "job": sid // 10000,
        "maxLevel": max_level,
        "masterLevel": 0,
        "type": classify(node, fields, [c.name for c in common.children()], sid),
        "invisible": invisible,
        "auto": False,
        "hexa": True,
        "from": kind,
        "core": core_id,
        "req": req,
        "icon": f"skillicon/{sid}.png" if has_icon else "",
        "levels": rows,
    }
    if magic:
        entry["magic"] = True
    if attack_info:
        entry["attackInfo"] = attack_info
    if bounds_from:
        entry["boundsFrom"] = int(bounds_from)
    if linked_fields:
        entry["linkedAttack"] = {"from": LINKED_ATTACK[sid][0], "fields": linked_fields}
    return entry


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    plan = wanted()
    skills, skipped = {}, []
    for sid, (kind, core_id) in sorted(plan.items()):
        row = export_skill(sid, kind, core_id)
        if row is None:
            skipped.append(sid)
        else:
            skills[str(sid)] = row

    from collections import Counter
    kinds = Counter(s["from"] for s in skills.values())
    types = Counter(s["type"] for s in skills.values())
    print(f"核心技能 {len(plan)} → 导出 {len(skills)}（V {kinds['v']} / HEXA {kinds['hexa']}）"
          f"  跳过 {len(skipped)}  levels {sum(len(s['levels']) for s in skills.values())}")
    print(f"  type={dict(sorted(types.items()))}")
    if skipped:
        print(f"  跳过（无 common / maxLevel=0）: {skipped[:10]}")
    for sid in (400011001, 1141000, 400001036, 400011052):
        row = skills.get(str(sid))
        if row:
            print(f"  {sid} {row['name']} type={row['type']} lv1={row['levels']['1']}")

    # 回归守卫：有 hit 分支（会掉血的对象）却一个伤害字段都没有 = 空放。
    # 2026-11 前这五条都是「能按、有动画、不掉血」：
    #   艾尔达斯的降临 / 蜘蛛之镜 / 烈阳印记 —— 伤害在 common/attackInfo/<职业>，被判成 buff；
    #   落雷凝聚 / 祝福之鎚 —— common 用单字母占位（v/q/s/z），level_rows 看不见。
    # 登记在 PROC_SKILLS 里的技能除外：它们的 hit 是「proc 发动时播的那一下」，
    # 由触发它们的技能打出来，本身不该施放（见 export_advanced_skills.PROC_SKILLS）。
    # info/type=53 + finalAttack=1 的「終極攻擊」组件同理：hit 就是追加攻击那一下。
    def final_attack_component(skill_id: int) -> bool:
        try:
            node = definition(skill_id)
        except SystemExit:
            return False
        return info_int(node, "type") == 53 and info_int(node, "finalAttack") == 1

    hollow = []
    for sid, row in skills.items():
        # summon 的 hit 是召唤物自己的攻击动画，伤害走 NativeSummon 那条链，不算空放。
        if row["type"] in ("attack", "summon") or "attackInfo" in row:
            continue
        if int(sid) in PROC_SKILLS or final_attack_component(int(sid)):
            continue
        if definition(int(sid)).get("hit") is not None:
            hollow.append(f"{sid} {row['name']}（type={row['type']}）")
    print(f"  带 hit 分支却不是攻击技能: {len(hollow)}")
    for item in hollow:
        print(f"    {item}")

    # PROC_SKILLS 是硬编码登记表，条目过期比漏登记更危险：id 不在库里、
    # 或者库里压根没有 damage，说明这条已经改了结构，登记的理由不再成立。
    # 例外：六七转的「追加攻击體」只出现在 AdvancedSkills（334 条）里，不进本表
    # （本表只收五/六转核心 526 条）。这类条目按 info/type=53+finalAttack=1 复核即可。
    stale = []
    for sid, (kind, why) in PROC_SKILLS.items():
        row = skills.get(str(sid))
        if row is None:
            if final_attack_component(sid):
                continue
            stale.append(f"{sid} 不在本次导出集合里（{why}）")
            continue
        if row["type"] != kind:
            stale.append(f"{sid} 期望 {kind}，实际 {row['type']}（{why}）")
        if not any((lv or {}).get("damage", 0) > 0 for lv in row["levels"].values()):
            stale.append(f"{sid} 已无 damage 行，登记理由失效（{why}）")
    print(f"  PROC_SKILLS 登记失效: {len(stale)}")
    for item in stale:
        print(f"    {item}")

    # 攻击技能每一级都必须有伤害段：damage 在 levels 里，或在 attackInfo 的职业行里。
    empty = []
    for sid, row in skills.items():
        if row["type"] != "attack":
            continue
        table = row.get("attackInfo")
        for level in range(1, row["maxLevel"] + 1):
            if table:
                missing = [job for job, levels in table.items()
                           if (levels.get(str(level)) or {}).get("damage", 0) <= 0]
                if missing:
                    empty.append(f"{sid} {row['name']} lv{level} attackInfo 缺伤害: {missing[:3]}")
            elif (row["levels"].get(str(level)) or {}).get("damage", 0) <= 0:
                empty.append(f"{sid} {row['name']} lv{level} damage=0")
    print(f"  攻击技能里伤害为 0 的等级: {len(empty)}")
    for item in empty[:10]:
        print(f"    {item}")

    # 运行时按 Profile.JobId 取 attackInfo 的行：精确命中，否则按 job/10 落回同一条支线。
    # 五六转编码是「四转 + 1」（113/123/…/1213），这里把「17 个四转 + 17 个五六转」都钉住。
    uncovered = []
    for sid, row in skills.items():
        table = {int(k) for k in (row.get("attackInfo") or {})}
        if not table:
            continue
        branches = {job // 10 for job in table}
        for job in JOBS:
            if job not in table:
                uncovered.append(f"{sid} 缺四转行 {job}")
            elif (job + 1) // 10 not in branches:
                uncovered.append(f"{sid} 五六转 {job + 1} 落不回支行")
    print(f"  attackInfo 职业覆盖缺口: {len(uncovered)}")
    for item in uncovered[:10]:
        print(f"    {item}")

    # 判定框：源端没有 lt/rb 的攻击技能。能证实的已经从 id+1 的伤害子技能继承
    # （见 child_damage_bounds）；剩下的运行时按「相机可见范围」判定，不再是身前 200px
    # （NativeSkillCatalog.AreaAttack）。这里只分档统计，没有证据就不凭空造框。
    inherited = [sid for sid, row in skills.items() if row.get("boundsFrom")]
    by_matrix, by_area = [], []
    for sid, row in skills.items():
        if row["type"] != "attack" or row.get("attackInfo"):
            continue
        first = row["levels"].get("1") or {}
        if ("lt" in first and "rb" in first) or first.get("range"):
            continue
        node = definition(int(sid))
        info = node.get("info")
        info2 = node.get("info2")
        area = any(block is not None and block.get("areaAttack") is not None
                   for block in (info, info2))
        (by_area if area else by_matrix).append(f"{sid} {row['name']}")
    print(f"  判定框从伤害子技能继承: {len(inherited)}"
          f"    仍无框→可见范围: {len(by_matrix) + len(by_area)}"
          f"（info/areaAttack {len(by_area)} / 五六转兜底 {len(by_matrix)}）")

    if args.check:
        raise SystemExit(1 if hollow or empty or uncovered or stale else 0)
    names = export_names(skills)
    print(f"技能名兜底 names 段: {len(names)} 条（核心引用但不在 skills 表的技能）")
    payload = {
        "source": {
            "packs": str(PACKS),
            "definitions": "Skill/<img>.img（282 定义层，BMS 密钥）",
            "cores": "Assets/Resources/UI/{VMatrix,HexaMatrix}.json",
            "jobs": "四转 ID（17 个迁移职业）",
            "levels": "common 公式按 1..maxLevel 展开（SkillFormulaEvaluator 语义）",
            "attackInfo": "common/attackInfo/<四转ID>：按职业分流的伤害，运行时按 Profile.JobId 取行",
            "alias": "COMMON_ALIAS：h 模板指定了单字母占位的真实含义（落雷凝聚/祝福之鎚）",
            "boundsFrom": "父技能缺 lt/rb 时，从 damage 逐级完全相等的 id+1 子技能继承判定框",
            "magic": "info/magicDamage=1：V/HEXA 没有 mad，魔法管线靠这个标记",
            "type": "classify() + PROC_SKILLS：追加攻击/Buff 载体的 damage 只在 proc 里结算，不判 attack",
            "names": "全部核心引用技能的 name/desc（282 String/Skill.img → 简体），"
                     "给 SkillBook.Get 直读 IMG 的那条路径兜底",
        },
        "skills": dict(sorted(skills.items(), key=lambda kv: int(kv[0]))),
        "names": names,
    }
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"写入 {OUT.relative_to(ROOT)}  {OUT.stat().st_size / 1024:.0f} KiB")


if __name__ == "__main__":
    main()
