#!/usr/bin/env python3
"""护盾表 ↔ 运行时 的一致性守卫。

上一轮的教训：字段映射写进 JSON 却没有人读，等于白做（indie* 那批）。
所以这里不只校验表本身，还要去 C# 源码里解析「谁读哪些键」。

    ① 表结构   mode 合法、数组长度 == maxLevel、数值不全为 0
    ② 有出处   绑定字段在 282 的 h 文案里以 #字段 出现（证据片段随表落盘）
    ③ 有消费   C# 源码里逐个键必须被读到
    ④ 无孤儿   C# 读的键必须都在表里声明过
    ⑤ 接线     SkillSystem 调 Activate、PlayerCtrl 调 Absorb、NativeBuffs 调 BuffInvincible、
               TemporaryStatView 调 Snapshot（护盾要看得见）

用法：
    python3 tools/ms282/check_shields.py
    python3 tools/ms282/check_shields.py --table
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "Assets/Resources/NativeShieldContract.json"
SKILL_TABLE = ROOT / "Assets/Resources/UI/VMatrixSkills.json"
SOURCE = ROOT / "Assets/Scripts/NativeShields.cs"
SKILL_SYSTEM = ROOT / "Assets/Scripts/SkillSystem.cs"
PLAYER = ROOT / "Assets/Scripts/PlayerCtrl.cs"
BUFFS = ROOT / "Assets/Scripts/NativeBuffs.cs"
HUD = ROOT / "Assets/Scripts/TemporaryStatView.cs"

MODES = {"pool", "stack", "elements", "invincible"}

# 每种模式必须带哪些绑定名。少一个运行时就少算一段（不会报错，只会静默少算）。
REQUIRED = {
    "pool": {"amountPct", "time"},
    "stack": {"gainPct", "capPct", "time", "decayPrimaryPct", "decaySecondaryPct"},
    "elements": {"elements", "time", "cost", "stepPct"},
    "invincible": {"time"},
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--table", action="store_true", help="打印护盾数值预览")
    args = parser.parse_args()

    problems: list[str] = []
    notes: list[str] = []

    if not CONTRACT.is_file():
        print(f"缺少 {CONTRACT.relative_to(ROOT)}；先跑 export_skill_shields.py")
        return 1
    data = json.loads(CONTRACT.read_text(encoding="utf-8"))
    shields = data.get("shields") or {}
    skills = json.loads(SKILL_TABLE.read_text(encoding="utf-8"))["skills"]
    source = SOURCE.read_text(encoding="utf-8") if SOURCE.is_file() else ""
    if not source:
        problems.append("Assets/Scripts/NativeShields.cs 缺失")

    # ① 表结构 + ② 出处
    for sid, entry in shields.items():
        label = f"{sid} {entry.get('name')}"
        mode = entry.get("mode")
        if mode not in MODES:
            problems.append(f"{label} 的 mode={mode!r} 不认识")
            continue
        row = skills.get(sid)
        if row is None:
            problems.append(f"{label} 不在 VMatrixSkills（运行时永远读不到）")
            continue
        if row.get("type") == "attack":
            problems.append(f"{label} 是 attack 类型，护盾表不该登记攻击技能")
        max_level = int(entry.get("maxLevel") or 0)
        if max_level != int(row.get("maxLevel") or 0):
            problems.append(f"{label} 的 maxLevel={max_level} 与技能表 "
                            f"{row.get('maxLevel')} 不一致")
        for name in REQUIRED[mode]:
            if name not in entry:
                problems.append(f"{label} ({mode}) 缺绑定 {name}")
                continue
            series = entry[name]
            if not isinstance(series, list):
                problems.append(f"{label} 的 {name} 不是数组")
                continue
            if len(series) != max_level:
                problems.append(f"{label} 的 {name} 长度 {len(series)} != {max_level}")
            if not any(series):
                problems.append(f"{label} 的 {name} 全为 0")
        if not entry.get("evidence"):
            problems.append(f"{label} 没有 evidence（h 原文片段）")

    # ③ 有消费：表里出现的每个绑定名都要在 C# 里被读到。
    #    At(spec, "键", …) / spec["键"] / TryGetValue("键", …) 三种写法都要认。
    declared = {name for entry in shields.values() for name in REQUIRED.get(entry.get("mode"), ())}
    for name in sorted(declared):
        if f'"{name}"' not in source:
            problems.append(f'NativeShields.cs 没有读 "{name}" —— 表里有、代码里没人用')

    # ④ 无孤儿：C# 里 At(spec,"x") 的键必须声明过。
    for name in sorted(set(re.findall(r'At\(\s*spec\s*,\s*"([A-Za-z0-9_]+)"', source))):
        if name not in declared and name != "time":
            problems.append(f'NativeShields.cs 读了表里没有的 "{name}"')

    # ⑤ 接线
    system = SKILL_SYSTEM.read_text(encoding="utf-8")
    if "NativeShields.For(p)" not in system or ".Activate(" not in system:
        problems.append("SkillSystem 没有调用 NativeShields.Activate —— 按下技能不会有护盾")
    player = PLAYER.read_text(encoding="utf-8")
    if player.count("shields.Absorb(") + player.count("shields != null ? shields.Absorb") < 2:
        problems.append("PlayerCtrl 的受击/跌落结算没有都接 Absorb")
    if "NativeShields.BuffInvincible" not in BUFFS.read_text(encoding="utf-8"):
        problems.append("NativeBuffs.SkillStats 没有调用 BuffInvincible —— 虚无型态不会无敌")
    # 护盾有存量、会被打掉，玩家必须看得见还剩多少；只 Toast 一下等于没有 HUD。
    hud = HUD.read_text(encoding="utf-8") if HUD.is_file() else ""
    if "NativeShields.Snapshot" not in hud:
        problems.append("TemporaryStatView 没有读 NativeShields.Snapshot —— 护盾没有任何 HUD")

    # ⑥ stack 型必须是「命中触发」，而不是要求玩家按键
    for sid, entry in shields.items():
        if entry.get("mode") != "stack":
            continue
        if "OnSkillHit" not in system:
            problems.append(f"{sid} {entry.get('name')} 是命中触发型，但 SkillSystem 没接 OnSkillHit")

    if args.table:
        def cell(entry: dict, level: int) -> str:
            """按等级下标取该条的数值（time 单列，其余是百分比/计数）。"""
            index = level - 1
            parts = []
            for name, series in entry.items():
                if not isinstance(series, list) or name == "time":
                    continue
                if index < len(series):
                    parts.append(f"{name}={series[index]}")
            return " ".join(parts)

        print(f"\n{'id':>10} {'模式':<11} {'名称':<14} {'lv1':<40} lv30")
        for sid, entry in shields.items():
            print(f"{sid:>10} {entry['mode']:<11} {entry['name']:<14} "
                  f"{cell(entry, 1):<40} {cell(entry, 30)}")

    for item in notes:
        print("  NOTE", item)
    for item in problems:
        print("  FAIL", item)
    print(f"护盾 {len(shields)} 条，不一致 {len(problems)} 条")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
