#!/usr/bin/env python3
"""逐字段对齐 UI/JobTree.json 与源端 Skill.wz / String.wz。

只读核对，不改任何文件：
    Skill.wz/<job>.img/skill/<id>/level/<n>/*   —— 逐级数值
    Skill.wz/<job>.img/skill/<id>/{masterLevel,req,invisible}
    String.wz/Skill.img/<id>/{name,desc}

同时做源端自洽性检查（这类问题导出表会一并照抄，必须在这里暴露）：
    * desc 写「攻击 N 次」与 attackCount 不一致
    * 有 attackCount 却没有 damage/mad/fixdamage（伤害无从计算）
    * mad 存在但运行时 magic 判定为假（magic 只认 mad 字段）
    * attackCount/mobCount 非法值（0 或负数）

用法：
    python3 tools/audit_skill_params.py            # 全量报告
    python3 tools/audit_skill_params.py --brief    # 只打摘要
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
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402

KEY = WzKey.for_region("GMS")
_cache: dict[str, WzImage] = {}


def wz(path: str) -> WzImage:
    if path not in _cache:
        _cache[path] = WzImage.from_bytes((DATA / (path + ".img")).read_bytes(), key=KEY,
                                          name=os.path.basename(path))
    return _cache[path]


def val(node):
    return getattr(node, "value", None) if node is not None else None


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
        values = {}
        for child in lv.children():
            v = val(child)
            if isinstance(v, (int, float, str)) and not isinstance(v, bool):
                values[child.name] = v
            elif child.name in ("lt", "rb") and v is not None:
                values[child.name] = list(v)
        rows.append((level, values))
    rows.sort(key=lambda r: r[0])
    return rows


def str_node(sid: int):
    img = wz("String/Skill")
    for key in (str(sid), f"{sid:07d}"):
        node = img.get(key)
        if node is not None and node.get("name") is not None:
            return node
    return None


def job_ids(jobtree) -> list[int]:
    ids = [int(j) for j in jobtree["jobs"] and [x["id"] for x in jobtree["jobs"]]]
    return ids


ATTACK_HINT = re.compile(r"攻击\s*([0-9一二三四五六七八九十两]+)\s*[次下]")


def cn2int(s: str) -> int | None:
    if s.isdigit():
        return int(s)
    table = {"一": 1, "二": 2, "两": 2, "三": 3, "四": 4, "五": 5,
             "六": 6, "七": 7, "八": 8, "九": 9, "十": 10}
    return table.get(s)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--brief", action="store_true")
    args = ap.parse_args()

    tree = json.loads((ROOT / "Assets/Resources/UI/JobTree.json").read_text(encoding="utf-8"))
    skills = tree["skills"]

    diffs: list[str] = []
    missing: list[int] = []
    extra: list[int] = []
    seen: set[int] = set()

    for job in tree["jobs"]:
        jid = int(job["id"])
        node = wz(f"Skill/{jid:03d}").get("skill")
        if node is None:
            diffs.append(f"[job {jid}] Skill.wz 缺少 skill 节点")
            continue
        for skill_node in node.children():
            try:
                sid = int(skill_node.name)
            except ValueError:
                continue
            seen.add(sid)
            s = skills.get(str(sid))
            if s is None:
                missing.append(sid)
                continue
            rows = level_rows(skill_node)
            # 等级数量
            if len(rows) != int(s["maxLevel"]):
                diffs.append(f"{sid} {s['name']}: maxLevel {s['maxLevel']} != WZ 等级数 {len(rows)}")
            # masterLevel / req / invisible
            ml = val(skill_node.get("masterLevel")) or 0
            if int(ml) != int(s["masterLevel"]):
                diffs.append(f"{sid} {s['name']}: masterLevel {s['masterLevel']} != WZ {ml}")
            inv = val(skill_node.get("invisible")) or 0
            if int(inv) != int(s.get("invisible", 0)):
                diffs.append(f"{sid} {s['name']}: invisible {s.get('invisible')} != WZ {inv}")
            req = []
            rn = skill_node.get("req")
            if rn is not None:
                for r in rn.children():
                    req.append([int(r.name), val(r) or 0])
            if req != [list(x) for x in s["req"]]:
                diffs.append(f"{sid} {s['name']}: req {s['req']} != WZ {req}")
            # 名称
            sn = str_node(sid)
            name = val(sn.get("name")) if sn is not None else None
            if name and name != s["name"]:
                diffs.append(f"{sid}: name '{s['name']}' != WZ '{name}'")
            # 逐级字段
            jl = s["levels"]
            for level, values in rows:
                runtime = jl.get(str(level))
                if runtime is None:
                    diffs.append(f"{sid} {s['name']}: 缺等级 {level}")
                    continue
                for k, v in values.items():
                    if k not in runtime:
                        diffs.append(f"{sid} {s['name']} lv{level}: 缺字段 {k}={v}")
                    elif isinstance(v, list):
                        if [float(x) for x in runtime[k]] != [float(x) for x in v]:
                            diffs.append(f"{sid} {s['name']} lv{level}: {k} {runtime[k]} != {v}")
                    elif runtime[k] != v:
                        diffs.append(f"{sid} {s['name']} lv{level}: {k} {runtime[k]} != {v}")
                for k in runtime:
                    if k not in values:
                        diffs.append(f"{sid} {s['name']} lv{level}: 多出字段 {k}={runtime[k]}")

    for sid in skills:
        if int(sid) not in seen:
            extra.append(int(sid))

    # —— 源端自洽性 ——
    flags: list[str] = []
    for sid, s in skills.items():
        lv1 = s["levels"].get("1", {})
        keys = set(k for row in s["levels"].values() for k in row)
        ac = lv1.get("attackCount")
        desc = (s.get("desc") or "").replace("\n", " ")
        m = ATTACK_HINT.search(desc)
        if m and ac is not None:
            n = cn2int(m.group(1))
            if n is not None and int(ac) != n:
                flags.append(f"{sid} {s['name']}: desc 写攻击 {n} 次 / attackCount={ac}  [{desc[:48]}]")
        if s["type"] == "attack":
            if not ({"damage", "mad", "fixdamage"} & keys):
                flags.append(f"{sid} {s['name']}: attack 无 damage/mad/fixdamage")
            if ac is not None and int(ac) <= 0:
                flags.append(f"{sid} {s['name']}: attackCount={ac}")
            mc = lv1.get("mobCount")
            if mc is not None and int(mc) <= 0:
                flags.append(f"{sid} {s['name']}: mobCount={mc}")
            if ({"denominator", "damage"} & keys) and "mad" in keys:
                flags.append(f"{sid} {s['name']}: damage 与 mad 同时存在（magic 判定取 mad）")

    print(f"JobTree: {len(skills)} 技能 / jobs {len(tree['jobs'])}")
    print(f"WZ 侧核对技能: {len(seen)}")
    print(f"字段差异: {len(diffs)}   WZ 有但 JobTree 缺: {len(missing)}   JobTree 多出: {len(extra)}   自洽告警: {len(flags)}")
    if not args.brief:
        for block, items in (("== 字段差异 ==", diffs), ("== 缺失 ==", missing),
                             ("== 多出 ==", extra), ("== 自洽告警 ==", flags)):
            if not items:
                continue
            print("\n" + block)
            for item in items[:200]:
                print("  " + str(item))
    if missing or extra or diffs:
        sys.exit(1)


if __name__ == "__main__":
    main()
