#!/usr/bin/env python3
"""守卫：NativeSkillFinishers.json 必须等于商业端重算结果，且 C# 接线没被拆掉。

    python3 tools/ms282/check_skill_finishers.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import export_skill_finishers as build  # noqa: E402

OUT = ROOT / "Assets/Resources/NativeSkillFinishers.json"
CS = [
    ROOT / "Assets/Scripts/NativeSkillFinishers.cs",
    ROOT / "Assets/Scripts/SkillHeldInput.cs",
]
WIRING = {
    CS[0]: ("Resources.Load<TextAsset>(\"NativeSkillFinishers\")", "Art.Id(row, \"parent\")",
            "SkillBook.Native(childId, level)", "BeginVisuals(body, true)",
            "HitAt(body, body.native, origin, facing)"),
    CS[1]: ("StartFinisher(previous)",),
}


def main() -> int:
    errors = []
    if not OUT.is_file():
        print("✗ 缺少", OUT)
        return 1
    on_disk = json.loads(OUT.read_text(encoding="utf-8"))
    fresh = build.build()
    if on_disk != fresh:
        errors.append("JSON 与商业端重算不一致（重跑 export_skill_finishers.py）")
    rows = on_disk.get("finishers", [])
    print(f"  收尾子技能 {len(rows)} 条")
    for row in rows:
        print(f"    {row['parent']} → {row['child']}  waves={row['waves']} every {row['intervalMs']}ms")
        if row["waves"] < 1 or row["intervalMs"] < 1:
            errors.append(f"{row['parent']} 波数/间隔非法")
        if not row.get("childDamage") or not row.get("childAttackCount"):
            errors.append(f"{row['parent']} 子技能没有伤害/段数")
    for path, symbols in WIRING.items():
        text = path.read_text(encoding="utf-8")
        for symbol in symbols:
            if symbol not in text:
                errors.append(f"{path.name} 缺少接线 {symbol}")
    if errors:
        for e in errors:
            print("✗", e)
        return 1
    print("✓ 表与商业端一致，C# 接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
