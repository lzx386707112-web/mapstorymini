#!/usr/bin/env python3
"""导出 282「强化核心覆盖」→ Assets/Resources/NativeSkillOverrides.json

一条强化核心把**另一个技能**的字段替换掉，自己不产出任何 stat，所以过去被
check_skill_effects.py 报成「buff 但无 stat」。本表登记「谁覆盖谁的哪个字段」，
数值仍从技能表逐级读，C# 不写死。

    500004073 強化暗影侍從/暗影瞬移
      h「暗影侍從延伸的最終傷害反映率#x%，暗影瞬移持續時間增加為#time秒」
      · x    76..105 覆盖 400041028 暗影侍從 的 x 46..75
        （400041028 h「#c暗影飛鏢#：使用投擲飛鏢攻擊時，以最終傷害的#x%造成追加打擊」）
      · time 46..55 覆盖 400041095 暗影瞬移 的 time 30..45
        （400041095 h「召喚固定在一個地點的影子並維持#time秒期間」）
      两条都是**替换**不是相加：强化核心的 x 与被强化技能的 x 级差完全相同
      （都从 +29 走完 30 级），只是整体抬高 30；time 同理整体抬高。

用法
    python3 tools/ms282/export_skill_overrides.py
    python3 tools/ms282/export_skill_overrides.py --check
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

VMATRIX = ROOT / "Assets/Resources/UI/VMatrixSkills.json"
OUT = ROOT / "Assets/Resources/NativeSkillOverrides.json"

# 只登记字段名与语义；数值由下面的逐级校验读出来。
OVERRIDES: dict[int, dict] = {
    500004073: {
        "scaleTarget": 400041028, "scaleField": "x",
        "durationTarget": 400041095, "durationField": "time",
        "hLine": "最終傷害反映率",
    },
}


def h_of(sid: int) -> str:
    return simplify(text_of(skill_string(sid), "h")).replace("\\n", " ").strip()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只打印摘要，不写文件")
    args = ap.parse_args()

    skills = json.loads(VMATRIX.read_text(encoding="utf-8"))["skills"]
    problems: list[str] = []
    overrides: dict[str, dict] = {}

    for sid, cfg in sorted(OVERRIDES.items()):
        row = skills.get(str(sid))
        if row is None:
            problems.append(f"{sid} 不在 VMatrixSkills.json")
            continue
        entry = {
            "name": row.get("name"),
            "scaleTarget": cfg["scaleTarget"],
            "scaleField": cfg["scaleField"],
            "durationTarget": cfg["durationTarget"],
            "durationField": cfg["durationField"],
            "evidence": h_of(sid)[:220],
        }
        for kind, target_key, field_key in (("倍率", "scaleTarget", "scaleField"),
                                            ("时长", "durationTarget", "durationField")):
            target = skills.get(str(cfg[target_key]))
            if target is None:
                problems.append(f"{sid} 的{kind}目标 {cfg[target_key]} 不在技能表")
                continue
            field = cfg[field_key]
            src_lv = row.get("levels") or {}
            dst_lv = target.get("levels") or {}
            compared = 0
            for lv, srow in src_lv.items():
                drow = dst_lv.get(lv)
                if drow is None:
                    continue
                stronger, weaker = srow.get(field), drow.get(field)
                if stronger is None or weaker is None:
                    problems.append(f"{sid} lv{lv}: {kind}字段 {field} 在强化核心或目标里缺失")
                    continue
                compared += 1
                if stronger <= weaker:
                    problems.append(f"{sid} lv{lv}: 强化{kind} {stronger} 没有高于原值 {weaker}")
            if compared < 2:
                problems.append(f"{sid} 的{kind}只逐级比对了 {compared} 级")
            entry[f"evidence{ 'Scale' if kind == '倍率' else 'Duration'}"] = h_of(cfg[target_key])[:200]
        if not entry["evidence"]:
            problems.append(f"{sid} 没有 h 原文")
        overrides[str(sid)] = entry

    payload = {
        "source": "tools/ms282/export_skill_overrides.py",
        "note": "282 强化核心：把被强化技能的字段替换成核心自己的数值；不是 stat 增益",
        "overrides": overrides,
    }
    if not args.check:
        OUT.write_text(json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
                       encoding="utf-8")
    print(f"强化核心覆盖 {len(overrides)} 条；不一致 {len(problems)} 条")
    for p in problems:
        print("  !", p)
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
