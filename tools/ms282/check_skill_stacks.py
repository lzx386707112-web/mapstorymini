#!/usr/bin/env python3
"""守卫：NativeSkillStacks.json 与商业端一致、C# 接线齐全、stat 键都在 shared 里。

    python3 tools/ms282/check_skill_stacks.py
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import export_skill_stacks as build  # noqa: E402

OUT = ROOT / "Assets/Resources/NativeSkillStacks.json"
BUFFS = ROOT / "Assets/Scripts/NativeBuffs.cs"
WIRING = {
    "Assets/Scripts/NativeSkillStacks.cs": (
        "Resources.Load<TextAsset>(\"NativeSkillStacks\")", "public static bool IsStackSkill",
        "public void ApplyStack(SkillDef cast)", "NativeBuffs.For(p).ApplyStack",
        "float EffectiveCooldown(SkillDef s)", "void ClearStacks()",
    ),
    "Assets/Scripts/SkillSystem.cs": ("EffectiveCooldown(s)", "ApplyStack(s);"),
    "Assets/Scripts/NativeBuffs.cs": ("public void ApplyStack(int skillId", "\"STACK_BLESSING\""),
    "Assets/Scripts/PlayerCtrl.cs": ("buffs.Value(\"STR\")", "buffs.Value(\"LUK\")"),
    "Assets/Scripts/NativeSkillFollowups.cs": ("ClearStacks();",),
}


def main() -> int:
    errors = []
    if not OUT.is_file():
        print("✗ 缺少", OUT)
        return 1
    disk = json.loads(OUT.read_text(encoding="utf-8"))
    if disk != build.build():
        errors.append("JSON 与商业端重算不一致（重跑 export_skill_stacks.py）")
    shared_text = BUFFS.read_text(encoding="utf-8")
    shared_block = shared_text.split("static readonly HashSet<string> shared", 1)[1].split("};", 1)[0]
    for key, row in disk.get("stacks", {}).items():
        lv30 = row["levels"]["30"]
        print(f"  {key} {row['parentName']}  {lv30['maxStacks']} 层 / {lv30['durationSec']}s / "
              f"cd {lv30['cooldownSec']}s / 耗尽 {lv30['exhaustCooldownSec']}s")
        keys = set()
        for effect in lv30["effects"]:
            print(f"    {effect['layer']}層 {','.join(effect['stats'])} = {effect['value']}")
            keys.update(effect["stats"])
        for stat in sorted(keys - {"CooldownReduce"}):
            if f'"{stat}"' not in shared_block:
                errors.append(f"stat {stat} 没进 NativeBuffs.shared（新键必须登记）")
    for rel, symbols in WIRING.items():
        text = (ROOT / rel).read_text(encoding="utf-8")
        for symbol in symbols:
            if symbol not in text:
                errors.append(f"{Path(rel).name} 缺少接线 {symbol}")
    if errors:
        for e in errors:
            print("✗", e)
        return 1
    print("✓ 表与商业端一致，六层 stat 已登记 shared，C# 接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
