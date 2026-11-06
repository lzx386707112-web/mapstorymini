#!/usr/bin/env python3
"""持续伤害（DoT）表守卫。

校验：
  1. 表里的技能集合 = 全库「common 同时有 dot + dotInterval」的技能（去掉 unresolved）；
  2. 逐级 `dot` / `dotTime` / `dotInterval` 与 IMG 公式展开一致（抽 1 / 末级 / 中间级）；
  3. `unresolved` 里的每条确实缺 `dotTime` 或缺 `dot`（不是漏导）；
  4. C# 侧接线：`ApplyDots` 在 `TriggerReactiveSkills` 里、`ClearDots` 在换图清理处。

用法：python3 tools/ms282/check_skill_dots.py
"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
REPO = os.path.dirname(ROOT)
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from export_advanced_skills import eval_formula, level_rows  # noqa: E402

from wzpy import WzImage, WzKey  # noqa: E402

DATA = os.path.join(REPO, "MapleDashResources", "Data", "Skill")
TABLE = os.path.join(ROOT, "Assets", "Resources", "NativeSkillDots.json")

_cache = {}


def all_skills():
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        try:
            image = int(name[:-4])
        except ValueError:
            continue
        if image not in _cache:
            img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
            img.parse()
            _cache[image] = img.root.get("skill")
        root = _cache[image]
        if root is None:
            continue
        for child in root.children():
            if child.name.isdigit():
                yield child.name, child


def main():
    table = json.load(open(TABLE, encoding="utf-8"))
    skills, unresolved = table["skills"], table.get("unresolved", {})
    problems = []

    truth = {}
    for skill_id, node in all_skills():
        common = node.get("common")
        if common is None or common.get("dotInterval") is None:
            continue
        _, fields = level_rows(common)
        truth[skill_id] = fields

    missing = sorted(set(truth) - set(skills) - set(unresolved), key=int)
    extra = sorted(set(skills) - set(truth), key=int)
    if missing:
        problems.append("表里缺 %d 条：%s" % (len(missing), missing[:6]))
    if extra:
        problems.append("表里多出 %d 条端上没有的：%s" % (len(extra), extra[:6]))

    for skill_id in unresolved:
        fields = truth.get(skill_id)
        if fields is None:
            problems.append("%s 登记为待定但端上没有 dotInterval" % skill_id)
        elif fields.get("dotTime") and fields.get("dot"):
            problems.append("%s 登记为待定，但端上 dotTime/dot 都齐全（应该接进表）" % skill_id)

    checked = 0
    for skill_id, spec in skills.items():
        fields = truth.get(skill_id)
        if fields is None:
            continue
        rows = spec["lv"]
        for level in sorted({1, spec["max"], max(1, spec["max"] // 2)}):
            if level < 1 or level > len(rows):
                problems.append("%s 缺 %s 级" % (skill_id, level))
                continue
            row = rows[level - 1]
            expect = [
                int(round(eval_formula(str(fields["dot"]), level) or 0)),
                float(eval_formula(str(fields["dotTime"]), level) or 0),
                float(eval_formula(str(fields["dotInterval"]), level) or 0),
            ]
            if [row[0], round(row[1], 3), round(row[2], 3)] != [expect[0], round(expect[1], 3), round(expect[2], 3)]:
                problems.append("%s %s 级：表=%s 端=%s" % (skill_id, level, row, expect))
        checked += 1

    followups = open(os.path.join(ROOT, "Assets", "Scripts", "NativeSkillFollowups.cs"),
                     encoding="utf-8").read()
    if "ApplyDots(cast,captured)" not in followups:
        problems.append("TriggerReactiveSkills 没有调 ApplyDots（DoT 不会挂）")
    if "ClearDots();" not in followups:
        problems.append("换图清理处没有调 ClearDots（DoT 会跨图继续跳）")
    runtime = os.path.join(ROOT, "Assets", "Scripts", "NativeSkillDots.cs")
    if not os.path.exists(runtime):
        problems.append("缺 Assets/Scripts/NativeSkillDots.cs")

    print("DoT 技能：表 %d 条 / 端 %d 条（待定 %d 条），逐条校验 %d 个"
          % (len(skills), len(truth), len(unresolved), checked))
    if problems:
        print("不一致 %d 条：" % len(problems))
        for line in problems[:15]:
            print("  " + line)
        return 1
    print("✓ 表与商业端一致，运行时接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
