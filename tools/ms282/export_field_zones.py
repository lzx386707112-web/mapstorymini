#!/usr/bin/env python3
"""导出 282 六转「周期领域」→ Assets/Resources/NativeFieldZones.json

背景（2026-11）
    NativeSkillStates.json 里 12 条子状态中，有 4 条原先只是计时器：殛凍領域
    2241504、箭影紛飛 3141501、禁咒之符 4141502、闇黑天魔VI 14141021。
    查 282 原文后确认它们各自对应一个「持续期间按周期结算追加攻击」的领域／召唤体，
    而且**每个数值都能在母技能的 h 与 level 行里逐级对上**，不需要拍脑袋写常量：

      2241500 h「每#s2秒冰寒地帶上…#c魔力迸發#：最多對#s名敵人以#q2%的傷害攻擊#q3次」
        状态 2241504.subTime=2000ms == s2(2)×1000，time=30 == 母 time
        伤害体 2241501.damage=q2、attackCount=q3、mobCount=s（30 级逐级相等）
      3141500 h「#time秒內攻擊命中時，每隔#s秒發動最多鎖定#z名敵方，
                以#x%傷害攻擊#y次的箭矢洗禮#q2次」
        状态 3141501.subTime=3000ms == s(3)×1000；q2=7 是总次数上限
        伤害体 3141502.damage=x、attackCount=y、mobCount=z
      4141500 h「生成#z個在#time秒內經過隨機路徑、以#y%的傷害對最多#u名敵人攻擊#v次的巨型飛鏢」
        desc「永劫時空中形成的飛標會優先追蹤最大HP最高的BOSS怪物」
        伤害体 4141501.damage=y、attackCount=v、mobCount=u、bulletCount=z(5)
      14141017 h「套用#c暗影蝙蝠期間# #s秒內，每#t2秒召喚對最多#w名敵人
                  以#w2%的傷害攻擊#w3次的弱化蝙蝠群」
        弱化召唤体 14141020.time=60（s）、subTime=1470ms、damage=w2、
        attackCount=w3、mobCount=w；状态 14141021.time=7 是触发窗口（母 time）

    唯一没进表的 2141502 劇毒煉獄：h「#time秒內元素吸收的上限擴充至#x堆疊數」，
    本工程根本没有「元素吸收」系统，挂上计时器也没有消费方，先不冒充已实现。

用法
    python3 tools/ms282/export_field_zones.py            # 生成 + 自校验
    python3 tools/ms282/export_field_zones.py --check    # 只打印，不写文件
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

ADVANCED = ROOT / "Assets/Resources/UI/AdvancedSkills.json"
VMATRIX = ROOT / "Assets/Resources/UI/VMatrixSkills.json"
JOBTREE = ROOT / "Assets/Resources/UI/JobTree.json"
STATES = ROOT / "Assets/Resources/NativeSkillStates.json"
OUT = ROOT / "Assets/Resources/NativeFieldZones.json"

# 每条只登记「字段名」与「语义」，具体数值一律从 282 表逐级读出来再校验。
# intervalFrom = (母技能字段名, 换算到毫秒的乘数)，必须等于状态/召唤体的 subTime。
# checks[bodyId] = {伤害体字段: 母技能字段名}，30 级逐行比对。
ZONES: dict[int, dict] = {
    2241504: {
        "parent": 2241500, "bodies": [2241501],
        "durationFrom": ("parent", "time"), "intervalFrom": ("s2", 1000),
        "checks": {2241501: {"damage": "q2", "attackCount": "q3", "mobCount": "s"}},
        "trigger": "onCast", "targetMode": "area", "projectiles": 1, "maxActivations": 0,
        "hLine": "每#s2秒冰寒地带上",
    },
    3141501: {
        "parent": 3141500, "bodies": [3141502],
        "durationFrom": ("parent", "time"), "intervalFrom": ("s", 1000),
        "checks": {3141502: {"damage": "x", "attackCount": "y", "mobCount": "z"}},
        "trigger": "onHit", "targetMode": "area", "projectiles": 1,
        "maxActivationsField": "q2",
        "hLine": "每隔#s秒发动",
    },
    4141502: {
        "parent": 4141500, "bodies": [4141501],
        "durationFrom": ("parent", "time"), "intervalFrom": ("subTime", 1),
        "checks": {4141501: {"damage": "y", "attackCount": "v", "mobCount": "u",
                             "bulletCount": "z"}},
        "trigger": "onCast", "targetMode": "boss",
        # z=5 个飞镖：伤害体自己写了 bulletCount=5，逐级等于母技能 z。
        "projectilesFrom": ("body", "bulletCount"), "maxActivations": 0,
        "hLine": "生成#z个在#time秒内",
    },
    14141021: {
        "parent": 14141017, "bodies": [14141020],
        # 弱化蝙蝠的持续时间与周期都写在召唤体自己的 level 行；状态 time=7 只是触发窗口。
        "durationFrom": ("body", "time"), "intervalFrom": ("subTime", 1),
        "checks": {14141020: {"damage": "w2", "attackCount": "w3", "mobCount": "w"}},
        "trigger": "onSkillHitWhileState", "targetMode": "area",
        "projectiles": 1, "maxActivations": 0,
        "hLine": "弱化蝙蝠群",
    },
}


def tables() -> tuple[dict[str, dict], set[str]]:
    merged: dict[str, dict] = {}
    for path in (VMATRIX, ADVANCED):
        merged.update(json.loads(path.read_text(encoding="utf-8"))["skills"])
    every: set[str] = set(merged)
    if JOBTREE.is_file():
        every |= set(json.loads(JOBTREE.read_text(encoding="utf-8"))["skills"])
    return merged, every


def levels(row: dict) -> dict[str, dict]:
    return row.get("levels") or {}


def h_evidence(parent: int, needle: str) -> str:
    node = skill_string(parent)
    h = simplify(text_of(node, "h")).replace("\\n", "\n")
    for line in h.split("\n"):
        if needle in line:
            return line.strip()[:220]
    return h.replace("\n", " ")[:220]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只打印摘要，不写文件")
    args = ap.parse_args()

    skills, every_id = tables()
    states = json.loads(STATES.read_text(encoding="utf-8"))["states"]
    problems: list[str] = []
    zones: dict[str, dict] = {}

    for sid, cfg in sorted(ZONES.items()):
        state = states.get(str(sid))
        parent = cfg["parent"]
        if state is None:
            problems.append(f"{sid} 不在 NativeSkillStates.json（周期领域必须是子状态）")
            continue
        prow = skills.get(str(parent))
        srow = skills.get(str(sid))
        if prow is None or srow is None:
            problems.append(f"{sid} / {parent} 不在技能表")
            continue
        if int(state["parent"]) != parent:
            problems.append(f"{sid} 的母技能登记为 {state['parent']}，与本表 {parent} 不一致")

        plv = levels(prow)
        slv = levels(srow)
        if not plv or not slv:
            problems.append(f"{sid} 缺 level 行")
            continue
        sample = plv.get("1") or next(iter(plv.values()))
        state_lv1 = slv.get("1") or next(iter(slv.values()))

        # ① 周期：母技能字段换算成毫秒，必须等于状态（或召唤体）的 subTime。
        field, mul = cfg["intervalFrom"]
        if field == "subTime":
            body_lv1 = (levels(skills[str(cfg["bodies"][0])]).get("1") or {})
            interval = int(body_lv1.get("subTime", 0)) if cfg["durationFrom"][0] == "body" \
                else int(state_lv1.get("subTime", 0))
            origin = "召唤体 subTime" if cfg["durationFrom"][0] == "body" else "状态 subTime"
        else:
            interval = int(sample.get(field, 0)) * mul
            origin = f"母技能 {field}×{mul}"
            if int(state_lv1.get("subTime", 0)) != interval:
                problems.append(f"{sid} 状态 subTime={state_lv1.get('subTime')} != {origin}={interval}")
        if interval <= 0:
            problems.append(f"{sid} 周期算不出来（{origin}）")

        # ② 持续时间：母技能 time 或召唤体 time；状态自己写的 time 只作旁证。
        dsrc, dfield = cfg["durationFrom"]
        duration_row = plv.get("1", {}) if dsrc == "parent" \
            else (levels(skills[str(cfg["bodies"][0])]).get("1") or {})
        duration = int(duration_row.get(dfield, 0))
        if duration <= 0:
            problems.append(f"{sid} 持续时间算不出来（{dsrc}.{dfield}）")
        if dsrc == "parent" and int(state_lv1.get("time", 0)) != duration:
            problems.append(f"{sid} 状态 time={state_lv1.get('time')} != 母技能 time={duration}")

        # ③ 伤害体字段 30 级逐行等于母技能对应字段。
        checks: dict[str, dict[str, str]] = {}
        for body_id, mapping in cfg["checks"].items():
            brow = skills.get(str(body_id))
            if brow is None or str(body_id) not in every_id:
                problems.append(f"{sid} 的伤害体 {body_id} 不在技能表")
                continue
            blv = levels(brow)
            checks[str(body_id)] = mapping
            for body_field, parent_field in mapping.items():
                compared = 0
                for lv, line in blv.items():
                    expected = plv.get(lv, {}).get(parent_field)
                    actual = line.get(body_field)
                    if expected is None:
                        continue
                    compared += 1
                    if actual != expected:
                        problems.append(
                            f"{sid} lv{lv}: {body_id}.{body_field}={actual} != "
                            f"{parent}.{parent_field}={expected}")
                if compared < 2:
                    problems.append(f"{sid} {body_id}.{body_field} 只比对了 {compared} 级，无法自证")

        # ④ 飞镖数 / 次数上限也从字段读，不写死。
        projectiles = cfg.get("projectiles", 1)
        if "projectilesFrom" in cfg:
            src, fld = cfg["projectilesFrom"]
            row = plv.get("1", {}) if src == "parent" \
                else (levels(skills[str(cfg["bodies"][0])]).get("1") or {})
            projectiles = int(row.get(fld, 0))
            if projectiles != int(sample.get("z", 0)):
                problems.append(f"{sid} 飞镖数 {projectiles} != 母技能 z={sample.get('z')}")
        max_act = int(cfg.get("maxActivations", 0))
        if "maxActivationsField" in cfg:
            fld = cfg["maxActivationsField"]
            max_act = int(sample.get(fld, 0))
            if max_act <= 0:
                problems.append(f"{sid} 次数上限字段 {fld} 为空")
        if projectiles <= 0 or interval <= 0 or duration <= 0:
            problems.append(f"{sid} 落盘前存在非正值")

        zones[str(sid)] = {
            "name": prow.get("name"),
            "state": sid, "parent": parent,
            "durationSec": duration, "intervalMs": interval,
            "bodies": cfg["bodies"],
            "mobCount": int(sample.get("u", sample.get("z", sample.get("s", 0))) or 0),
            "projectiles": projectiles, "maxActivations": max_act,
            "trigger": cfg["trigger"], "targetMode": cfg["targetMode"],
            "checks": checks,
            "intervalField": field,
            "durationField": f"{dsrc}.{dfield}",
            "evidence": h_evidence(parent, cfg["hLine"]),
        }

    # 目标上限逐条兜底：直接取伤害体的 mobCount，避免按字母猜错。
    for sid, entry in zones.items():
        body = skills[str(entry["bodies"][0])]
        entry["mobCount"] = int((levels(body).get("1") or {}).get("mobCount", 1))
        if entry["mobCount"] <= 0:
            problems.append(f"{sid} 伤害体没有 mobCount")

    payload = {
        "source": "tools/ms282/export_field_zones.py",
        "note": "282 六转周期领域：子状态挂上后按 subTime 周期结算同族伤害体；数值逐级自证",
        "zones": zones,
    }
    text = json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    if not args.check:
        OUT.write_text(text, encoding="utf-8")
    print(f"周期领域 {len(zones)} 条；不一致 {len(problems)} 条")
    for p in problems:
        print("  !", p)
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
