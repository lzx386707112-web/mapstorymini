#!/usr/bin/env python3
"""导出 282「按住结束 → 收尾子技能」表 → Assets/Resources/NativeSkillFinishers.json

商业端 Skill/*.img 上有 4 个技能带 `keydownFinish/<子技能id>`，另有 `psdSkill` 把同一个
子技能登记为攻击体。子技能一律 `invisible=1`，自己的 common 里带完整的
`damage / attackCount / mobCount / lt / rb`，`String/Skill.img` 里只有名字没有 h ——
也就是说**收尾攻击的数值与表现全在子技能**，母技能只是给「几次、间隔多少」。

上一轮把它记成「psdSkill 400011069 仍缺」，其实是我们没接 `keydownFinish` 这条线。

h 文案逐条自证波数（WAVES 里的 evidence 就是原文片段）：

  400011068 槍刺旋風 「…造成#s%傷害並攻擊#q次的衝擊波#z次作為收尾攻擊」
      → 子 400011069 damage=564+23x(==s) / attackCount=15(==q) / mobCount=12(==y)，z=8 波
  400041059 暗影投擲 「最後以巨型暗影飛鏢收尾…共攻擊#v次」
      → 子 400041060 attackCount=13(==v)，只有 1 波
  400051046 小萌新吉祥物 「…發動以#x%的傷害攻擊#y次的爆炸#u2次」
      → 子 400051097 damage=675+27x(==x) / attackCount=10(==y)，u2=7 波
  400051078 破碎連拳 「長按結束時將發動#v次衝擊」
      → 子 400051079 damage=800+32x(==y) / attackCount=14(==s)，v=8 波

间隔一律取母技能 `common/attackDelay`（源端「連射每發間隔」，4 条都有）。
子技能等级由 C# 用 `SkillBook.Native(child, 母技能等级)` 从 IMG 兜底展开，
所以本表**不复制伤害数值**，只登记接线关系与波数/间隔。

用法：
    python3 tools/ms282/export_skill_finishers.py            # 生成
    python3 tools/ms282/export_skill_finishers.py --check    # 只打印
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import (  # noqa: E402
    eval_formula, level_rows, skill_string, text_of, simplify,
)
from wzpy import WzImage, WzKey  # noqa: E402

BMS = WzKey.for_region("BMS")
PACKS = Path("/tmp/packs")
OUT = ROOT / "Assets/Resources/NativeSkillFinishers.json"
IMGS = (40000, 40001, 40002, 40003, 40004, 40005)

# parent → (母技能 common 里的波数字段, h 原文片段)。None = 只有 1 波。
WAVES = {
    "400011068": ("z", "…造成#s%傷害並攻擊#q次的衝擊波#z次作為收尾攻擊"),
    "400041059": (None, "最後以巨型暗影飛鏢收尾，對最多#z名敵人造成#u%傷害，共攻擊#v次（v 是段數）"),
    "400051046": ("u2", "…發動以#x%的傷害攻擊#y次的爆炸#u2次"),
    "400051078": ("v", "長按結束時將發動#v次衝擊"),
}


def scalar(node):
    try:
        value = node.value
    except Exception:  # noqa: BLE001
        return None
    if isinstance(value, tuple):
        return list(value)
    return value


def img_root(prefix):
    path = PACKS / f"Skill_{prefix}.img"
    if not path.is_file():
        raise SystemExit(f"缺少 {path}（先把 282 Data/Packs/Skill_*.ms 解到 /tmp/packs）")
    img = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
    img.parse()
    return img.root.get("skill")


def build():
    finishers = []
    for prefix in IMGS:
        skill = img_root(prefix)
        for node in skill.children():
            if not node.name.isdigit():
                continue
            finish = node.get("keydownFinish")
            if finish is None:
                continue
            child_id = scalar(finish)
            if not isinstance(child_id, int):
                continue
            parent = node.name
            if parent not in WAVES:
                raise SystemExit(f"发现未核实的 keydownFinish：{parent} → {child_id}，"
                                 "先按 String/Skill.img 的 h 确认波数字段再登记 WAVES")
            wave_field, evidence = WAVES[parent]
            _, parent_fields = level_rows(node.get("common"))
            # common 里的数字也写成 string（"140"/"8"），按源端公式求值器展开到 Lv30。
            # 波数与间隔都不随等级变化，取哪一级都一样。
            def at_level(field, fallback):
                raw = parent_fields.get(field, fallback)
                return int(eval_formula(raw, 30)) if isinstance(raw, str) else int(raw)

            interval = at_level("attackDelay", 120)
            waves = 1 if wave_field is None else at_level(wave_field, 1)
            if waves < 1 or interval < 1:
                raise SystemExit(f"{parent} 波数/间隔异常：{waves} / {interval}")
            child = skill.get(str(child_id))
            child_common = {}
            if child is not None and child.get("common") is not None:
                child_common = {c.name: scalar(c) for c in child.get("common").children()}
            strings = skill_string(int(parent))
            finishers.append({
                "parent": int(parent),
                "child": child_id,
                "parentName": simplify(text_of(strings, "name")) or parent,
                "waves": int(waves),
                "wavesField": wave_field or "1",
                "intervalMs": int(interval),
                "childDamage": child_common.get("damage"),
                "childAttackCount": child_common.get("attackCount"),
                "childMobCount": child_common.get("mobCount"),
                "evidence": evidence,
            })
    finishers.sort(key=lambda r: r["parent"])
    return {
        "source": {
            "packs": str(PACKS),
            "nodes": "Skill/<img>.img/skill/<id>/keydownFinish + 子技能 common + String/Skill.img h",
            "note": "波数/间隔来自母技能 common 与 h；子技能数值由运行时 SkillBook.Native 从 IMG 展开",
        },
        "finishers": finishers,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只打印，不写文件")
    args = parser.parse_args()
    data = build()
    for row in data["finishers"]:
        print(f"  {row['parent']} {row['parentName']} → {row['child']}  "
              f"waves={row['waves']}({row['wavesField']}) every {row['intervalMs']}ms  "
              f"子: {row['childDamage']}% x{row['childAttackCount']} x{row['childMobCount']}")
    print(f"共 {len(data['finishers'])} 条")
    if args.check:
        return
    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"-> {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
