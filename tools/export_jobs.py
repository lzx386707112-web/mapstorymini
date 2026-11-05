#!/usr/bin/env python3
"""导出职业树（冒险家 28 + 骑士团 21 job）到 Assets/Resources/UI/JobTree.json。

数据来源全部只读，不做任何改写：
    Skill.wz/<job>.img/skill/<id>        —— 逐级数值、masterLevel、req、icon
    String.wz/Skill.img/<id>/name|desc   —— 技能名/说明
    String.wz/Skill.img/<job>/bookName   —— 技能书页签名
    gms-server i18n/message_zh_CN.properties —— job.name.*（仅用于补齐 client 侧缺的职业名）
    现有 UI/JobSkills.json               —— 已上线 210 条技能的 type 判定（人工校准过，优先保留）

输出两份：
    1) Assets/Resources/UI/JobTree.json  —— 运行时唯一职业/技能数据源
    2) Assets/Resources/NativeAssetIndex.json 的 aliases 增补 skillicon/<id>
       （新增或纠正源节点路径，不删除别名；保留 IMG 节点名的前导零）

用法：
    python3 tools/export_jobs.py             # 生成 + 校验
    python3 tools/export_jobs.py --check     # 只校验不写
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
SERVER = REPO.parent / "BeiDou-Server"
DATA = SERVER / "clien" / "Data"
WZPY = SERVER / "tool" / "wz-python"
sys.path.insert(0, str(WZPY))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

KEY = WzKey.for_region("GMS")

# 北斗当年把 TMS 五/六转技能塞进了四转 Skill/<job>.img，现已按 282 结构重做。
# docs/legacy-advanced-deletion.json 是唯一删除清单（audit_legacy_advanced.py 产出），
# 导出时按它过滤，防止重新导出把旧技能带回 JobTree/图标别名。
LEGACY_DELETION = REPO / "docs" / "legacy-advanced-deletion.json"


def load_legacy_deleted() -> dict[int, set[int]]:
    if not LEGACY_DELETION.exists():
        return {}
    data = json.loads(LEGACY_DELETION.read_text(encoding="utf-8"))
    out: dict[int, set[int]] = {}
    for entry in data.get("delete", []):
        out.setdefault(int(entry["job"]), set()).add(int(entry["id"]))
    return out

# —— 职业表 ——
# 冒险家：0 → 五系一转 → 二转(+10，战士/法师有三选一分支) → 三转(+1) → 四转(+1)
ADVENTURER = [
    0,
    100, 110, 111, 112,
    120, 121, 122,
    130, 131, 132,
    200, 210, 211, 212,
    220, 221, 222,
    230, 231, 232,
    300, 310, 311, 312,
    320, 321, 322,
    400, 410, 411, 412,
    420, 421, 422,
    500, 510, 511, 512,
    520, 521, 522,
]
# 骑士团：1000 → 五系一转(1100..1500) → 2/3/4 转各 +1/+10
CYGNUS = [
    1000,
    1100, 1110, 1111, 1112,
    1200, 1210, 1211, 1212,
    1300, 1310, 1311, 1312,
    1400, 1410, 1411, 1412,
    1500, 1510, 1511, 1512,
]
JOBS = ADVENTURER + CYGNUS

# —— 五转觉醒后的独立职业编码（四转 ID + 1）——
# 282 的 38601..38625 六转链任务 Check/0 的 job 白名单用的就是这套编码
# （113/123/133/213/…/1113/1213/1313，见 docs/tms282-skill/19 §2.1），
# 角色的四转 ID 在完成 1465（五转：紀錄力量，完成覺醒）后 +1，六转链直接按新编码比对，
# 不做 -1 归一化（17 §7 决议 2）。
#
# 六转技能本体在 282 的 Skill_<四转 ID + 2>.img（114/1114…，如 1141000 狂暴攻擊VI），
# 运行时落到我们自己的 Skill/<四转 ID + 1>.img，由 tools/ms282/import_advanced_skills.py
# 导入并写出 UI/AdvancedSkills.json；本导出器只负责把职业节点挂进 JobTree，
# 技能清单读该文件（缺失时留空，不影响四转页）。
ADVANCED = {
    112: 113, 122: 123, 132: 133,
    212: 213, 222: 223, 232: 233,
    312: 313, 322: 323,
    412: 413, 422: 423,
    512: 513, 522: 523,
    1112: 1113, 1212: 1213, 1312: 1313, 1412: 1413, 1512: 1513,
}
ADVANCED_OF = {v: k for k, v in ADVANCED.items()}
# 282 的 String/Skill.img 没有 114/1114 的 bookName 节点，页签名由本项目给（显示在技能窗书条）
ADVANCED_BOOK = "六转技能"
ADVANCED_SKILLS = ROOT / "Assets/Resources/UI/AdvancedSkills.json"

# 一转能力值门槛（AbstractPlayerInteraction.canGetFirstJob）。
# 注意 use_auto_assign_starters_ap=true 时 canGetFirstJob 恒真，该表只用于 UI 提示。
FIRST_JOB_STAT = {100: {"str": 35}, 200: {"int": 20}, 300: {"dex": 25}, 400: {"dex": 25}, 500: {"dex": 20}}
# 一转等级门槛：法师 8 级，其余 10 级（npc/1032001.js:126 vs 1022000.js:126）
FIRST_JOB_LEVEL = {200: 8}

# 骑士团源端没有转职脚本（npc/1101008.js 只有引导对白，全仓无 changeJobById(11xx)），
# 阈值按 v83 骑士团惯例给出，标记 derived=True 以便文档与 UI 区分。
CYGNUS_LEVELS = {1: 10, 2: 30, 3: 60, 4: 100}

# —— 转职凭证 / 赠品（scripts-zh-CN/npc 各系一转教官脚本逐条提取）——
# 一转赠武器：战士 1302077；法师 1372043；弓手 1452051；飞侠 1472061+1332063（镖）；海盗 1492000+1482000（子弹）
FIRST_JOB_WEAPON = {
    100: [1302077], 200: [1372043], 300: [1452051], 400: [1472061, 1332063], 500: [1492000, 1482000],
}
PROOF_2ND = 4031012        # 英雄的证明（二转试炼 30 黑珠后获得，全系共用）
PROOF_3RD = 4031058        # 智慧项链（三转智力试炼通过，最终交给三转教官）
QUEST_4TH = 6904           # 四转任务链
BOOK_4TH = 2280003         # 四转技能书（能书）

# 四转自动授技（teachSkill(sid, 0, 10, -1)，masterLevel 由 WZ masterLevel 决定）
# 来源：scripts-zh-CN/npc/2081100.js（战士）、2081200（法师）、2081300（弓手）、
#       2081400（飞侠）、2081500（海盗）
TEACH_4TH = {
    112: [1120004, 1120005, 1121001, 1121002, 1121008, 1121010],
    122: [1220005, 1221001, 1221002, 1221003, 1221004, 1221009],
    132: [1320005, 1320008, 1320009, 1321001, 1321002, 1321007],
    212: [2121001, 2121002, 2121005, 2121006, 2121007],
    222: [2221001, 2221002, 2221003, 2221005, 2221006, 2221007],
    232: [2321001, 2321002, 2321005, 2321006, 2321008],
    312: [3120005, 3121002, 3121004, 3121006, 3121007, 3121008],
    322: [3220004, 3221001, 3221002, 3221005, 3221006, 3221007],
    412: [4120002, 4120005, 4121004, 4121006, 4121008],
    422: [4220002, 4220005, 4221001, 4221004, 4221007],
    512: [5121001, 5121002, 5121003, 5121004, 5121005, 5121007, 5121009, 5121010],
    522: [5220001, 5220002, 5220011, 5221003, 5221004, 5221006, 5221007, 5221008, 5221009],
}

# 冒险家 2/3/4 转等级门槛（各系教官脚本 getLevel() 判定）
ADV_LEVEL = {2: 30, 3: 70, 4: 120}

FAMILY = {1: "warrior", 2: "magician", 3: "bowman", 4: "thief", 5: "pirate"}

# 精灵的祝福（12 / 10000012 …）：按账号等级自动习得，不占 SP、不能手动加点
def AUTO_SKILL(sid: int) -> bool:
    return sid % 10000000 == 12


def wz(path: str) -> WzImage:
    full = DATA / (path + ".img")
    return WzImage.from_bytes(full.read_bytes(), key=KEY, name=os.path.basename(path))


def num(node):
    return getattr(node, "value", None) if node is not None else None


def job_img_name(job: int) -> str:
    return "Skill/" + str(job).zfill(3)


def tier_of(job: int) -> int:
    if job in ADVANCED_OF:
        return 5                      # 五转觉醒后的独立编码，技能来自六转 img
    if job in (0, 1000):
        return 0
    if job % 100 == 0:
        return 1
    if job % 10 == 0:
        return 2
    return 3 if job % 10 == 1 else 4


def family_of(job: int) -> str:
    if job in ADVANCED_OF:
        return family_of(ADVANCED_OF[job])
    if job == 0:
        return "beginner"
    if job == 1000:
        return "noblesse"
    if job < 1000:
        return FAMILY.get(job // 100, "unknown")
    return "cygnus-" + {11: "dawnwarrior", 12: "blazewizard", 13: "windarcher",
                        14: "nightwalker", 15: "thunderbreaker"}.get(job // 100, "unknown")


def next_jobs(job: int) -> list[int]:
    if job in ADVANCED:
        return [ADVANCED[job]]
    if job in ADVANCED_OF:
        return []
    if job == 0:
        return [100, 200, 300, 400, 500]
    if job == 1000:
        return [1100, 1200, 1300, 1400, 1500]
    if job in (100, 200):
        return [job + 10, job + 20, job + 30]
    if job >= 1100 and job % 100 == 0:
        return [job + 10]
    if job % 10 in (0, 1) and job % 100 != 0:
        return [job + 1]
    return []


def advance_rule(job: int, parent: int) -> dict | None:
    """job 的转职规则。parent 为上一阶职业 id。
    SP/AP 数值逐条对齐 Character.changeJob：一转 +1SP/+0AP，二转三转 +1SP/+5AP
    （骑士团 +7AP），四转 +3SP/+5AP；resetStats 只发生在第一次转职。"""
    if tier_of(job) == 0:
        return None
    if tier_of(job) == 5:
        # 五转觉醒（1465 完成）由任务钩子改码，没有 NPC 转职脚本可依，不填 advance 规则：
        # JobSystem.CanAdvance 看到 advance==null 就会拒绝「直接转入」。
        return None
    cygnus = job >= 1000
    tier = tier_of(job)
    ap = 7 if (cygnus and tier >= 2) else (5 if tier >= 2 else 0)
    sp = 3 if tier == 4 else 1
    if tier == 1:
        return {
            "level": CYGNUS_LEVELS[1] if cygnus else FIRST_JOB_LEVEL.get(job, 10),
            "stat": {} if cygnus else FIRST_JOB_STAT.get(job, {}),
            "resetStats": True,
            "grant": FIRST_JOB_WEAPON.get(job, []),
            "sp": sp, "ap": ap, "derived": cygnus,
        }
    if tier == 4:
        return {
            "level": CYGNUS_LEVELS[4] if cygnus else ADV_LEVEL[4],
            "quest": QUEST_4TH,
            "grant": [BOOK_4TH],
            "teach": TEACH_4TH.get(job, []),
            "sp": sp, "ap": ap, "derived": cygnus,
        }
    return {
        "level": CYGNUS_LEVELS[tier] if cygnus else ADV_LEVEL[tier],
        "requireItem": PROOF_3RD if tier == 3 else PROOF_2ND,
        "consumeItem": PROOF_3RD if tier == 3 else PROOF_2ND,
        "sp": sp, "ap": ap, "derived": cygnus,
    }


def load_existing_types() -> dict[int, str]:
    path = ROOT / "Assets/Resources/UI/JobSkills.json"
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return {int(k): v.get("type", "") for k, v in data.get("skills", {}).items()}


def load_existing_skills() -> dict[int, dict]:
    path = ROOT / "Assets/Resources/UI/JobSkills.json"
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return {int(k): v for k, v in data.get("skills", {}).items()}


def load_existing_tabs() -> dict[int, list[int]]:
    """旧 JobSkills.json 的 jobs[].tabs[] 已经按 job id 分好，且已在真机截图里验证过。
    新职业没有这份"已验证列表"，只能靠 WZ 的 invisible 标记过滤。"""
    path = ROOT / "Assets/Resources/UI/JobSkills.json"
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    out: dict[int, list[int]] = {}
    for line in data.get("jobs", []):
        for tab in line.get("tabs", []):
            job = tab.get("job")
            if isinstance(job, int) and job not in out:
                out[job] = [int(x) for x in tab.get("skills", [])]
    return out


def load_i18n_job_names() -> dict[int, str]:
    path = SERVER / "gms-server/src/main/resources/i18n/message_zh_CN.properties"
    if not path.exists():
        return {}
    text = path.read_text(encoding="utf-8", errors="ignore")

    def dec(s: str) -> str:
        return re.sub(r"\\u([0-9A-Fa-f]{4})", lambda m: chr(int(m.group(1), 16)), s)

    out = {}
    for m in re.finditer(r"^job\.name\.(\d+)\s*=\s*(.+)$", text, re.M):
        out[int(m.group(1))] = dec(m.group(2)).strip()
    return out


def load_advanced_skills() -> tuple[dict[int, list[int]], dict[int, dict], dict[int, str]]:
    """六转技能清单（tools/ms282/import_advanced_skills.py 产出）。

    返回 (职业 → 技能 id 列表, 技能 id → JobTree 技能数据, 技能 id → 图标源)。
    文件不存在时三张表都为空：职业节点照挂，该技能页留空，不影响四转页。
    """
    if not ADVANCED_SKILLS.exists():
        return {}, {}, {}
    data = json.loads(ADVANCED_SKILLS.read_text(encoding="utf-8"))
    by_job = {int(k): [int(x) for x in v] for k, v in data.get("jobs", {}).items()}
    skills = {int(k): v for k, v in data.get("skills", {}).items()}
    icons = {int(k): v for k, v in data.get("iconSources", {}).items()}
    return by_job, skills, icons


def classify(skill, level1: dict) -> str:
    """技能类型：只用于"能不能施放/怎么施放"，不参与数值。"""
    if skill.get("summon") is not None:
        return "summon"
    if any(k in level1 for k in ("damage", "fixdamage", "mad", "attackCount")):
        return "attack"
    if "time" in level1 or "prop" in level1:
        return "buff"
    return "passive"


def level_rows(skill) -> list[dict]:
    node = skill.get("level")
    if node is None:
        return []
    rows = []
    for lv in node.children():
        try:
            level = int(lv.name)
        except ValueError:
            continue
        values = {"level": level}
        for child in lv.children():
            value = num(child)
            if isinstance(value, (int, float, str)):
                values[child.name] = value
            elif child.name in ("lt", "rb") and value is not None:
                values[child.name] = list(value)
        rows.append(values)
    rows.sort(key=lambda r: r["level"])
    return rows


def str_node(strings, sid: int):
    """String.wz/Skill.img 里技能名的键：≥10000000 用原值，其余左侧补零到 7 位
    （beginner 的 1000 → "0001000"、8 → "0000008"）。
    注意 "100"/"1000" 同时也是职业节点的键（只有 bookName），必须挑真正带 name 的节点。"""
    for key in (str(sid), f"{sid:07d}"):
        node = strings.get(key)
        if node is not None and node.get("name") is not None:
            return node
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true", help="只校验，不写文件")
    args = ap.parse_args()

    strings = wz("String/Skill")
    old_types = load_existing_types()
    old_skills = load_existing_skills()
    old_tabs = load_existing_tabs()
    i18n = load_i18n_job_names()
    legacy_deleted = load_legacy_deleted()

    jobs = []
    skills: dict[str, dict] = {}
    icon_sources: dict[int, str] = {}
    missing_icon, missing_name, no_levels = [], [], []
    derived_names = []

    for job in JOBS:
        img = wz(job_img_name(job))
        skill_root = img.get("skill")
        if skill_root is None:
            raise SystemExit(f"Skill.wz 缺少职业 {job}")
        book = strings.get(str(job))
        book = (num(book.get("bookName")) or "") if book is not None else ""

        ids = []
        skip = legacy_deleted.get(job, set())
        for node in skill_root.children():
            try:
                sid = int(node.name)
            except ValueError:
                continue
            if sid in skip:
                continue
            ids.append(sid)
            rows = level_rows(node)
            if not rows:
                no_levels.append(sid)
                continue
            sid_name = str_node(strings, sid)
            sname = num(sid_name.get("name")) if sid_name is not None else None
            sdesc = num(sid_name.get("desc")) if sid_name is not None else None
            if not sname:
                missing_name.append(sid)
                sname = str(sid)

            req = []
            req_node = node.get("req")
            if req_node is not None:
                for r in req_node.children():
                    req.append([int(r.name), num(r) or 0])

            icon = node.get("icon")
            has_icon = isinstance(icon, WzCanvasProperty)
            if has_icon:
                icon_sources[sid] = f"{job_img_name(job)}.img/skill/{node.name}/icon"
            if not has_icon:
                missing_icon.append(sid)

            prev = old_skills.get(sid) or {}
            typ = prev.get("type") or classify(node, rows[0])
            invisible = num(node.get("invisible")) or 0
            skills[str(sid)] = {
                "id": sid,
                "name": sname,
                "desc": sdesc or "",
                "job": sid // 10000,
                "maxLevel": len(rows),
                "masterLevel": num(node.get("masterLevel")) or 0,
                "type": typ,
                "invisible": invisible,
                "auto": bool(prev.get("auto")) or AUTO_SKILL(sid),
                "req": req,
                "icon": f"skillicon/{sid}.png" if has_icon else "",
                "levels": {str(r["level"]): {k: v for k, v in r.items() if k != "level"} for r in rows},
            }

        name = None
        # 2) i18n job.name.*（骑士团带 "(N转)" 后缀，去掉）
        if job in i18n:
            name = re.sub(r"\([0-9]转\)$", "", i18n[job]).strip()
        if not name:
            name = book or str(job)
            derived_names.append(job)

        parent = None
        for cand in JOBS:
            if job in next_jobs(cand):
                parent = cand
                break

        # 技能窗列表：
        #   - 旧 6 条线沿用已验证的 tab 列表（新手 4 个）
        #   - 其余新手阶（骑士团 1000）按 invisible==0 || auto 过滤 → 同样是 4 个
        #   - 进阶职业列该 img 的全部技能（与旧 4 转线一致；4 转技能的 invisible=1
        #     在 v83 里表示"技能书/隐藏被动"，不代表技能窗不显示）
        if job in old_tabs:
            listed = [sid for sid in old_tabs[job] if sid not in skip]
        elif tier_of(job) == 0:
            listed = [sid for sid in ids
                      if str(sid) in skills and (skills[str(sid)]["invisible"] == 0 or skills[str(sid)]["auto"])]
        else:
            listed = ids

        jobs.append({
            "id": job,
            "name": name,
            "book": book,
            "family": family_of(job),
            "tier": tier_of(job),
            "parent": parent if parent is not None else -1,
            "next": next_jobs(job),
            "advance": advance_rule(job, parent if parent is not None else -1),
            "skills": listed,
            "allSkills": ids,
        })

    # —— 五转觉醒后的独立编码节点（四转 ID + 1，tier 5）——
    # 这些职业没有 NPC 转职脚本（改码点是 1465 完成的任务钩子），所以 advance=null、
    # next 为空；技能清单读 AdvancedSkills.json（缺文件时留空）。
    adv_ids, adv_skills, adv_icons = load_advanced_skills()
    name_by_job = {j["id"]: j["name"] for j in jobs}
    all_by_job = {j["id"]: j["allSkills"] for j in jobs}
    for adv, parent in ADVANCED_OF.items():
        ids = [sid for sid in adv_ids.get(adv, []) if sid in adv_skills]
        for sid in ids:
            skills[str(sid)] = adv_skills[sid]
            if sid in adv_icons:
                icon_sources[sid] = adv_icons[sid]
        inherited = [sid for sid in all_by_job.get(parent, [])]
        jobs.append({
            "id": adv,
            "name": name_by_job.get(parent, str(adv)),
            "book": ADVANCED_BOOK,
            "family": family_of(adv),
            "tier": 5,
            "parent": parent,
            "next": [],
            "advance": None,
            "skills": ids,
            # 六转角色仍持有四转技能：allSkills 带上父职业，GM「学会本职业全部技能」才不会漏
            "allSkills": inherited + [sid for sid in ids if sid not in inherited],
        })
    if adv_ids and not any(j["skills"] for j in jobs if j["tier"] == 5):
        print("  警告: AdvancedSkills.json 有职业键但没有可用技能数据")

    # 职业名只认源端 i18n（message_zh_CN.properties 的 job.name.<id>）。
    # 旧版这里会用 Assets/Resources/UI/JobSkills.json 的 jobNames 覆盖，直接把名字写歪：
    #   100 剑士→应为战士、210 巫师(火毒)→应为法师（火毒）、211/212 整体提前一阶、
    #   311 游侠→应为射手、321 狙击手→应为游侠、322 神箭手→应为箭神。
    # 交叉校验（2026-11-01）：String/Skill.img 的 bookName 与 i18n 完全对应
    #   （311 book=射手技能、321 book=游侠技能、211 book=巫师（火毒）技能），
    #   BeiDou.exe 字符串池 32..50 的中文也与 i18n 一致，所以那套旧表是错的。
    # 骑士团保留 (N转) 后缀？否 —— 上面的 re.sub 已统一去掉，UI 显示「魂骑士」。

    for s in skills.values():
        s["job"] = s["id"] // 10000

    job_names = {str(j["id"]): j["name"] for j in jobs}
    out = {
        "source": {
            "skill": str(Path("Skill").as_posix()),
            "string": "String/Skill.img",
            "jobNamesFallback": "gms-server/src/main/resources/i18n/message_zh_CN.properties",
        },
        "jobNames": job_names,
        "jobs": jobs,
        "skills": skills,
    }

    total_levels = sum(len(s["levels"]) for s in skills.values())
    print(f"jobs={len(jobs)}  skills={len(skills)}  levels={total_levels}")
    print(f"missing icon={len(missing_icon)}  missing name={len(missing_name)}  no levels={len(no_levels)}")
    if missing_icon:
        print("  无图标:", missing_icon[:12])
    if missing_name:
        print("  无名字:", missing_name[:12])
    if no_levels:
        print("  无等级:", no_levels[:12])
    print(f"职业名派生（i18n 缺失）: {derived_names}")

    # —— 图标别名 ——
    index_path = ROOT / "Assets/Resources/NativeAssetIndex.json"
    index = json.loads(index_path.read_text(encoding="utf-8"))
    aliases = index["aliases"]
    added = updated = 0
    for s in skills.values():
        if not s["icon"]:
            continue
        key = "skillicon/" + str(s["id"])
        source = icon_sources[s["id"]]
        if key not in aliases:
            aliases[key] = {"source": source}
            added += 1
        elif aliases[key]["source"] != source:
            aliases[key]["source"] = source
            updated += 1
    print(f"skillicon 别名: 新增 {added}，纠正 {updated}，共 {len(aliases)}")

    if args.check:
        return
    target = ROOT / "Assets/Resources/UI/JobTree.json"
    target.write_text(json.dumps(out, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    index_path.write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"写入 {target.relative_to(ROOT)}  {target.stat().st_size / 1024:.0f} KiB")


if __name__ == "__main__":
    main()
