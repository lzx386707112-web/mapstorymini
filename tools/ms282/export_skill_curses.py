#!/usr/bin/env python3
"""导出 282「诅咒 + 命中生成冰塊」表 → Assets/Resources/NativeSkillCurses.json

400011121 極冰風暴 上一轮被判成「没有 damage/attackCount/psdSkill，放行就是 79 帧特效 + 0 伤害」，
其实商业端把整条链拆成了三个技能，只是我们没接线：

  400011121 母技能  施放时上诅咒；`effect` 23 帧 + `special` 4 组（就是那 79 帧）
  400011123 光芒状态 invisible=1，time=20 / subTime=5000 / mobCount=5
  400011122 冰塊攻击体 invisible=1，damage=640+25*x / attackCount=4 / mobCount=15 /
            cooltimeMS=400 / lt=[-750,-750] rb=[750,400]，special/special0/hit

h 与三条 common 逐级自证（没有一处靠猜）：

  h「對最多#mobCount名敵人施加鑽石狼的詛咒」        → curseTargets = 母 mobCount = 10
  h「持續#x秒…最多增加至#q2秒」                    → 15 / 20
  h「詛咒最多可累積#y次，每次…冰塊的攻擊次數增加#v次」 → y=6、v=1，且与 400011122 的 x=6 / y=1 相等
  h「#q秒內，定期發動能對最多#w名敵人施加…的光芒」    → 400011123 time=20==q、mobCount=5==w、
                                                    subTime=5000 就是周期（不需要推断）
  h「冰塊：對最多#u名敵人造成#u2%傷害#u3次，冷卻#t秒」→ 400011122 mobCount=15==u、
                                                    damage=640+25*x==u2、attackCount=4==u3、
                                                    cooltimeMS=400==t*1000

伤害/段数由运行时 `SkillBook.Native(400011122, level)` 从 IMG 展开，本表只登记
接线关系、目标数、时长、周期、叠层与冷却（全部读商业端，不写死常量）。

用法：
    python3 tools/ms282/export_skill_curses.py            # 生成
    python3 tools/ms282/export_skill_curses.py --check    # 只打印
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import eval_formula, level_rows, skill_string, text_of, simplify  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402

BMS = WzKey.for_region("BMS")
PACKS = Path("/tmp/packs")
OUT = ROOT / "Assets/Resources/NativeSkillCurses.json"

# parent → (光芒状态, 冰塊伤害体)，逐字段核实过才登记。
CURSES = {
    "400011121": (400011123, 400011122),
}


def scalar(node):
    try:
        value = node.value
    except Exception:  # noqa: BLE001
        return None
    return list(value) if isinstance(value, tuple) else value


def skill_root(prefix):
    path = PACKS / f"Skill_{prefix}.img"
    if not path.is_file():
        raise SystemExit(f"缺少 {path}")
    img = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
    img.parse()
    return img.root.get("skill")


def common(skill, sid):
    node = skill.get(str(sid))
    if node is None or node.get("common") is None:
        raise SystemExit(f"缺少技能 {sid} 的 common")
    max_level, fields = level_rows(node.get("common"))
    return max_level, fields, [c.name for c in node.children()]


def as_int(fields, key, level=30, fallback=0):
    raw = fields.get(key, fallback)
    return int(eval_formula(raw, level)) if isinstance(raw, str) else int(raw)


def build():
    curses = {}
    for parent, (state, body) in CURSES.items():
        parent_prefix = int(parent) // 10000
        skill = skill_root(parent_prefix)
        max_level, pf, pnodes = common(skill, parent)
        _, sf, snodes = common(skill_root(state // 10000), state)
        _, bf, bnodes = common(skill_root(body // 10000), body)

        # 交叉校验：h 里的单字母占位必须与两条子技能的字段完全相等，否则说明版本号变了。
        checks = {
            "maxStacks": (as_int(pf, "y"), as_int(bf, "x")),
            "bonusHitsPerStack": (as_int(pf, "v"), as_int(bf, "y")),
            "lightTargets": (as_int(pf, "w"), as_int(sf, "mobCount")),
            "lightDurationSec": (as_int(pf, "q"), as_int(sf, "time")),
            "iceTargets": (as_int(pf, "u"), as_int(bf, "mobCount")),
            # t 是秒且带小数（"0.4"），求值器末尾会 (int) 截断，这里直接按秒换算。
            "iceCooldownMs": (round(float(pf.get("t", 0.4)) * 1000), as_int(bf, "cooltimeMS")),
            "curseDurationSec": (as_int(pf, "x"), as_int(bf, "time")),
        }
        for name, (a, b) in checks.items():
            if a != b:
                raise SystemExit(f"{parent} {name} 母子不一致：{a} != {b}，回去核对 h")

        rows = {}
        for level in range(1, max_level + 1):
            rows[str(level)] = {
                "curseTargets": as_int(pf, "mobCount", level),
                "curseDurationSec": as_int(pf, "x", level),
                "refreshCapSec": as_int(pf, "q2", level),
                "maxStacks": as_int(pf, "y", level),
                "bonusHitsPerStack": as_int(pf, "v", level),
                "lightTargets": as_int(sf, "mobCount", level),
                "lightDurationSec": as_int(sf, "time", level),
                "lightIntervalMs": as_int(sf, "subTime", level),
                "iceTargets": as_int(bf, "mobCount", level),
                "iceCooldownMs": as_int(bf, "cooltimeMS", level),
            }
        strings = skill_string(int(parent))
        curses[parent] = {
            "parent": int(parent),
            "state": state,
            "body": body,
            "parentName": simplify(text_of(strings, "name")) or parent,
            "levels": rows,
            "evidence": {
                "hCurse": "對最多#mobCount名敵人施加鑽石狼的詛咒；#q秒內定期對最多#w名敵人施加",
                "hStack": "詛咒最多可累積#y次，每次累積次數可使冰塊的攻擊次數增加#v次",
                "hIce": "冰塊：對最多#u名敵人造成#u2%傷害#u3次，冷卻時間為#t秒",
                "stateNodes": snodes,
                "bodyNodes": bnodes,
                "parentNodes": pnodes,
            },
        }
    return {
        "source": {
            "packs": str(PACKS),
            "nodes": "Skill/<img>.img/skill/{parent,state,body}/common + String/Skill.img h",
            "note": "伤害/段数由运行时 SkillBook.Native(body, level) 展开；本表只登记接线与周期/叠层/冷却",
        },
        "curses": curses,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只打印，不写文件")
    args = parser.parse_args()
    data = build()
    for key, row in data["curses"].items():
        lv30 = row["levels"]["30"]
        print(f"  {key} {row['parentName']}  state={row['state']} body={row['body']}")
        print(f"    Lv30 {json.dumps(lv30, ensure_ascii=False)}")
    print(f"共 {len(data['curses'])} 条")
    if args.check:
        return
    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"-> {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
