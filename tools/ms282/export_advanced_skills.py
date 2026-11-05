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


# ——— 282 里「伤害写在单字母字段上」的技能 ———
# 这些 common 用的是每个技能自己约定的占位字母，含义只能从 String.wz 的 h 模板读出来，
# 不能按字母猜、也不能跨技能套用（同一个字母在不同的技能里含义不同）。
# 不登记就会整条技能「能按、有动画、不掉血」。
COMMON_ALIAS = {
    12141007: {"z": "damage"},                     # 極致熾烈VI
    400021030: {"v": "damage", "s": "attackCount", "q": "mobCount"},   # 落雷凝聚
    400011052: {"q": "damage", "s": "mobCount", "z": "attackCount", "v": "time"},  # 祝福之鎚
}


def apply_alias(skill_id: int, fields: dict) -> None:
    """按 COMMON_ALIAS 把占位字母补成引擎认识的字段（原字段保留，同名不覆盖）。"""
    for source_name, target_name in COMMON_ALIAS.get(skill_id, {}).items():
        if source_name in fields and target_name not in fields:
            fields[target_name] = fields[source_name]


# ——— 「damage 只在别的动作触发时结算」的技能 ———
#
# 这批技能 common 里确实有 damage/attackCount，`classify()` 按字段判会得到 attack，
# 但它们不是能手动按出来的攻击：String.wz 的 h 模板写的是「命中 N 次 / 攻击时概率 /
# 受到伤害时」才发动。当成 attack 的后果是双重的 —— 按下去多一次假结算，而且因为
# 定义层没有 lt/rb，会用「身前 200px」兜底框打到本不该被打的怪。
#
# 值 = (判定类型, 依据)。依据一律抄 h 模板原文，改判前先回去读那一段。
# passive：完全由别的技能/受击触发，连施放动作都没有 ⇒ BeginCast 直接拒绝。
# buff   ：本身是可施放的 Buff/开关，伤害由它带来的 proc 结算，施放时不结算。
PROC_SKILLS = {
    # 終極攻擊系列：主攻击命中后按 prop 概率追加一击，282 四条同构。
    1140009: ("passive", "終極攻擊VI：h「#prop%機率發動可造成#damage%傷害#attackCount次的終極攻擊」"),
    1240008: ("passive", "終極攻擊VI：h「攻擊命中時，#prop%機率發動…追加攻擊」"),
    1340007: ("passive", "終極之槍VI：h「#prop%機率發動可造成#damage%傷害#attackCount次的終極攻擊」"),
    3240014: ("passive", "終極之弩VI：h「以#prop%機率發動可造成#damage%傷害的終極攻擊」"),
    # 命中计数触发
    12140015: ("passive", "魔法爆發VI：h「命中#x次時，下次…將產生以#damage%的傷害攻擊#attackCount次的追加攻擊」"),
    14141019: ("passive", "深淵黑暗：h「飛鏢系列技能命中#x次時，發動將造成#damage%傷害#attackCount次的深淵重擊」"),
    1340004: ("passive", "闇靈復仇VI：h「黑騎士的直接攻擊技能命中時，有#prop%機率對敵人發動攻擊」"),
    14141004: ("passive", "暗影蝙蝠VI：h「每命中#z次時，會召喚可維持#w秒的暗影蝙蝠」"),
    14141012: ("passive", "飢餓蝙蝠VI：h「每當使用飛鏢的技能命中#z次時，召喚1隻可維持#w秒的飢餓蝙蝠」"),
    5140004: ("passive", "海龍VI：h「發動海龍技能時消耗MP#s」＝強化被動，自己不是可施放技能"),
    # Buff / 开关：伤害是 buff 期间的 proc
    13141003: ("buff", "風暴使者VI：h「#time秒內攻擊時以#prop%機率召喚巨大弓箭」"),
    1141006: ("buff", "劍士意念VI：h「#time秒內…每隔一段時間形成…劍擊」"),
    # 燎原之火VI 的 desc 写「開關技能」，但 h 写的才是效果：命中时按 prop 生成火之壁。
    # 没有 time 字段 ⇒ BuffRoll 的计时期刊无从启动，按 buff 处理只会「按下去什么都不发生」，
    # 所以判 passive（真正的触发在 NativeProcSkills 的 Roll 模式里）。
    2141007: ("passive", "燎原之火VI：h「施展火焰魔法時，有#prop%的機率生成火之壁…」"),
    400011055: ("buff", "極樂之境：h「在#time秒內可使用#w次交叉冥河」"),
    400041001: ("buff", "散式投擲：h「在#time秒期間使用投擲類技能時一次向多個方向投擲」"),
    400041008: ("buff", "影之槍：h「在#time秒期間進行…暗屬性攻擊時生成影子槍」"),
    5141011: ("buff", "海龍石VI：h「#time秒內可發動突擊技能」"),
    # 自足的追加攻击（母体合一，自带 cooltimeMS 再发动冷却）
    500001016: ("passive", "突擊爆發：h「直接攻擊的技能命中時，對所有命中的敵人發動以#damage%的傷害#attackCount次攻擊的劍擊，劍擊再發動冷卻時間#t秒」"),
    500001020: ("passive", "突擊爆發(黑暗長槍版)：h「直接攻擊的技能命中時，對所有命中的敵人產生黑暗長槍，以#damage%的傷害#attackCount次」"),
    # ——— 以下是「母技能 + 追加攻击体」里的伤害体 ———
    # info/type=53 + finalAttack=1 已能自动判成 passive（见 classify），
    # 这里登记是为了留下逐条的母子取证：每条体的 damage/attackCount 都等于
    # 母技能 h 里引用的那个字母字段（x/y/w），数值逐级相等。
    2341010: ("passive", "天怒VI(追加體)：母 2341009 h「[終極攻擊系列技能]…有#prop%機率發動將造成#x%傷害的天使的懲罰」，x=195+7*x==本體 damage"),
    2140013: ("passive", "火流星VI(追加體)：母 2141012 h「…#prop%機率召喚將造成#x%傷害的火流星墜落」，x=231+7*x==本體 damage"),
    2241004: ("passive", "暴風雪VI(追加體)：母 2241003 h「…#prop%機率對單一敵方降下以#x%傷害的暴風雪」，x=250+5*x==本體 damage"),
    5140010: ("passive", "戰艦鯨魚號VI(追加體)：母 5141009 h「…#x%機率以#y%傷害追加攻擊#w次」，x=100==prop、y=205+11*x==damage、w=2==attackCount"),
    12141016: ("passive", "魔法爆發VI(追加體)：母 12140015 h「命中#x次時…以#damage%的傷害攻擊#attackCount次的追加攻擊」，本體與母技能 damage 同源"),
    1341005: ("passive", "闇靈復仇VI(追加體)：母 1340004 h「命中時，有#prop%機率對敵人發動攻擊，每次對最多#mobCount名敵人造成#damage%傷害#attackCount次」，本體與母技能同源"),
    1341006: ("passive", "闇靈懲戒：母 1340004 h「復仇的烙印集滿3個時，將消耗所有烙印並發動闇靈懲戒：造成#s%傷害#s2次」，s=133+3*x==damage、s2=5==attackCount"),
}


