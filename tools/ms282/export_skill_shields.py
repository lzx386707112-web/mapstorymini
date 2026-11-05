#!/usr/bin/env python3
"""护盾 / 无敌类技能 → Assets/Resources/NativeShieldContract.json

## 为什么单开一张表

`NativeBuffs.SkillStats` 只产出「属性 stat」，护盾不是属性，是一个会被消耗的池子：
同样的 `x` 在突击之盾是「最大HP的百分比」，在海神护体是「每次命中累积的百分比」，
在西尔芙之壁压根不是百分比而是 `w` 个元素。硬塞进 stat 表只能得到「按下后什么都没发生」。

## 判据：只认 h 文案能自证的四类

| 模式 | h 原文形状 | 语义 |
|---|---|---|
| `pool` | 「生成相当于最大HP的#x%的护盾，持续 #time秒」 | 一次性定量护盾，吸收完或超时消失 |
| `stack` | 「生成可吸收#x%最大HP伤害的保护罩#time秒／可累积等同#u%最大HP的数值，每秒失去#y% + #z当前保护罩数值」 | 每次命中累积，有上限，按秒衰减 |
| `elements` | 「生成持有#w的元素之风之屏障…受击时每造成相当于最大HP的1%伤害便#z消耗元素以无效化伤害」 | 计数型，按「最大HP 1%」为一档扣元素 |
| `invincible` | 「在#time间进入无敌」 | 纯无敌；`s` 等 h 里没引用的字段一律不读 |

虚无型态 400021060 的 `common/s = 16769792` 就是「无引用字段」的例子：h 里没有 `#s`，
上一轮把它记成「语义未自证」是判据错了 —— 它压根不需要被解释。

## 输出形状

逐级数值在导出期按 1..maxLevel 展开成数组（与 NativeSkillEffects 一致），
运行时只按下标取，不再需要公式求值器。

    shields: { "<id>": { "mode": "...", "evidence": "<h 片段>",
                         "<绑定名>": [v1..vN], "stepPct": 1 } }

用法：
    python3 tools/ms282/export_skill_shields.py           # 生成 + 校验
    python3 tools/ms282/export_skill_shields.py --check   # 只校验，不落盘
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
sys.path.insert(0, str(Path(__file__).resolve().parent))

from export_advanced_skills import simplify, strings, text_of  # noqa: E402

OUT = ROOT / "Assets/Resources/NativeShieldContract.json"
SKILL_TABLE = ROOT / "Assets/Resources/UI/VMatrixSkills.json"

# id → (模式, {绑定名: WZ 字段}, h 里必须出现的片段)
#
# 「h 里必须出现的片段」是这张表的自证条件：282 改了文案就把登记打成 FAIL，
# 而不是让运行时继续按旧语义算。片段里的 `#字段` 必须与该技能绑定的字段对得上。
SHIELDS: dict[int, tuple[str, dict[str, str], str]] = {
    # 三条 HEXA 核心（40000002 / 40000003 / 40000004）数值完全相同，都是「最大HP的 21%」。
    # 文案不同源：015 写「最大HP的#x%」，017/019 写「最大HP #x%」（少一个「的」）。
    # 保留各自原文而不是把片段放宽，否则哪天语义真变了也照样通过。
    500001015: ("pool", {"amountPct": "x", "time": "time"},
                "生成相当于最大HP的#x%的护盾，持续 #time秒"),
    500001017: ("pool", {"amountPct": "x", "time": "time"},
                "生成相当于最大HP #x%的护盾，持续 #time秒"),
    500001019: ("pool", {"amountPct": "x", "time": "time"},
                "生成相当于最大HP #x%的护盾，持续 #time秒"),
    # 连接技能命中时累积；上限 u%、每次 +x%、每秒失去 (y+z)% 的当前存量。
    # y 与 z 是同一个衰减率拆成的两段：逐级 y+z = 90 → 75 平滑递减、两者同步下降，
    # 不是「上限 y% + 当前值 z%」两个基准 —— 按后者 lv30 每秒就掉 25% 上限，
    # 与 h 的「可累积」直接矛盾（永远攒不起来）。
    15140010: ("stack", {"gainPct": "x", "capPct": "u", "time": "time",
                         "decayPrimaryPct": "y", "decaySecondaryPct": "z"},
               "生成可吸收#x%最大HP伤害的保护罩#time秒"),
    # 元素计数：持有 w 个，每「最大HP 1%」的伤害消耗 z 个。
    400031030: ("elements", {"elements": "w", "time": "time", "cost": "z"},
                "生成持有#w的元素之风之屏障并维持#time秒"),
    400021060: ("invincible", {"time": "time"}, "在#time间进入无敌"),
}

# 元素型护盾的「一档」固定是最大HP的 1%：400031030 的 h 把 1% 写死在文案里，
# 不是一个字段，只能在这里给常量。key 必须与 SHIELDS 一致用 int（sid 是 int）。
ELEMENT_STEP_PCT = {400031030: 1}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只校验，不落盘")
    args = parser.parse_args()

    if not SKILL_TABLE.is_file():
        print(f"缺少 {SKILL_TABLE.relative_to(ROOT)}；先跑 export_vhexa_skills.py")
        return 1
    skills = json.loads(SKILL_TABLE.read_text(encoding="utf-8"))["skills"]
    strings_ = strings()

    shields: dict[str, dict] = {}
    problems: list[str] = []
    for sid, (mode, bindings, evidence) in SHIELDS.items():
        row = skills.get(str(sid))
        if row is None:
            problems.append(f"{sid} 不在 VMatrixSkills（登记过期或编号写错）")
            continue
        label = f"{sid} {row.get('name')}"
        max_level = int(row.get("maxLevel") or 0)
        levels = row.get("levels") or {}
        if max_level <= 0 or not levels:
            problems.append(f"{label} 没有等级表")
            continue

        # ① 自证：h 文案必须仍含登记片段，且片段里的 #字段 都绑定过。
        h = simplify(text_of(strings_.get(str(sid)), "h"))
        if not h:
            problems.append(f"{label} 的 String.wz h 为空，无法自证")
            continue
        if evidence not in h:
            problems.append(f"{label} 的 h 与登记片段不符：{h[:80]!r}")
        bound = set(bindings.values())
        for field in bound:
            if f"#{field}" not in h:
                problems.append(f"{label} 的 h 里没有 #{field}（绑定字段无出处）")

        # ② 逐级展开：缺级/全 0 都是真问题（运行时会静默按 0 处理）。
        entry: dict = {"mode": mode, "name": row.get("name"), "maxLevel": max_level,
                       "evidence": evidence}
        for name, field in bindings.items():
            series: list[int] = []
            for level in range(1, max_level + 1):
                cell = levels.get(str(level))
                if not isinstance(cell, dict) or cell.get(field) is None:
                    problems.append(f"{label} 的 {field} 在第 {level} 级缺失")
                    series = []
                    break
                series.append(int(cell[field]))
            if not series:
                continue
            if not any(series):
                problems.append(f"{label} 的 {field} 全为 0")
            entry[name] = series
        if sid in ELEMENT_STEP_PCT:
            # 常量也展开成数组：运行时统一走 At(spec, key, level) 按下标取，
            # 不给「有的键是标量、有的键是数组」留分叉。
            entry["stepPct"] = [ELEMENT_STEP_PCT[sid]] * max_level
        if all(name in entry for name in bindings):
            shields[str(sid)] = entry

    payload = {
        "source": "tools/ms282/export_skill_shields.py",
        "shields": dict(sorted(shields.items())),
    }
    if not args.check:
        text = json.dumps(payload, ensure_ascii=False, indent=1)
        # 逐级数组（30 个整数）按 indent=1 展开要 60 行，压回单行；
        # 表里只有整数数组，不会误伤嵌套对象。
        text = re.sub(r"\[\s*\n\s*([0-9,\s\n]*?)\s*\n\s*\]",
                      lambda m: "[" + ", ".join(
                          v.strip().rstrip(",") for v in m.group(1).splitlines() if v.strip()) + "]",
                      text)
        OUT.write_text(text + "\n", encoding="utf-8")

    print(f"护盾登记 {len(SHIELDS)} 条 → 可落盘 {len(shields)} 条，问题 {len(problems)} 条")
    for sid, entry in payload["shields"].items():
        summary = ", ".join(f"{k}[{v[0]}..{v[-1]}]" for k, v in entry.items()
                            if isinstance(v, list))
        print(f"  {sid:>10} {entry['mode']:<11} {entry['name']}: {summary}")
    for item in problems:
        print("  FAIL", item)
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
