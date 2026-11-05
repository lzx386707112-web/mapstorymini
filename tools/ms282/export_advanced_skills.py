#!/usr/bin/env python3
"""导出 282 六转（五转觉醒后编码）职业的技能运行时数据 → Assets/Resources/UI/AdvancedSkills.json

数据来源（只读）：
    /tmp/packs/Skill_<四转ID+2>.img          —— 定义层（common 公式 / 表现层骨架）
    MapleStory/Data/String/Skill.img         —— 技能名与说明（BMS 密钥，繁体，转简体后落盘）
    docs/legacy-advanced-deletion.json       —— 不参与（那是旧迁移件清单）

为什么 img 号是「四转 + 2」而不是职业编码「四转 + 1」：
    282 把六转技能定义放在 Skill_114.img（Hero）/ Skill_1114.img（魂骑士）…，
    而任务 38601..38625 的 job 白名单是 113 / 1113（四转 + 1）。技能 id 前缀跟着定义
    文件走（1141000 狂暴攻擊VI），运行时按 id/10000 找 img，所以文件号必须是 114。
    职业节点仍然是 113（见 tools/export_jobs.py 的 ADVANCED 表）——一职业一 img，不合并。

数值公式（282 语义，02 §4 / SkillFormulaEvaluator.java）：
    x / y 都是等级；d() 向下取整；u() 向上取整；logN 先 floor（282 分支）；
    隐式乘法（5log10(x)）；未知标识符求值 0；末尾整体 (int) 截断。
    这里在导出期按 1..maxLevel 展开成 levels 行，运行时不再需要公式求值器。

用法：
    python3 tools/ms282/export_advanced_skills.py            # 生成 + 校验
    python3 tools/ms282/export_advanced_skills.py --check    # 只打印摘要
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
REPO = ROOT.parent
TOOLS = ROOT / "tools"
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

BMS = WzKey.for_region("BMS")
PACKS = Path("/tmp/packs")
STRING282 = Path("/Users/lizixian/Documents/mxd/MapleStory/Data/String/Skill.img")
OUT = ROOT / "Assets/Resources/UI/AdvancedSkills.json"

# 四转 ID → 五转觉醒后编码（与 tools/export_jobs.py 的 ADVANCED 保持一致）
ADVANCED = {
    112: 113, 122: 123, 132: 133,
    212: 213, 222: 223, 232: 233,
    312: 313, 322: 323,
    412: 413, 422: 423,
    512: 513, 522: 523,
    1112: 1113, 1212: 1213, 1312: 1313, 1412: 1413, 1512: 1513,
}


# ———————————————————————————— 公式求值（移植 SkillFormulaEvaluator）————————————

def _preprocess(text: str) -> str:
    for junk in ("\n", "\r", "\\n", "\\r", "%", "０"):
        text = text.replace(junk, "0" if junk == "０" else "")
    return text


class _Parser:
    def __init__(self, text: str, level: int):
        self.s = text
        self.i = 0
        self.level = level

    def parse(self) -> float:
        value = self.expression()
        return value

    def _ws(self):
        while self.i < len(self.s) and self.s[self.i] in " \t":
            self.i += 1

    def _peek(self):
        self._ws()
        return self.s[self.i] if self.i < len(self.s) else ""

    def _eat(self, ch: str) -> bool:
        self._ws()
        if self.i < len(self.s) and self.s[self.i] == ch:
            self.i += 1
            return True
        return False

    def expression(self) -> float:
        value = self.term()
        while True:
            c = self._peek()
            if c == "+":
                self.i += 1
                value += self.term()
            elif c == "-":
                self.i += 1
                value -= self.term()
            else:
                return value

    def _factor_start(self) -> bool:
        c = self._peek()
        return c.isdigit() or c.isalpha() or c == "("

    def term(self) -> float:
        value = self.factor()
        while True:
            c = self._peek()
            if c == "*":
                self.i += 1
                value *= self.factor()
            elif c == "/":
                self.i += 1
                rhs = self.factor()
                value = value / rhs if rhs else 0.0
            elif self._factor_start():
                value *= self.factor()
            else:
                return value

    def factor(self) -> float:
        c = self._peek()
        if c == "+":
            self.i += 1
            return self.factor()
        if c == "-":
            self.i += 1
            return -self.factor()
        return self.primary()

    def _ident(self) -> str:
        start = self.i
        while self.i < len(self.s) and self.s[self.i].isalnum():
            self.i += 1
        return self.s[start:self.i]

    def primary(self) -> float:
        c = self._peek()
        if c.isdigit():
            start = self.i
            while self.i < len(self.s) and self.s[self.i].isdigit():
                self.i += 1
            return float(self.s[start:self.i])
        if c.isalpha():
            name = self._ident()
            if name in ("x", "X", "y"):
                return float(self.level)
            if name.startswith("log") and len(name) > 3:
                base = float(name[3:])
                if self._eat("("):
                    inner = self.expression()
                    self._eat(")")
                    if inner <= 0:
                        return 0.0
                    return math.floor(math.log(inner) / math.log(base))   # 282 分支：先 floor
                return 0.0
            if name in ("min", "max"):
                if not self._eat("("):
                    return 0.0
                first = self.expression()
                if self._eat(","):
                    second = self.expression()
                    self._eat(")")
                    return min(first, second) if name == "min" else max(first, second)
                self._eat(")")
                return first
            if name in ("d", "u") and self._eat("("):
                inner = self.expression()
                self._eat(")")
                return math.floor(inner) if name == "d" else math.ceil(inner)
            return 0.0                     # 未知标识符 / 动作名 → 0（282 同样静默）
        if c == "(":
            self.i += 1
            value = self.expression()
            self._eat(")")
            return value
        if c:
            self.i += 1
        return 0.0


def eval_formula(expr: str, level: int) -> int:
    if expr is None:
        return 0
    text = _preprocess(str(expr))
    if not text.strip():
        return 0
    return int(_Parser(text, level).parse())    # 末尾 (int) 截断


# ———————————————————————————— WZ 读取 ————————————————————————————

_strings = None


def strings():
    global _strings
    if _strings is None:
        _strings = WzImage.from_bytes(STRING282.read_bytes(), key=BMS, name="Skill.img")
    return _strings


def skill_string(sid: int):
    root = strings()
    for key in (str(sid), f"{sid:07d}"):
        node = root.get(key)
        if node is not None and node.get("name") is not None:
            return node
    return None


def text_of(node, name: str) -> str:
    if node is None:
        return ""
    child = node.get(name)
    value = getattr(child, "value", None) if child is not None else None
    return str(value) if value is not None else ""


def simplify(text: str) -> str:
    try:
        from zhconv import convert
        return convert(text, "zh-cn")
    except Exception:
        return text


def classify(node, common: dict) -> str:
    if node.get("summon") is not None:
        return "summon"
    if any(k in common for k in ("damage", "fixdamage", "mad", "attackCount")):
        return "attack"
    if any(k in common for k in ("time", "prop")):
        return "buff"
    return "passive"


def level_rows(common) -> tuple[int, dict]:
    """common 节点 → (maxLevel, {field: 公式或字面量})。"""
    fields = {}
    max_level = 0
    for child in common.children():
        value = getattr(child, "value", None)
        if child.name == "maxLevel":
            try:
                max_level = int(value)
            except (TypeError, ValueError):
                max_level = 0
            continue
        if isinstance(value, str):
            fields[child.name] = value
        elif isinstance(value, (int, float)):
            fields[child.name] = value
        elif child.name in ("lt", "rb") and value is not None:
            fields[child.name] = list(value)
    return max_level, fields


def expand(fields: dict, max_level: int) -> dict:
    rows = {}
    for level in range(1, max_level + 1):
        row = {}
        for name, value in fields.items():
            if isinstance(value, str):
                row[name] = eval_formula(value, level)
            elif isinstance(value, (int, float)):
                row[name] = value
            elif isinstance(value, (list, tuple)):
                row[name] = list(value)
        rows[str(level)] = row
    return rows


def export_job(base: int, adv: int, warnings: list[str]) -> tuple[list[int], dict, dict]:
    src = base + 2
    path = PACKS / f"Skill_{src}.img"
    if not path.is_file():
        raise SystemExit(f"缺少 {path}（先解包 282 Data/Packs/Skill_*.ms 到 /tmp/packs）")
    img = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
    root = img.get("skill")
    ids, skills, icons = [], {}, {}
    for node in root.children():
        if not node.name.isdigit():
            continue
        sid = int(node.name)
        common_node = node.get("common")
        if common_node is None:
            warnings.append(f"{sid} 无 common 节点（按缺失定义跳过）")
            continue
        max_level, fields = level_rows(common_node)
        if max_level <= 0:
            warnings.append(f"{sid} maxLevel=0（跳过）")
            continue
        rows = expand(fields, max_level)
        if not rows:
            warnings.append(f"{sid} 等级行为空（跳过）")
            continue
        name_node = skill_string(sid)
        name = simplify(text_of(name_node, "name")) or str(sid)
        desc = simplify(text_of(name_node, "desc"))
        req = []
        req_node = node.get("req")
        if req_node is not None:
            for child in req_node.children():
                try:
                    req.append([int(child.name), int(getattr(child, "value", 0) or 0)])
                except (TypeError, ValueError):
                    continue
        has_icon = isinstance(node.get("icon"), WzCanvasProperty)
        if has_icon:
            icons[sid] = f"Skill/{src}.img/skill/{sid}/icon"
        invisible = 0
        inv = node.get("invisible")
        if inv is not None:
            try:
                invisible = int(getattr(inv, "value", 0) or 0)
            except (TypeError, ValueError):
                invisible = 0
        skills[sid] = {
            "id": sid,
            "name": name,
            "desc": desc,
            "job": sid // 10000,
            "maxLevel": max_level,
            "masterLevel": 0,
            "type": classify(node, fields),
            "invisible": invisible,
            "auto": False,
            "hexa": True,             # 六转技能由 HEXA 矩阵提升，不花 SP
            "req": req,
            "icon": f"skillicon/{sid}.png" if has_icon else "",
            "levels": rows,
        }
        ids.append(sid)
    return ids, skills, icons


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    warnings: list[str] = []
    jobs, skills, icons = {}, {}, {}
    for base, adv in ADVANCED.items():
        ids, job_skills, job_icons = export_job(base, adv, warnings)
        jobs[str(adv)] = ids
        skills.update(job_skills)
        icons.update(job_icons)

    multi = {sid: s for sid, s in skills.items() if 0}
    print(f"jobs={len(jobs)}  skills={len(skills)}  levels={sum(len(s['levels']) for s in skills.values())}")
    print(f"attack={sum(1 for s in skills.values() if s['type']=='attack')} "
          f"buff={sum(1 for s in skills.values() if s['type']=='buff')} "
          f"summon={sum(1 for s in skills.values() if s['type']=='summon')} "
          f"passive={sum(1 for s in skills.values() if s['type']=='passive')}")
    if warnings:
        print(f"警告 {len(warnings)} 条: {warnings[:6]}")
    # 抽检：狂暴攻擊VI 1/30 级
    for sid in (1141000, 1141500):
        s = skills.get(sid)
        if s:
            print(f"  {sid} {s['name']} lv1={s['levels']['1']} lv{ s['maxLevel'] }={s['levels'][str(s['maxLevel'])]}")

    if args.check:
        return
    payload = {
        "source": {
            "packs": str(PACKS),
            "definitions": "Skill/<四转ID+2>.img（282 客户端，BMS 密钥）",
            "strings": str(STRING282),
            "jobs": "四转 ID + 1（282 六转任务白名单）",
            "levels": "common 公式按 1..maxLevel 展开（SkillFormulaEvaluator 语义）",
        },
        "jobs": jobs,
        "skills": {str(k): v for k, v in sorted(skills.items())},
        "iconSources": {str(k): v for k, v in sorted(icons.items())},
    }
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"写入 {OUT.relative_to(ROOT)}  {OUT.stat().st_size/1024:.0f} KiB")


if __name__ == "__main__":
    main()