def info_int(node, name: str) -> int:
    info = node.get("info")
    if info is None:
        return 0
    child = info.get(name)
    if child is None:
        return 0
    try:
        return int(getattr(child, "value", 0) or 0)
    except (TypeError, ValueError):
        return 0


def classify(node, common: dict, common_names=(), skill_id: int = 0) -> str:
    forced = PROC_SKILLS.get(skill_id)
    if forced is not None:
        return forced[0]
    # 282 的「終極攻擊系列技能」追加攻击体：info/type=53 + finalAttack=1。
    # 这是字段级判据，不靠 h 文案猜：全库（17 个职业定义 img + 五转 4000x）
    # type=53 共 12 条，其中 10 条带 finalAttack=1，每一条都能在 String.wz 里
    # 找到一条母技能，其 h 写「[終極攻擊系列技能] 直接攻擊的技能命中時，有#prop%
    # 機率…#x%傷害」，且母技能的 x/y/w 与本组件的 damage/attackCount 逐级相等
    # （天怒 2341009.x=405 == 2341010.damage / 战舰鲸鱼号 5141009.y=535 == 5140010.damage）。
    # 另两条 type=53：1140005 狂暴劍痕是真技能（有 lt/rb、有 h、**无 prop**），
    # 1240008 终极攻击VI 只是缺 finalAttack 字段但带 prop —— 所以判据取
    # 「finalAttack=1 或 common 里有 prop」，两条路都能命中，且都不会误伤 1140005。
    if info_int(node, "type") == 53 and (info_int(node, "finalAttack") == 1 or "prop" in common):
        return "passive"
    if node.get("summon") is not None:
        return "summon"
    if any(k in common for k in ("damage", "fixdamage", "mad", "attackCount")):
        return "attack"
    # V 技能按职业分流伤害：common/attackInfo/<jobId>/{damage,attackCount}。
    # 只在 common 下面才会看到 attackInfo（「蜘蛛之镜」这类 40+ 职业变体），
    # 而 level_rows 只收标量字段、把子节点整棵跳过 ⇒ 必须另看子节点名。
    if "attackInfo" in common_names or "attackInfo" in common:
        return "attack"
    if any(k in common for k in ("time", "prop")):
        return "buff"
    # 有冷却却既无 damage 也无 time 的主动技能（连接绳索、瞬移、满载骰子、
    # 超载魔力、落雷凝聚…）：282 用 cooltime 表示「可主动按」，不是被动。
    # 但 invisible 的除外——天堂神罚(2341505)/暗影手里剑(4140012)/风之圣谕(13141505)
    # 只有 cooltime + invisible=1、连 action/effect/hit 都没有，那是被动触发的内部冷却标记。
    if "cooltime" in common:
        marker = node.get("invisible")
        return "passive" if marker is not None and getattr(marker, "value", 0) else "buff"
    # 连冷却都没有、但带动作/特效/弹道分支的（祝福之锤的 ball、落雷凝聚的 action）：
    # 同样只能主动施放。真正的被动没有这些分支。
    if any(node.get(k) is not None for k in ("action", "effect", "ball", "hit", "screen")):
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
        apply_alias(sid, fields)
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
            "type": classify(node, fields, [c.name for c in common_node.children()], sid),
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
