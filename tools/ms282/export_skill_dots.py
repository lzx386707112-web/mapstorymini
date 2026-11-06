#!/usr/bin/env python3
"""导出技能的持续伤害（DoT）参数表。

282 的持续伤害写在技能自己的 `common` 里，三个字段成组出现：

| 字段 | 含义 |
|---|---|
| `dot` | **每跳**伤害%（与 `damage` 同一口径，吃同一个技能伤害乘区） |
| `dotInterval` | 每跳间隔（秒） |
| `dotTime` | 持续总时长（秒） |

例：`1121015 蓝焰恐惧` `dot=75+3*x / dotInterval=2 / dotTime=45+d(x/2)`
⇒ 45 秒内每 2 秒跳一次，共 22 跳，每跳 165%（Lv30）。

项目此前**完全没有 DoT 结算**（代码里搜不到 `dotInterval`）：这 36 条技能
只结算了首段 `damage`，后续所有跳伤全丢 —— 对持续伤害类技能等于少一大半输出。

⚠️ 有 6 条只写了 `dot`/`dotInterval` 而没有 `dotTime`
（`1241500` `15141500` `2241000` `400011039` `400021042` `400041009`）——
它们都是**放置型领域**，持续时长由 `updatableTime` / `ndTime` 管，不按 `dotTime`。
本表**不猜**：这 6 条写进 `unresolved`，等领域的时长口径确认后再接。

产物：Assets/Resources/NativeSkillDots.json
    skills/<skillId> = {max, lv: [[dmg, seconds, interval], ...]}
    unresolved = {<skillId>: 原因}

用法：python3 tools/ms282/export_skill_dots.py
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
OUT = os.path.join(ROOT, "Assets", "Resources", "NativeSkillDots.json")


def main():
    skills, unresolved, problems = {}, {}, []
    images = 0
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        try:
            img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
            img.parse()
        except Exception as error:                       # noqa: BLE001
            problems.append("%s 读取失败：%s" % (name, error))
            continue
        images += 1
        root = img.root.get("skill")
        if root is None:
            continue
        for child in root.children():
            if not child.name.isdigit():
                continue
            common = child.get("common")
            if common is None or common.get("dotInterval") is None:
                continue
            _, fields = level_rows(common)
            if "dot" not in fields:
                # 只有 dotInterval 没有 dot：这个间隔在源端不是 DoT（2241000 暴风雪VI、
                # 400041009 都是「周期效果」的另一套语义）。不猜，登记待定。
                unresolved[child.name] = {
                    "interval": float(eval_formula(str(fields["dotInterval"]), 1) or 0)
                    if isinstance(fields.get("dotInterval"), str) else 0,
                    "why": "有 dotInterval 但没有 dot 字段（不是 DoT 语义，需逆向确认）",
                }
                continue
            max_level = int(fields.get("maxLevel") or 30)
            interval = fields.get("dotInterval")
            duration = fields.get("dotTime")
            # interval 实测是纯数字（"1"/"2"/"14"），写成公式的按公式展开
            interval_rows = []
            for level in range(1, max_level + 1):
                value = eval_formula(str(interval), level) if isinstance(interval, str) else interval
                interval_rows.append(float(value or 0))
            if not duration:
                unresolved[child.name] = {
                    "interval": interval_rows[0] if interval_rows else 0,
                    "why": "没有 dotTime（放置型领域，时长由 updatableTime/ndTime 管）",
                }
                continue
            rows = []
            for level in range(1, max_level + 1):
                dmg = eval_formula(str(fields["dot"]), level)
                secs = eval_formula(str(duration), level) if isinstance(duration, str) else duration
                rows.append([int(round(dmg or 0)), float(secs or 0), interval_rows[level - 1]])
            if any(row[0] <= 0 or row[1] <= 0 or row[2] <= 0 for row in rows):
                problems.append("%s 的 dot/dotTime/dotInterval 展开后出现非正数" % child.name)
                continue
            skills[child.name] = {"max": max_level, "lv": rows}

    payload = {
        "source": {
            "nodes": "Skill/<img>.img/skill/<id>/common{dot,dotTime,dotInterval}",
            "note": "每跳伤害按 dot% 与首段 damage% 的比例缩放；dotTime 秒后停止，间隔 dotInterval 秒",
        },
        "skills": skills,
        "unresolved": unresolved,
    }
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)

    print("扫描 %d 个 IMG：可结算 DoT %d 条，时长待定 %d 条"
          % (images, len(skills), len(unresolved)))
    for skill_id, row in list(skills.items())[:5]:
        print("  %s Lv30 → %s" % (skill_id, row["lv"][min(29, len(row["lv"]) - 1)]))
    if unresolved:
        print("  时长待定：" + "、".join(sorted(unresolved)))
    if problems:
        print("问题 %d 条：" % len(problems))
        for line in problems[:10]:
            print("  " + line)
    print("写入 " + os.path.relpath(OUT, REPO))
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
