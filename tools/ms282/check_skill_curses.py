#!/usr/bin/env python3
"""守卫：NativeSkillCurses.json 必须等于商业端重算结果，且 C# 四处接线齐全。

    python3 tools/ms282/check_skill_curses.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import export_skill_curses as build  # noqa: E402

OUT = ROOT / "Assets/Resources/NativeSkillCurses.json"
WIRING = {
    "Assets/Scripts/NativeSkillCurses.cs": (
        "Resources.Load<TextAsset>(\"NativeSkillCurses\")", "public static bool IsCurseParent",
        "void StartCurse(SkillDef cast)", "void TriggerCurseOnHit(", "void FireIce(",
        "PlayNativeReplayEffect(curseActive.Body", "void ClearCurses()",
    ),
    "Assets/Scripts/SkillBook.cs": ("SkillSystem.IsCurseParent(id)",),
    "Assets/Scripts/SkillSystem.cs": ("StartCurse(s);",),
    "Assets/Scripts/NativeSkillFollowups.cs": ("TriggerCurseOnHit(cast,captured,origin,facing);", "ClearCurses();"),
}


def main() -> int:
    errors = []
    if not OUT.is_file():
        print("✗ 缺少", OUT)
        return 1
    disk = json.loads(OUT.read_text(encoding="utf-8"))
    if disk != build.build():
        errors.append("JSON 与商业端重算不一致（重跑 export_skill_curses.py）")
    for key, row in disk.get("curses", {}).items():
        lv30 = row["levels"]["30"]
        print(f"  {key} {row['parentName']}  state={row['state']} body={row['body']}  Lv30={lv30}")
        if lv30["lightIntervalMs"] <= 0 or lv30["iceCooldownMs"] <= 0 or lv30["maxStacks"] <= 0:
            errors.append(f"{key} 周期/冷却/叠层非法")
    for rel, symbols in WIRING.items():
        text = (ROOT / rel).read_text(encoding="utf-8")
        for symbol in symbols:
            if symbol not in text:
                errors.append(f"{Path(rel).name} 缺少接线 {symbol}")
    if errors:
        for e in errors:
            print("✗", e)
        return 1
    print("✓ 表与商业端一致，诅咒/冰塊四处接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
