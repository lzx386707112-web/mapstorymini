#!/usr/bin/env python3
"""导出 282「可叠层主动 buff」表 → Assets/Resources/NativeSkillStacks.json

400001024 普力特的祝福 h 把 6 层效果逐层写死，数值全在 common（公式里的 x 是等级）：

  #c1層#：不受冷卻時間初始化效果影響的技能之外，其餘技能的冷卻時間縮短#w%   → w=10
  #c2層#：異常狀態耐性增加#z                                             → z=10+d(x/2)
  #c3層#：全屬性增加#q                                                   → q=25+x
  #4層#：攻擊力/魔力增加#u                                                → u=10+d(x/2)
  #5層#：攻擊Boss怪物時傷害增加#v%                                        → v=10+d(x/2)
  #c6層#：持續時間內無敵                                                  → INVINCIBLE=100
  使用時持續#s秒並賦予可疊加最多#x次的增益                                  → s=30 / x=6
  若未刷新疊加而持續時間結束或達到最大疊加，則冷卻時間為#y秒                  → y=240
  冷卻時間 #cooltime秒                                                    → cooltime=25

stat 键名取自本项目现有消费点（不是新造）：
  ASR        NativeMobDebuffs / NativeBuffs.shared
  STR/DEX/INT/LUK  PlayerCtrl.RefreshAttributes（flat 主属性）
  PAD/MAD    PlayerCtrl.RefreshAttributes
  BossDamage PlayerCtrl.cs:1731
  INVINCIBLE PlayerCtrl.cs:1406
  CooldownReduce 不是 buff stat，由 SkillSystem 在冷却计算处单独读

用法：
    python3 tools/ms282/export_skill_stacks.py            # 生成
    python3 tools/ms282/export_skill_stacks.py --check    # 只打印
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
OUT = ROOT / "Assets/Resources/NativeSkillStacks.json"

# (层号, 母技能字段 or None, stat 键 or 键列表, h 原文)
LAYERS = [
    (1, "w", ["CooldownReduce"], "不受冷卻時間初始化效果影響的技能之外，其餘技能的冷卻時間縮短#w%"),
    (2, "z", ["ASR"], "異常狀態耐性增加#z"),
    (3, "q", ["STR", "DEX", "INT", "LUK"], "全屬性增加#q"),
    (4, "u", ["PAD", "MAD"], "攻擊力/魔力增加#u"),
    (5, "v", ["BossDamage"], "攻擊Boss怪物時傷害增加#v%"),
    (6, None, ["INVINCIBLE"], "持續時間內無敵"),
]

STACK_SKILLS = {"400001024"}


def fields_of(sid):
    path = PACKS / f"Skill_{int(sid) // 10000}.img"
    if not path.is_file():
        raise SystemExit(f"缺少 {path}")
    img = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
    img.parse()
    node = img.root.get("skill").get(str(sid))
    if node is None or node.get("common") is None:
        raise SystemExit(f"缺少 {sid} 的 common")
    return *level_rows(node.get("common")), [c.name for c in node.children()]


def number(fields, key, level):
    raw = fields.get(key, 0)
    return int(eval_formula(raw, level)) if isinstance(raw, str) else int(raw)


def build():
    stacks = {}
    for sid in sorted(STACK_SKILLS):
        max_level, fields, nodes = fields_of(sid)
        rows = {}
        for level in range(1, max_level + 1):
            effects = []
            for layer, field, stats, h in LAYERS:
                value = 100 if field is None else number(fields, field, level)
                effects.append({
                    "layer": layer,
                    "field": field or "invincible",
                    "stats": stats,
                    "value": value,
                    "h": h,
                })
            rows[str(level)] = {
                "maxStacks": number(fields, "x", level),
                "durationSec": number(fields, "s", level),
                "cooldownSec": number(fields, "cooltime", level),
                "exhaustCooldownSec": number(fields, "y", level),
                "effects": effects,
            }
        strings = skill_string(int(sid))
        stacks[sid] = {
            "parent": int(sid),
            "parentName": simplify(text_of(strings, "name")) or sid,
            "levels": rows,
            "evidence": {
                "hStack": "使用時持續#s秒並賦予可疊加最多#x次的增益，每疊加一次獲得先前效果並疊加新效果",
                "hExhaust": "若未刷新疊加而持續時間結束或達到最大疊加，則冷卻時間為#y秒",
                "nodes": nodes,
            },
        }
    return {
        "source": {
            "packs": str(PACKS),
            "nodes": "Skill/<img>.img/skill/<id>/common + String/Skill.img h",
            "note": "公式按 1..maxLevel 展开；stat 键对应本项目 PlayerCtrl / NativeBuffs 现有消费点",
        },
        "stacks": stacks,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只打印，不写文件")
    args = parser.parse_args()
    data = build()
    for key, row in data["stacks"].items():
        lv30 = row["levels"]["30"]
        print(f"  {key} {row['parentName']}  maxStacks={lv30['maxStacks']} "
              f"duration={lv30['durationSec']}s cd={lv30['cooldownSec']}s "
              f"exhaustCd={lv30['exhaustCooldownSec']}s")
        for e in lv30["effects"]:
            print(f"    {e['layer']}層 {','.join(e['stats'])} = {e['value']}  ({e['field']})")
    if args.check:
        return
    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"-> {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
